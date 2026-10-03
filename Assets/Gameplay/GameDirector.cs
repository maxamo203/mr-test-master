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

        [Tooltip("Al entrar (chase), a que distancia (m) del marcador, del lado del ambiente, " +
                 "reaparece el Sorken para no arrancar detras de la pared. ~medio metro.")]
        [SerializeField] private float _enterClearance = 0.5f;

        [Tooltip("Profundidad interior que alcanza al terminar de cruzar una ventana. " +
                 "El apoyo de los pies agrega despues su pequena compensacion.")]
        [Min(0f)] [SerializeField] private float _windowPerchClearance = 0.25f;

        [Tooltip("Separacion final respecto del plano interior de la puerta. Solo unos " +
                 "centimetros: la persecucion agrega el resto del desplazamiento.")]
        [Min(0f)] [SerializeField] private float _doorInsideClearance = 0.08f;

        [Tooltip("Distancia inicial al plano exterior de la puerta. Debe dejar al cuerpo " +
                 "completo afuera y visible antes de comenzar a atravesar la pared.")]
        [Min(0f)] [SerializeField] private float _doorOutsideDepth = 1f;

        private enum Phase { Idle, Entering, WindowLanding, Chasing, CoverStarting, Grabbed, Retreating }

        private NightConfig _night;

        // La noche EFECTIVA de la corrida. No es lo mismo que GameSession.SelectedNight:
        // si se entra a SampleScene sin pasar por el menu (tipico en el editor) esa es null
        // y aca queda un NightConfig por defecto. Los sub-directores (ArbmosDirector) leen
        // de aca para no quedarse sin noche en ese caso.
        public NightConfig NocheActual => _night;

        private bool  _running;
        private Phase _phase = Phase.Idle;

        // Reloj de la noche (condicion de victoria). 0 = noche sin limite de tiempo.
        private float _nightDuration;
        private float _nightTimer;
        private float _clockTimer;     // cada cuanto publicamos el reloj (1 Hz)

        // Timers.
        private float _attemptTimer;   // Idle: cuenta al proximo intento
        private float _repel;          // segundos continuos de iluminacion sobre el objetivo
        private float _grace;          // Entering: cuenta a la entrada
        private float _phaseTimer;     // Grabbed / Retreating
        private bool  _coverStartPlayedThisAttempt;
        private bool  _firstAttemptPending;

        // Sorken actual (null entre intentos).
        private SorkenEntity _sorken;
        private uint         _sorkenNetId;
        private MarkerObject _marker;

        // Chase.
        private readonly List<Vector3> _path = new();
        private int   _pathIndex;
        private float _repathTimer;

        // Trayectoria de la ventana al piso. Se guarda al terminar el emerge para que el
        // movimiento sea continuo aunque el marcador o el origen AR se actualicen.
        private Vector3 _windowLandingStart;
        private Vector3 _windowLandingEnd;
        private float _windowLandingDuration;

        // Retirada: direccion (horizontal) hacia la que huye tras ser repelido.
        private Vector3 _retreatDir;

        // El estado de muerte de los jugadores vive en Gameplay.ServerDeaths (compartido
        // con el ArbmosDirector, para no perseguir a un jugador ya muerto).

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
            if (Instance == this) Instance = null;
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

            ServerDeaths.Reset();
            _coverStartPlayedThisAttempt = false;
            _firstAttemptPending = true;
            _phase = Phase.Idle;
            _attemptTimer = _night.initialAttemptDelay;

            // Reloj de la noche: sobrevivir hasta que llegue a 0 la gana.
            _nightDuration = Mathf.Max(0f, _night.nightDurationSeconds);
            _nightTimer    = _nightDuration;
            _clockTimer    = 0f;
            PublicarReloj();
            SorkerNav.Ensure();
            ArbmosDirector.Ensure().StartRun();      // alucinacion de cordura (individual por jugador)
            RitualBookDirector.Ensure().StartRun();  // libro sobre la imagen: eventos de oscuridad
            _running = true;
        }

        // El reloj local (host) + el broadcast a los clientes, que no conocen la
        // NightConfig y por eso no pueden contarlo por su cuenta.
        private void PublicarReloj()
        {
            int restantes = Mathf.CeilToInt(Mathf.Max(0f, _nightTimer));
            int totales   = Mathf.CeilToInt(_nightDuration);
            NightResult.SetReloj(restantes, totales);
            NetworkManager.Instance?.ServerSendNightClock(restantes, totales);
        }

        // Corta la noche sin cerrar la sesión (ver Gameplay.NightTransition). Las
        // entidades ya las despawnea NetworkManager.ServerResetNight; acá sólo
        // soltamos las referencias y frenamos. El próximo OnGameStarted re-inicializa.
        public void StopRun()
        {
            _running      = false;
            _phase        = Phase.Idle;
            _sorken       = null;
            _sorkenNetId  = 0;
            _marker       = null;
            _path.Clear();
            _pathIndex    = 0;
            _repel = _grace = _phaseTimer = 0f;
        }

        private void Update()
        {
            if (!_running) return;
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return;
            float dt = Time.deltaTime;

            // Amanecer: si el reloj llega a 0 la noche está ganada y se corta acá.
            if (_nightDuration > 0f)
            {
                _nightTimer -= dt;

                _clockTimer -= dt;
                if (_clockTimer <= 0f) { _clockTimer = 1f; PublicarReloj(); }

                if (_nightTimer <= 0f)
                {
                    _nightTimer = 0f;
                    PublicarReloj();
                    StopRun();
                    NetworkManager.Instance.ServerNightSurvived();
                    return;
                }
            }

            switch (_phase)
            {
                case Phase.Idle:       TickIdle(dt);       break;
                case Phase.Entering:   TickEntering(dt);   break;
                case Phase.WindowLanding: TickWindowLanding(dt); break;
                case Phase.Chasing:       TickChasing(dt);       break;
                case Phase.CoverStarting: TickCoverStarting(dt); break;
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
            // Sorken desactivado en esta noche (dev/testing): no spawnear nunca.
            if (_night != null && !_night.sorkenActive)
            {
                _attemptTimer = 5f;
                return;
            }

            var markers = SceneRegistry.Instance != null ? SceneRegistry.Instance.Markers : null;
            if (markers == null || markers.Count == 0 || !AnyAlivePlayer())
            {
                Debug.Log($"[GameDirector] No spawn: markers={(markers != null ? markers.Count : 0)} " +
                          $"alivePlayers={CountAlivePlayers()} (dead={ServerDeaths.Count}). Reintento en 2s.");
                _attemptTimer = 2f;   // sin puntos o sin jugadores vivos: reintentar pronto
                return;
            }

            int nightIndex = GameSession.Instance != null ? GameSession.Instance.NightIndex : -1;
            bool forceFirstDoor = ShouldForceDoorOnFirstAttempt(nightIndex, _firstAttemptPending);
            _marker = forceFirstDoor ? RandomDoorMarker(markers) : null;
            if (_marker == null)
            {
                if (forceFirstDoor)
                    Debug.LogWarning("[GameDirector] Noche 1: no hay marcador de puerta; " +
                                     "el primer intento usara cualquier entrada disponible.");
                _marker = markers[UnityEngine.Random.Range(0, markers.Count)];
            }
            if (_marker == null) { _attemptTimer = 1f; return; }

            // Spawn ya a la altura del piso (EmergePosition con _sorken null usa depth 0);
            // luego lo reposicionamos aplicando el EmergeDepth del modelo.
            _sorkenNetId = NetworkManager.Instance.ServerSpawn(EntityTypeIds.Sorken, EmergePosition(), 0);
            _sorken = GetSorken(_sorkenNetId);
            if (_sorken == null) { _attemptTimer = 2f; return; }

            // US-4.1: que tipo de punto es (puerta/ventana/...), para que suene distinto.
            // Se resuelve contra el AudioCatalog y viaja como un byte junto al estado, asi
            // los clientes tambien lo saben (ver SorkenEntity.MarkerTypeIndex).
            var catAudio = AudioManager.Catalogo;
            _sorken.SetMarkerTypeIndex(catAudio != null
                ? catAudio.IndiceEntrada(_marker.KindId)
                : AudioCatalog.IndiceDesconocido);

            _sorken.SetPositionDirectly(EmergePosition());
            _sorken.FaceDirection(_marker.transform.forward); // mira hacia adentro (normal)
            // La entrada tiene dos etapas: primero se mantiene en el punto para
            // que el jugador pueda detectarlo y repelerlo; la animacion de emergencia
            // solo arranca en el tramo final configurado de la ventana.
            _sorken.SetState(SorkenState.Idle);
            _coverStartPlayedThisAttempt = false;
            _firstAttemptPending = false;

            _repel = 0f; _grace = 0f; _windowLandingDuration = 0f;
            _phase = Phase.Entering;
            Debug.Log($"[GameDirector] Emerge netId={_sorkenNetId} en marcador {_marker.name}.");
        }

        public static bool ShouldForceDoorOnFirstAttempt(int nightIndex, bool firstAttemptPending) =>
            nightIndex == 0 && firstAttemptPending;

        public static bool IsDoorKind(string kindId)
        {
            string kind = kindId != null ? kindId.ToLowerInvariant() : string.Empty;
            return kind.Contains("door") || kind.Contains("puerta");
        }

        private static MarkerObject RandomDoorMarker(IReadOnlyList<MarkerObject> markers)
        {
            int doorCount = 0;
            for (int i = 0; i < markers.Count; i++)
                if (markers[i] != null && IsDoorKind(markers[i].KindId)) doorCount++;

            if (doorCount == 0) return null;
            int selected = UnityEngine.Random.Range(0, doorCount);
            for (int i = 0; i < markers.Count; i++)
            {
                MarkerObject marker = markers[i];
                if (marker == null || !IsDoorKind(marker.KindId)) continue;
                if (selected-- == 0) return marker;
            }
            return null;
        }

        // --- Entering ---
        private void TickEntering(float dt)
        {
            if (_sorken == null || _marker == null) { EndAttempt(); return; }

            // Repeler: iluminar el PUNTO del marcador (cara de la pared) lo ahuyenta.
            if (AnyIlluminating(_marker.transform.position)) _repel += dt; else _repel = 0f;
            if (_repel >= _night.entryRepelSeconds) { Retreat(); return; }

            // Si NO se lo repele durante la ventana, entra a perseguir. Reservamos
            // el tramo final para la animacion de emergencia, para que no se ejecute
            // completa apenas aparece en el marcador.
            _grace += dt;
            bool window = IsWindowMarker();
            float animationDuration = window
                ? Mathf.Max(0.2f, _sorken.WindowEntryDuration)
                : Mathf.Max(0.2f, _night.entryAnimationSeconds);
            float animationStart = Mathf.Max(0f, _night.entryGraceSeconds - animationDuration);
            if (_grace >= animationStart &&
                _sorken.State != SorkenState.EmergingDoor &&
                _sorken.State != SorkenState.EmergingWindow)
                _sorken.SetState(EmergingStateForMarker());

            Vector3 emergePosition = EmergePosition();
            if (_grace >= animationStart)
            {
                float animationTime = Mathf.Clamp01((_grace - animationStart) / animationDuration);
                if (window)
                {
                    emergePosition = WindowEntryPosition(
                        emergePosition,
                        _marker.transform.forward,
                        animationTime,
                        _sorken.EmergeDepth + _windowPerchClearance,
                        _sorken.WindowEntryRootOffset(animationTime));
                }
                else
                {
                    // La traslacion ocurre junto con el clip: el espectador ve al cuerpo
                    // atravesar el plano de la pared. Termina apenas dentro, sin el salto
                    // posterior de medio metro que ocultaba la animacion.
                    emergePosition = DoorEntryPosition(
                        emergePosition,
                        _marker.transform.forward,
                        animationTime,
                        _doorOutsideDepth + _doorInsideClearance);
                }
            }
            _sorken.SetPositionDirectly(emergePosition);
            _sorken.FaceDirection(_marker.transform.forward);

            // Una ventana puede necesitar un clip mas largo que la gracia configurada.
            // No iniciamos la caida hasta que termino el traspaso y el segundo pie esta
            // apoyado; asi el cambio de clip no lo despega del marco a mitad de gesto.
            float entryEnd = window
                ? Mathf.Max(_night.entryGraceSeconds, animationStart + animationDuration)
                : _night.entryGraceSeconds;
            if (_grace >= entryEnd) EnterChase();
        }

        private void EnterChase()
        {
            Vector3 floorEntry = ChaseEntryPosition();
            if (IsWindowMarker() && _sorken.Position.y > floorEntry.y + 0.05f)
            {
                _windowLandingStart = _sorken.Position;
                _windowLandingEnd = floorEntry;
                _phaseTimer = 0f;
                _sorken.ConfigureWindowLanding(_windowLandingStart.y - _windowLandingEnd.y);
                _windowLandingDuration = Mathf.Max(0.2f, _sorken.WindowLandingDuration);
                _sorken.SetState(SorkenState.WindowLanding);
                _phase = Phase.WindowLanding;
                Debug.Log($"[GameDirector] El Sorken salio de la ventana: descenso de " +
                          $"{Mathf.Max(0f, _windowLandingStart.y - _windowLandingEnd.y):0.00} m " +
                          $"en {_windowLandingDuration:0.00} s.");
                return;
            }

            BeginChaseAt(floorEntry);
        }

        private void TickWindowLanding(float dt)
        {
            if (_sorken == null || _marker == null) { EndAttempt(); return; }

            float duration = _windowLandingDuration > 0f
                ? _windowLandingDuration
                : Mathf.Max(0.2f, _sorken.WindowLandingDuration);
            _phaseTimer = Mathf.Min(duration, _phaseTimer + dt);
            float t = _phaseTimer / duration;

            _sorken.SetPositionDirectly(WindowLandingPosition(
                _windowLandingStart, _windowLandingEnd, t));
            _sorken.FaceDirection(_marker.transform.forward);

            if (_phaseTimer >= duration)
                BeginChaseAt(_windowLandingEnd);
        }

        private void BeginChaseAt(Vector3 position)
        {
            _sorken.SetPositionDirectly(position);
            _sorken.SetState(SorkenState.Chasing);
            _repel = 0f; _path.Clear(); _pathIndex = 0; _repathTimer = 0f;
            _phase = Phase.Chasing;
            Debug.Log("[GameDirector] El Sorken ENTRO: chase.");
        }

        // Baja de forma controlada: conserva un instante el apoyo de la ventana, desplaza
        // el peso hacia adentro y desacelera antes de tocar el piso. El tramo final queda
        // reservado para recuperar la postura y mezclar con la caminata de persecucion.
        public static Vector3 WindowLandingPosition(Vector3 start, Vector3 end, float normalizedTime)
        {
            const float descentStart = 0.05f;
            const float touchdown = 0.82f;
            float t = Mathf.Clamp01(normalizedTime);
            float travel = Mathf.Clamp01(t / touchdown);
            float descent = Mathf.InverseLerp(descentStart, touchdown, t);
            float horizontal = Mathf.SmoothStep(0f, 1f, travel);
            float vertical = Mathf.SmoothStep(0f, 1f, descent);
            Vector3 position = Vector3.Lerp(start, end, horizontal);
            position.y = Mathf.Lerp(start.y, end.y, vertical);
            if (t >= touchdown) position.y = end.y;
            return position;
        }

        public static Vector3 DoorEntryPosition(
            Vector3 outsidePosition, Vector3 markerForward, float normalizedTime,
            float crossingDistance)
        {
            markerForward.y = 0f;
            if (markerForward.sqrMagnitude <= 1e-6f) return outsidePosition;

            // Deja leer el inicio y el final del gesto: el root empieza a cruzar al 10%
            // del clip y ya esta asentado al 90%, sin un segundo desplazamiento separado.
            float t = Mathf.InverseLerp(0.1f, 0.9f, Mathf.Clamp01(normalizedTime));
            float crossing = Mathf.SmoothStep(0f, 1f, t) * Mathf.Max(0f, crossingDistance);
            return outsidePosition + markerForward.normalized * crossing;
        }

        public static Vector3 DoorEmergePosition(
            Vector3 markerPosition, Vector3 markerForward, float depth, float floorY)
        {
            Vector3 position = markerPosition - markerForward * Mathf.Max(0f, depth);
            position.y = floorY;
            return position;
        }

        public static Vector3 WindowEntryPosition(
            Vector3 outsidePosition, Vector3 markerForward, float normalizedTime,
            float crossingDistance, float signedSupportOffset)
        {
            markerForward.y = 0f;
            if (markerForward.sqrMagnitude <= 1e-6f) return outsidePosition;

            // El clip anima el cuerpo, pero no puede conocer el grosor ni la orientacion
            // de la pared real. El root cruza desde el punto exterior hasta una pequena
            // profundidad interior. Encima se suma la compensacion medida en Blender que
            // mantiene quieto el pie que esta apoyado en cada tramo.
            float crossing = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(normalizedTime)) *
                             Mathf.Max(0f, crossingDistance);
            return outsidePosition + markerForward.normalized *
                   (crossing + signedSupportOffset);
        }

        // --- Chasing ---
