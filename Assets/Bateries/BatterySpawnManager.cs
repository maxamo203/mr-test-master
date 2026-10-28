using System.Collections.Generic;
using Gameplay.Spawning;
using Scanner;
using UnityEngine;
using UnityEngine.Serialization;

namespace Bateries
{
    // Gestor de pilas: SOLO actua en el servidor (host). Sortea los puntos de spawn al
    // azar sobre el escaneo (SpawnSampler: cualquier recinto cerrado, arriba de los cubos,
    // o un radio de respaldo si no hay recintos — distintos en cada noche), spawnea pilas
    // por rareza (via NetworkManager.ServerSpawn, que las replica a todos los clientes en
    // coordenadas anchor-relativas) y maneja la reaparicion respetando la posicion de los
    // jugadores.
    //
    // Reglas de reaparicion (post-pickup): la pila no reaparece dentro de respawnSeconds
    // y el temporizador solo corre cuando NINGUN jugador esta dentro de playerBlockRadius
    // del punto — si un jugador se queda cerca, el timer se congela. Con
    // relocateOnRespawn, al cumplirse sale en otro lugar al azar (lejos de los jugadores).
    //
    // Wiring: poner este componente en un GameObject de la escena multijugador y
    // asignarle el BatteryRaritySet. No requiere nada en los clientes.
    //
    // Orden de ejecución ANTES que CollectibleSpawnManager (orden 0 por defecto): las
    // reliquias evitan los lugares de pilas vía SpawnReservationRegistry, así que las
    // reservas de las pilas tienen que existir cuando Collectibles.CollectibleSpawnManager
    // arranca en el mismo evento OnGameStarted.
    [DefaultExecutionOrder(-10)]
    public class BatterySpawnManager : MonoBehaviour
    {
        public static BatterySpawnManager Instance { get; private set; }

        // Frena la aparición de pilas (pasos del TUTORIAL). Estático para poder fijarlo antes
        // de que arranque la noche; quien lo prende lo apaga al morir con la escena.
        public static bool Retenidas;

        [Header("Tipos de pila (archivo independiente)")]
        [SerializeField] private BatteryRaritySet rarities;

        [Header("Reaparicion")]
        [Tooltip("Segundos que tarda en reaparecer una pila en un punto tras recogerla.")]
        [SerializeField] private float respawnSeconds = 30f;
        [Tooltip("Si algun jugador esta a esta distancia (m) del punto, el timer de " +
                 "reaparicion se congela (la pila no reaparece mientras se queden cerca).")]
        [SerializeField] private float playerBlockRadius = 1.5f;
        [Tooltip("Demora (s) antes del primer llenado de todos los puntos al arrancar.")]
        [SerializeField] private float initialSpawnDelay = 2f;

        [Header("Recoleccion")]
        [Tooltip("Distancia maxima (m) a la que el server acepta un pickup del jugador.")]
        [SerializeField] private float pickupMaxDistance = 2.5f;

        [Header("Derivacion de puntos desde el escaneo")]
        [Tooltip("Admitir pilas sobre la cara superior de los muebles (cubos): un punto " +
                 "sorteado que cae sobre un cubo va arriba del cubo.")]
        [SerializeField] private bool  useFurnitureTops = true;
        [Tooltip("Area minima (m^2) de la cara superior del mueble para admitir una pila. " +
                 "Un cubo mas chico es solo un obstaculo.")]
        [SerializeField] private float minFurnitureTopArea = 0.05f;
        [Tooltip("Altura (m) a la que flota la pila sobre la superficie.")]
        [SerializeField] private float surfaceOffset = 0.06f;
        [Tooltip("Admitir pilas en el piso. Apagado: solo sobre muebles.")]
        [SerializeField] private bool  scatterOnFloor = true;
        [Tooltip("Separacion media (m) entre puntos: se arma un punto cada floorSpacing² m² " +
                 "de area jugable (hasta maxSpawnPoints), sorteados al azar en cada noche.")]
        [SerializeField] private float floorSpacing = 1.5f;
        [Tooltip("Si las paredes no forman ningun recinto (o no hay paredes): radio (m) " +
                 "alrededor del PISO (o del 0,0 del mapa) donde se sortean las pilas.")]
        [FormerlySerializedAs("floorRadius")]
        [SerializeField] private float fallbackRadius = 3f;
        [Tooltip("Tope de puntos de spawn.")]
        [SerializeField] private int   maxSpawnPoints = 12;
        [Tooltip("Al reaparecer tras un pickup, la pila sale en OTRO lugar al azar.")]
        [SerializeField] private bool  relocateOnRespawn = true;
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
            public string   key;            // clave en SpawnReservationRegistry
            public bool     relocate;       // al reaparecer, sortear otro lugar
        }

