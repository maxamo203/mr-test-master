using System.Collections.Generic;
using Scanner;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Anchor points distribuidos: anclas AR extra que el jugador coloca por su cuarto
// ANTES de empezar la partida, para corregir la deriva (drift) del SLAM sin tener
// que volver a la imagen de referencia.
//
// La idea: WorldOrigin cuelga de un ARAnchor, así que las correcciones de pose que
// la plataforma le hace a ese anchor propagan solas a toda la escena escaneada. Con
// una sola ancla (la de la imagen) esa corrección es buena cerca de la imagen y
// mala lejos. Con varias repartidas, reparentamos WorldOrigin a la más cercana que
// esté trackeando y la alineación se recupera en cada zona del cuarto.
//
// La cadena se enraiza en el ANCLA #1, colocada justo después de detectar la imagen
// (cuando la alineación todavía es buena):
//   ancla #1  → guarda T(a1←WO), la alineación "fresca".
//   ancla N   → guarda T(a1←aN) y de ahí DERIVA T(aN←WO) = T(aN←a1)·T(a1←WO).
// Rutear por a1 en vez de medir T(aN←WO) directo es lo que evita hornear en el ancla
// nueva el drift que se acumuló mientras el jugador caminaba hasta ella.
//
// Nada de esto se persiste: los ARAnchor viven sólo en la sesión AR actual.
//
// Se crea en runtime con Ensure() desde ARLobbyManager. NO va en ARLobbyRoot: ese
// GameObject es el de WorldOrigin y DeathScreenUI lo destruye al volver al menú.
[DefaultExecutionOrder(200)]
public class AnchorPointManager : MonoBehaviour
{
    public static AnchorPointManager Instance { get; private set; }

    public const int MaxAnclas = 16;   // tope técnico: cada ancla cuesta tracking nativo
    public const int MinAnclas = 2;    // menos de dos no da cobertura útil

    private const float MinSeparacion = 0.4f;   // m entre anclas (clusters hacen oscilar la elección)

    // Selección del ancla activa.
    private const float IntervaloEval    = 0.25f;  // 4 Hz
    private const float HisteresisM      = 0.5f;   // el candidato tiene que estar esto más cerca
    private const float CooldownS        = 3f;     // mínimo entre re-enraizados
    private const float EsperaTrackingS  = 2f;     // tras volver a SessionTracking (relocalización)
    private const float MaxCorreccionM   = 1.5f;   // salto mayor = ancla mala; en estéreo marea
    private const float MaxCorreccionDeg = 30f;

    private class Ancla
    {
        public int          Id;
        public GameObject   Go;
        public ARAnchor     Ar;            // null en editor (stub sin subsistema AR)
        public Vector3      RelPosInA1;    // T(a1←aN)
        public Quaternion   RelRotInA1;
        public Vector3      WoLocalPos;    // T(aN←WO) cacheado (constante entre re-derivaciones)
        public Quaternion   WoLocalRot;
        public RaycastSource Fuente;
        public float        Calidad;       // score 0..1 en el momento de colocarla
        public GameObject   Visual;

        // En editor no hay ARAnchor: el stub se considera siempre trackeando.
        public bool Trackeando => Go != null && (Ar == null || Ar.trackingState == TrackingState.Tracking);
        public TrackingState Estado => Ar != null ? Ar.trackingState : TrackingState.Tracking;
    }

    private readonly List<Ancla> _anclas = new();
    private int _nextId = 1;

    // T(a1←WO): la alineación "fresca" capturada al colocar el ancla #1.
    private Vector3    _woInA1Pos;
    private Quaternion _woInA1Rot = Quaternion.identity;

    // "Ancla #0": el anchor de la imagen, con su propio T(a0←WO).
    private bool       _tieneImg;
    private Vector3    _imgLocalPos;
    private Quaternion _imgLocalRot = Quaternion.identity;

