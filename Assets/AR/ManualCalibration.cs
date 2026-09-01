using System;
using Scanner;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

// Calibración manual: alternativa a la imagen de referencia para ubicar el mapa
// escaneado cuando el jugador NO tiene a mano la imagen física (se perdió, se
// despegó, quedó en otra habitación). El escaneo YA está cargado; lo único que
// falta es decidir dónde cae su 0,0 en el cuarto real.
//
// Cómo funciona:
//   1) "NO TENGO LA IMAGEN" → CentrarBajoLaMira(): raycast desde el centro de
//      pantalla, se crea un ARAnchor sobre esa superficie con el rumbo horizontal
//      de la cámara y WorldOrigin se pega ahí. Como todo el escaneo cuelga de
//      WorldOrigin, el mapa entero se recentra en el punto que el jugador apuntó.
//      Si el escaneo tiene punto de piso, además se baja el origen para que ese
//      piso virtual coincida con la superficie real apuntada.
//   2) Fase de ajuste: el gizmo se engancha al propio WorldOrigin (mover XYZ +
//      rotar en Y, sin escalar) para acomodar el mapa a mano. LISTO la cierra.
//
// AbrirAjuste() sirve también con la imagen YA detectada: si la pose que dio el
// tracking quedó corrida, se corrige a mano sin volver a buscar la imagen.
//
// NADA de esto se persiste: es alineación de sesión, no un edit del escaneo. Los
// objetos guardan coordenadas locales a WorldOrigin, así que mover el origen no
// cambia el JSON — al volver a cargar el escaneo hay que calibrar otra vez.
[DefaultExecutionOrder(210)]
public class ManualCalibration : MonoBehaviour
{
    public static ManualCalibration Instance { get; private set; }

    // Hay un anchor manual sosteniendo el mapa (se calibró sin imagen).
    public static bool Calibrado => Instance != null && Instance._anchorGo != null;

    // Fase de ajuste abierta (gizmo enganchado al 0,0). Mientras sea true,
    // AnchorPointManager no corrige: mover el mapa bajo los pies del jugador justo
    // cuando lo está acomodando a mano lo pelearía.
    public static bool Ajustando => Instance != null && Instance._ajustando;

    // El anchor manual (null si la calibración vino de la imagen).
    public Transform AnchorManual => _anchorGo != null ? _anchorGo.transform : null;

    // ── Abrir el ajuste tocando el 0,0 ────────────────────────────────────
    // Tocar el marcador del origen (las esferas del escáner, el LIBRO en partida)
    // abre el ajuste. Es la interacción que uno espera —"agarro el centro y lo
    // muevo"— y en SampleScene es la ÚNICA posible: ahí no hay SelectionController,
    // que es un componente del escáner.
    //
    // El dueño de cada escena decide cuándo se permite (el escáner mientras no haya
    // un flujo de colocación a medias; el lobby, mientras la noche no arrancó) y
    // reacciona al evento para mover su propia FSM / pantalla.
    public Func<bool> PuedeAbrirPorTap;
    public event Action OnTapEnOrigen;

    [Tooltip("Radio en píxeles alrededor del 0,0 proyectado que cuenta como tap sobre él.")]
    private const float TapRadioPx = 90f;

    private GameObject _anchorGo;
    private bool       _ajustando;

    // Pose del origen al abrir el ajuste, para poder CANCELAR.
    private Vector3    _poseInicialPos;
    private Quaternion _poseInicialRot = Quaternion.identity;

    private ARImageAnchor _imageAnchor;

    public static ManualCalibration Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("ManualCalibration");
        return go.AddComponent<ManualCalibration>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
        if (_imageAnchor != null) _imageAnchor.OnImageReacquired += OnImagenDetectada;

