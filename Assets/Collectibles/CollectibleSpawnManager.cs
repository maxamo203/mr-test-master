using System;
using System.Collections.Generic;
using Scanner;
using UnityEngine;
using UnityEngine.Serialization;
using Gameplay;
using Gameplay.Spawning;

namespace Collectibles
{
    // Gestor de reliquias: SOLO actua en el servidor (host). A diferencia de las pilas
    // (N puntos, cada uno con su propio timer de reaparicion), acá hay UNA SOLA
    // reliquia viva a la vez: al recogerla se agenda la proxima con una demora
    // aleatoria (por noche) medida desde ESE pickup, no desde que la reliquia habia
    // aparecido — irse sin agarrarla no la hace desaparecer, pero tampoco adelanta a
    // la siguiente, así que perder tiempo cuesta reliquias que no van a llegar a
    // aparecer antes de que amanezca. Ese castigo por lentitud es el puntaje.
    //
    // El pickup es MANUAL (apuntar + botón), igual que las pilas — ver
    // CollectiblePickupAction/ContextActionController. Este manager sólo valida
    // cercania server-side (anti-cheat) y no escanea jugadores por su cuenta.
    //
    // Wiring: poner este componente en un GameObject de la escena multijugador (no
    // requiere ningun asset tipo BatteryRaritySet: las variantes visuales se
    // descubren solas desde el PrefabRegistry).
    //
    // Lugar propio en cada aparicion: se sortea uno nuevo al azar (SpawnSampler: cualquier
    // recinto cerrado, arriba de los cubos, o un radio de respaldo si no hay recintos),
    // así que no hay posiciones fijas por mapa. La separacion de las pilas no depende de
    // consultar al otro manager: ambos reservan en el SpawnReservationRegistry compartido.
    public class CollectibleSpawnManager : MonoBehaviour
    {
        public static CollectibleSpawnManager Instance { get; private set; }

        [Header("Recoleccion")]
        [Tooltip("Distancia maxima (m) a la que el server acepta un pickup del jugador.")]
        [SerializeField] private float pickupMaxDistance = 2.5f;

        [Header("Derivacion de puntos desde el escaneo")]
        [Tooltip("Admitir reliquias sobre la cara superior de los muebles (cubos): un punto " +
                 "sorteado que cae sobre un cubo va arriba del cubo.")]
        [SerializeField] private bool  useFurnitureTops = true;
        [SerializeField] private float minFurnitureTopArea = 0.05f;
        [SerializeField] private float surfaceOffset = 0.06f;
        [Tooltip("Admitir reliquias en el piso. Apagado: solo sobre muebles.")]
        [SerializeField] private bool  scatterOnFloor = true;
        [Tooltip("Si las paredes no forman ningun recinto (o no hay paredes): radio (m) " +
                 "alrededor del PISO (o del 0,0 del mapa) donde se sortean las reliquias.")]
        [FormerlySerializedAs("floorRadius")]
        [SerializeField] private float fallbackRadius = 3f;
        [Tooltip("Distancia minima (m) a los jugadores al aparecer: que no salga bajo los pies.")]
        [SerializeField] private float minDistanceFromPlayers = 1f;
        [SerializeField] private float objectFootprintRadius = 0.12f;

        [Header("Separación de las pilas")]
        [Tooltip("Distancia mínima (m) a cualquier punto de spawn de PILAS (ver " +
                 "Bateries.BatterySpawnManager) — las reliquias tienen que tener sus propios " +
                 "lugares, no aparecer literalmente donde también puede salir una pila.")]
        [SerializeField] private float minDistanceFromBatteries = 0.6f;

        [Header("Debug")]
        [Tooltip("Muestra un panel en pantalla con el estado del sistema de reliquias. " +
                 "Dejalo apagado en device: el OnGUI tiene costo. Solo para diagnosticar.")]
        [SerializeField] private bool _showDebugHud = false;
        [SerializeField] private bool _drawSpawnGizmos = false;