private void TickChasing(float dt)
        {
            if (_sorken == null) { EndAttempt(); return; }
            if (!NearestAlivePlayer(_sorken.Position, out _, out var tpos)) { Retreat(); return; }

            // El agarre es la acción de máxima prioridad: gana aun si el jugador lo
            // ilumina cuando ya está dentro del rango.
            if (HorizDist(_sorken.Position, tpos) <= _night.grabRange) { Grab(); return; }

            bool iluminado = IsSorkenDirectlyLit();
            if (iluminado)
            {
                // Ante una nueva exposición, primero se detiene para cubrirse. La
                // entrada sólo ocurre una vez por exposición continua.
                if (!_coverStartPlayedThisAttempt)
                {
                    BeginCoverStart();
                    return;
                }

                // El gesto inicial solo se reproduce una vez por ingreso. Si la luz
                // vuelve despues, retoma directamente la marcha cubierta.
                if (_sorken.State != SorkenState.CoverWalking)
                    _sorken.SetState(SorkenState.CoverWalking);

                // Tras completar la transición, puede seguir cubierto un rato antes de
                // que la linterna sostenida lo repela por completo.
                _repel += dt;
                if (_repel >= _night.chaseRepelSeconds) { Retreat(); return; }
            }
            else
            {
                _repel = 0f;
                if (_sorken.State == SorkenState.CoverWalking)
                    _sorken.SetState(SorkenState.Chasing);
            }

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
            float speed = _night.sorkenChaseSpeed *
                          (_sorken.State == SorkenState.CoverWalking
                              ? _night.sorkenCoverWalkSpeedMultiplier
                              : 1f);
            _sorken.MoveTo(step, speed * EntitySpeedSettings.Multiplier *
                           _sorken.MovementMultiplier, dt);
            if (_pathIndex < _path.Count && HorizDist(_sorken.Position, _path[_pathIndex]) <= 0.2f) _pathIndex++;
        }

