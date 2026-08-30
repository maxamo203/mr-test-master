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

        [Header("Defaults (igual que WallBuilder)")]
        [SerializeField] private float _defaultWidth = 0.15f;

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
        [Tooltip("Cantidad máxima de puntos guardados a la vez. Al superarla se olvida el más viejo (FIFO).")]
        [SerializeField] private int _maxPuntos = 12;

        // Orden de creación (para el límite FIFO): posición (chequeo de distancia
        // mínima) + la pared que representa ese punto (para poder borrarla).
        private readonly Queue<(Vector3 pos, WallObject wall)> _puntos = new();
        private bool _running;
        private float _timer;

        public static LiveWallDetector Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("LiveWallDetector");
            // DontDestroyOnLoad igual que SorkerNav/ArbmosDirector/etc. — si no,
            // sobrevive la escena pero no la SESIÓN, y NightTransition.StopRun()
            // (pensado justo para este caso, ver CLAUDE.md) no tendría nada que apagar
            // entre partidas.
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

        // ── Activación / salida ──────────────────────────────────────────────

        public void StartDetecting()
        {
            if (_running) return;
            _puntos.Clear();
            _timer = 0f;
            _running = true;

            // Ver comentario de SpawnSyntheticStarterMarker: sin esto, GameDirector
            // no tiene ningún marcador hasta la primera muestra aceptada — el
            // usuario pidió explícitamente que Sorken pueda aparecer igual en los
            // primeros segundos.
            SpawnSyntheticStarterMarker();
        }

        // Mismo nombre que GameDirector/ArbmosDirector/etc. — ver
        // NightTransition.DetenerSistemas, que los llama a todos por igual.
        public void StopRun()
        {
            _running = false;
            // Borrar las paredes/marcadores de esta noche: la escena NO se recarga
            // entre noches (ver corolario de NightTransition en CLAUDE.md), así que
            // si no se limpian acá quedan pisando la próxima noche de la sesión.
            foreach (var (_, wall) in _puntos)
                if (wall != null) wall.Delete();
            _puntos.Clear();
        }

        // ── Muestreo continuo ────────────────────────────────────────────────

        private void Update()
        {
            if (!_running) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = _muestreoIntervalo;
            MuestrearPunto();
        }

        private void MuestrearPunto()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var resolver = RaycastResolver.Ensure();
            if (resolver == null) return;
            var hit = resolver.ResolveFromScreenCenter();
            // Solo datos REALMENTE sensados (malla LiDAR / profundidad AR / plano AR /
            // feature points) — el Fallback es un punto inventado sobre el rayo
            // cuando no se sensó nada real, no sirve como "acá hay una pared".
            if (!hit.Hit || hit.Source == RaycastSource.Fallback) return;

            float dist = Vector3.Distance(cam.transform.position, hit.Position);
            if (dist < _distanciaMin || dist > _distanciaMax) return;

            // Superficie vertical (pared) y no piso/techo: la normal casi no
            // apunta hacia arriba/abajo.
            float verticalidad = 1f - Mathf.Abs(Vector3.Dot(hit.Normal.normalized, Vector3.up));
            if (verticalidad < _verticalidadMinima) return;

            foreach (var (p, _) in _puntos)
                if (Vector3.Distance(p, hit.Position) < _distanciaMinimaEntrePuntos) return;

            CrearPuntoMarcador(hit.Position, hit.Normal);
        }

        // ── Creación del punto (pared invisible chiquita + marcador genérico) ──

        private void CrearPuntoMarcador(Vector3 worldPos, Vector3 worldNormal)
        {
            if (WorldOrigin.Instance == null) return;

            var normalHoriz = new Vector3(worldNormal.x, 0f, worldNormal.z);
            if (normalHoriz.sqrMagnitude < 1e-6f) return; // normal casi vertical: no debería pasar (ya filtrado), red de seguridad
            normalHoriz.Normalize();
            var baseHat = Vector3.Cross(Vector3.up, normalHoriz).normalized;

            const float halfWidth = 0.4f, height = 2.2f;
            var aWorld = worldPos - baseHat * halfWidth; aWorld.y = worldPos.y - height * 0.5f;
            var bWorld = worldPos + baseHat * halfWidth; bWorld.y = aWorld.y;

            var aLocal = WorldOrigin.Instance.ToRelative(aWorld);
            var bLocal = WorldOrigin.Instance.ToRelative(bWorld);

            var n0Local = Vector3.Cross(Vector3.up, (bLocal - aLocal).normalized);
            var normalLocal = WorldOrigin.Instance.ToRelativeDir(normalHoriz);
            int side = n0Local.sqrMagnitude > 1e-6f && Vector3.Dot(n0Local.normalized, normalLocal) >= 0f ? 1 : -1;

            WallObject wall = null;
            ScanLoader.RunDisplayOnly(() =>
            {
                wall = WallObject.Create(aLocal, bLocal, height, _defaultWidth, side);
                if (wall == null) return;
                MarkerObject.Create(null, wall, wall.Length * 0.5f, height * 0.5f, faceSign: 1);
            });
            if (wall == null) return;

            _puntos.Enqueue((worldPos, wall));
            // Límite fijo: se olvida el punto más viejo (FIFO) — borra su pared
            // (que de paso borra su marcador, ver WallObject.Delete/DeleteMarkers).
            while (_puntos.Count > _maxPuntos)
            {
                var (_, viejo) = _puntos.Dequeue();
                if (viejo != null) viejo.Delete();
            }

            Debug.Log($"[LiveWallDetector] Punto registrado a {Vector3.Distance(Camera.main.transform.position, worldPos):F1}m ({_puntos.Count}/{_maxPuntos}).");
        }

        // Punto de spawn disponible desde el primer instante, antes de que se
        // acepte ninguna muestra real: 2m adelante de la cámara del host, con la
        // normal apuntando HACIA el jugador (mismo criterio "mira hacia adentro"
        // que un punto real). Se crea una sola vez por noche.
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