        private string _status = "esperando arranque de partida…";

        private struct Candidate
        {
            public Vector3 relPos;
            public SpawnSurfaceKind surface;
            public CubeObject support;
        }

        private const string Owner = "collectible";
        private const string ActiveKey = "activa";

        private readonly List<Vector3> _rejectedPoints = new();
        private readonly List<byte>    _variantTypeIds = new();

        private SpawnSampler _sampler;
        private NightConfig _night;
        private bool   _started;
        private bool   _hasActive;
        private uint   _activeNetId;
        private Vector3 _activeRel;
        private float  _timer;
        // Apariciones YA disparadas esta noche (recogidas o no), contra
        // NightConfig.collectibleMaxPerNight — no confundir con NightLoot.Total, que
        // solo cuenta pickups.
        private int    _spawnedCount;
        private int _geometryVersion = int.MinValue;
        private int _rejectedCount;
        private string _lastRejection;
        private float _effectiveFootprintRadius;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnGameStarted     += HandleGameStarted;
                // Único punto que mantiene NightLoot.Total al día en TODO dispositivo
                // (host incluido: ServerBroadcastCollectibleTotal invoca este mismo
                // evento localmente, no le llega al host por red).
                NetworkManager.Instance.OnCollectibleTotal += NightLoot.SetTotal;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnGameStarted     -= HandleGameStarted;
                NetworkManager.Instance.OnCollectibleTotal -= NightLoot.SetTotal;
            }
            if (Instance == this) Instance = null;
        }

        private void HandleGameStarted()
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer)
            {
                _status = "no soy servidor: el sistema de reliquias solo corre en el host.";
                return;
            }

            _night = GameSession.Instance != null ? GameSession.Instance.SelectedNight : null;
            if (_night == null)
            {
                Debug.LogWarning("[Reliquias] Sin NightConfig; uso valores por defecto (collectiblesActive=false).");
                _night = ScriptableObject.CreateInstance<NightConfig>();
            }

            if (!_night.collectiblesActive)
            {
                _status = "desactivado para esta noche (NightConfig.collectiblesActive=false).";
                return;
            }

            if (!ValidateSetup())
            {
                _status = "SETUP INVÁLIDO — ver Console. " + _status;
                return;
            }

            BuildCandidatePoints();
            _hasActive    = false;
            _spawnedCount = 0;
            _timer        = _night.collectibleInitialDelaySeconds;
            _started      = true;

            Debug.Log($"[Reliquias] {_variantTypeIds.Count} variante(s). {_status}");
        }

        // Corta la noche sin cerrar la sesión (ver Gameplay.NightTransition). La
        // reliquia viva ya la despawnea NetworkManager.ServerResetNight; todo se
        // reconstruye desde cero en el próximo HandleGameStarted.
        public void StopRun()
        {
            _started      = false;
            _hasActive    = false;
            _spawnedCount = 0;
            _sampler      = null;
            _variantTypeIds.Clear();
            _rejectedPoints.Clear();
            SpawnReservationRegistry.ClearOwner(Owner);
            _status = "detenido entre noches.";
        }

        // Práctica PILAS Y RELIQUIAS: las reliquias no aparecen hasta terminar la parte de
        // las pilas (Gameplay.TutorialPilasReliquias). Mientras tanto el timer no corre, así
        // que al soltarlas todavía falta la demora inicial. Estático para poder fijarlo antes
        // de que arranque la noche; quien lo prende lo apaga al morir con la escena.
        public static bool Retenidas;

        // Chequea que haya al menos una variante registrada en el PrefabRegistry bajo
        // CollectibleReliquia + i (i = 0..31) y que tenga CollectibleEntity. Tolera
        // huecos (por si se borra una variante mas adelante).
        private bool ValidateSetup()
        {
            _variantTypeIds.Clear();
            _effectiveFootprintRadius = objectFootprintRadius;

            var reg = NetworkManager.Instance.PrefabRegistry;
            if (reg == null)
            {
                _status = "el NetworkManager no tiene PrefabRegistry.";
                Debug.LogError("[Reliquias] El NetworkManager no tiene PrefabRegistry asignado.");
                return false;
            }

            for (int i = 0; i < 32; i++)
            {
                byte typeId = (byte)(EntityTypeIds.CollectibleReliquia + i);
                GameObject prefab = null;
                try { prefab = reg.Get(typeId); } catch { /* no registrado en este indice */ }

                if (prefab != null && prefab.GetComponent<CollectibleEntity>() != null)
                {
                    _variantTypeIds.Add(typeId);
                    _effectiveFootprintRadius = Mathf.Max(_effectiveFootprintRadius,
                        InteriorSpawnValidator.EstimatePrefabFootprintRadius(prefab, objectFootprintRadius));
                }
            }

            if (_variantTypeIds.Count == 0)
            {
                _status = "no hay ningun prefab de reliquia registrado.";
                Debug.LogError("[Reliquias] No hay ningun prefab de reliquia registrado en el " +
                               "PrefabRegistry. Poné los modelos en Assets/Collectibles/Models/ y " +
                               "corré Mortuorium > Crear prefabs de Reliquias.");
                return false;
            }
            return true;
        }

        // ── Area de sorteo desde el mapa escaneado. No hay lista fija de puntos: solo
        // hay UNA reliquia viva a la vez y cada aparicion sortea su lugar en el momento
        // (ver TrySortear). ─────────────────────────────────────────────────────────

        private void BuildCandidatePoints()
        {
            if (_hasActive && NetworkManager.Instance != null && NetworkManager.Instance.IsServer)
                NetworkManager.Instance.ServerDespawn(_activeNetId);
            _hasActive = false;
            SpawnReservationRegistry.Release(Owner, ActiveKey);
            _rejectedPoints.Clear();
            _rejectedCount = 0;
            _lastRejection = null;

            _geometryVersion = InteriorSpawnValidator.GeometryVersion;
            _sampler = new SpawnSampler(fallbackRadius, useFurnitureTops, scatterOnFloor,
                                        minFurnitureTopArea, surfaceOffset);
            if (!_sampler.Preparar())
            {
                _status = $"sin area para reliquias: {_sampler.Detalle}; esperando.";
                Debug.LogWarning($"[Reliquias] {_status}");
                return;
            }

            SpawnReservationRegistry.BeginGeometry(_geometryVersion);
            SpawnReservationRegistry.ClearOwner(Owner);
            _status = _sampler.Detalle;
        }

        // Un lugar al azar valido: interior, soporte, acceso, lejos de los jugadores y de
        // las pilas (reservas). No reserva: eso lo hace Activar con el elegido.
        private bool TrySortear(IReadOnlyList<Vector3> players, out Candidate candidate)
        {
            candidate = default;
            if (_sampler == null) return false;
            float footprint = EffectiveFootprintRadius;
            for (int i = 0; i < 16; i++)
            {
                if (!_sampler.TrySample(footprint, out var rel, out var surface, out var support))
                    continue;
                string reason = null;
                bool ok = !CercaDeJugador(rel, players, out reason) &&
                          InteriorSpawnValidator.TryValidate(rel, surface, support, footprint,
                              pickupMaxDistance, out reason) &&
                          SpawnReservationRegistry.IsFree(rel, footprint, minDistanceFromBatteries, out reason);
                if (!ok)
                {
                    _rejectedCount++;
                    _lastRejection = reason;
                    if (_rejectedPoints.Count < 64) _rejectedPoints.Add(rel);
                    continue;
                }
                candidate = new Candidate { relPos = rel, surface = surface, support = support };
                return true;
            }
            return false;
        }

        private bool CercaDeJugador(Vector3 rel, IReadOnlyList<Vector3> players, out string reason)
        {
            reason = null;
            if (players == null || minDistanceFromPlayers <= 0f) return false;
            Vector3 world = WorldOrigin.Instance.ToWorld(rel);
            float r2 = minDistanceFromPlayers * minDistanceFromPlayers;
            for (int i = 0; i < players.Count; i++)
            {
                Vector3 d = players[i] - world;
                d.y = 0f;
                if (d.sqrMagnitude > r2) continue;
                reason = "demasiado cerca de un jugador";
                return true;
            }
            return false;
        }

        // ── Loop de spawn (server) ─────────────────────────────────────────────

        private void Update()
        {
            if (!_started) return;
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;

            int currentGeometry = InteriorSpawnValidator.GeometryVersion;
            if (currentGeometry != _geometryVersion)
            {
                BuildCandidatePoints();
                return;
            }
            if (_hasActive) return; // esperando pickup: el timer solo corre DESPUES
            if (Retenidas) return;
            if (_night.collectibleMaxPerNight > 0 && _spawnedCount >= _night.collectibleMaxPerNight) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f) SpawnOne();
        }

        private void SpawnOne()
        {
            if (_variantTypeIds.Count == 0 ||
                !TrySortear(NetworkManager.Instance.ServerPlayerWorldPositions(), out var candidate))
            {
                _timer = 5f; // reintentar mas tarde
                _status = $"spawn pospuesto: {_lastRejection ?? "sin lugar valido"}.";
                return;
            }
            if (!Activar(candidate)) _timer = 5f;
        }

        // Spawnea una variante al azar en el lugar elegido y lo reserva (las pilas que
        // reaparezcan lo esquivan) hasta que la recojan.
        private bool Activar(Candidate candidate)
        {
            Vector3 world = WorldOrigin.Instance.ToWorld(candidate.relPos);
            byte typeId = _variantTypeIds[UnityEngine.Random.Range(0, _variantTypeIds.Count)];

            try
            {
                _activeNetId = NetworkManager.Instance.ServerSpawn(typeId, world, ownerClientId: 0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Reliquias] Falló el spawn de la reliquia TypeId={typeId}: {ex.Message}. " +
                               $"Revisá el prefab registrado para ese TypeId.");
                return false;
            }
            _hasActive = true;
            _activeRel = candidate.relPos;
            _spawnedCount++;
            SpawnReservationRegistry.Release(Owner, ActiveKey);
            SpawnReservationRegistry.TryReserve(Owner, ActiveKey, candidate.relPos,
                EffectiveFootprintRadius, 0f, out _);
            return true;
        }

        // TUTORIAL: deja UNA reliquia ya mismo, sin esperar el intervalo, en el candidato
        // válido que quede más cerca de DistanciaGuia del jugador. False si todavía no hay
        // dónde: se vuelve a pedir más tarde. Retenidas sigue frenando las siguientes.
        public bool ServerSpawnUnaAhora(Vector3 jugador, out uint netId)
        {
            netId = 0;
            if (!_started || NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return false;
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return false;
            if (_hasActive) { netId = _activeNetId; return true; }
            if (_variantTypeIds.Count == 0) return false;

            // Varios sorteos validos y se queda con el que mas se acerque a DistanciaGuia.
            var players = NetworkManager.Instance.ServerPlayerWorldPositions();
            bool hay = false;
            Candidate mejor = default;
            float mejorPuntaje = float.MaxValue;
            for (int i = 0; i < 12; i++)
            {
                if (!TrySortear(players, out var c)) continue;
                Vector3 d = WorldOrigin.Instance.ToWorld(c.relPos) - jugador;
                d.y = 0f;
                float puntaje = Mathf.Abs(d.magnitude - DistanciaGuia);
                if (puntaje >= mejorPuntaje) continue;
                mejor = c;
                mejorPuntaje = puntaje;
                hay = true;
            }
            if (!hay || !Activar(mejor)) return false;
            netId = _activeNetId;
            return true;
        }

        // Distancia ideal (metros) de la reliquia del tutorial al jugador.
        private const float DistanciaGuia = 2.5f;

        // ── Pickup (server) ───────────────────────────────────────────────────

        // Llamado por NetworkManager cuando un cliente pide recoger (o directamente
        // por CollectiblePickupAction en el host). Valida que sea la reliquia activa
        // y la cercania (solo clientes, el host ya validó con su propio apuntado),
        // despawnea, suma al contador compartido y agenda la próxima.
        public void ServerHandlePickup(uint clientId, uint netId)
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            // Un muerto (espectando) no interviene en la partida.
            if (Gameplay.ServerDeaths.IsDead(clientId)) return;
            if (!_hasActive || netId != _activeNetId)
            {
                Debug.Log($"[Reliquias] Pickup ignorado: {netId} no es la reliquia activa (¿ya recogida?).");
                return;
            }
            if (!EntityRegistry.Instance.TryGet(netId, out var entity))
            {
                Debug.Log($"[Reliquias] Pickup ignorado: reliquia {netId} no está en EntityRegistry.");
                return;
            }

            float maxDist = pickupMaxDistance + 0.75f;
            if (!InteriorSpawnValidator.CanPickupNow(clientId, entity.transform.position,
                    maxDist, EffectiveFootprintRadius, out var pickupReason))
            {
                Debug.Log($"[Reliquias] Pickup rechazado para cliente {clientId}: {pickupReason}.");
                return;
            }

            NetworkManager.Instance.ServerDespawn(netId);
            _hasActive = false;
            SpawnReservationRegistry.Release(Owner, ActiveKey);

            int nuevoTotal = NightLoot.Total + 1;
            NetworkManager.Instance.ServerBroadcastCollectibleTotal(nuevoTotal);

            _timer = UnityEngine.Random.Range(_night.collectibleIntervalMin, _night.collectibleIntervalMax);

            Debug.Log($"[Reliquias] Reliquia {netId} recogida por cliente {clientId}. Total: {nuevoTotal}. " +
                      $"Próxima en {_timer:0.0}s.");
        }

        // ── HUD de diagnostico ────────────────────────────────────────────────
        public string DebugSnapshot()
        {
            if (!_showDebugHud) return null;

            var net       = NetworkManager.Instance;
            bool isServer = net != null && net.IsServer;
            bool started  = net != null && net.GameStarted;
            bool woReady  = WorldOrigin.Instance != null && WorldOrigin.Instance.IsReady;

            return
                $"[Reliquias]\n" +
                $"NetworkManager: {(net != null ? "OK" : "NULL")}   Server: {isServer}   GameStarted: {started}\n" +
                $"WorldOrigin ready: {woReady}\n" +
                $"Variantes: {_variantTypeIds.Count}   Activa: {_hasActive}   Total: {NightLoot.Total}\n" +
                $"Aparecidas: {_spawnedCount}/{(_night != null && _night.collectibleMaxPerNight > 0 ? _night.collectibleMaxPerNight.ToString() : "∞")}\n" +
                $"Geometría: {_geometryVersion}   Rechazados: {_rejectedCount}   Último: {_lastRejection ?? "-"}\n" +
                $"Área: {(_sampler != null ? _sampler.Detalle : "-")}\n" +
                $"Estado: {_status}";
        }

        private void OnDrawGizmosSelected()
        {
            if (!_drawSpawnGizmos || WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;
            Gizmos.color = Color.cyan;
            if (_hasActive)
                Gizmos.DrawWireSphere(WorldOrigin.Instance.ToWorld(_activeRel), EffectiveFootprintRadius);
            Gizmos.color = Color.red;
            foreach (var point in _rejectedPoints)
                Gizmos.DrawWireSphere(WorldOrigin.Instance.ToWorld(point), EffectiveFootprintRadius);
        }

        private float EffectiveFootprintRadius =>
            _effectiveFootprintRadius > 0f ? _effectiveFootprintRadius : objectFootprintRadius;
    }
}
