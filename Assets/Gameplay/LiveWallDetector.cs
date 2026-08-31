using System.Collections.Generic;
using UnityEngine;
using Scanner;

namespace Gameplay
{
    // Modo alternativo al escaneo manual (WIP, ver plan/conversación): en vez de
    // pre-escanear el cuarto antes de jugar, va juntando PUNTOS sueltos durante la
    // noche a partir del mismo sensor de distancia que ya usa la mira del escáner
    // manual (RaycastResolver — el mismo que hace que el puntito naranja se
    // achique/agrande según la superficie esté cerca o lejos). No espera a
    // confirmar una pared completa (eso es lento y necesita mucho movimiento de
    // cámara): apenas el centro de pantalla "toca" una superficie vertical real
    // (no el piso/techo) a una distancia razonable, y no hay ya un punto guardado
    // muy cerca, se guarda como un posible punto de emergencia de Sorken.
    //
    // Cada punto se representa con una pared invisible chiquita + un marcador
    // genérico, reusando el mismo pipeline que ya alimenta:
    //   - SorkerNav (pathing): reconstruye su grid solo con que cambie la
    //     cantidad de paredes de SceneRegistry — no hace falta tocarlo.
    //   - GameDirector (spawn de Sorken): elige un marcador al azar de
    //     SceneRegistry.Markers sin que le importe si es semánticamente una
    //     puerta — no hace falta tocarlo tampoco.
    //
    // Nunca persiste a disco (no pasa por ScanSerializer): es 100% de sesión,
    // igual que el resto de lo que arma GameDirector/ArbmosDirector.
    //
    // SERVER-ONLY (igual que SorkerNav: "la IA es autoritativa" — solo el
    // SceneRegistry del host importa para pathing/spawn; Camera.main acá es
    // siempre la cámara del host). Arranca con NetworkManager.OnGameStarted y se
    // apaga con NightTransition.DetenerSistemas (mismo patrón que
    // GameDirector/ArbmosDirector/etc.).
    [DefaultExecutionOrder(150)]
    public class LiveWallDetector : MonoBehaviour
    {
        public static LiveWallDetector Instance { get; private set; }

        [Header("Muestreo de puntos")]
        [Tooltip("Cada cuánto (s) se toma una muestra del centro de pantalla.")]
        [SerializeField] private float _muestreoIntervalo = 0.35f;
        [Tooltip("Distancia mínima (m) entre dos puntos guardados — evita amontonar puntos mirando fijo al mismo lugar.")]
        [SerializeField] private float _distanciaMinimaEntrePuntos = 0.8f;
        [Tooltip("Rango de distancia (m) a la cámara para aceptar un punto como posible pared.")]
        [SerializeField] private float _distanciaMin = 0.5f;
        [SerializeField] private float _distanciaMax = 8f;
        [Tooltip("Qué tan vertical tiene que ser la superficie (0 = da igual, 1 = perfectamente vertical, descarta piso/techo).")]
        [Range(0f, 1f)]
        [SerializeField] private float _verticalidadMinima = 0.7f;
        [Tooltip("Diferencia máxima de altura (m) entre el celular y el punto detectado. Una " +
                 "pared real, mirando más o menos derecho, pega cerca de tu propia altura; algo " +
                 "mucho más bajo (una silla, una mesa) o mucho más alto (el techo) casi seguro no " +
                 "es pared, aunque su superficie sea bastante vertical (el respaldo de una silla, " +
                 "por ejemplo, engaña al filtro de verticalidad de arriba).")]
        [SerializeField] private float _diferenciaAlturaMaxima = 0.8f;
        [Tooltip("Cantidad máxima de puntos guardados a la vez. Al superarla se olvida el más viejo (FIFO).")]
        [SerializeField] private int _maxPuntos = 12;
        [Tooltip("Caída mínima (m) de la cámara al piso para aceptar la muestra como piso real. " +
                 "Descarta mesas/escritorios cerca de la cámara (un jugador de pie sostiene el " +
                 "teléfono bastante más arriba que eso; alguien agachado o muy bajo de estatura " +
                 "no es el caso típico que este modo intenta soportar).")]
        [SerializeField] private float _caidaMinimaPiso = 1.1f;