        private readonly List<SpawnPoint>          _points  = new();
        private SpawnSampler _sampler;
        private int _nextKey;
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

            _geometryVersion = InteriorSpawnValidator.GeometryVersion;
            _sampler = new SpawnSampler(fallbackRadius, useFurnitureTops, scatterOnFloor,
                                        minFurnitureTopArea, surfaceOffset);
            if (!_sampler.Preparar())
            {
                _status = $"sin area para pilas: {_sampler.Detalle}; esperando.";
                Debug.LogWarning($"[Bateries] {_status}");
                return;
            }

            SpawnReservationRegistry.BeginGeometry(_geometryVersion);
            SpawnReservationRegistry.ClearOwner("battery");

            // Puntos al azar (distintos cada noche) repartidos por TODA el area jugable: un
            // punto cada floorSpacing² m², como la vieja grilla, pero sin posiciones fijas.
            float areaPorPunto = Mathf.Max(0.25f, floorSpacing * floorSpacing);
            int objetivo = Mathf.Clamp(Mathf.RoundToInt(_sampler.AreaTotal / areaPorPunto), 1, maxSpawnPoints);
            for (int intentos = objetivo * 12; _points.Count < objetivo && intentos > 0; intentos--)
            {
                if (!_sampler.TrySample(_effectiveFootprintRadius, out var rel, out var surface, out var support))
                    continue;
                var key = (_nextKey++).ToString();
                if (!TryClaim(key, rel, surface, support, null)) continue;
                _points.Add(new SpawnPoint { relPos = rel, surface = surface, support = support, key = key });
            }

            // Arrancar todos vacios: se llenan tras initialSpawnDelay, sin exigir que el
            // jugador se aleje (la regla de proximidad solo aplica a la reaparicion).
            foreach (var p in _points)
            {
                p.occupied        = false;
                p.timer           = initialSpawnDelay;
                p.blockUntilClear = false;
            }

