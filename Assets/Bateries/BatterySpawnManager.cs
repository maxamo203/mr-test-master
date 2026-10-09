using System.Collections.Generic;
using Gameplay.Spawning;
using Scanner;
using UnityEngine;

namespace Bateries
{
    // Gestor de pilas: SOLO actua en el servidor (host). Deriva los puntos de spawn
    // del escaneo del mapa, spawnea pilas por rareza (via NetworkManager.ServerSpawn,
    // que las replica a todos los clientes en coordenadas anchor-relativas) y maneja la
    // reaparicion respetando la posicion de los jugadores.
    //
    // Reglas de reaparicion (post-pickup): la pila no reaparece dentro de respawnSeconds
    // y el temporizador solo corre cuando NINGUN jugador esta dentro de playerBlockRadius
    // del punto — si un jugador se queda cerca, el timer se congela.
    //
    // Wiring: poner este componente en un GameObject de la escena multijugador y
    // asignarle el BatteryRaritySet. No requiere nada en los clientes.
    //
    // Orden de ejecución ANTES que CollectibleSpawnManager (orden 0 por defecto): las
    // reliquias evitan los puntos de pilas ya derivados (ver IsNear más abajo), así que
    // esta lista tiene que existir cuando Collectibles.CollectibleSpawnManager arma la
    // suya en el mismo evento OnGameStarted.
    [DefaultExecutionOrder(-10)]
    public class BatterySpawnManager : MonoBehaviour
    {
        public static BatterySpawnManager Instance { get; private set; }

        [Header("Tipos de pila (archivo independiente)")]
        [SerializeField] private BatteryRaritySet rarities;

        [Header("Reaparicion")]
        [Tooltip("Segundos que tarda en reaparecer una pila en un punto tras recogerla.")]
        [SerializeField] private float respawnSeconds = 60f;
        [Tooltip("Si algun jugador esta a esta distancia (m) del punto, el timer de " +
                 "reaparicion se congela (la pila no reaparece mientras se queden cerca).")]
        [SerializeField] private float playerBlockRadius = 1.5f;
        [Tooltip("Demora (s) antes del primer llenado de todos los puntos al arrancar.")]
        [SerializeField] private float initialSpawnDelay = 2f;

        [Header("Recoleccion")]
        [Tooltip("Distancia maxima (m) a la que el server acepta un pickup del jugador.")]
        [SerializeField] private float pickupMaxDistance = 2.5f;

        [Header("Derivacion de puntos desde el escaneo")]
        [Tooltip("Colocar pilas sobre la cara superior de los muebles (cubos) escaneados.")]
        [SerializeField] private bool  useFurnitureTops = true;
        [Tooltip("Area minima (m^2) de la cara superior del mueble para admitir una pila.")]
        [SerializeField] private float minFurnitureTopArea = 0.05f;
        [Tooltip("Altura (m) a la que flota la pila sobre la superficie.")]
        [SerializeField] private float surfaceOffset = 0.06f;
        [Tooltip("Ademas de los muebles, esparcir puntos por el piso en una grilla.")]
        [SerializeField] private bool  scatterOnFloor = false;
        [Tooltip("Solo poner pilas en piso DESPEJADO: descarta los puntos que tengan un " +
                 "mueble (cubo) directamente encima.")]
        [SerializeField] private bool  floorOnlyIfClearAbove = true;
        [Tooltip("Separacion (m) de la grilla del piso.")]
        [SerializeField] private float floorSpacing = 1.5f;
        [Tooltip("Radio (m) alrededor del punto de piso para la grilla.")]
        [SerializeField] private float floorRadius = 3f;
        [Tooltip("Tope de puntos de spawn.")]
        [SerializeField] private int   maxSpawnPoints = 12;
        [Tooltip("Radio de la huella reservada por una pila, incluyendo su volumen.")]
        [SerializeField] private float objectFootprintRadius = 0.10f;