private void BeginCoverStart()
        {
            _coverStartPlayedThisAttempt = true;
            _sorken.SetState(SorkenState.CoverStarting);
            _phaseTimer = 0f;
            _phase = Phase.CoverStarting;
        }

        private void TickCoverStarting(float dt)
        {
            if (_sorken == null) { EndAttempt(); return; }
            if (!NearestAlivePlayer(_sorken.Position, out _, out var target)) { Retreat(); return; }

            // El agarre conserva la máxima prioridad durante la transición.
            if (HorizDist(_sorken.Position, target) <= _night.grabRange) { Grab(); return; }

            // Si la luz deja de tocarlo antes de completar el gesto, retoma la
            // persecución normal y el próximo haz iniciará una nueva transición.
            if (!IsSorkenDirectlyLit())
            {
                _sorken.SetState(SorkenState.Chasing);
                _phase = Phase.Chasing;
                return;
            }

            _phaseTimer += dt;
            if (_phaseTimer >= _night.sorkenCoverStartSeconds)
            {
                _sorken.SetState(SorkenState.CoverWalking);
                _repel = 0f;
                _phase = Phase.Chasing;
            }
        }

        private bool IsSorkenDirectlyLit()
        {
            if (_sorken == null || !PlayerLights.TryConoReal(out var angle, out var range)) return false;
            // Centro del torso + radio del cuerpo: evita que el jugador deba apuntar
            // exactamente a un punto infinitesimal y usa el cono real de la linterna.
            return PlayerLights.AnyIlluminating(_sorken.Position + Vector3.up * 1f,
                                                angle, range, 0.4f,
                                                FlashlightMode.Bright);
        }

        private SorkenState EmergingStateForMarker()
        {
            return IsWindowMarker()
                ? SorkenState.EmergingWindow
                : SorkenState.EmergingDoor;
        }

        private bool IsWindowMarker()
        {
            string kind = _marker != null && _marker.KindId != null
                ? _marker.KindId.ToLowerInvariant()
                : string.Empty;
            return kind.Contains("window") || kind.Contains("ventana");
        }


        private void Grab()
        {
            // Fijar la animación antes de notificar la muerte: si era el último jugador,
            // la notificación detiene la partida y limpia la referencia al Sorken.
            _sorken.SetState(SorkenState.Grabbing);
            _phaseTimer = 0f;
            _phase = Phase.Grabbed;

            if (NearestAlivePlayer(_sorken.Position, out var victim, out _))
            {
                Debug.Log($"[GameDirector] GRAB: jugador {victim} atrapado.");
                KillPlayer(victim);
            }
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
                _sorken.MoveTo(_sorken.Position + _retreatDir, _night.sorkenRetreatSpeed * EntitySpeedSettings.Multiplier, dt);
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
            ServerDeaths.Kill(clientId, _sorken != null ? _sorken.transform : null);   // marca + avisa una sola vez
        }

        // --- Jugadores vivos ---
        private bool AnyAlivePlayer()
        {
            var net = NetworkManager.Instance;
            if (Camera.main != null && ServerDeaths.IsAlive(0)) return true;
            foreach (var cid in net.ConnectedClients)
                if (ServerDeaths.IsAlive(cid) && net.TryGetClientWorldPosition(cid, out _)) return true;
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

            if (Camera.main != null && ServerDeaths.IsAlive(0))
            {
                best  = (Camera.main.transform.position - from).sqrMagnitude;
                pos   = Camera.main.transform.position;
                clientId = 0; found = true;
            }
            foreach (var cid in net.ConnectedClients)
            {
                if (ServerDeaths.IsDead(cid)) continue;
                if (!net.TryGetClientWorldPosition(cid, out var p)) continue;
                float d = (p - from).sqrMagnitude;
                if (d < best) { best = d; pos = p; clientId = cid; found = true; }
            }
            return found;
        }

        private IEnumerable<Vector3> AlivePlayerPositions()
        {
            var net = NetworkManager.Instance;
            if (Camera.main != null && ServerDeaths.IsAlive(0)) yield return Camera.main.transform.position;
            foreach (var cid in net.ConnectedClients)
                if (ServerDeaths.IsAlive(cid) && net.TryGetClientWorldPosition(cid, out var p)) yield return p;
        }

        // --- Repel: hay algun jugador iluminando el objetivo con su linterna? ---
        // El test vive en Gameplay.PlayerLights, compartido con el libro ritual.
        //
        // OJO: el cono de la NightConfig (30° / 8 m) es MUCHO mas ancho que el haz real de
        // la linterna (7.2° / 4.2 m en el prefab), asi que aca se repele apuntando bastante
        // afuera del Sorken. El libro ya usa el cono real; esto sigue con el de la noche
        // para no cambiar la dificultad del Sorken sin querer — cuando se decida, es
        // pasarle PlayerLights.TryConoReal como hace RitualBookDirector.
        private bool AnyIlluminating(Vector3 target) =>
            PlayerLights.AnyIlluminating(target, _night.flashlightConeAngleDeg,
                                         _night.flashlightRange, 0f,
                                         FlashlightMode.Bright);

        private static float HorizDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        private SorkenEntity GetSorken(uint netId)
        {
            if (EntityRegistry.Instance != null && EntityRegistry.Instance.TryGet(netId, out var ne))
                return ne.GetComponent<SorkenEntity>();
            return null;
        }

        // Posicion de emerge: el punto del marcador (a SU altura — ventana/puerta) empujado
        // hacia adentro de la pared segun EmergeDepth (empuje horizontal, no toca la Y),
        // para que se vea medio cuerpo. Al ENTRAR (chase) se reposiciona (ChaseEntryPosition).
        private Vector3 EmergePosition()
        {
            float depth = _sorken != null ? _sorken.EmergeDepth : 0f;
            // Una puerta nace y cruza a ras del piso. Antes heredaba la altura del
            // marcador y al terminar saltaba verticalmente hasta ChaseEntryPosition.
            if (!IsWindowMarker())
                return DoorEmergePosition(
                    _marker.transform.position, _marker.transform.forward,
                    _doorOutsideDepth, FloorWorldY());

            return _marker.transform.position - _marker.transform.forward * depth;
        }

        // Posicion exacta donde comienza el chase. La puerta termina apenas separada de
        // la pared; la ventana conserva un margen mayor para completar el aterrizaje.
        private Vector3 ChaseEntryPosition()
        {
            var mp  = _marker.transform.position;
            var fwd = _marker.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-6f) fwd.Normalize();
            float clearance = IsWindowMarker() ? _enterClearance : _doorInsideClearance;
            var pos = mp + fwd * clearance;
            pos.y = FloorWorldY();
            return pos;
        }

        // Y del piso en world (FloorPoint). Si no hay piso, la Y actual del Sorken.
        private float FloorWorldY()
        {
            var wo = WorldOrigin.Instance;
            if (FloorPoint.Instance != null && wo != null)
                return wo.ToWorld(FloorPoint.Instance.LocalPosition).y;

            // En el editor y en algunos escaneos antiguos puede no existir FloorPoint.
            // La base de la pared asociada al marcador sigue siendo una referencia de
            // piso valida. Usar la altura actual del Sorken (la ventana) hacia que el
            // director creyera que ya habia aterrizado y saltara esta transicion.
            if (_marker != null && _marker.Wall != null)
            {
                if (wo != null) return wo.ToWorld(_marker.Wall.ALocal).y;
                return _marker.Wall.transform.position.y;
            }

            return wo != null ? wo.transform.position.y : 0f;
        }

        private int CountAlivePlayers()
        {
            int n = 0;
            foreach (var _ in AlivePlayerPositions()) n++;
            return n;
        }

        // --- HUD de diagnostico (solo host) ---
        // El dibujo vive en DebugHud/DebugDirectorUI (solo development builds);
        // aca solo se arma el snapshot legible. Null si el director no corre aca.
        public string DebugSnapshot()
        {
            if (!_debugHud || !_running) return null;
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer) return null;

            int markers = SceneRegistry.Instance != null ? SceneRegistry.Instance.Markers.Count : 0;
            string libro = RitualBookDirector.Instance != null ? RitualBookDirector.Instance.DebugLine() : null;
            string veleth = VelethDirector.Instance != null ? VelethDirector.Instance.DebugLine() : null;
            return
                $"[GameDirector]  phase={_phase}\n" +
                $"attemptTimer={_attemptTimer:F1}  repel={_repel:F1}  grace={_grace:F1}\n" +
                $"sorken={(_sorken != null ? _sorken.State.ToString() : "null")}  netId={_sorkenNetId}\n" +
                $"markers={markers}  alivePlayers={CountAlivePlayers()}  dead={ServerDeaths.Count}" +
                (libro != null ? $"\n{libro}" : "") +
                (veleth != null ? $"\n{veleth}" : "");
        }
    }
}
