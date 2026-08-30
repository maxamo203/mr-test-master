using System;
using System.Collections.Generic;
using Scanner;
using UnityEngine;
using Gameplay;

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
    // Puntos propios, separados de las pilas: por defecto NO usa tops de muebles
    // (useFurnitureTops=false — eso es territorio de BatterySpawnManager) y su grilla
    // de piso va desfasada medio paso respecto de la de las pilas (ver
    // ScatterFloorGrid), así que estructuralmente casi no coincide con la de
    // BatterySpawnManager. Como red de seguridad adicional, cada candidato también se
    // descarta si cae a menos de minDistanceFromBatteries de un punto de pila YA
    // derivado (ver AddCandidateRel / BatterySpawnManager.IsNear) — depende de que
    // BatterySpawnManager corra primero en el mismo evento OnGameStarted (garantizado
    // por su [DefaultExecutionOrder(-10)]).
    public class CollectibleSpawnManager : MonoBehaviour
    {
        public static CollectibleSpawnManager Instance { get; private set; }

        [Header("Recoleccion")]
        [Tooltip("Distancia maxima (m) a la que el server acepta un pickup del jugador.")]
        [SerializeField] private float pickupMaxDistance = 2.5f;

        [Header("Derivacion de puntos desde el escaneo")]
        [Tooltip("Apagado por defecto: el TOP de un mueble es exactamente el mismo punto " +
                 "que usa BatterySpawnManager para las pilas (mismos cubos, mismo offset), " +
                 "asi que dejarlo prendido filtra casi todos los candidatos contra las pilas " +
                 "y termina colapsando a un unico lugar repetido. Las reliquias viven en el " +
                 "piso (ver scatterOnFloor), que tiene grilla propia.")]
        [SerializeField] private bool  useFurnitureTops = false;
        [SerializeField] private float minFurnitureTopArea = 0.05f;
        [SerializeField] private float surfaceOffset = 0.06f;
        [Tooltip("Ademas de los muebles, esparcir puntos por el piso en una grilla — " +
                 "a diferencia de las pilas, acá viene ACTIVADO por defecto (se busca " +
                 "variedad de ubicaciones, no sólo sobre muebles). La grilla esta desfasada " +
                 "medio paso respecto de la de BatterySpawnManager (ver ScatterFloorGrid) " +
                 "para que no coincida celda a celda con la de las pilas.")]
        [SerializeField] private bool  scatterOnFloor = true;
        [SerializeField] private bool  floorOnlyIfClearAbove = true;
        [SerializeField] private float floorSpacing = 1.5f;
        [SerializeField] private float floorRadius = 3f;
        [SerializeField] private int   maxSpawnPoints = 16;

        [Header("Separación de las pilas")]
        [Tooltip("Distancia mínima (m) a cualquier punto de spawn de PILAS (ver " +
                 "Bateries.BatterySpawnManager) — las reliquias tienen que tener sus propios " +
                 "lugares, no aparecer literalmente donde también puede salir una pila.")]
        [SerializeField] private float minDistanceFromBatteries = 0.6f;

        [Header("Debug")]
        [Tooltip("Muestra un panel en pantalla con el estado del sistema de reliquias. " +
                 "Dejalo apagado en device: el OnGUI tiene costo. Solo para diagnosticar.")]
        [SerializeField] private bool _showDebugHud = false;

        private string _status = "esperando arranque de partida…";

        private readonly List<Vector3> _candidates    = new(); // anchor-relative
        private readonly List<byte>    _variantTypeIds = new();

        private NightConfig _night;
        private bool   _started;
        private bool   _hasActive;
        private uint   _activeNetId;
        private float  _timer;
        private int    _lastPointIndex = -1;
        // Apariciones YA disparadas esta noche (recogidas o no), contra
        // NightConfig.collectibleMaxPerNight — no confundir con NightLoot.Total, que
        // solo cuenta pickups.
        private int    _spawnedCount;

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

            _status = $"{_variantTypeIds.Count} variante(s), {_candidates.Count} puntos derivados del escaneo.";
            Debug.Log($"[Reliquias] {_status}");
        }

        // Corta la noche sin cerrar la sesión (ver Gameplay.NightTransition). La
        // reliquia viva ya la despawnea NetworkManager.ServerResetNight; todo se
        // reconstruye desde cero en el próximo HandleGameStarted.
        public void StopRun()
        {
            _started      = false;
            _hasActive    = false;
            _spawnedCount = 0;
            _candidates.Clear();
            _variantTypeIds.Clear();
            _status = "detenido entre noches.";
        }

        // Chequea que haya al menos una variante registrada en el PrefabRegistry bajo
        // CollectibleReliquia + i (i = 0..31) y que tenga CollectibleEntity. Tolera
        // huecos (por si se borra una variante mas adelante).
        private bool ValidateSetup()
        {
            _variantTypeIds.Clear();

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
                    _variantTypeIds.Add(typeId);
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

        // ── Derivacion de puntos desde el mapa escaneado (mismo algoritmo que las
        // pilas, simplificado a una lista plana: solo hay UNA reliquia viva a la vez,
        // asi que no hace falta estado de ocupacion/timer por punto). ──────────────

        private void BuildCandidatePoints()
        {
            _candidates.Clear();

            if (useFurnitureTops && SceneRegistry.Instance != null)
            {
                foreach (var cube in SceneRegistry.Instance.Cubes)
                {
                    if (cube == null) continue;
                    var t     = cube.transform;
                    var scale = t.lossyScale;
                    if (scale.x * scale.z < minFurnitureTopArea) continue;

                    Vector3 topWorld = t.position + t.up * (scale.y * 0.5f + surfaceOffset);
                    AddPoint(topWorld);
                    if (_candidates.Count >= maxSpawnPoints) break;
                }
            }

            if (scatterOnFloor && _candidates.Count < maxSpawnPoints && FloorPoint.Instance != null)
                ScatterFloorGrid();

            // Fallback: sin muebles ni piso util, unos puntos en anillo alrededor del anchor.
            if (_candidates.Count == 0)
            {
                float y = FloorPoint.Instance != null ? FloorPoint.Instance.LocalY : 0f;
                int n = Mathf.Min(6, maxSpawnPoints);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    var rel = new Vector3(Mathf.Cos(a) * 1.2f, y + surfaceOffset, Mathf.Sin(a) * 1.2f);
                    AddCandidateRel(rel);
                }

                // Caso limite: el anillo entero coincidio con puntos de pilas (escaneo
                // casi vacio) y quedo filtrado a cero. Mejor repetir lugar con una pila
                // que no tener ninguna reliquia en toda la noche.
                if (_candidates.Count == 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * Mathf.PI * 2f / n;
                        _candidates.Add(new Vector3(Mathf.Cos(a) * 1.2f, y + surfaceOffset, Mathf.Sin(a) * 1.2f));
                    }
                }
            }
        }

        // Filtra por distancia a los puntos de PILAS antes de sumar un candidato (ver
        // minDistanceFromBatteries). Si BatterySpawnManager todavia no existe o no
        // corrio (orden de ejecucion, ver su comentario de clase) no filtra nada.
        private void AddCandidateRel(Vector3 relPos)
        {
            var bsm = Bateries.BatterySpawnManager.Instance;
            if (bsm != null && bsm.IsNear(relPos, minDistanceFromBatteries)) return;
            _candidates.Add(relPos);
        }

        private void ScatterFloorGrid()
        {
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;

            // Grilla desfasada medio paso (en X y Z) respecto de la de BatterySpawnManager:
            // con el mismo floorSpacing/floorRadius por defecto, una grilla SIN desfasar
            // cae en las mismas celdas exactas que la de las pilas y el filtro de
            // minDistanceFromBatteries las descarta casi todas — de ahí venía el bug de
            // repetir siempre el mismo (único) punto sobreviviente.
            float half = floorSpacing * 0.5f;
            Vector3 c = FloorPoint.Instance.LocalPosition;
            for (float dx = -floorRadius + half; dx <= floorRadius; dx += floorSpacing)
            for (float dz = -floorRadius + half; dz <= floorRadius; dz += floorSpacing)
            {
                if (_candidates.Count >= maxSpawnPoints) return;
                if (dx * dx + dz * dz > floorRadius * floorRadius) continue;

                var rel = new Vector3(c.x + dx, c.y + surfaceOffset, c.z + dz);

                if (floorOnlyIfClearAbove && HasFurnitureAbove(WorldOrigin.Instance.ToWorld(rel)))
                    continue;

                AddCandidateRel(rel);
            }
        }

        private bool HasFurnitureAbove(Vector3 worldPoint)
        {
            if (SceneRegistry.Instance == null) return false;
            foreach (var cube in SceneRegistry.Instance.Cubes)
            {
                if (cube == null) continue;
                Vector3 local = cube.transform.InverseTransformPoint(worldPoint);
                if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f)
                    return true;
            }
            return false;
        }

        private void AddPoint(Vector3 worldPos)
        {
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;
            AddCandidateRel(WorldOrigin.Instance.ToRelative(worldPos));
        }

        // ── Loop de spawn (server) ─────────────────────────────────────────────

        private void Update()
        {
            if (!_started) return;
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return;
            if (_hasActive) return; // esperando pickup: el timer solo corre DESPUES
            if (_night.collectibleMaxPerNight > 0 && _spawnedCount >= _night.collectibleMaxPerNight) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f) SpawnOne();
        }

        private void SpawnOne()
        {
            if (_candidates.Count == 0 || _variantTypeIds.Count == 0)
            {
                _timer = 5f; // reintentar mas tarde
                return;
            }

            int idx = PickPointIndex();
            Vector3 world = WorldOrigin.Instance.ToWorld(_candidates[idx]);
            byte typeId = _variantTypeIds[UnityEngine.Random.Range(0, _variantTypeIds.Count)];

            try
            {
                _activeNetId = NetworkManager.Instance.ServerSpawn(typeId, world, ownerClientId: 0);
                _hasActive   = true;
                _lastPointIndex = idx;
                _spawnedCount++;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Reliquias] Falló el spawn de la reliquia TypeId={typeId}: {ex.Message}. " +
                               $"Revisá el prefab registrado para ese TypeId.");
                _timer = 5f;
            }
        }

        // Evita repetir el mismo punto dos veces seguidas cuando hay mas de uno.
        private int PickPointIndex()
        {
            if (_candidates.Count <= 1) return 0;
            int idx = UnityEngine.Random.Range(0, _candidates.Count);
            if (idx == _lastPointIndex) idx = (idx + 1) % _candidates.Count;
            return idx;
        }

        // ── Pickup (server) ───────────────────────────────────────────────────

        // Llamado por NetworkManager cuando un cliente pide recoger (o directamente
        // por CollectiblePickupAction en el host). Valida que sea la reliquia activa
        // y la cercania (solo clientes, el host ya validó con su propio apuntado),
        // despawnea, suma al contador compartido y agenda la próxima.
        public void ServerHandlePickup(uint clientId, uint netId)
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
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

            if (clientId != 0 &&
                NetworkManager.Instance.TryGetClientWorldPosition(clientId, out var playerWorld))
            {
                float maxDist = pickupMaxDistance + 0.75f; // margen sobre el apuntado
                if ((playerWorld - entity.transform.position).sqrMagnitude > maxDist * maxDist)
                {
                    Debug.Log($"[Reliquias] Pickup rechazado: cliente {clientId} demasiado lejos de la reliquia {netId}.");
                    return;
                }
            }

            NetworkManager.Instance.ServerDespawn(netId);
            _hasActive = false;

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
                $"Variantes: {_variantTypeIds.Count}   Puntos: {_candidates.Count}   Activa: {_hasActive}   Total: {NightLoot.Total}\n" +
                $"Aparecidas: {_spawnedCount}/{(_night != null && _night.collectibleMaxPerNight > 0 ? _night.collectibleMaxPerNight.ToString() : "∞")}\n" +
                $"Estado: {_status}";
        }
    }
}