        [Header("Debug")]
        [Tooltip("Muestra un panel en pantalla con el estado del sistema de pilas. " +
                 "Dejalo apagado en device: el OnGUI tiene costo. Solo para diagnosticar.")]
        [SerializeField] private bool _showDebugHud = false;
        [SerializeField] private bool _drawSpawnGizmos = false;

        // Ultimo estado legible para el HUD de diagnostico.
        private string _status = "esperando arranque de partida…";

        // Estado de un punto de spawn (todo en espacio anchor-relativo).
        private class SpawnPoint
        {
            public Vector3 relPos;
            public bool    occupied;
            public float   timer;
            public bool    blockUntilClear; // si respeta la regla de proximidad (post-pickup)
            public uint    netId;
            public byte    rarityIndex;
            public SpawnSurfaceKind surface;
            public CubeObject support;
        }

        private readonly List<SpawnPoint>          _points  = new();
        private readonly Dictionary<uint, SpawnPoint> _byNetId = new();
        private readonly List<Vector3> _rejectedPoints = new();
        private bool _started;
        private int _geometryVersion = int.MinValue;
        private int _rejectedCount;
        private string _lastRejection;
        private float _effectiveFootprintRadius;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            // Publicar el set de rarezas para que las pilas (incluso en clientes, que no
            // corren la logica del manager) puedan leer el color/tint de su rareza.
            if (rarities != null) BatteryRaritySet.Current = rarities;
        }

