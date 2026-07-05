using System.Collections.Generic;
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
        [Tooltip("Separacion (m) de la grilla del piso.")]
        [SerializeField] private float floorSpacing = 1.5f;
        [Tooltip("Radio (m) alrededor del punto de piso para la grilla.")]
        [SerializeField] private float floorRadius = 3f;
        [Tooltip("Tope de puntos de spawn.")]
        [SerializeField] private int   maxSpawnPoints = 12;

        // Estado de un punto de spawn (todo en espacio anchor-relativo).
        private class SpawnPoint
        {
            public Vector3 relPos;
            public bool    occupied;
            public float   timer;
            public bool    blockUntilClear; // si respeta la regla de proximidad (post-pickup)
            public uint    netId;
            public byte    rarityIndex;
        }

        private readonly List<SpawnPoint>          _points  = new();
        private readonly Dictionary<uint, SpawnPoint> _byNetId = new();
        private bool _started;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
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
        }

        private void HandleGameStarted()
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;

            // Validar el wiring ANTES de arrancar: si falta un prefab o el componente
            // BatteryEntity, no arrancamos (evita el loop infinito de spawns fallidos).
            if (!ValidateSetup())
            {
                Debug.LogError("[Bateries] Setup inválido: sistema de pilas deshabilitado. " +
                               "Corregí el wiring y volvé a arrancar la partida.");
                return;
            }

            BuildSpawnPoints();
            _started = true;
            Debug.Log($"[Bateries] {_points.Count} puntos de spawn derivados del escaneo.");
        }

        // Chequea que cada rareza tenga un prefab registrado en el PrefabRegistry bajo su
        // TypeId (BatteryBase + rarityIndex) y que ese prefab tenga el componente
        // BatteryEntity/NetworkEntity. Devuelve false y loguea el problema exacto si no.
        private bool ValidateSetup()
        {
            if (rarities == null || rarities.Rarities == null || rarities.Rarities.Length == 0)
            {
                Debug.LogError("[Bateries] Falta asignar un BatteryRaritySet con rarezas en el BatterySpawnManager.");
                return false;
            }

            var reg = NetworkManager.Instance.PrefabRegistry;
            if (reg == null)
            {
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
                    Debug.LogError($"[Bateries] No hay prefab registrado en el PrefabRegistry para " +
                                   $"TypeId {typeId} (rareza '{r.displayName}', rarityIndex {r.rarityIndex}). " +
                                   $"Registrá el prefab de la pila con ese TypeId.");
                    return false;
                }
                if (prefab.GetComponent<NetworkEntity>() == null)
                {
                    Debug.LogError($"[Bateries] El prefab '{prefab.name}' (TypeId {typeId}) no tiene el " +
                                   $"componente BatteryEntity. Agregáselo al prefab.");
                    return false;
                }
            }
            return true;
        }

        // ── Derivacion de puntos desde el mapa escaneado ──────────────────────

        private void BuildSpawnPoints()
        {
            _points.Clear();
            _byNetId.Clear();

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
                    AddPoint(topWorld);
                    if (_points.Count >= maxSpawnPoints) break;
                }
            }

            if (scatterOnFloor && _points.Count < maxSpawnPoints && FloorPoint.Instance != null)
                ScatterFloorGrid();

            // Fallback: sin muebles ni piso util, unos puntos en anillo alrededor del anchor.
            if (_points.Count == 0)
            {
                float y = FloorPoint.Instance != null ? FloorPoint.Instance.LocalY : 0f;
                int n = Mathf.Min(6, maxSpawnPoints);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    var rel = new Vector3(Mathf.Cos(a) * 1.2f, y + surfaceOffset, Mathf.Sin(a) * 1.2f);
                    AddPointRel(rel);
                }
            }

            // Arrancar todos vacios: se llenan tras initialSpawnDelay, sin exigir que el
            // jugador se aleje (la regla de proximidad solo aplica a la reaparicion).
            foreach (var p in _points)
            {
                p.occupied        = false;
                p.timer           = initialSpawnDelay;
                p.blockUntilClear = false;
            }
        }

        private void ScatterFloorGrid()
        {
            Vector3 c = FloorPoint.Instance.LocalPosition;
            for (float dx = -floorRadius; dx <= floorRadius; dx += floorSpacing)
            for (float dz = -floorRadius; dz <= floorRadius; dz += floorSpacing)
            {
                if (_points.Count >= maxSpawnPoints) return;
                if (dx * dx + dz * dz > floorRadius * floorRadius) continue;
                AddPointRel(new Vector3(c.x + dx, c.y + surfaceOffset, c.z + dz));
            }
        }

        private void AddPoint(Vector3 worldPos)
        {
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;
            AddPointRel(WorldOrigin.Instance.ToRelative(worldPos));
        }

        private void AddPointRel(Vector3 relPos)
        {
            _points.Add(new SpawnPoint { relPos = relPos });
        }

        // ── Loop de reaparicion (server) ──────────────────────────────────────

        private void Update()
        {
            if (!_started) return;
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;

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
            var rarity = rarities != null ? rarities.WeightedPick() : null;
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
            if (!_byNetId.TryGetValue(batteryNetId, out var point)) return; // ya recogida / desconocida
            if (!EntityRegistry.Instance.TryGet(batteryNetId, out var entity)) return;

            Vector3 batteryWorld = entity.transform.position;

            // Validar cercania del solicitante. Host (0) = Camera.main; cliente = su pose.
            Vector3 playerWorld;
            if (clientId == 0)
            {
                if (Camera.main == null) return;
                playerWorld = Camera.main.transform.position;
            }
            else if (!NetworkManager.Instance.TryGetClientWorldPosition(clientId, out playerWorld))
            {
                return; // aun no reportó pose
            }

            if ((playerWorld - batteryWorld).sqrMagnitude > pickupMaxDistance * pickupMaxDistance)
                return; // demasiado lejos: rechazar

            byte rarityIndex = point.rarityIndex;
            var  rarity      = rarities != null ? rarities.ByIndex(rarityIndex) : null;
            float charge     = rarity != null ? rarity.charge : 0f;

            // Despawnear y liberar el punto para reaparicion (respetando proximidad).
            NetworkManager.Instance.ServerDespawn(batteryNetId);
            _byNetId.Remove(batteryNetId);
            point.occupied        = false;
            point.timer           = respawnSeconds;
            point.blockUntilClear = true;

            // Acreditar la carga: el host la aplica local; al cliente se le avisa.
            if (clientId == 0)
                FindFirstObjectByType<Flashlight>()?.AddCharge(charge);
            else
                NetworkManager.Instance.ServerSendBatteryCollected(clientId, rarityIndex, charge);

            Debug.Log($"[Bateries] Pila {batteryNetId} (rareza {rarityIndex}) recogida por cliente {clientId}, +{charge} carga.");
        }
    }
}