            _status = _points.Count > 0
                ? $"{_points.Count}/{objetivo} puntos al azar; {_rejectedCount} rechazados. {_sampler.Detalle}."
                : $"sin puntos validos ({_lastRejection ?? "sin superficies candidatas"}). {_sampler.Detalle}; esperando.";
        }

        // Separacion extra entre lugares de pilas (ademas de las huellas): que no salgan
        // amontonadas aunque el sorteo sea libre.
        private float SeparacionEntrePilas => Mathf.Max(0.05f, floorSpacing * 0.4f);

        // Valida el lugar y lo reserva bajo `key`. `players` (world) != null: ademas no
        // puede caer cerca de un jugador (reaparicion).
        private bool TryClaim(string key, Vector3 relPos, SpawnSurfaceKind surface, CubeObject support,
                              IReadOnlyList<Vector3> players)
        {
            string reason = null;
            bool ok = (players == null || !AnyPlayerNear(relPos, players, out reason)) &&
                      InteriorSpawnValidator.TryValidate(relPos, surface, support,
                          _effectiveFootprintRadius, pickupMaxDistance, out reason) &&
                      SpawnReservationRegistry.TryReserve("battery", key, relPos,
                          _effectiveFootprintRadius, SeparacionEntrePilas, out reason);
            if (ok) return true;
            _rejectedCount++;
            _lastRejection = reason;
            if (_rejectedPoints.Count < 64) _rejectedPoints.Add(relPos);
            return false;
        }

        // Reaparicion en otro lugar: sortea uno nuevo lejos de los jugadores. Si no
        // encuentra, vuelve a reservar el de antes (la pila sale donde estaba).
        private void Relocate(SpawnPoint p, IReadOnlyList<Vector3> players)
        {
            p.relocate = false;
            SpawnReservationRegistry.Release("battery", p.key);
            if (_sampler != null)
            {
                for (int i = 0; i < 12; i++)
                {
                    if (!_sampler.TrySample(EffectiveFootprintRadius, out var rel, out var surface, out var support))
                        continue;
                    if (!TryClaim(p.key, rel, surface, support, players)) continue;
                    p.relPos  = rel;
                    p.surface = surface;
                    p.support = support;
                    return;
                }
            }
            SpawnReservationRegistry.TryReserve("battery", p.key, p.relPos, EffectiveFootprintRadius, 0f, out _);
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

            // Pasos del TUTORIAL donde todavía no van pilas (la linterna; en pilas y cordura,
            // hasta que la batería se vacía): los timers no corren, así al soltarlas se
            // respeta la demora de cada punto.
            if (Retenidas) return;

            var players = NetworkManager.Instance.ServerPlayerWorldPositions();
            float dt = Time.deltaTime;

            foreach (var p in _points)
            {
                if (p.occupied) continue;

                // Regla de proximidad: si corresponde y hay un jugador cerca, congelar.
                if (p.blockUntilClear && AnyPlayerNear(p, players)) continue;

                p.timer -= dt;
                if (p.timer <= 0f) Spawn(p, players);
            }
        }

        private bool AnyPlayerNear(SpawnPoint p, IReadOnlyList<Vector3> players) =>
            AnyPlayerNear(p.relPos, players, out _);

        private bool AnyPlayerNear(Vector3 relPos, IReadOnlyList<Vector3> players, out string reason)
        {
            Vector3 world = WorldOrigin.Instance.ToWorld(relPos);
            float r2 = playerBlockRadius * playerBlockRadius;
            for (int i = 0; i < players.Count; i++)
            {
                if ((players[i] - world).sqrMagnitude > r2) continue;
                reason = "demasiado cerca de un jugador";
                return true;
            }
            reason = null;
            return false;
        }

        private void Spawn(SpawnPoint p, IReadOnlyList<Vector3> players)
        {
            if (p.relocate) Relocate(p, players);

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

        // TUTORIAL: deja UNA pila ya mismo, sin esperar los timers, en el punto válido que
        // quede más cerca de DistanciaGuia del jugador (ni encima ni en la otra punta). Las
        // demás siguen frenadas con Retenidas. False si todavía no hay dónde (puntos sin
        // armar o ninguno válido): se vuelve a pedir más tarde.
        public bool ServerSpawnUnaAhora(Vector3 jugador, out uint netId)
        {
            netId = 0;
            if (!_started || NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return false;
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return false;
            foreach (var p in _points)
                if (p.occupied) { netId = p.netId; return true; }

            _ordenGuia.Clear();
            _ordenGuia.AddRange(_points);
            _ordenGuia.Sort((a, b) => PuntajeGuia(a, jugador).CompareTo(PuntajeGuia(b, jugador)));
            var players = NetworkManager.Instance.ServerPlayerWorldPositions();
            foreach (var p in _ordenGuia)
            {
                // El tutorial elige el punto por distancia: no reubicarlo al sacarlo.
                p.relocate = false;
                Spawn(p, players);
                if (p.occupied) { netId = p.netId; return true; }
            }
            return false;
        }

        // Al comenzar el cierre del tutorial del libro, retirar las pilas normales para
        // dejar solamente la pila guiada. No altera el ciclo de las noches.
        public void ServerPrepararPilaTutorialLibro()
        {
            if (Gameplay.GameSession.Instance == null ||
                Gameplay.GameSession.Instance.Tutorial != Gameplay.TutorialTipo.Libro ||
                NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            DespawnTrackedObjects();
            _byNetId.Clear();
            foreach (var p in _points)
            {
                p.occupied = false;
                p.netId = 0;
            }
        }

        // Distancia ideal (metros) de la pila del tutorial al jugador.
        private const float DistanciaGuia = 2.5f;
        private readonly List<SpawnPoint> _ordenGuia = new();

        private static float PuntajeGuia(SpawnPoint p, Vector3 jugador)
        {
            Vector3 d = WorldOrigin.Instance.ToWorld(p.relPos) - jugador;
            d.y = 0f;
            return Mathf.Abs(d.magnitude - DistanciaGuia);
        }

        // ── Pickup (server) ───────────────────────────────────────────────────

        // Llamado por NetworkManager cuando un cliente pide recoger (o directamente por
        // el controlador del host). Valida cercania, despawnea la pila, arma la
        // reaparicion y acredita la carga al jugador correcto.
        public void ServerHandlePickup(uint clientId, uint batteryNetId)
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            // Un muerto (espectando) no interviene en la partida.
            if (Gameplay.ServerDeaths.IsDead(clientId)) return;
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
            point.relocate        = relocateOnRespawn;

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
                $"Área: {(_sampler != null ? _sampler.Detalle : "-")}\n" +
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
                // Recintos (incluye las paredes de cierre interno de las cadenas abiertas).
                Gizmos.color = Color.cyan;
                foreach (var room in InteriorSpawnValidator.Topology.Rooms)
                    for (int i = 0, j = room.Count - 1; i < room.Count; j = i++)
                        Gizmos.DrawLine(WorldOrigin.Instance.ToWorld(new Vector3(room[j].x, y, room[j].y)),
                                        WorldOrigin.Instance.ToWorld(new Vector3(room[i].x, y, room[i].y)));
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