        // Orden de creación (para el límite FIFO): posición (chequeo de distancia
        // mínima) + la pared que representa ese punto (para poder borrarla).
        private readonly Queue<(Vector3 pos, WallObject wall)> _puntos = new();
        private bool _running;
        private float _timer;

        // ── Altura del piso (para que Sorken no quede flotando) ───────────────
        // Los puntos se detectan a la altura de la MIRA (buena para evitar muebles
        // en el medio), pero un marcador ahí deja a Sorken flotando a esa altura.
        // Estimamos el piso con un rayo recto hacia abajo desde la cámara, cada
        // muestra, y promediamos: el usuario asume (con razón) que el piso es
        // plano y no cambia durante la noche, así que un promedio de varias
        // lecturas reales es más confiable que una sola.
        private float _floorYSum;
        private int   _floorYCount;
        private float? FloorY => _floorYCount > 0 ? _floorYSum / _floorYCount : (float?)null;

        // Altura de piso estimada, en coordenadas LOCALES a WorldOrigin — mismo contrato
        // que FloorPoint.LocalY, para que sistemas que hoy solo miran FloorPoint (ej.
        // BatterySpawnManager) tengan de dónde sacar una referencia de piso también en
        // modo detección en vivo, donde nunca hay un FloorPoint (es un objeto del
        // escáner, y acá no hay ningún escaneo). El anchor solo tiene rumbo horizontal
        // (nunca inclinación — ver ARImageAnchor/ManualCalibration), así que restar la Y
        // del mundo alcanza, sin pasar por WorldOrigin.ToRelative.
        public static bool TryGetFloorLocalY(out float localY)
        {
            localY = 0f;
            if (Instance == null || !Instance.FloorY.HasValue || WorldOrigin.Instance == null) return false;
            localY = Instance.FloorY.Value - WorldOrigin.Instance.transform.position.y;
            return true;
        }

        public static LiveWallDetector Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("LiveWallDetector");
            DontDestroyOnLoad(go);
            return go.AddComponent<LiveWallDetector>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnGameStarted += HandleGameStarted;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnGameStarted -= HandleGameStarted;
            if (Instance == this) Instance = null;
        }