    private ARImageAnchor        _imageAnchor;
    private MRCardboardController _cardboard;
    private Transform            _camT;

    private float _evalTimer;
    private float _ultimoCambio      = -999f;
    private float _ultimoResetImagen = -999f;
    private float _tSessionTracking  = float.PositiveInfinity;

    // Diagnóstico (lo lee DebugAnclasUI en development build).
    private int   _activaId;
    private int   _cambios;
    private int   _rechazos;
    private float _ultimaCorreccionM;
    private float _ultimaCorreccionDeg;

    // ── Estado público ────────────────────────────────────────────────────

    public bool Listo    { get; private set; }   // el jugador cerró la colocación
    public bool Omitido  { get; private set; }   // la cerró sin anclas (escape)
    public int  Count    => _anclas.Count;
    public bool PuedeColocar => _anclas.Count < MaxAnclas;
    public bool PuedeCerrar  => _anclas.Count >= MinAnclas;

    // El jugador está en Cardboard: la retícula del centro de pantalla no coincide
    // con lo que ve ninguno de los dos ojos (ver MRCardboardController), así que no
    // se puede apuntar. Colocar queda bloqueado mientras tanto.
    public bool CardboardBloquea => _cardboard != null && _cardboard.CardboardActive;

    // ¿Hay una UI de colocación abierta? Mientras sea true medimos calidad del punto
    // y NO corregimos: mover el mapa bajo los pies del jugador justo cuando está
    // apuntando sería desorientador. Es un flag propio y no `!Listo` porque la
    // herramienta ANCLAS del escáner puede abrir la colocación aunque la opción del
    // menú esté apagada.
    public bool Colocando { get; private set; }

    // Hit vivo bajo la mira mientras se coloca (lo dibujan ReticleController /
    // ARLobbyUI en vez de resolver el raycast por su cuenta otra vez).
    public ResolvedHit UltimoHit { get; private set; }

    // ── Ciclo de vida ─────────────────────────────────────────────────────

    public static AnchorPointManager Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("AnchorPoints");
        return go.AddComponent<AnchorPointManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
        _cardboard   = FindFirstObjectByType<MRCardboardController>();

        if (_imageAnchor != null)
        {
            _imageAnchor.OnImageReacquired += OnImageReacquired;
            // Si nos crearon DESPUÉS de que la imagen ya se detectó, el evento no va
            // a volver a disparar: capturamos T(a0←WO) ahora.
            if (_imageAnchor.IsFound) CapturarImagen();
        }

        ARSession.stateChanged += OnSessionStateChanged;
        if (ARSession.state == ARSessionState.SessionTracking) _tSessionTracking = Time.time;

