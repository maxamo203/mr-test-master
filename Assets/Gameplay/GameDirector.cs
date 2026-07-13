using System.Collections.Generic;
using UnityEngine;
using Scanner;

namespace Gameplay
{
    // Director de la partida (SERVER-AUTHORITATIVE; solo corre en el host). Orquesta los
    // intentos de entrada del Sorken en los marcadores, el chase y el grab/muerte. Lee
    // toda la dificultad de la NightConfig (GameSession). Los clientes solo ven el
    // resultado (Sorken replicado + estado de animacion + mensaje de muerte).
    //
    // Wiring: poner en un GameObject de SampleScene (junto a SanitySystem/BatterySpawnManager).
    public class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }

        [Tooltip("Panel de diagnostico en pantalla (solo host): fase, timers, jugadores.")]
        [SerializeField] private bool _debugHud = true;

        [Tooltip("Modo practica (testing): el grab NO mata, asi el ciclo emerge->chase->" +
                 "grab->respawn no se corta por la muerte del unico jugador.")]
        [SerializeField] private bool _practiceMode = false;

        private enum Phase { Idle, Entering, Chasing, Grabbed, Retreating }

        private NightConfig _night;
        private bool  _running;
        private Phase _phase = Phase.Idle;

        // Timers.
        private float _attemptTimer;   // Idle: cuenta al proximo intento
        private float _repel;          // segundos continuos de iluminacion sobre el objetivo
        private float _grace;          // Entering: cuenta a la entrada
        private float _phaseTimer;     // Grabbed / Retreating

        // Sorken actual (null entre intentos).
        private SorkenEntity _sorken;
        private uint         _sorkenNetId;
        private MarkerObject _marker;

        // Chase.
        private readonly List<Vector3> _path = new();
        private int   _pathIndex;
        private float _repathTimer;

        // Retirada: direccion (horizontal) hacia la que huye tras ser repelido.
        private Vector3 _retreatDir;

        // Jugadores muertos por clientId (0 = host).
        private readonly HashSet<uint> _dead = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnGameStarted += HandleGameStarted;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnGameStarted -= HandleGameStarted;
        }

        private void HandleGameStarted()
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;

            _night = GameSession.Instance != null ? GameSession.Instance.SelectedNight : null;
            if (_night == null)
            {
                Debug.LogWarning("[GameDirector] Sin NightConfig; uso valores por defecto.");
                _night = ScriptableObject.CreateInstance<NightConfig>();
            }

            _dead.Clear();
            _phase = Phase.Idle;
            _attemptTimer = _night.initialAttemptDelay;
            SorkerNav.Ensure();
            _running = true;
        }

        private void Update()
        {
            if (!_running) return;
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            float dt = Time.deltaTime;

            switch (_phase)
            {
                case Phase.Idle:       TickIdle(dt);       break;
                case Phase.Entering:   TickEntering(dt);   break;
                case Phase.Chasing:    TickChasing(dt);    break;
                case Phase.Grabbed:    TickGrabbed(dt);    break;
                case Phase.Retreating: TickRetreating(dt); break;
            }
        }

        // --- Idle ---
        private void TickIdle(float dt)
        {
            _attemptTimer -= dt;
            if (_attemptTimer <= 0f) StartAttempt();
        }

        private void StartAttempt()
        {
            var markers = SceneRegistry.Instance != null ? SceneRegistry.Instance.Markers : null;
            if (markers == null || markers.Count == 0 || !AnyAlivePlayer())
            {
                Debug.Log($"[GameDirector] No spawn: markers={(markers != null ? markers.Count : 0)} " +
                          $"alivePlayers={CountAlivePlayers()} (dead={_dead.Count}). Reintento en 2s.");
                _attemptTimer = 2f;   // sin puntos o sin jugadores vivos: reintentar pronto
                return;
            }

            _marker = markers[UnityEngine.Random.Range(0, markers.Count)];
            if (_marker == null) { _attemptTimer = 1f; return; }

            // Spawn ya a la altura del piso (EmergePosition con _sorken null usa depth 0);
            // luego lo reposicionamos aplicando el EmergeDepth del modelo.
            _sorkenNetId = NetworkManager.Instance.ServerSpawn(EntityTypeIds.Sorken, EmergePosition(), 0);
            _sorken = GetSorken(_sorkenNetId);
            if (_sorken == null) { _attemptTimer = 2f; return; }

            _sorken.SetPositionDirectly(EmergePosition());
            _sorken.FaceDirection(_marker.transform.forward); // mira hacia adentro (normal)
            _sorken.SetState(SorkenState.Emerging);

            _repel = 0f; _grace = 0f;
            _phase = Phase.Entering;
            Debug.Log($"[GameDirector] Emerge netId={_sorkenNetId} en marcador {_marker.name}.");
        }

        // --- Entering ---
        private void TickEntering(float dt)
        {
            if (_sorken == null || _marker == null) { EndAttempt(); return; }

            _sorken.SetPositionDirectly(EmergePosition());
            _sorken.FaceDirection(_marker.transform.forward);

            // Repeler: iluminar el PUNTO del marcador (cara de la pared) lo ahuyenta.
            if (AnyIlluminating(_marker.transform.position)) _repel += dt; else _repel = 0f;
            if (_repel >= _night.entryRepelSeconds) { Retreat(); return; }

            // Si NO se lo repele dentro del grace, ENTRA (persigue). No se va solo.
            _grace += dt;
            if (_grace >= _night.entryGraceSeconds) EnterChase();
        }

        private void EnterChase()
        {
            DropToFloor();                       // baja de la altura de la ventana al piso
            _sorken.SetState(SorkenState.Chasing);
            _repel = 0f; _path.Clear(); _pathIndex = 0; _repathTimer = 0f;
            _phase = Phase.Chasing;
            Debug.Log("[GameDirector] El Sorken ENTRO: chase.");
        }

        // --- Chasing ---
        private void TickChasing(float dt)
        {
            if (_sorken == null) { EndAttempt(); return; }
            if (!NearestAlivePlayer(_sorken.Position, out _, out var tpos)) { Retreat(); return; }

            // Distancia HORIZONTAL: el jugador es la camara AR (~1.5m de alto) y el
            // Sorken esta en el piso; con distancia 3D el grab nunca dispararia.
            if (HorizDist(_sorken.Position, tpos) <= _night.grabRange) { Grab(); return; }

            if (AnyIlluminating(_sorken.Position)) _repel += dt; else _repel = 0f;
            if (_repel >= _night.chaseRepelSeconds) { Retreat(); return; }

            // Path-following con SorkerNav (A* respeta paredes/puertas/cubos).
            _repathTimer -= dt;
            if (_repathTimer <= 0f || _pathIndex >= _path.Count)
            {
                _repathTimer = 0.3f;
                if (SorkerNav.Instance != null && SorkerNav.Instance.TryGetPath(_sorken.Position, tpos, _path))
                    _pathIndex = 0;
                else
                    _path.Clear();
            }
            Vector3 step = _pathIndex < _path.Count ? _path[_pathIndex] : tpos;
            _sorken.MoveTo(step, _night.sorkenChaseSpeed, dt);
            if (_pathIndex < _path.Count && HorizDist(_sorken.Position, _path[_pathIndex]) <= 0.2f) _pathIndex++;
        }

        private void Grab()
        {
            if (NearestAlivePlayer(_sorken.Position, out var victim, out _))
            {
                Debug.Log($"[GameDirector] GRAB: jugador {victim} atrapado.");
                KillPlayer(victim);
            }
            _sorken.SetState(SorkenState.Grabbing);
            _phaseTimer = 0f;
            _phase = Phase.Grabbed;
        }

        private void TickGrabbed(float dt)
        {
            _phaseTimer += dt;
            if (_phaseTimer >= _night.grabHoldSeconds) EndAttempt();
        }

        // --- Retreat ---
        private void Retreat()
        {
            _retreatDir = Vector3.zero;
            if (_sorken != null)
            {
                _sorken.SetState(SorkenState.Retreating);
                // Huye hacia atras (contrario a su facing): en emerge = de vuelta por la
                // pared; en chase = alejandose del jugador.
                var f = _sorken.transform.forward; f.y = 0f;
                if (f.sqrMagnitude > 1e-4f) _retreatDir = -f.normalized;
            }
            _phaseTimer = 0f;
            _phase = Phase.Retreating;
        }

        private void TickRetreating(float dt)
        {
            _phaseTimer += dt;
            // Se da vuelta y sale corriendo en _retreatDir mientras dura la retirada.
            if (_sorken != null && _retreatDir != Vector3.zero)
                _sorken.MoveTo(_sorken.Position + _retreatDir, _night.sorkenRetreatSpeed, dt);
            if (_phaseTimer >= _night.retreatSeconds) EndAttempt();
        }

        // Cierra el intento: despawnea el Sorken y agenda el proximo.
        private void EndAttempt()
        {
            DespawnSorken();
            _attemptTimer = UnityEngine.Random.Range(_night.attemptIntervalMin, _night.attemptIntervalMax);
            _phase = Phase.Idle;
            Debug.Log($"[GameDirector] Fin del intento. Proximo en {_attemptTimer:F1}s.");
        }

        private void DespawnSorken()
        {
            if (_sorkenNetId != 0 && NetworkManager.Instance != null)
                NetworkManager.Instance.ServerDespawn(_sorkenNetId);
            _sorken = null; _sorkenNetId = 0; _marker = null;
        }

        // --- Muerte ---
        private void KillPlayer(uint clientId)
        {
            if (_practiceMode) { Debug.Log($"[GameDirector] (practica) grab {clientId}, no muere."); return; }
            if (!_dead.Add(clientId)) return;            // ya estaba muerto
            if (clientId == 0) LocalDeath.Ensure().Die();                       // host
            else               NetworkManager.Instance.ServerSendPlayerDied(clientId); // cliente
        }

        // --- Jugadores vivos ---
        private bool AnyAlivePlayer()
        {
            var net = NetworkManager.Instance;
            if (Camera.main != null && !_dead.Contains(0)) return true;
            foreach (var cid in net.ConnectedClients)
                if (!_dead.Contains(cid) && net.TryGetClientWorldPosition(cid, out _)) return true;
            return false;
        }

        private bool AnyAlivePlayerWithin(Vector3 world, float dist)
        {
            // Horizontal: proximidad al punto sin contar la altura de la camara AR.
            foreach (var p in AlivePlayerPositions())
                if (HorizDist(p, world) <= dist) return true;
            return false;
        }

        private bool NearestAlivePlayer(Vector3 from, out uint clientId, out Vector3 pos)
        {
            clientId = 0; pos = Vector3.zero;
            float best = float.MaxValue; bool found = false;
            var net = NetworkManager.Instance;

            if (Camera.main != null && !_dead.Contains(0))
            {
                best  = (Camera.main.transform.position - from).sqrMagnitude;
                pos   = Camera.main.transform.position;
                clientId = 0; found = true;
            }
            foreach (var cid in net.ConnectedClients)
            {
                if (_dead.Contains(cid)) continue;
                if (!net.TryGetClientWorldPosition(cid, out var p)) continue;
                float d = (p - from).sqrMagnitude;
                if (d < best) { best = d; pos = p; clientId = cid; found = true; }
            }
            return found;
        }

        private IEnumerable<Vector3> AlivePlayerPositions()
        {
            var net = NetworkManager.Instance;
            if (Camera.main != null && !_dead.Contains(0)) yield return Camera.main.transform.position;
            foreach (var cid in net.ConnectedClients)
                if (!_dead.Contains(cid) && net.TryGetClientWorldPosition(cid, out var p)) yield return p;
        }

        // --- Repel: hay algun jugador iluminando el objetivo con su linterna? ---
        private bool AnyIlluminating(Vector3 target)
        {
            var net = NetworkManager.Instance;
            float ang = _night.flashlightConeAngleDeg, range = _night.flashlightRange;

            if (Camera.main != null && !_dead.Contains(0) && net.LocalFlashlightOn() &&
                InCone(Camera.main.transform.position, Camera.main.transform.forward, target, ang, range))
                return true;

            foreach (var cid in net.ConnectedClients)
            {
                if (_dead.Contains(cid)) continue;
                if (!net.TryGetClientFlashlightOn(cid, out var on) || !on) continue;
                if (!net.TryGetClientWorldPosition(cid, out var pos)) continue;
                if (!net.TryGetClientForward(cid, out var fwd)) continue;
                if (InCone(pos, fwd, target, ang, range)) return true;
            }
            return false;
        }

        private static bool InCone(Vector3 pos, Vector3 forward, Vector3 target, float angleDeg, float range)
        {
            var to = target - pos;
            float d = to.magnitude;
            if (d > range) return false;
            if (d < 1e-3f) return true;
            float cos = Vector3.Dot(to / d, forward.normalized);
            return cos >= Mathf.Cos(angleDeg * Mathf.Deg2Rad);
        }

        private static float HorizDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        private SorkenEntity GetSorken(uint netId)
        {
            if (EntityRegistry.Instance != null && EntityRegistry.Instance.TryGet(netId, out var ne))
                return ne.GetComponent<SorkenEntity>();
            return null;
        }

        // Posicion de emerge: el punto del marcador (a SU altura — ventana/puerta) empujado
        // hacia adentro de la pared segun EmergeDepth (empuje horizontal, no toca la Y),
        // para que se vea medio cuerpo. Al ENTRAR (chase) el Sorken baja al piso (DropToFloor).
        private Vector3 EmergePosition()
        {
            float depth = _sorken != null ? _sorken.EmergeDepth : 0f;
            return _marker.transform.position - _marker.transform.forward * depth;
        }

        // Baja al Sorken a la altura del piso (FloorPoint), manteniendo su XZ. Se llama al
        // entrar en chase: deja de estar a la altura de la ventana y camina por el piso.
        private void DropToFloor()
        {
            if (_sorken == null) return;
            var wo = WorldOrigin.Instance;
            var p  = _sorken.Position;
            if (FloorPoint.Instance != null && wo != null)
                p.y = wo.ToWorld(FloorPoint.Instance.LocalPosition).y;
            _sorken.SetPositionDirectly(p);
        }

        private int CountAlivePlayers()
        {
            int n = 0;
            foreach (var _ in AlivePlayerPositions()) n++;
            return n;
        }

        // --- HUD de diagnostico (solo host) ---
        private GUIStyle _hudStyle;
        private void OnGUI()
        {
            if (!_debugHud || !_running) return;
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;

            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.UpperLeft };
                _hudStyle.normal.textColor = Color.white;
            }

            int markers = SceneRegistry.Instance != null ? SceneRegistry.Instance.Markers.Count : 0;
            string txt =
                $"[GameDirector]  phase={_phase}\n" +
                $"attemptTimer={_attemptTimer:F1}  repel={_repel:F1}  grace={_grace:F1}\n" +
                $"sorken={(_sorken != null ? _sorken.State.ToString() : "null")}  netId={_sorkenNetId}\n" +
                $"markers={markers}  alivePlayers={CountAlivePlayers()}  dead={_dead.Count}";
            GUI.Box(new Rect(10, 300, 560, 110), txt, _hudStyle);
        }
    }
}