        // Suscribir en Start (no OnEnable) para garantizar que NetworkManager.Instance
        // ya exista (su Awake corrió). Mismo patrón que ARLobbyManager.
        private void Start()
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnGameStarted += HandleGameStarted;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnGameStarted -= HandleGameStarted;
            if (Instance == this) Instance = null;
        }

        private void HandleGameStarted()
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer)
            {
                _status = "no soy servidor: el sistema de pilas solo corre en el host.";
                return;
            }

            // Validar el wiring ANTES de arrancar: si falta un prefab o el componente
            // BatteryEntity, no arrancamos (evita el loop infinito de spawns fallidos).
            if (!ValidateSetup())
            {
                _status = "SETUP INVÁLIDO — ver Console. " + _status;
                Debug.LogError("[Bateries] Setup inválido: sistema de pilas deshabilitado. " +
                               "Corregí el wiring y volvé a arrancar la partida.");
                return;
            }

            BuildSpawnPoints();
            _started = true;

            Debug.Log($"[Bateries] {_status}");
        }

        // Dificultad de la noche en curso. Puede ser null si se entra a la escena sin
        // pasar por el menú (típico en el editor): ahí manda el ajuste del componente.
        private Gameplay.NightConfig Noche =>
            Gameplay.GameSession.Instance != null ? Gameplay.GameSession.Instance.SelectedNight : null;

        // Segundos de reaparicion de ESTA noche. NightConfig.batterySpawnRateMultiplier es
        // "frecuencia": >1 = pilas mas seguido (espera mas corta), <1 = mas escasas.
        private float RespawnDeLaNoche()
        {
            var n = Noche;
            float mul = n != null ? Mathf.Max(0.05f, n.batterySpawnRateMultiplier) : 1f;
            return respawnSeconds / mul;
        }

        // Corta la noche sin cerrar la sesión (ver Gameplay.NightTransition). Las pilas
        // vivas ya las despawnea NetworkManager.ServerResetNight; los puntos se
        // reconstruyen desde cero en el próximo HandleGameStarted.
        public void StopRun()
        {
            _started = false;
            _points.Clear();
            _byNetId.Clear();
            SpawnReservationRegistry.ClearOwner("battery");
            _status = "detenido entre noches.";
        }

        // Chequea que cada rareza tenga un prefab registrado en el PrefabRegistry bajo su
        // TypeId (BatteryBase + rarityIndex) y que ese prefab tenga el componente
        // BatteryEntity/NetworkEntity. Devuelve false y loguea el problema exacto si no.
        private bool ValidateSetup()
        {
            _effectiveFootprintRadius = objectFootprintRadius;
            if (rarities == null || rarities.Rarities == null || rarities.Rarities.Length == 0)
            {
                _status = "falta el BatteryRaritySet (o no tiene rarezas).";
                Debug.LogError("[Bateries] Falta asignar un BatteryRaritySet con rarezas en el BatterySpawnManager.");
                return false;
            }

            var reg = NetworkManager.Instance.PrefabRegistry;
            if (reg == null)
            {
                _status = "el NetworkManager no tiene PrefabRegistry.";
                Debug.LogError("[Bateries] El NetworkManager no tiene PrefabRegistry asignado.");
                return false;
            }

            foreach (var r in rarities.Rarities)
            {
                byte typeId = (byte)(EntityTypeIds.BatteryBase + r.rarityIndex);
                GameObject prefab = null;
                try { prefab = reg.Get(typeId); } catch { /* no registrado */ }

                if (prefab == null)
                {
                    _status = $"falta prefab en PrefabRegistry para TypeId {typeId} (rareza '{r.displayName}').";
                    Debug.LogError($"[Bateries] No hay prefab registrado en el PrefabRegistry para " +
                                   $"TypeId {typeId} (rareza '{r.displayName}', rarityIndex {r.rarityIndex}). " +
                                   $"Registrá el prefab de la pila con ese TypeId.");
                    return false;
                }
                if (prefab.GetComponent<NetworkEntity>() == null)
                {
                    _status = $"el prefab '{prefab.name}' (TypeId {typeId}) no tiene BatteryEntity.";
                    Debug.LogError($"[Bateries] El prefab '{prefab.name}' (TypeId {typeId}) no tiene el " +
                                   $"componente BatteryEntity. Agregáselo al prefab.");
                    return false;
                }
                _effectiveFootprintRadius = Mathf.Max(_effectiveFootprintRadius,
                    InteriorSpawnValidator.EstimatePrefabFootprintRadius(prefab, objectFootprintRadius));
            }
            return true;
        }

        // ── Derivacion de puntos desde el mapa escaneado ──────────────────────

        private void BuildSpawnPoints()
        {
            DespawnTrackedObjects();
            _points.Clear();
            _byNetId.Clear();
            _rejectedPoints.Clear();
            _rejectedCount = 0;
            _lastRejection = null;

            if (!InteriorSpawnValidator.TryEnsureReady(out var topologyReason))
            {
                _geometryVersion = InteriorSpawnValidator.GeometryVersion;
                _status = $"esperando contorno interior cerrado: {topologyReason}.";
                Debug.LogWarning($"[Bateries] {_status} No se usa fallback exterior.");
                return;
            }

            _geometryVersion = InteriorSpawnValidator.GeometryVersion;
            SpawnReservationRegistry.BeginGeometry(_geometryVersion);
            SpawnReservationRegistry.ClearOwner("battery");

            if (useFurnitureTops && SceneRegistry.Instance != null)
            {
                foreach (var cube in SceneRegistry.Instance.Cubes)
                {
                    if (cube == null) continue;
                    var t     = cube.transform;
                    var scale = t.lossyScale;
                    if (scale.x * scale.z < minFurnitureTopArea) continue; // mueble muy chico

                    // Centro de la cara superior en world, con un pequeño offset arriba.
                    Vector3 topWorld = t.position + t.up * (scale.y * 0.5f + surfaceOffset);
                    AddPoint(topWorld, SpawnSurfaceKind.FurnitureTop, cube);
                    if (_points.Count >= maxSpawnPoints) break;
                }
            }

            if (scatterOnFloor && _points.Count < maxSpawnPoints && FloorPoint.Instance != null)
                ScatterFloorGrid();

            // Arrancar todos vacios: se llenan tras initialSpawnDelay, sin exigir que el
            // jugador se aleje (la regla de proximidad solo aplica a la reaparicion).
            foreach (var p in _points)
            {
                p.occupied        = false;
                p.timer           = initialSpawnDelay;
                p.blockUntilClear = false;
            }

            _status = _points.Count > 0
                ? $"{_points.Count} puntos interiores; {_rejectedCount} rechazados."
                : $"sin puntos interiores validos ({_lastRejection ?? "sin superficies candidatas"}); esperando.";
        }

        private void ScatterFloorGrid()
        {
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;

            Vector3 c = FloorPoint.Instance.LocalPosition;
            for (float dx = -floorRadius; dx <= floorRadius; dx += floorSpacing)
            for (float dz = -floorRadius; dz <= floorRadius; dz += floorSpacing)
            {
                if (_points.Count >= maxSpawnPoints) return;
                if (dx * dx + dz * dz > floorRadius * floorRadius) continue;

                var rel = new Vector3(c.x + dx, c.y + surfaceOffset, c.z + dz);

                // Requisito: pilas en piso SOLO donde no haya un mueble encima.
                if (floorOnlyIfClearAbove &&
                    HasFurnitureAbove(WorldOrigin.Instance.ToWorld(rel)))
                    continue;

                AddPointRel(rel, SpawnSurfaceKind.Floor, null);
            }
        }

        // ¿Hay un mueble (cubo escaneado) directamente encima de este punto? Chequeo
        // geometrico: si el punto cae dentro de la huella XZ de algun cubo, tiene algo
        // arriba. Usa el espacio local del cubo (InverseTransformPoint contempla la
        // rotacion/escala), asi no depende de colliders ni de Physics.autoSyncTransforms.
        private bool HasFurnitureAbove(Vector3 worldPoint)
        {
            if (SceneRegistry.Instance == null) return false;
            foreach (var cube in SceneRegistry.Instance.Cubes)
            {
                if (cube == null) continue;
                Vector3 local = cube.transform.InverseTransformPoint(worldPoint);
                // El cubo primitivo mide 1 unidad => media-extension 0.5 en cada eje local.
                if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f)
                    return true;
            }
            return false;
        }

        private void AddPoint(Vector3 worldPos, SpawnSurfaceKind surface, CubeObject support)
        {
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;
            AddPointRel(WorldOrigin.Instance.ToRelative(worldPos), surface, support);
        }

        private void AddPointRel(Vector3 relPos, SpawnSurfaceKind surface, CubeObject support)
        {
            if (!InteriorSpawnValidator.TryValidate(relPos, surface, support,
                    _effectiveFootprintRadius, pickupMaxDistance, out var reason) ||
                !SpawnReservationRegistry.TryReserve("battery", _points.Count.ToString(), relPos,
                    _effectiveFootprintRadius, 0.05f, out reason))
            {
                _rejectedCount++;
                _lastRejection = reason;
                _rejectedPoints.Add(relPos);
                return;
            }
            _points.Add(new SpawnPoint { relPos = relPos, surface = surface, support = support });
        }

        // ¿Hay un punto de spawn de PILA a menos de minDist de relPos (anchor-relative)?
        // Lo usa Collectibles.CollectibleSpawnManager para que las reliquias tengan sus
        // propios lugares y no compartan literalmente el mismo punto que una pila.
        public bool IsNear(Vector3 relPos, float minDist)
        {
            float d2 = minDist * minDist;
            foreach (var p in _points)
                if ((p.relPos - relPos).sqrMagnitude <= d2) return true;
            return false;
        }

        // ── Loop de reaparicion (server) ──────────────────────────────────────

        private void Update()
        {
            if (!_started) return;
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;

            int currentGeometry = InteriorSpawnValidator.GeometryVersion;
            if (currentGeometry != _geometryVersion)
            {
                BuildSpawnPoints();
                return;
            }

            var players = NetworkManager.Instance.ServerPlayerWorldPositions();
            float dt = Time.deltaTime;

            foreach (var p in _points)
            {
                if (p.occupied) continue;

                // Regla de proximidad: si corresponde y hay un jugador cerca, congelar.
                if (p.blockUntilClear && AnyPlayerNear(p, players)) continue;

                p.timer -= dt;
                if (p.timer <= 0f) Spawn(p);
            }
        }

        private bool AnyPlayerNear(SpawnPoint p, IReadOnlyList<Vector3> players)
        {
            Vector3 world = WorldOrigin.Instance.ToWorld(p.relPos);
            float r2 = playerBlockRadius * playerBlockRadius;
            for (int i = 0; i < players.Count; i++)
                if ((players[i] - world).sqrMagnitude <= r2) return true;
            return false;
        }

        private void Spawn(SpawnPoint p)
        {
            if (!InteriorSpawnValidator.TryValidate(p.relPos, p.surface, p.support,
                    _effectiveFootprintRadius, pickupMaxDistance, out var invalidReason))
            {
                p.timer = 5f;
                _status = $"spawn pospuesto: {invalidReason}.";
                return;
            }

            // La noche puede sesgar la mezcla de rarezas (NightConfig.batteryChanceMods):
            // las ultimas noches reparten mas pilas pero peores, asi el jugador corre mas
            // por menos carga. Sin NightConfig, la probabilidad base del set.
            var noche  = Noche;
            var rarity = rarities != null
                ? rarities.WeightedPick(noche != null ? noche.BatteryChanceScale : (System.Func<byte, float>)null)
                : null;
            if (rarity == null)
            {
                Debug.LogWarning("[Bateries] No hay BatteryRaritySet configurado; no se spawnean pilas.");
                p.timer = 5f; // reintentar mas tarde
                return;
            }

            byte typeId  = (byte)(EntityTypeIds.BatteryBase + rarity.rarityIndex);
            Vector3 world = WorldOrigin.Instance.ToWorld(p.relPos);

            uint netId;
            try
            {
                netId = NetworkManager.Instance.ServerSpawn(typeId, world, ownerClientId: 0);
            }
            catch (System.Exception ex)
            {
                // Si el spawn falla (prefab mal configurado, etc.), NO reintentar en
                // loop cada frame: back-off largo y marcar ocupado para no spamear.
                Debug.LogError($"[Bateries] Falló el spawn de la pila TypeId={typeId}: {ex.Message}. " +
                               $"Revisá el prefab registrado para ese TypeId.");
                p.occupied = true; // deja el punto quieto hasta un futuro pickup/reset
                return;
            }

            p.occupied    = true;
            p.netId       = netId;
            p.rarityIndex = rarity.rarityIndex;
            _byNetId[netId] = p;
        }

        // ── Pickup (server) ───────────────────────────────────────────────────

        // Llamado por NetworkManager cuando un cliente pide recoger (o directamente por
        // el controlador del host). Valida cercania, despawnea la pila, arma la
        // reaparicion y acredita la carga al jugador correcto.
        public void ServerHandlePickup(uint clientId, uint batteryNetId)
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            if (!_byNetId.TryGetValue(batteryNetId, out var point))
            {
                Debug.Log($"[Bateries] Pickup ignorado: pila {batteryNetId} no registrada (¿ya recogida?).");
                return;
            }
            if (!EntityRegistry.Instance.TryGet(batteryNetId, out var entity))
            {
                Debug.Log($"[Bateries] Pickup ignorado: pila {batteryNetId} no está en EntityRegistry.");
                return;
            }

            float maxDist = pickupMaxDistance + 0.75f;
            if (!InteriorSpawnValidator.CanPickupNow(clientId, entity.transform.position,
                    maxDist, EffectiveFootprintRadius, out var pickupReason))
            {
                Debug.Log($"[Bateries] Pickup rechazado para cliente {clientId}: {pickupReason}.");
                return;
            }

            byte rarityIndex = point.rarityIndex;
            var  rarity      = rarities != null ? rarities.ByIndex(rarityIndex) : null;
            float charge     = rarity != null ? rarity.charge : 0f;

            // Despawnear y liberar el punto para reaparicion (respetando proximidad).
            NetworkManager.Instance.ServerDespawn(batteryNetId);
            _byNetId.Remove(batteryNetId);
            point.occupied        = false;
            point.timer           = RespawnDeLaNoche();
            point.blockUntilClear = true;

            // Acreditar la carga: el host la aplica local; al cliente se le avisa.
            if (clientId == 0)
                FindFirstObjectByType<Flashlight>()?.AddCharge(charge);
            else
                NetworkManager.Instance.ServerSendBatteryCollected(clientId, rarityIndex, charge);

            Debug.Log($"[Bateries] Pila {batteryNetId} (rareza {rarityIndex}) recogida por cliente {clientId}, +{charge} carga.");
        }

        // ── HUD de diagnostico ────────────────────────────────────────────────
        // El dibujo vive en DebugHud/DebugBateriasUI (solo development builds);
        // aca solo se arma el snapshot legible. Null si el toggle esta apagado.
        public string DebugSnapshot()
        {
            if (!_showDebugHud) return null;

            var net       = NetworkManager.Instance;
            bool isServer = net != null && net.IsServer;
            bool started  = net != null && net.GameStarted;
            bool woReady  = WorldOrigin.Instance != null && WorldOrigin.Instance.IsReady;

            return
                $"[Bateries]\n" +
                $"NetworkManager: {(net != null ? "OK" : "NULL")}   Server: {isServer}   GameStarted: {started}\n" +
                $"WorldOrigin ready: {woReady}   Camera.main: {(Camera.main != null)}\n" +
                $"RaritySet: {(rarities != null ? "OK" : "FALTA")}   Puntos: {_points.Count}   Activas: {_byNetId.Count}\n" +
                $"Geometría: {_geometryVersion}   Rechazados: {_rejectedCount}   Último: {_lastRejection ?? "-"}\n" +
                $"Estado: {_status}";
        }

        private void DespawnTrackedObjects()
        {
            if (_byNetId.Count == 0 || NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            var ids = new List<uint>(_byNetId.Keys);
            for (int i = 0; i < ids.Count; i++)
                NetworkManager.Instance.ServerDespawn(ids[i]);
        }

        private void OnDrawGizmosSelected()
        {
            if (!_drawSpawnGizmos || WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;
            if (InteriorSpawnValidator.TryEnsureReady(out _) && InteriorSpawnValidator.Topology != null)
            {
                float y = FloorPoint.Instance != null ? FloorPoint.Instance.LocalY + 0.02f : 0.02f;
                Gizmos.color = Color.yellow;
                foreach (var edge in InteriorSpawnValidator.Topology.Boundaries)
                {
                    Vector3 a = WorldOrigin.Instance.ToWorld(new Vector3(edge.A.x, y, edge.A.y));
                    Vector3 b = WorldOrigin.Instance.ToWorld(new Vector3(edge.B.x, y, edge.B.y));
                    Gizmos.DrawLine(a, b);
                }
            }
            Gizmos.color = Color.green;
            foreach (var point in _points)
                Gizmos.DrawWireSphere(WorldOrigin.Instance.ToWorld(point.relPos), EffectiveFootprintRadius);
            Gizmos.color = Color.red;
            foreach (var point in _rejectedPoints)
                Gizmos.DrawWireSphere(WorldOrigin.Instance.ToWorld(point), EffectiveFootprintRadius);
        }

        private float EffectiveFootprintRadius =>
            _effectiveFootprintRadius > 0f ? _effectiveFootprintRadius : objectFootprintRadius;
    }
}