        private void HandleGameStarted()
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            StartDetecting();
        }

        public void StartDetecting()
        {
            if (_running) return;
            _puntos.Clear();
            _floorYSum = 0f; _floorYCount = 0;
            _timer = 0f;
            _running = true;

            MuestrearPiso();
            SpawnSyntheticStarterMarker();
        }

        public void StopRun()
        {
            _running = false;
            foreach (var (_, wall) in _puntos)
                if (wall != null) wall.Delete();
            _puntos.Clear();
            _floorYSum = 0f; _floorYCount = 0;
        }

        private void Update()
        {
            if (!_running) return;

            // Ver LiveWallDetectorViz: solo Debug.isDebugBuild, no cuesta nada en release.
            if (Debug.isDebugBuild) DibujarDebug();

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = _muestreoIntervalo;
            MuestrearPiso();
            MuestrearPunto();
        }

        // Wireframe de diagnóstico: un puntito por cada muestra guardada (para ver dónde
        // está "creyendo" que hay pared) y un anillo a la altura de piso estimada, para
        // compararla a ojo contra el piso real — ver conversación (mesa cerca de la
        // cámara confundida con el piso).
        private void DibujarDebug()
        {
            foreach (var (p, _) in _puntos)
                LiveWallDetectorViz.DibujarPunto(p);

            var cam = Camera.main;
            if (cam == null) return;
            float y = FloorY ?? (cam.transform.position.y - 1.5f);
            LiveWallDetectorViz.DibujarPiso(cam.transform.position, y);
        }

        private void MuestrearPiso()
        {
            var cam = Camera.main;
            var resolver = RaycastResolver.Ensure();
            if (cam == null || resolver == null) return;

            var hit = resolver.ResolveFromRay(new Ray(cam.transform.position, Vector3.down));
            if (!hit.Hit || hit.Source == RaycastSource.Fallback) return;

            // Sanity check: si lo que pegó está a menos de _caidaMinimaPiso de la
            // cámara, lo más probable es que sea una mesa/escritorio, no el piso
            // real — un solo dato así contaminando el promedio ya alcanza para que
            // Sorken aparezca "más alto" (ver conversación).
            float caida = cam.transform.position.y - hit.Position.y;
            if (caida < _caidaMinimaPiso) return;

            _floorYSum += hit.Position.y;
            _floorYCount++;
        }

        private void MuestrearPunto()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var resolver = RaycastResolver.Ensure();
            if (resolver == null) return;
            var hit = resolver.ResolveFromScreenCenter();
            if (!hit.Hit || hit.Source == RaycastSource.Fallback) return;

            float dist = Vector3.Distance(cam.transform.position, hit.Position);
            if (dist < _distanciaMin || dist > _distanciaMax) return;

            float verticalidad = 1f - Mathf.Abs(Vector3.Dot(hit.Normal.normalized, Vector3.up));
            if (verticalidad < _verticalidadMinima) return;

            if (Mathf.Abs(hit.Position.y - cam.transform.position.y) > _diferenciaAlturaMaxima) return;

            foreach (var (p, _) in _puntos)
                if (Vector3.Distance(p, hit.Position) < _distanciaMinimaEntrePuntos) return;

            CrearPuntoMarcador(hit.Position, hit.Normal);
        }

        private void CrearPuntoMarcador(Vector3 worldPos, Vector3 worldNormal)
        {
            if (WorldOrigin.Instance == null) return;

            var normalHoriz = new Vector3(worldNormal.x, 0f, worldNormal.z);
            if (normalHoriz.sqrMagnitude < 1e-6f) return;
            normalHoriz.Normalize();
            var baseHat = Vector3.Cross(Vector3.up, normalHoriz).normalized;

            const float halfWidth = 0.005f, height = 0.1f, grosor = 0.02f;
            float floorY = FloorY ?? (worldPos.y - 1.5f);
            var aWorld = worldPos - baseHat * halfWidth; aWorld.y = floorY;
            var bWorld = worldPos + baseHat * halfWidth; bWorld.y = floorY;

            var aLocal = WorldOrigin.Instance.ToRelative(aWorld);
            var bLocal = WorldOrigin.Instance.ToRelative(bWorld);

            var n0Local = Vector3.Cross(Vector3.up, (bLocal - aLocal).normalized);
            var normalLocal = WorldOrigin.Instance.ToRelativeDir(normalHoriz);
            int side = n0Local.sqrMagnitude > 1e-6f && Vector3.Dot(n0Local.normalized, normalLocal) >= 0f ? 1 : -1;

            WallObject wall = null;
            ScanLoader.RunDisplayOnly(() =>
            {
                wall = WallObject.Create(aLocal, bLocal, height, grosor, side);
                if (wall == null) return;
                MarkerObject.Create(null, wall, wall.Length * 0.5f, 0f, faceSign: 1);
            });
            if (wall == null) return;

            _puntos.Enqueue((worldPos, wall));
            while (_puntos.Count > _maxPuntos)
            {
                var (_, viejo) = _puntos.Dequeue();
                if (viejo != null) viejo.Delete();
            }

            Debug.Log($"[LiveWallDetector] Punto registrado a {Vector3.Distance(Camera.main.transform.position, worldPos):F1}m ({_puntos.Count}/{_maxPuntos}).");
        }

        private void SpawnSyntheticStarterMarker()
        {
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 fwd = cam.transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;

            CrearPuntoMarcador(cam.transform.position + fwd * 2f, -fwd);
        }
    }
}
