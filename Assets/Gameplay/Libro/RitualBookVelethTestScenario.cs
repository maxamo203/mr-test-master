using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Scanner;

namespace Gameplay
{
    // Harness de desarrollo para probar la epica sin ARKit ni una sesion LAN.
    // Usa el flow, la vista y la entidad reales; solo reemplaza el ancla y la red.
    public class RitualBookVelethTestScenario : MonoBehaviour
    {
        [SerializeField] private RitualBookView _book;
        [SerializeField] private VelethEntity _veleth;
        [SerializeField] private Transform _player;
        [SerializeField] private Light _flashlight;

        [Header("Tiempos de las US")]
        [SerializeField, Min(0f)] private float _quickDelaySeconds = 3f;
        [SerializeField, Min(0.1f)] private float _consumeSeconds = 6f;
        [SerializeField, Min(0.1f)] private float _defenseSeconds = 4f;
        [SerializeField, Min(0.1f)] private float _velethSpeed = 1.2f;
        [SerializeField, Min(0.1f)] private float _catchRange = 0.9f;
        [SerializeField, Min(0.1f)] private float _playerSpeed = 2.8f;

        [Header("Multijugador simulado")]
        [SerializeField, Range(1, 8)] private int _simulatedPlayers = 4;
        [SerializeField, Range(0, 8)] private int _aimingPlayers;

        public int SimulatedPlayers => _simulatedPlayers;
        public int SimulatedAimingPlayers => _aimingPlayers;

        [Header("Obstaculos de navegacion")]
        [SerializeField] private Vector3[] _obstaclePositions =
        {
            new Vector3(0f, 0.65f, -0.65f),
            new Vector3(-1.25f, 0.65f, -2.05f),
            new Vector3(1.2f, 0.65f, -3.1f),
        };
        [SerializeField] private Vector3[] _obstacleScales =
        {
            new Vector3(2.5f, 1.3f, 0.5f),
            new Vector3(1.6f, 1.3f, 0.5f),
            new Vector3(1.6f, 1.3f, 0.5f),
        };

        private RitualBookFlow _flow;
        private bool _hunting;
        private bool _dead;
        private bool _normalInterval;
        private readonly List<Vector3> _path = new();
        private int _pathIndex;
        private float _repathTimer;
        private bool _navigationReady;
        private LineRenderer _pathLine;
        private Material _pathMaterial;
        private string _qaResult = "Selecciona un escenario de prueba.";

        private void Start()
        {
            PrepareNavigationScenario();
            ResetScenario(true);
        }

        private void OnValidate()
        {
            _simulatedPlayers = Mathf.Clamp(_simulatedPlayers, 1, 8);
            _aimingPlayers = Mathf.Clamp(_aimingPlayers, 0, _simulatedPlayers);
        }

        public void SetSimulatedPlayers(int players)
        {
            _simulatedPlayers = Mathf.Clamp(players, 1, 8);
            _aimingPlayers = Mathf.Clamp(_aimingPlayers, 0, _simulatedPlayers);
        }

        public void SetSimulatedAimingPlayers(int players) =>
            _aimingPlayers = Mathf.Clamp(players, 0, _simulatedPlayers);

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.rKey.wasPressedThisFrame) ResetScenario(false);
            if (keyboard.nKey.wasPressedThisFrame) ResetScenario(true);
            if (keyboard.tKey.wasPressedThisFrame) StartAttackNow();
            if (keyboard.vKey.wasPressedThisFrame) InvokeVeleth();

            if (keyboard.digit0Key.wasPressedThisFrame) _aimingPlayers = 0;
            if (keyboard.digit1Key.wasPressedThisFrame) _aimingPlayers = 1;
            if (keyboard.digit2Key.wasPressedThisFrame) _aimingPlayers = Mathf.Min(2, _simulatedPlayers);
            if (keyboard.digit3Key.wasPressedThisFrame) _aimingPlayers = Mathf.Min(3, _simulatedPlayers);
            if (keyboard.digit4Key.wasPressedThisFrame) _aimingPlayers = Mathf.Min(4, _simulatedPlayers);
            if (keyboard.digit5Key.wasPressedThisFrame) _aimingPlayers = Mathf.Min(5, _simulatedPlayers);
            if (keyboard.digit6Key.wasPressedThisFrame) _aimingPlayers = Mathf.Min(6, _simulatedPlayers);
            if (keyboard.digit7Key.wasPressedThisFrame) _aimingPlayers = Mathf.Min(7, _simulatedPlayers);
            if (keyboard.digit8Key.wasPressedThisFrame) _aimingPlayers = Mathf.Min(8, _simulatedPlayers);