        var net = NetworkManager.Instance;
        if (net != null) net.OnGameStarted += OcultarVisuales;
    }

    private void OnDestroy()
    {
        if (_imageAnchor != null) _imageAnchor.OnImageReacquired -= OnImageReacquired;
        ARSession.stateChanged -= OnSessionStateChanged;

        var net = NetworkManager.Instance;
        if (net != null) net.OnGameStarted -= OcultarVisuales;

        // Soltar WorldOrigin ANTES de destruir las anclas: es hijo de una de ellas y
        // se iría con toda la escena escaneada.
        SoltarDeTodas();

        for (int i = 0; i < _anclas.Count; i++)
            if (_anclas[i].Go != null) Destroy(_anclas[i].Go);
        _anclas.Clear();

        if (Instance == this) Instance = null;
    }

    // ── Colocación ────────────────────────────────────────────────────────

    // Coloca un ancla en el punto que está bajo la retícula. Devuelve false y un
    // motivo en español si no se puede.
    public bool TryColocar(out string error)
    {
        error = null;

        var wo = WorldOrigin.Instance;
        if (wo == null || !wo.IsReady) { error = "todavía no hay calibración"; return false; }
        if (_imageAnchor == null || !_imageAnchor.IsFound) { error = "buscando la imagen…"; return false; }
        if (_anclas.Count >= MaxAnclas) { error = $"máximo {MaxAnclas} anclas"; return false; }
        if (CardboardBloquea) { error = "salí de Cardboard para colocar anclas"; return false; }

        var camT = CamaraT();
        if (camT == null) { error = "sin cámara"; return false; }

        var resolver = EnsureResolver();
        if (resolver == null) { error = "raycast no disponible"; return false; }

        // Reusamos el hit que Update ya resolvió para la vista previa (es el mismo
        // frame y el mismo punto que el jugador está viendo bajo la mira).
        var hit = UltimoHit.Hit ? UltimoHit : resolver.ResolveFromScreenCenter();
        if (!hit.Hit) { error = "apuntá a una superficie"; return false; }

#if !UNITY_EDITOR
        // Sin superficie real el ancla queda en el aire, sin features cerca, y trackea
        // mal. En editor NO hay subsistema AR, así que el raycast siempre cae al
        // fallback: ahí lo aceptamos para poder ejercitar el flujo en play mode.
        if (hit.Source == RaycastSource.Fallback)
        {
            error = "apuntá a una pared o mueble que AR reconozca";
            return false;
        }
#endif

        for (int i = 0; i < _anclas.Count; i++)
        {
            var otra = _anclas[i];
            if (otra.Go == null) continue;
            if ((otra.Go.transform.position - hit.Position).sqrMagnitude < MinSeparacion * MinSeparacion)
            {
                error = "muy cerca de otra ancla";
                return false;
            }
        }

        var go = new GameObject($"AnchorPoint{_nextId}");
        go.transform.SetPositionAndRotation(hit.Position, UprightDesde(camT));

        ARAnchor ar = null;
#if !UNITY_EDITOR
        ar = go.AddComponent<ARAnchor>();
        // ARAnchor.OnEnable llama a TryAddAnchor sincrónicamente; si falla loguea un
        // warning y se auto-deshabilita, dejando un ancla falsa congelada.
        if (ar == null || !ar.enabled || ar.trackableId == TrackableId.invalidId)
        {
            Destroy(go);
            error = "AR no pudo anclar acá; probá otra superficie";
            return false;
        }
        // Ver ARImageAnchor: sin esto una remoción del lado nativo destruiría el
        // GameObject y, con él, el WorldOrigin que cuelga.
        ar.destroyOnRemoval = false;
#endif

        var ancla = new Ancla
        {
            Id      = _nextId++,
            Go      = go,
            Ar      = ar,
            Fuente  = hit.Source,
            Calidad = AnchorQuality.Instance != null ? AnchorQuality.Instance.Score01 : 0f,
        };
        var raiz = RaizValida();

        if (raiz == null)
        {
            // Ancla #1: raíz de la cadena. Guardamos la alineación mientras todavía
            // es la que dejó la imagen.
            ancla.RelPosInA1 = Vector3.zero;
            ancla.RelRotInA1 = Quaternion.identity;
            Medir(go.transform, wo.transform, out _woInA1Pos, out _woInA1Rot);
            ancla.WoLocalPos = _woInA1Pos;
            ancla.WoLocalRot = _woInA1Rot;
        }
        else
        {
            // Anclas siguientes: se guardan RELATIVAS AL ANCLA #1, no a WorldOrigin.
            Medir(raiz.Go.transform, go.transform, out ancla.RelPosInA1, out ancla.RelRotInA1);
            Derivar(ancla);
        }

        ancla.Visual = CrearVisual(go.transform, _anclas.Count == 0);
        _anclas.Add(ancla);
        return true;
    }

    public void DeshacerUltima()
    {
        if (_anclas.Count == 0) return;
        int last = _anclas.Count - 1;
        var a = _anclas[last];
        _anclas.RemoveAt(last);

        if (a.Go != null)
        {
            SoltarSiEsPadre(a.Go.transform);
            Destroy(a.Go);
        }
        if (_activaId == a.Id) _activaId = 0;
    }

    // Abre (o reabre) la colocación: lobby al sincronizar con la imagen, escáner al
    // sincronizar o al tocar la herramienta ANCLAS. Mientras esté abierta el loop de
    // corrección queda en pausa.
    public void AbrirColocacion()
    {
        Colocando = true;
        Listo     = false;
        Omitido   = false;
    }

    // El jugador cerró la colocación: a partir de acá el loop corrige.
    public void MarcarListo()
    {
        Colocando = false;
        Listo     = true;
        _ultimoCambio = Time.time;
        AnchorQuality.Instance?.SetActivo(false);
    }

    // Escape: en un cuarto sin superficies trackeables el jugador no llega al mínimo
    // y quedaría sin poder arrancar la partida. Cierra sin anclas y sin corrección.
    public void Omitir()
    {
        // Soltamos UNA vez antes de destruir nada: hacerlo ancla por ancla dentro del
        // bucle dependería de cuándo Destroy hace nula la referencia.
        SoltarDeTodas();
        for (int i = 0; i < _anclas.Count; i++)
            if (_anclas[i].Go != null) Destroy(_anclas[i].Go);
        _anclas.Clear();
        _activaId = 0;
        Colocando = false;
        Omitido   = true;
        Listo     = true;
        AnchorQuality.Instance?.SetActivo(false);
    }

    // ── Vista previa bajo la mira mientras se coloca ──────────────────────

    private void Update()
    {
#if UNITY_EDITOR
        NudgeCamaraEditor();
#endif
        bool colocando = Colocando
                         && _imageAnchor != null && _imageAnchor.IsFound
                         && !CardboardBloquea;

        var calidad = AnchorQuality.Instance;
        if (!colocando)
        {
            if (calidad != null) calidad.SetActivo(false);
            UltimoHit = ResolvedHit.Miss;
            return;
        }

        if (calidad == null) calidad = AnchorQuality.Ensure();
        calidad.SetActivo(true);

        var resolver = EnsureResolver();
        if (resolver == null) return;

        UltimoHit = resolver.ResolveFromScreenCenter();
        calidad.Evaluar(UltimoHit);
    }

    // Al arrancar la noche las esferas sobran (inmersión + costo). El loop de
    // selección SIGUE corriendo: corregir en partida es el punto del feature.
    private void OcultarVisuales()
    {
        for (int i = 0; i < _anclas.Count; i++)
        {
            if (_anclas[i].Visual == null) continue;
            Destroy(_anclas[i].Visual);
            _anclas[i].Visual = null;
        }
    }

    // ── Selección del ancla activa ────────────────────────────────────────

    private void LateUpdate()
    {
        if (!Listo || _anclas.Count == 0) return;

        var wo = WorldOrigin.Instance;
        if (wo == null || !wo.IsReady) return;

        _evalTimer += Time.deltaTime;
        if (_evalTimer < IntervaloEval) return;
        _evalTimer = 0f;

        Evaluar(wo);
    }

    private void Evaluar(WorldOrigin wo)
    {
        var camT = CamaraT();
        if (camT == null) return;

        // Nunca pelear con una búsqueda de imagen en curso: RestartTracking desparenta
        // WorldOrigin a propósito y re-ancla al detectar.
        if (_imageAnchor != null && !_imageAnchor.IsFound) return;

        // Tras una relocalización (app resume) las poses se mueven en bloque.
        if (!SesionEstable()) return;

        // En el escáner: nunca re-enraizar en medio de un flujo multi-paso. Una pared
        // cuyo 1er vértice se colocó antes del salto y el 2º después quedaría torcida.
        var fsm = ScanStateMachine.Instance;
        if (fsm != null && EnFlujoMultiPaso(fsm.Current)) return;

        var actual = wo.CurrentAnchor;
        bool parentPerdido = actual == null;

        Transform  mejorT   = null;
        Vector3    mejorPos = Vector3.zero;
        Quaternion mejorRot = Quaternion.identity;
        int        mejorId  = 0;
        float      mejorD2  = float.MaxValue;

        // Candidato #0: el anchor de la imagen (el marco más confiable si estás cerca).
        if (_tieneImg && _imageAnchor != null && _imageAnchor.CurrentAnchor != null)
        {
            mejorT   = _imageAnchor.CurrentAnchor;
            mejorPos = _imgLocalPos;
            mejorRot = _imgLocalRot;
            mejorD2  = (mejorT.position - camT.position).sqrMagnitude;
        }

        for (int i = 0; i < _anclas.Count; i++)
        {
            var a = _anclas[i];
            if (!a.Trackeando) continue;
            float d2 = (a.Go.transform.position - camT.position).sqrMagnitude;
            if (d2 >= mejorD2) continue;
            mejorD2  = d2;
            mejorT   = a.Go.transform;
            mejorPos = a.WoLocalPos;
            mejorRot = a.WoLocalRot;
            mejorId  = a.Id;
        }

        if (mejorT == null) return;             // nada trackeando: dejamos todo como está
        if (mejorT == actual) { _activaId = mejorId; return; }

        if (!parentPerdido)
        {
            // Histéresis: sólo cambiamos si el candidato está CLARAMENTE más cerca.
            float dActual = Vector3.Distance(actual.position, camT.position);
            if (Mathf.Sqrt(mejorD2) > dActual - HisteresisM) return;

            if (Time.time - _ultimoCambio < CooldownS) return;
        }

        // Pose a la que quedaría WorldOrigin y cuánto se movería.
        Vector3    destinoPos = mejorT.position + mejorT.rotation * mejorPos;
        Quaternion destinoRot = mejorT.rotation * mejorRot;
        float dPos = Vector3.Distance(destinoPos, wo.transform.position);
        float dAng = Quaternion.Angle(destinoRot, wo.transform.rotation);

        // Clamp: un salto así es ancla mala o relocalización, y en estéreo marea.
        if (!parentPerdido && (dPos > MaxCorreccionM || dAng > MaxCorreccionDeg))
        {
            _rechazos++;
            _ultimoCambio = Time.time;   // no reintentar cada 250 ms
            if (Debug.isDebugBuild)
                Debug.LogWarning($"[Anclas] Cambio a #{mejorId} rechazado: {dPos:0.00} m / {dAng:0.0}°");
            return;
        }

        wo.SetOriginLocal(mejorT, mejorPos, mejorRot);

        _ultimoCambio        = Time.time;
        _activaId            = mejorId;
        _ultimaCorreccionM   = dPos;
        _ultimaCorreccionDeg = dAng;
        _cambios++;

        if (Debug.isDebugBuild)
            Debug.Log($"[Anclas] WorldOrigin → ancla #{mejorId} (corrección {dPos * 100f:0} cm / {dAng:0.0}°)");
    }

    // ── Reset con la imagen (la única fuente de verdad) ────────────────────

    private void OnImageReacquired()
    {
        var wo = WorldOrigin.Instance;
        if (wo == null || !wo.IsReady) return;

        CapturarImagen();
        _ultimoResetImagen = Time.time;

        if (_anclas.Count == 0) return;

        // ARImageAnchor ya reparentó WorldOrigin al anchor nuevo, así que las poses del
        // mundo están frescas: re-medimos la cadena entera contra ellas. Con eso todas
        // las relaciones cacheadas vuelven a valer contra la imagen de una sola vez.
        var raiz = _anclas[0].Trackeando ? _anclas[0] : PrimeraTrackeando();
        if (raiz == null) return;   // sin nada trackeando dejamos lo cacheado como está

        if (raiz != _anclas[0]) PromoverRaiz(raiz);

        Medir(raiz.Go.transform, wo.transform, out _woInA1Pos, out _woInA1Rot);
        for (int i = 0; i < _anclas.Count; i++)
        {
            var a = _anclas[i];
            if (a.Trackeando && a != raiz)
                Medir(raiz.Go.transform, a.Go.transform, out a.RelPosInA1, out a.RelRotInA1);
            Derivar(a);
        }

        // Que se quede unos segundos en el marco de la imagen antes de volver a saltar.
        _ultimoCambio = Time.time;
        _activaId     = 0;
    }

    // WorldOrigin acaba de quedar parentado al anchor de la imagen: su pose local ES
    // T(a0←WO) (identidad si SetOrigin fue con keepVisualPosition=false, no si fue true).
    private void CapturarImagen()
    {
        var wo  = WorldOrigin.Instance;
        var img = _imageAnchor != null ? _imageAnchor.CurrentAnchor : null;
        if (wo == null || img == null || wo.CurrentAnchor != img) return;

        _imgLocalPos = wo.transform.localPosition;
        _imgLocalRot = wo.transform.localRotation;
        _tieneImg    = true;
    }

    // Si el ancla #1 no trackea, promovemos un sobreviviente como raíz de la cadena.
    // Es álgebra pura sobre datos cacheados: T(R←aN) = T(a1←R)⁻¹·T(a1←aN).
    private void PromoverRaiz(Ancla nueva)
    {
        var invR = Quaternion.Inverse(nueva.RelRotInA1);
        var posR = nueva.RelPosInA1;

        for (int i = 0; i < _anclas.Count; i++)
        {
            var a = _anclas[i];
            a.RelPosInA1 = invR * (a.RelPosInA1 - posR);
            a.RelRotInA1 = invR * a.RelRotInA1;
        }
        _woInA1Pos = invR * (_woInA1Pos - posR);
        _woInA1Rot = invR * _woInA1Rot;

        _anclas.Remove(nueva);
        _anclas.Insert(0, nueva);
    }

    // Modos del escáner en los que hay una colocación a medio terminar (o el anchor
    // de la imagen todavía no está). Sólo aplica en ScannerScene: en SampleScene no
    // hay ScanStateMachine y el chequeo se saltea.
    private static bool EnFlujoMultiPaso(ScannerMode m) =>
        m == ScannerMode.Calibrating   || m == ScannerMode.Anchor_Place ||
        m == ScannerMode.Wall_V1       || m == ScannerMode.Wall_Height  || m == ScannerMode.Wall_Vn ||
        m == ScannerMode.DoorPickWall  || m == ScannerMode.Door_V1      || m == ScannerMode.Door_V2 ||
        m == ScannerMode.Cube_V1       || m == ScannerMode.Cube_V2      || m == ScannerMode.Cube_V3 ||
        m == ScannerMode.Marker_Place  || m == ScannerMode.EditMoveTarget;

    private Ancla PrimeraTrackeando()
    {
        for (int i = 0; i < _anclas.Count; i++)
            if (_anclas[i].Trackeando) return _anclas[i];
        return null;
    }

    // Raíz de la cadena (el "ancla #1"). Null si todavía no hay ninguna. Si la #1
    // desapareció por algún motivo, promovemos a la primera que siga viva antes de
    // medir contra ella: sus RelPosInA1 están expresadas en el marco viejo.
    private Ancla RaizValida()
    {
        if (_anclas.Count == 0) return null;
        if (_anclas[0].Go != null) return _anclas[0];

        for (int i = 1; i < _anclas.Count; i++)
        {
            if (_anclas[i].Go == null) continue;
            PromoverRaiz(_anclas[i]);
            return _anclas[0];
        }
        return null;
    }

    // ── Matemática ────────────────────────────────────────────────────────

    // T(marco←objetivo). Sin InverseTransformPoint a propósito: eso arrastra escala.
    private static void Medir(Transform marco, Transform objetivo, out Vector3 pos, out Quaternion rot)
    {
        var inv = Quaternion.Inverse(marco.rotation);
        pos = inv * (objetivo.position - marco.position);
        rot = inv * objetivo.rotation;
    }

    // T(aN←WO) = T(aN←a1)·T(a1←WO)
    private void Derivar(Ancla a)
    {
        var inv = Quaternion.Inverse(a.RelRotInA1);
        a.WoLocalRot = inv * _woInA1Rot;
        a.WoLocalPos = inv * (_woInA1Pos - a.RelPosInA1);
    }

    // Rotación upright: eje Y SIEMPRE el up del mundo, sólo rumbo horizontal — misma
    // convención que ARImageAnchor con la imagen. Así la cadena de parentado aporta
    // posición + yaw y nunca inclina el cuarto virtual.
    private static Quaternion UprightDesde(Transform camT)
    {
        var fwd = Vector3.ProjectOnPlane(camT.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(camT.up, Vector3.up);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        return Quaternion.LookRotation(fwd.normalized, Vector3.up);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // CRÍTICO: WorldOrigin es hijo del anchor. Destruir el anchor sin soltarlo antes
    // se lleva puesta TODA la escena escaneada (misma invariante que documenta
    // ARImageAnchor.RestartTracking).
    private void SoltarSiEsPadre(Transform t)
    {
        var wo = WorldOrigin.Instance;
        if (wo == null || wo.CurrentAnchor != t) return;

        var img = _imageAnchor != null ? _imageAnchor.CurrentAnchor : null;
        if (_tieneImg && img != null && img != t)
        {
            wo.SetOriginLocal(img, _imgLocalPos, _imgLocalRot);
            return;
        }
        for (int i = 0; i < _anclas.Count; i++)
        {
            var o = _anclas[i];
            if (o.Go == null || o.Go.transform == t || !o.Trackeando) continue;
            wo.SetOriginLocal(o.Go.transform, o.WoLocalPos, o.WoLocalRot);
            return;
        }
        wo.transform.SetParent(null, worldPositionStays: true);
    }

    // Igual que SoltarSiEsPadre pero para cuando se van TODAS las anclas: el único
    // destino posible es el anchor de la imagen (o el root, conservando la pose).
    private void SoltarDeTodas()
    {
        var wo = WorldOrigin.Instance;
        if (wo == null || !EsNuestro(wo.CurrentAnchor)) return;

        var img = _imageAnchor != null ? _imageAnchor.CurrentAnchor : null;
        if (_tieneImg && img != null) wo.SetOriginLocal(img, _imgLocalPos, _imgLocalRot);
        else                          wo.transform.SetParent(null, worldPositionStays: true);
    }

    private bool EsNuestro(Transform t)
    {
        if (t == null) return false;
        for (int i = 0; i < _anclas.Count; i++)
            if (_anclas[i].Go != null && _anclas[i].Go.transform == t) return true;
        return false;
    }

    private Transform CamaraT()
    {
        // Camera.main puede cambiar (Cardboard); re-resolvemos sólo si se perdió.
        if (_camT == null)
        {
            var c = Camera.main;
            _camT = c != null ? c.transform : null;
        }
        return _camT;
    }

    // El RaycastResolver vive en ScannerScene; en SampleScene lo creamos acá, y recién
    // en la primera colocación para que Camera.main y ARRaycastManager ya existan.
    private static RaycastResolver EnsureResolver()
    {
        if (RaycastResolver.Instance != null) return RaycastResolver.Instance;
        new GameObject("RaycastResolver").AddComponent<RaycastResolver>();
        return RaycastResolver.Instance;
    }

    private static GameObject CrearVisual(Transform padre, bool esPrimera)
    {
        try
        {
            // La primera es la raíz de la cadena: la marcamos distinto.
            var color = esPrimera ? new Color(0.79f, 0.54f, 0.24f) : new Color(0.56f, 0.68f, 0.48f);
            return AnchorVisuals.MakeSphere(padre, Vector3.zero, 0.06f, color);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Anclas] No se pudo crear el visual: {e.Message}");
            return null;
        }
    }

    private void OnSessionStateChanged(ARSessionStateChangedEventArgs e)
    {
        _tSessionTracking = e.state == ARSessionState.SessionTracking
            ? Time.time
            : float.PositiveInfinity;
    }

    private bool SesionEstable()
    {
#if UNITY_EDITOR
        return true;
#else
        if (ARSession.state != ARSessionState.SessionTracking) return false;
        if (ARSession.notTrackingReason != NotTrackingReason.None) return false;
        return Time.time - _tSessionTracking >= EsperaTrackingS;
#endif
    }

    // ── Debug (lo consume DebugAnclasUI, sólo development build) ───────────

    public string DebugSnapshot()
    {
        var sb = new System.Text.StringBuilder(256);
        sb.Append("ANCLAS ").Append(_anclas.Count).Append('/').Append(MaxAnclas);
        sb.Append(Listo ? (Omitido ? "  [omitido]" : "  [listo]") : "  [colocando]");
        sb.Append("\nactiva: ").Append(_activaId == 0 ? "imagen" : "#" + _activaId);
        sb.Append("   cambios ").Append(_cambios).Append("  rechazos ").Append(_rechazos);
        sb.Append("\núlt. corrección: ").Append((_ultimaCorreccionM * 100f).ToString("0"))
          .Append(" cm / ").Append(_ultimaCorreccionDeg.ToString("0.0")).Append('°');

        if (_ultimoResetImagen > 0f)
            sb.Append("   reset imagen hace ").Append((Time.time - _ultimoResetImagen).ToString("0")).Append(" s");

        var cal = AnchorQuality.Instance;
        if (Colocando && cal != null)
            sb.Append("\npunto: ").Append(cal.Etiqueta)
              .Append(" (").Append((cal.Score01 * 100f).ToString("0")).Append("%, ")
              .Append(cal.PuntosCerca).Append(" feats, ").Append(UltimoHit.Source).Append(')');

        var camT = CamaraT();
        for (int i = 0; i < _anclas.Count; i++)
        {
            var a = _anclas[i];
            sb.Append("\n #").Append(a.Id).Append(' ').Append(a.Estado);
            if (camT != null && a.Go != null)
                sb.Append("  ").Append(Vector3.Distance(a.Go.transform.position, camT.position).ToString("0.0")).Append(" m");
            sb.Append("  ").Append(a.Fuente).Append("  cal ").Append((a.Calidad * 100f).ToString("0")).Append('%');
            if (a.Go != null)
            {
                float inclin = Vector3.Angle(a.Go.transform.up, Vector3.up);
                sb.Append("  incl ").Append(inclin.ToString("0.0")).Append('°');
            }
        }
        return sb.ToString();
    }

#if UNITY_EDITOR
    // Ayudas de editor: sin device no hay forma de caminar por el cuarto, así que
    // movemos la cámara con el teclado para ejercitar el cambio de ancla, y podemos
    // desplazar un stub a mano para ver disparar el clamp.
    private void NudgeCamaraEditor()
    {
        if (!Listo) return;   // no pisar el teclado mientras se navega el lobby

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        var camT = CamaraT();
        if (camT == null) return;

        float paso = 2f * Time.deltaTime;
        if (kb.iKey.isPressed) camT.position += camT.forward * paso;
        if (kb.kKey.isPressed) camT.position -= camT.forward * paso;
        if (kb.jKey.isPressed) camT.position -= camT.right * paso;
        if (kb.lKey.isPressed) camT.position += camT.right * paso;
    }

    // Corre el stub activo para simular una deriva grande y verificar que el clamp
    // rechaza el cambio en vez de pegar un salto.
    public void SimulateDrift(float metros)
    {
        if (_anclas.Count == 0) return;
        var a = _anclas[_anclas.Count - 1];
        if (a.Go != null) a.Go.transform.position += Vector3.right * metros;
    }
#endif
}