        if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
    }

    // Tap sobre el marcador del 0,0. Picking en SCREEN-SPACE (proyectamos la posición
    // del origen y medimos distancia en píxeles) en vez de un raycast físico: el
    // visual del ancla no tiene colliders — las esferas los evitan a propósito (ver
    // AnchorVisuals) y el libro no está en la layer de selección —, así que un
    // raycast no lo tocaría nunca. Mismo criterio que SelectionController usa con las
    // esferas-handle.
    private void Update()
    {
        if (_ajustando) return;
        var wo = WorldOrigin.Instance;
        if (wo == null || !wo.IsReady) return;
        if (PuedeAbrirPorTap != null && !PuedeAbrirPorTap()) return;

        if (!TapSoltado(out var screenPos)) return;
        if (UIBlocker.IsPointerOver(screenPos)) return;   // el tap fue sobre un panel

        var cam = Camera.main;
        if (cam == null) return;

        var sp = cam.WorldToScreenPoint(wo.transform.position);
        if (sp.z <= 0f) return;                            // el origen está detrás nuestro
        if (Vector2.Distance(new Vector2(sp.x, sp.y), screenPos) > TapRadioPx) return;

        // El evento va PRIMERO: los dueños de escena limpian su selección al
        // atenderlo, y ClearSelection hace Detach del gizmo — si engancháramos antes,
        // el handler nos lo desengancharía en el mismo frame.
        OnTapEnOrigen?.Invoke();
        AbrirAjuste();
    }

    // Tap/click soltado este frame, en píxeles con origen abajo-izquierda.
    private static bool TapSoltado(out Vector2 pos)
    {
        pos = default;

        foreach (var t in ETouch.activeTouches)
        {
            if (t.phase != UnityEngine.InputSystem.TouchPhase.Ended) continue;
            pos = t.screenPosition;
            return true;
        }

        var ms = Mouse.current;
        if (ms != null && ms.leftButton.wasReleasedThisFrame)
        {
            pos = ms.position.ReadValue();
            return true;
        }
        return false;
    }

    private void OnDestroy()
    {
        if (_imageAnchor != null) _imageAnchor.OnImageReacquired -= OnImagenDetectada;

        // CRÍTICO: WorldOrigin es hijo del anchor manual. Destruirlo sin soltarlo
        // antes se lleva puesta toda la escena escaneada (misma invariante que
        // documentan ARImageAnchor.RestartTracking y AnchorPointManager).
        SoltarWorldOrigin();
        if (_anchorGo != null) Destroy(_anchorGo);

        if (Instance == this) Instance = null;
    }

    // ── Calibrar sin imagen ───────────────────────────────────────────────

    // Coloca el 0,0 del mapa sobre el punto que está bajo la retícula y abre la
    // fase de ajuste. Devuelve false + un motivo en español si no se puede.
    public bool CentrarBajoLaMira(out string error)
    {
        error = null;

        var wo = WorldOrigin.Instance;
        if (wo == null) { error = "no hay WorldOrigin en la escena"; return false; }

        var camT = CamaraT();
        if (camT == null) { error = "sin cámara"; return false; }

        var resolver = RaycastResolver.Ensure();
        if (resolver == null) { error = "raycast no disponible"; return false; }

        var hit = resolver.ResolveFromScreenCenter();
        if (!hit.Hit) { error = "apuntá a una superficie"; return false; }

        var go = new GameObject("ManualAnchor");
        go.transform.SetPositionAndRotation(hit.Position, UprightDesde(camT));

#if !UNITY_EDITOR
        // Igual que AnchorPointManager: ARAnchor.OnEnable intenta anclar de forma
        // sincrónica y, si falla, se auto-deshabilita dejando un ancla congelada.
        var ar = go.AddComponent<ARAnchor>();
        if (ar == null || !ar.enabled || ar.trackableId == TrackableId.invalidId)
        {
            Destroy(go);
            error = "AR no pudo anclar acá; probá otra superficie";
            return false;
        }
        // Sin esto, una remoción del lado nativo destruiría el GameObject y, con él,
        // el WorldOrigin que cuelga.
        ar.destroyOnRemoval = false;
#endif

        // El anchor viejo (manual o de la imagen) queda libre en cuanto SetOrigin
        // reparenta WorldOrigin, así que recién ahí lo destruimos.
        var anterior = _anchorGo;
        _anchorGo = go;

        // keepVisualPosition:false → WorldOrigin se pega al anchor y los hijos
        // conservan su localPosition: el mapa entero viaja hasta acá.
        wo.SetOrigin(go.transform, keepVisualPosition: false);
        AlinearPiso(wo);

        if (anterior != null) Destroy(anterior);

        // Mismo marcador del 0,0 que deja la imagen al detectarse (esferas en
        // escáner/lobby, libro ritual en partida — ver ARImageAnchor.SpawnVisual):
        // sin esto, calibrar a mano dejaba el origen sin ningún visual.
        _imageAnchor?.SpawnVisualEnOrigen(go.transform);

        // El jugador declaró que no tiene la imagen: cortamos la búsqueda para que
        // una detección tardía no le mueva el mapa después de haberlo acomodado.
        _imageAnchor?.StopTracking();

        Debug.Log($"[CalibraciónManual] Origen recentrado en {hit.Position} (fuente {hit.Source}).");

        AbrirAjuste();
        // El anchor cambió, así que la pose que CANCELAR tenía guardada estaba medida
        // contra OTRO marco y ya no significa nada: la línea de base pasa a ser este
        // recentrado (AbrirAjuste no la toca si el ajuste ya estaba abierto).
        GuardarPoseInicial(wo);
        return true;
    }

    // El escaneo guarda un punto de piso anchor-relativo. El anchor manual quedó
    // sobre la superficie real apuntada, así que bajamos el origen esa misma altura
    // para que el piso virtual apoye ahí en vez de quedar flotando o hundido.
    // WorldOrigin y el anchor son upright (sólo rumbo), así que la Y local es la Y
    // del mundo y alcanza con corregir esa componente.
    private static void AlinearPiso(WorldOrigin wo)
    {
        var piso = FloorPoint.Instance;
        if (piso == null) return;

        var lp = wo.transform.localPosition;
        lp.y -= piso.LocalY;
        wo.transform.localPosition = lp;
    }

    // ── Fase de ajuste (gizmo sobre el 0,0) ───────────────────────────────

    // Engancha el gizmo al origen. Sirve tanto después de CentrarBajoLaMira como
    // con la imagen ya detectada (corregir una pose que quedó torcida).
    public void AbrirAjuste()
    {
        var wo = WorldOrigin.Instance;
        if (wo == null || !wo.IsReady) return;
        if (_ajustando) return;

        _ajustando = true;
        GuardarPoseInicial(wo);

        // Sin visual propio: el 0,0 YA está marcado por el visual del anchor, que cuelga
        // de WorldOrigin (ver ARImageAnchor.SpawnVisual) — el LIBRO en partida, las
        // esferas en el escáner. Agregar otra esfera encima sólo tapaba el libro.

        // moveOnly:false + sinEscala:true = flechas XYZ + anillo de yaw, sin cubos
        // de escala: escalar el origen deformaría todo el mapa.
        EnsureGizmo()?.Attach(wo.transform, moveOnly: false, sinEscala: true);
    }

    // Cierra el ajuste conservando lo que el jugador acomodó.
    public void Listo()
    {
        CerrarAjuste();   // idempotente: no-op si el ajuste no estaba abierto

        // Las relaciones que AnchorPointManager tiene cacheadas (T(aN←WO)) quedaron
        // viejas: si no las re-derivamos, el primer cambio de ancla devuelve el mapa
        // de un salto a la pose anterior al ajuste.
        AnchorPointManager.Instance?.RecalibrarCadena();
    }

    // Cierra el ajuste devolviendo el origen a como estaba al abrirlo.
    public void Cancelar()
    {
        if (!_ajustando) return;

        var wo = WorldOrigin.Instance;
        if (wo != null && wo.CurrentAnchor != null)
        {
            wo.transform.localPosition = _poseInicialPos;
            wo.transform.localRotation = _poseInicialRot;
        }
        CerrarAjuste();
    }

    // Pose (local al anchor) a la que vuelve CANCELAR.
    private void GuardarPoseInicial(WorldOrigin wo)
    {
        _poseInicialPos = wo.transform.localPosition;
        _poseInicialRot = wo.transform.localRotation;
    }

    private void CerrarAjuste()
    {
        _ajustando = false;

        // Sólo soltamos el gizmo si sigue siendo NUESTRO target: mientras ajustábamos
        // nadie más debería haberlo tomado, pero si pasó, Detach le sacaría el suyo.
        var giz = TransformGizmoController.Instance;
        var wo  = WorldOrigin.Instance;
        if (giz != null && wo != null && giz.Target == wo.transform) giz.Detach();
    }

    // Tira el anchor manual (sin tocar la pose del mapa, que queda donde está). La
    // llama quien vuelva a lanzar una búsqueda de imagen: mientras haya una en
    // curso NO puede quedar un anchor manual sosteniendo el mapa, o los dos marcos
    // de referencia se pelean por WorldOrigin.
    public void Descartar()
    {
        if (_ajustando) CerrarAjuste();
        if (_anchorGo == null) return;

        SoltarWorldOrigin();
        Destroy(_anchorGo);
        _anchorGo = null;
    }

    // ── La imagen apareció igual ──────────────────────────────────────────

    // ARImageAnchor ya reparentó WorldOrigin al anchor de la imagen, que es la
    // referencia buena: el anchor manual sobra y la pose que el jugador venía
    // acomodando ya no es la que está en pantalla.
    private void OnImagenDetectada()
    {
        if (_anchorGo == null) { if (_ajustando) CerrarAjuste(); return; }
        Descartar();
        Debug.Log("[CalibraciónManual] Imagen detectada: se descarta el anchor manual.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // Si WorldOrigin cuelga del anchor manual, lo desparentamos conservando su pose
    // en el mundo antes de que el anchor desaparezca.
    private void SoltarWorldOrigin()
    {
        var wo = WorldOrigin.Instance;
        if (wo == null || _anchorGo == null) return;
        if (wo.CurrentAnchor != _anchorGo.transform) return;
        wo.transform.SetParent(null, worldPositionStays: true);
    }

    private Transform _camT;

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

    // El gizmo vive en ScannerScene; en SampleScene lo creamos acá, recién cuando
    // hace falta para que Camera.main ya exista.
    private static TransformGizmoController EnsureGizmo()
    {
        var giz = TransformGizmoController.Instance;
        if (giz == null)
            giz = new GameObject("TransformGizmo").AddComponent<TransformGizmoController>();

        // La cámara de SampleScene no tiene por qué renderizar la layer del gizmo.
        int layer = LayerMask.NameToLayer("Gizmo");
        var cam = Camera.main;
        if (layer >= 0 && cam != null) cam.cullingMask |= 1 << layer;

        return giz;
    }

    // Rotación upright: eje Y SIEMPRE el up del mundo, sólo rumbo horizontal — misma
    // convención que ARImageAnchor con la imagen y que AnchorPointManager con las
    // anclas, así el cuarto virtual nunca queda inclinado.
    private static Quaternion UprightDesde(Transform camT)
    {
        var fwd = Vector3.ProjectOnPlane(camT.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(camT.up, Vector3.up);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        return Quaternion.LookRotation(fwd.normalized, Vector3.up);
    }
}