            int aiming = keyboard.fKey.isPressed ? _simulatedPlayers : _aimingPlayers;
            bool defending = !_hunting && !_dead && aiming >= _simulatedPlayers;
            if (_flashlight != null) _flashlight.enabled = defending;

            if (!_hunting && !_dead && _flow != null)
            {
                var result = _flow.Tick(Time.deltaTime, aiming, _simulatedPlayers,
                    _consumeSeconds, _defenseSeconds);
                _book?.AplicarOscuridad(_flow.Darkness01);

                if ((result & RitualBookTickResult.Consumed) != 0)
                    InvokeVeleth();
            }

            MovePlayer(keyboard);
            UpdateHunt();
        }

        private void MovePlayer(Keyboard keyboard)
        {
            if (_player == null || _dead) return;

            float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
                      (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) -
                      (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            Vector3 movement = new Vector3(x, 0f, z);
            if (movement.sqrMagnitude > 1f) movement.Normalize();
            _player.position += movement * (_playerSpeed * Time.deltaTime);
        }

        private void UpdateHunt()
        {
            if (!_hunting || _dead || _veleth == null || _player == null) return;

            _repathTimer -= Time.deltaTime;
            if (_repathTimer <= 0f || _pathIndex >= _path.Count)
            {
                _repathTimer = 0.25f;
                if (SorkerNav.Instance != null &&
                    SorkerNav.Instance.TryGetPath(_veleth.Position, _player.position, _path))
                {
                    _pathIndex = 0;
                    DrawPath();
                }
                else
                {
                    _path.Clear();
                    DrawPath();
                }
            }

            Vector3 waypoint = _pathIndex < _path.Count ? _path[_pathIndex] : _player.position;
            _veleth.MoveTo(waypoint, _velethSpeed, Time.deltaTime);
            if (_pathIndex < _path.Count &&
                HorizontalDistance(_veleth.Position, _path[_pathIndex]) <= 0.2f)
                _pathIndex++;
            Vector3 delta = _player.position - _veleth.Position;
            delta.y = 0f;
            if (delta.magnitude > _catchRange) return;

            _veleth.SetState(VelethState.Grabbing);
            _dead = true;
            _hunting = false;
            if (_flashlight != null) _flashlight.enabled = false;
        }

        private void StartAttackNow()
        {
            if (_dead || _hunting) return;
            _normalInterval = false;
            _flow = new RitualBookFlow(() => 0f);
            _flow.Restart();
            _book?.SetDisponible(true);
            _book?.AplicarOscuridad(0f);
        }

        private RitualBookTickResult SimularTick(float segundos, bool alumbrando)
            => SimularTick(segundos, alumbrando ? _simulatedPlayers : 0);

        private RitualBookTickResult SimularTick(float segundos, int jugadoresApuntando)
        {
            if (_flow == null) return RitualBookTickResult.None;

            var result = _flow.Tick(segundos, jugadoresApuntando, _simulatedPlayers,
                _consumeSeconds, _defenseSeconds);
            _book?.AplicarOscuridad(_flow.Darkness01);
            if ((result & RitualBookTickResult.Consumed) != 0)
                InvokeVeleth();
            return result;
        }

        private bool EscenarioDemoraMultijugador()
        {
            StartAttackNow();
            if (_simulatedPlayers == 1)
            {
                SimularTick(3f, 0);
                SimularTick(1f, 1);
                bool singleOk = Mathf.Abs(_flow.Darkness01 - 0.375f) < 0.001f &&
                                Mathf.Abs(_flow.Defense01 - 0.25f) < 0.001f;
                _qaResult = Resultado("Sesion individual sin demora parcial", singleOk,
                    $"oscuridad={_flow.Darkness01:P1}, defensa={_flow.Defense01:P0}");
                return singleOk;
            }

            int aiming = Mathf.Clamp(_aimingPlayers, 1, _simulatedPlayers - 1);
            SimularTick(3f, aiming);
            float expected = 0.5f * (1f - aiming / (float)_simulatedPlayers);
            bool ok = Mathf.Abs(_flow.Darkness01 - expected) < 0.001f &&
                      Mathf.Approximately(_flow.Defense01, 0f);
            _qaResult = Resultado($"Demora con {aiming} de {_simulatedPlayers} jugadores", ok,
                $"valor={_flow.Darkness01:P1}, esperado={expected:P1}");
            return ok;
        }

        private bool EscenarioCincuentaPorCiento()
        {
            StartAttackNow();
            SimularTick(_consumeSeconds * 0.5f, false);
            bool ok = Mathf.Abs(_flow.Darkness01 - 0.5f) < 0.001f;
            _qaResult = Resultado("Oscuridad al 50%", ok,
                $"valor={_flow.Darkness01:P1}");
            return ok;
        }

        private bool EscenarioDefensaParcial()
        {
            StartAttackNow();
            SimularTick(3f, false);
            SimularTick(2f, true);
            SimularTick(1f, false);
            bool ok = Mathf.Abs(_flow.Darkness01 - 0.375f) < 0.001f &&
                      Mathf.Approximately(_flow.Defense01, 0f);
            _qaResult = Resultado("Defensa parcial + reanudacion", ok,
                $"valor={_flow.Darkness01:P1}");
            return ok;
        }

        private bool EscenarioLibroSalvado()
        {
            StartAttackNow();
            SimularTick(3f, false);
            var result = SimularTick(_defenseSeconds, true);
            bool ok = (result & RitualBookTickResult.Saved) != 0 &&
                      _flow.Phase == RitualBookPhase.Waiting &&
                      Mathf.Approximately(_flow.Darkness01, 0f);
            _qaResult = Resultado("Defensa completa", ok,
                $"fase={_flow.Phase}, oscuridad={_flow.Darkness01:P0}");
            return ok;
        }

        private bool EscenarioLibroPerdido()
        {
            StartAttackNow();
            var result = SimularTick(_consumeSeconds, false);
            bool libroDisponible = _book != null && _book.Disponible;
            bool ok = (result & RitualBookTickResult.Consumed) != 0 &&
                      _hunting && !libroDisponible;
            _qaResult = Resultado("Perdida e invocacion", ok,
                $"Veleth={_hunting}, libro={libroDisponible}");
            return ok;
        }

        private bool EscenarioCaptura()
        {
            ResetScenario(false);
            InvokeVeleth();
            if (_veleth != null && _player != null)
                _veleth.SetPositionDirectly(_player.position);
            UpdateHunt();
            bool ok = _dead && !_hunting &&
                      _veleth != null && _veleth.State == VelethState.Grabbing;
            _qaResult = Resultado("Captura del jugador", ok,
                $"muerto={_dead}, estado={_veleth?.State}");
            return ok;
        }

        private void EjecutarTodosLosEscenarios()
        {
            int aprobados = 0;
            if (EscenarioCincuentaPorCiento()) aprobados++;
            if (EscenarioDefensaParcial()) aprobados++;
            if (EscenarioLibroSalvado()) aprobados++;
            if (EscenarioLibroPerdido()) aprobados++;
            if (EscenarioCaptura()) aprobados++;
            if (EscenarioDemoraMultijugador()) aprobados++;
            _qaResult = $"{(aprobados == 6 ? "PASS" : "FAIL")} - bateria QA: {aprobados}/6 escenarios";
            Debug.Log($"[QA Libro/Veleth] {_qaResult}");
        }

        private static string Resultado(string nombre, bool ok, string detalle)
        {
            string resultado = $"{(ok ? "PASS" : "FAIL")} - {nombre}: {detalle}";
            Debug.Log($"[QA Libro/Veleth] {resultado}");
            return resultado;
        }

        private void ResetScenario(bool normalInterval)
        {
            _normalInterval = normalInterval;
            float delay = normalInterval ? Random.Range(30f, 50f) : _quickDelaySeconds;
            _flow = new RitualBookFlow(() => delay);
            _flow.Restart();
            _hunting = false;
            _dead = false;
            _aimingPlayers = 0;
            _path.Clear();
            DrawPath();
            _pathIndex = 0;
            _repathTimer = 0f;

            if (_book != null)
            {
                _book.SetDisponible(true);
                _book.AplicarOscuridad(0f);
            }

            if (_veleth != null)
            {
                _veleth.SetState(VelethState.Hunting);
                _veleth.gameObject.SetActive(false);
            }
            if (_flashlight != null) _flashlight.enabled = false;
        }

        private void InvokeVeleth()
        {
            if (_dead || _hunting || _veleth == null) return;

            _book?.SetDisponible(false);
            Vector3 spawn = _book != null ? _book.PuntoDeLuz : Vector3.zero;
            spawn.y = 0f;
            _veleth.gameObject.SetActive(true);
            _veleth.SetPositionDirectly(spawn);
            _veleth.SetState(VelethState.Hunting);
            VelethPresentation.PlayInvocation(spawn);
            _hunting = true;
        }

#if UNITY_EDITOR
        public void InvokeVelethForValidation() => InvokeVeleth();
#endif

        private void PrepareNavigationScenario()
        {
            if (_navigationReady) return;

            if (WorldOrigin.Instance == null)
            {
                var anchor = new GameObject("Ancla simulada - escenario Veleth");
                var origin = new GameObject("WorldOrigin - escenario Veleth");
                origin.AddComponent<WorldOrigin>().SetOrigin(anchor.transform);
            }
            if (SceneRegistry.Instance == null)
                new GameObject("SceneRegistry - escenario Veleth").AddComponent<SceneRegistry>();

            FloorPoint.Create(Vector3.zero);
            int count = Mathf.Min(_obstaclePositions?.Length ?? 0, _obstacleScales?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                var obstacle = CubeObject.Create(_obstaclePositions[i], Quaternion.identity,
                    _obstacleScales[i], $"qa-obstacle-{i + 1}");
                obstacle.gameObject.name = $"Obstaculo QA {i + 1}";
            }

            SorkerNav.Ensure().Rebuild();
            EnsurePathLine();
            _navigationReady = true;
        }

        private void EnsurePathLine()
        {
            if (_pathLine != null) return;
            var line = new GameObject("Ruta A* Veleth (QA)");
            line.transform.SetParent(transform, false);
            _pathLine = line.AddComponent<LineRenderer>();
            _pathLine.useWorldSpace = true;
            _pathLine.widthMultiplier = 0.045f;
            _pathLine.numCapVertices = 3;
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            _pathMaterial = new Material(shader) { name = "Ruta Veleth QA (runtime)" };
            _pathMaterial.color = new Color(1f, 0.78f, 0.05f, 1f);
            _pathLine.sharedMaterial = _pathMaterial;
            _pathLine.positionCount = 0;
        }

        private void DrawPath()
        {
            if (_pathLine == null) return;
            _pathLine.positionCount = _path.Count;
            for (int i = 0; i < _path.Count; i++)
                _pathLine.SetPosition(i, _path[i] + Vector3.up * 0.06f);
        }

        private void OnDestroy()
        {
            if (_pathMaterial != null) Destroy(_pathMaterial);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private void OnGUI()
        {
            var title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            title.normal.textColor = Color.white;
            var text = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            text.normal.textColor = Color.white;

            GUI.Box(new Rect(18, 18, 590, 560), GUIContent.none);
            GUI.Label(new Rect(35, 30, 470, 35), "PRUEBA: LIBRO RITUAL + VELETH", title);
            GUI.Label(new Rect(35, 70, 540, 150),
                "T  iniciar oscuridad ahora\n" +
                "0-8  cantidad de jugadores apuntando\n" +
                "F  mantener: todos apuntan, reduce y protege (4 s)\n" +
                "R  reiniciar con espera rapida de 3 s\n" +
                "N  reiniciar con intervalo real aleatorio 30–50 s\n" +
                "V  invocar Veleth directamente\n" +
                "WASD / flechas  mover al jugador", text);

            GUI.Label(new Rect(35, 214, 205, 28), $"Jugadores en sesion: {_simulatedPlayers}", text);
            if (GUI.Button(new Rect(245, 210, 42, 30), "-")) SetSimulatedPlayers(_simulatedPlayers - 1);
            if (GUI.Button(new Rect(292, 210, 42, 30), "+")) SetSimulatedPlayers(_simulatedPlayers + 1);

            GUI.Label(new Rect(35, 250, 205, 28), $"Jugadores apuntando: {_aimingPlayers}", text);
            if (GUI.Button(new Rect(245, 246, 42, 30), "-")) SetSimulatedAimingPlayers(_aimingPlayers - 1);
            if (GUI.Button(new Rect(292, 246, 42, 30), "+")) SetSimulatedAimingPlayers(_aimingPlayers + 1);
            if (GUI.Button(new Rect(345, 246, 115, 30), "TODOS"))
                SetSimulatedAimingPlayers(_simulatedPlayers);

            float attackSpeed = _aimingPlayers == 0 ? 1f
                : _aimingPlayers >= _simulatedPlayers ? 0f
                : 1f - _aimingPlayers / (float)_simulatedPlayers;
            string rule = _simulatedPlayers == 1
                ? "Individual: sin demora parcial"
                : $"Velocidad oscuridad: {attackSpeed:P0}";
            GUI.Label(new Rect(345, 210, 220, 28), rule, text);

            float bx = 35f;
            float by = 292f;
            float bw = 265f;
            float bh = 34f;
            if (GUI.Button(new Rect(bx, by, bw, bh), "1. Libro al 50%"))
                EscenarioCincuentaPorCiento();
            if (GUI.Button(new Rect(bx + 275f, by, bw, bh), "2. Defensa parcial"))
                EscenarioDefensaParcial();
            if (GUI.Button(new Rect(bx, by + 42f, bw, bh), "3. Salvar el libro"))
                EscenarioLibroSalvado();
            if (GUI.Button(new Rect(bx + 275f, by + 42f, bw, bh), "4. Perder / invocar"))
                EscenarioLibroPerdido();
            if (GUI.Button(new Rect(bx, by + 84f, bw, bh), "5. Captura de Veleth"))
                EscenarioCaptura();
            if (GUI.Button(new Rect(bx + 275f, by + 84f, bw, bh), "6. Probar formula actual"))
                EscenarioDemoraMultijugador();
            if (GUI.Button(new Rect(bx, by + 126f, 540f, bh), "EJECUTAR TODOS"))
                EjecutarTodosLosEscenarios();

            var qaStyle = new GUIStyle(text) { fontStyle = FontStyle.Bold };
            qaStyle.normal.textColor = _qaResult.StartsWith("FAIL")
                ? new Color(1f, 0.25f, 0.25f)
                : new Color(0.35f, 1f, 0.45f);
            GUI.Label(new Rect(35, by + 170f, 540, 55), _qaResult, qaStyle);

            string status = _dead ? "MUERTO — Veleth te alcanzo" :
                _hunting ? "VELETH: persiguiendo" :
                _flow == null ? "Preparando..." :
                $"LIBRO: {_flow.Phase}  oscuridad {_flow.Darkness01:P0}  " +
                $"defensa {_flow.Defense01:P0}  apuntando {_aimingPlayers}/{_simulatedPlayers}  " +
                $"ruta {_path.Count} pts  espera {_flow.WaitRemaining:0.0}s" +
                (_normalInterval ? " (intervalo real)" : "");

            var statusStyle = new GUIStyle(title) { fontSize = 20 };
            statusStyle.normal.textColor = _dead ? new Color(1f, 0.2f, 0.2f) :
                                           _hunting ? new Color(0.8f, 0.35f, 1f) :
                                           new Color(1f, 0.85f, 0.35f);
            GUI.Label(new Rect(25, Screen.height - 58, Screen.width - 50, 42), status, statusStyle);

            if (_dead)
            {
                Color old = GUI.color;
                GUI.color = new Color(0.45f, 0f, 0f, 0.78f);
                GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);
                GUI.color = old;
                var death = new GUIStyle(title) { fontSize = 54, alignment = TextAnchor.MiddleCenter };
                death.normal.textColor = Color.white;
                GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 90), "VELETH TE ENCONTRO", death);
                GUI.Label(new Rect(0, Screen.height * 0.55f, Screen.width, 40),
                    "Presiona R para volver a probar", new GUIStyle(text) { alignment = TextAnchor.MiddleCenter });
            }
        }

#if UNITY_EDITOR
        public void EditorConfigurar(RitualBookView book, VelethEntity veleth,
                                     Transform player, Light flashlight)
        {
            _book = book;
            _veleth = veleth;
            _player = player;
            _flashlight = flashlight;
        }
#endif
    }
}
