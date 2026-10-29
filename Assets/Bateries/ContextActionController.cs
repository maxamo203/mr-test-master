using System.Collections.Generic;
using Gamepad;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bateries
{
    public enum PrimaryGestureEvent
    {
        None,
        Tap,
        HoldStarted,
        HoldReleased,
    }

    // Traductor puro de un único botón físico/táctil: un toque se confirma al soltar;
    // mantener supera el umbral una sola vez y nunca produce además un toque.
    public sealed class PrimaryButtonGesture
    {
        public bool IsPressed { get; private set; }
        public bool IsHolding { get; private set; }
        public float Elapsed { get; private set; }

        public PrimaryGestureEvent Tick(bool pressed, float deltaTime, float holdSeconds)
        {
            if (pressed)
            {
                if (!IsPressed)
                {
                    IsPressed = true;
                    IsHolding = false;
                    Elapsed = 0f;
                }

                Elapsed += Mathf.Max(0f, deltaTime);
                if (!IsHolding && Elapsed >= Mathf.Max(0.05f, holdSeconds))
                {
                    IsHolding = true;
                    return PrimaryGestureEvent.HoldStarted;
                }
                return PrimaryGestureEvent.None;
            }

            if (!IsPressed) return PrimaryGestureEvent.None;
            IsPressed = false;
            Elapsed = 0f;
            if (IsHolding)
            {
                IsHolding = false;
                return PrimaryGestureEvent.HoldReleased;
            }
            return PrimaryGestureEvent.Tap;
        }

        public void Reset()
        {
            IsPressed = false;
            IsHolding = false;
            Elapsed = 0f;
        }
    }

    // Hub del BOTÓN PRIMARIO (A del joystick / un dedo en pantalla fuera de los botones —dos
    // con el visor— / tecla E en editor).
    // Cada frame elige, entre las acciones registradas, la disponible de mayor Priority.
    // Un toque se ejecuta al soltar; mantener activa la luz intensa. Así el mismo botón:
    // si estás apuntando una pila la recoge; si no, prende/apaga la linterna; y se puede
    // extender a puertas, interruptores, etc.
    //
    // Extender: implementá IContextAction (lo más simple, en un MonoBehaviour puesto en
    // ESTE GameObject — se auto-descubre), o registralo por código con
    // ContextActionController.Instance.Register(...). Las built-in (recoger pila / linterna)
    // se auto-agregan si no están, así funciona sin wiring extra.
    //
    // Wiring: un GameObject en la escena multijugador con este componente. La linterna
    // (Flashlight) ya existe en la escena.
    public class ContextActionController : MonoBehaviour
    {
        public static ContextActionController Instance { get; private set; }

        [Header("HUD")]
        [SerializeField] private bool showActionButton = true;
        [Header("Boton unico")]
        [SerializeField, Min(0.05f)] private float brightHoldSeconds = 0.5f;

        private readonly List<IContextAction> _actions = new();
        private IContextAction _current;
        private string         _currentLabel;
        private readonly PrimaryButtonGesture _primaryGesture = new();
        private IContextAction _pressedAction;
        private bool           _screenPrimaryHeld;
        private Flashlight     _flashlight;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            // Acciones built-in como componentes (se auto-agregan si no están): así funciona
            // sin wiring y podés pre-configurarlas o sumar nuevas al mismo GameObject.
            EnsureComponent<FlashlightToggleAction>();
            EnsureComponent<BatteryPickupAction>();
            EnsureComponent<Collectibles.CollectiblePickupAction>();
            // Control por mirada (Cardboard sin joystick).
            EnsureComponent<Rediseno.ControlMirada>();
        }

        private void Start()
        {
            // Descubrir todas las acciones (componentes) de este GameObject.
            foreach (var a in GetComponents<IContextAction>()) Register(a);

            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnBatteryCollected += HandleCollected;
        }

        private void OnDestroy()
        {
            CancelPrimaryGesture();
            if (Instance == this) Instance = null;
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnBatteryCollected -= HandleCollected;
        }

        private void OnDisable()
        {
            CancelPrimaryGesture();
        }

        private void EnsureComponent<T>() where T : Component
        {
            if (GetComponent<T>() == null) gameObject.AddComponent<T>();
        }

        public void Register(IContextAction a)
        {
            if (a != null && !_actions.Contains(a)) _actions.Add(a);
        }

        public void Unregister(IContextAction a) => _actions.Remove(a);

        // La acción disponible de mayor prioridad este frame (o null). La usa el control por
        // mirada para saber qué hay apuntado.
        public IContextAction AccionActual => _current;

        private static bool EnMenu => PauseMenuController.IsOpen;

        private void Update()
        {
            // Muerto (con la pantalla de muerte o espectando): el botón primario no hace
            // nada — ni recoger, ni linterna. El espectador mira, no interviene.
            var ld = Gameplay.LocalDeath.Instance;
            if (ld != null && ld.IsDead)
            {
                if (_primaryGesture.IsPressed || _current != null) CancelPrimaryGesture();
                _current = null;
                _currentLabel = null;
                return;
            }

            // Pausa abierta: el South/A navega la pausa, no prende la linterna ni recoge.
            // Y al cerrarla con A (REANUDAR) el botón sigue apretado: se ignora hasta que
            // se suelte, si no el release contaba como un tap de gameplay.
            if (EnMenu) _esperarSoltarTrasPausa = true;
            if (_esperarSoltarTrasPausa)
            {
                if (_primaryGesture.IsPressed) CancelPrimaryGesture();
                if (EnMenu || PrimaryHeld()) return;
                _esperarSoltarTrasPausa = false;
            }

            ResolveCurrent();
            UpdatePrimaryGesture();
            if (Debug.isDebugBuild) DiagnosticoInput();
        }

        // TEMPORAL (diagnóstico VR BOX, sólo development build): loguea qué fuentes de
        // input ve Unity cada vez que cambian, para saber por dónde llega la A del mando
        // en partida. Sacar cuando quede resuelto.
        private string _diagPrevio;
        private void DiagnosticoInput()
        {
            var gm    = GamepadManager.Instance;
            var mouse = Mouse.current;
            var ts    = Touchscreen.current;
            int toques = 0;
            string posToques = "";
            if (ts != null)
                for (int i = 0; i < ts.touches.Count; i++)
                    if (ts.touches[i].press.isPressed)
                    {
                        toques++;
                        var tp = ts.touches[i].position.ReadValue();
                        posToques += $"({tp.x:0},{tp.y:0} ui={Scanner.UIBlocker.IsPointerOver(tp)})";
                    }
            var g = gm?.Current;
            string estado =
                $"pad='{(g != null ? g.displayName + "/" + g.name : "-")}' usesMouse={gm != null && gm.UsesMouseInput} " +
                $"south={(g != null && g.buttonSouth.isPressed)} east={(g != null && g.buttonEast.isPressed)} " +
                $"mouse='{(mouse != null ? mouse.name : "-")}' L={(mouse != null && mouse.leftButton.isPressed)} " +
                $"back={(mouse != null && mouse.backButton.isPressed)} toques={toques}{posToques} pantalla={Screen.width}x{Screen.height} " +
                $"accion={(_current != null ? _current.GetType().Name : "-")} linterna={(_flashlight != null && _flashlight.isOn)} " +
                $"esc={(Keyboard.current != null && Keyboard.current.escapeKey.isPressed)} " +
                $"pausa={PauseMenuController.IsOpen} esperar={_esperarSoltar} held={PrimaryHeld()}";
            if (estado == _diagPrevio) return;
            _diagPrevio = estado;
            string mice = "";
            foreach (var d in InputSystem.devices) mice += $"[{d.GetType().Name}:{d.name}] ";
            Debug.Log($"[DiagA] {estado} | devices: {mice}");
        }

        // Con la pausa abierta el botón primario es el "clic" del menú: no puede además
        // prender/apagar la linterna. Y al cerrarla (p. ej. con REANUDAR, que se acciona
        // con ese mismo botón) hay que esperar a que se suelte, o esa misma pulsación
        // terminaba como un toque sobre la linterna.
        private bool _esperarSoltar;

        private bool _esperarSoltarTrasPausa;

        private void UpdatePrimaryGesture()
        {
            EnsureFlashlight();

            if (EnMenu)
            {
                if (_primaryGesture.IsPressed) CancelPrimaryGesture();
                _esperarSoltar = true;
                return;
            }
            if (_esperarSoltar)
            {
                if (PrimaryHeld()) return;
                _esperarSoltar = false;
            }
            UpdatePalancaVRBox();
            bool wasPressed = _primaryGesture.IsPressed;
            PrimaryGestureEvent gesture = _primaryGesture.Tick(
                PrimaryHeld(), Time.unscaledDeltaTime, brightHoldSeconds);

            if (!wasPressed && _primaryGesture.IsPressed)
                _pressedAction = _current;

            if (Debug.isDebugBuild && gesture != PrimaryGestureEvent.None)
                Debug.Log($"[DiagA] GESTO {gesture} accion={(_pressedAction != null ? _pressedAction.GetType().Name : "null")} " +
                          $"linternaAntes={(_flashlight != null && _flashlight.isOn)}");

            switch (gesture)
            {
                case PrimaryGestureEvent.Tap:
                    _pressedAction?.Execute();
                    _pressedAction = null;
                    break;
                case PrimaryGestureEvent.HoldStarted:
                    _flashlight?.BeginBrightHold();
                    break;
                case PrimaryGestureEvent.HoldReleased:
                    _flashlight?.SetBrightHeld(false);
                    _pressedAction = null;
                    break;
            }
        }

        // VR BOX en modo mouse: la B es un pulso (no informa cuándo se suelta, ver VRBoxInput),
        // así que no puede haber "mantener para concentrar" con ella. La luz intensa va por la
        // palanca, que sí se sostiene. Posiciones de la palanca:
        //   arriba (mantenida) = intensa (prende si estaba apagada)
        //   centro             = normal
        //   abajo  (mantenida) = apagada; al volver al centro se vuelve a prender
        // Abajo sólo vuelve a prender la luz que ELLA apagó: si ya estaba apagada (con la B),
        // soltar la palanca no la prende.
        private bool _palancaArriba, _palancaAbajo, _apagadaPorPalanca;

        private void UpdatePalancaVRBox()
        {
            bool arriba = VRBoxInput.PalancaArriba;
            bool abajo  = VRBoxInput.PalancaAbajo;
            if (_flashlight != null)
            {
                if (arriba && !_palancaArriba) _flashlight.BeginBrightHold();
                else if (!arriba && _palancaArriba) _flashlight.SetBrightHeld(false);

                if (abajo && !_palancaAbajo && _flashlight.isOn)
                {
                    _flashlight.Toggle();
                    _apagadaPorPalanca = true;
                }
                else if (!abajo && _palancaAbajo)
                {
                    if (_apagadaPorPalanca && !_flashlight.isOn) _flashlight.Toggle();
                    _apagadaPorPalanca = false;
                }
            }
            _palancaArriba = arriba;
            _palancaAbajo  = abajo;
        }

        private void CancelPrimaryGesture()
        {
            _palancaArriba = _palancaAbajo = _apagadaPorPalanca = false;
            _primaryGesture.Reset();
            _screenPrimaryHeld = false;
            _pressedAction = null;
            _flashlight?.SetBrightHeld(false);
        }

        // Elige la acción disponible de mayor prioridad.
        private void ResolveCurrent()
        {
            _current = null;
            _currentLabel = null;
            int best = int.MinValue;
            for (int i = 0; i < _actions.Count; i++)
            {
                var a = _actions[i];
                if (a == null) continue;
                if (a.TryResolve(out var label) && a.Priority > best)
                {
                    best = a.Priority;
                    _current = a;
                    _currentLabel = label;
                }
            }
        }

        // El mismo botón se lee como estado continuo para poder distinguir toque de hold.
        private bool PrimaryHeld()
        {
            var g = GamepadManager.Instance?.Current;
            bool held = g != null && (g.buttonSouth.isPressed || g.buttonEast.isPressed ||
                                      g.buttonNorth.isPressed || g.buttonWest.isPressed);

            // VR Box en modo mouse: la A es el botón de acción (recoger lo apuntado; si no,
            // prender/apagar). La palanca va aparte: hemisferio norte = intensa, sur = apagada.
            // La B abre la pausa. Como la B, la A puede llegar con bajada y subida casi juntas:
            // leer sólo si está apretada (polling) perdía la mayoría de las pulsaciones y había
            // que insistir para recoger una pila. La pulsación vista por la acción
            // (APressedThisFrame, que ve cada evento) cuenta como un frame apretado = un toque.
            held |= VRBoxInput.AHeld || VRBoxInput.APressedThisFrame;

            // Mobile: un dedo apoyado fuera de los botones = el mismo botón. Toque corto
            // prende/apaga (o recoge lo apuntado), mantener concentra.
            held |= DedoEnPantalla();

#if UNITY_EDITOR
            if (Keyboard.current != null) held |= Keyboard.current.eKey.isPressed;
#endif
            return held || _screenPrimaryHeld;
        }

        // "RECOGER · botón A" en cada ojo (píxeles de pantalla, centros calibrados del
        // visor: ver Rediseno.UI.OjosVisor), un poco por debajo del centro de la vista.
        private static void CartelEnOjos(string accion)
        {
            var prevM = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            float s = Screen.height / 1080f;
            bool vrBox = GamepadManager.Instance != null && GamepadManager.Instance.UsesMouseInput;
            string como = "botón A";
            var stTit = MortuoriumTheme.Estilo(MortuoriumTheme.FBebas, Mathf.RoundToInt(40 * s),
                                               MortuoriumTheme.Cream, TextAnchor.MiddleCenter);
            var stSub = MortuoriumTheme.Estilo(MortuoriumTheme.FMono, Mathf.RoundToInt(20 * s),
                                               MortuoriumTheme.Tan, TextAnchor.MiddleCenter);
            Rediseno.UI.OjosVisor.Centros(out var izq, out var der);

            // Las barras de la noche (linterna / cordura / amanecer) también van bajo cada
            // ojo, justo en esa franja: el cartel se apoya encima de la fila más alta para
            // no pisarlas. Su borde superior sale del mismo layout que las dibuja (coords
            // virtuales de UIScale → px reales).
            MortuoriumTheme.HudBarRects(Scanner.UIScale.VirtualWidth, Scanner.UIScale.VirtualHeight,
                                        2, out var filaAlta, out _);
            float topeBarras = filaAlta.y * Scanner.UIScale.Factor + Scanner.UIScale.SafeGui.y;

            for (int i = 0; i < 2; i++)
            {
                var c = i == 0 ? izq : der;
                float w = 300f * s, h = 86f * s;
                float y = Mathf.Min(c.y + 120f * s, topeBarras - 12f * s - h);
                var r = new Rect(c.x - w * 0.5f, y, w, h);
                MortuoriumTheme.Tarjeta2(r, MortuoriumTheme.Hoja2, MortuoriumTheme.Tan, 24f * s, 2f * s);
                GUI.Label(new Rect(r.x, r.y + 6f * s, w, 44f * s), accion.ToUpperInvariant(), stTit);
                GUI.Label(new Rect(r.x, r.y + 50f * s, w, 26f * s), como, stSub);
            }
            GUI.matrix = prevM;
        }

        // touchId → si ese dedo cuenta como botón primario. Se decide UNA vez, al apoyarlo:
        // un dedo que arrancó sobre un botón (pausa, RECOGER) o un panel no prende la
        // linterna aunque después se deslice afuera, y uno que arrancó afuera sigue contando
        // aunque pase por encima de un botón.
        private readonly Dictionary<int, bool> _dedos = new();
        private readonly List<int> _dedosSueltos = new();

        private bool DedoEnPantalla()
        {
            var ts = Touchscreen.current;
            if (ts == null) { _dedos.Clear(); return false; }

            // Con el visor (Cardboard / mirada) o el VR BOX en modo mouse —cuyo clic puede
            // llegar como un toque de un dedo— un toque suelto no es intencional: ahí sigue
            // el gesto de antes, dos dedos apoyados.
            var gm = GamepadManager.Instance;
            bool enMano = !MRCardboardController.Activo && !Rediseno.ControlMirada.Activo &&
                          !(gm != null && gm.UsesMouseInput);

            bool held = false;
            int dedos = 0;
            _dedosSueltos.Clear();
            foreach (var id in _dedos.Keys) _dedosSueltos.Add(id);

            var touches = ts.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                if (!t.press.isPressed) continue;
                dedos++;
                int id = t.touchId.ReadValue();
                _dedosSueltos.Remove(id);
                if (!_dedos.TryGetValue(id, out bool cuenta))
                {
                    // Sólo en plena noche (no en la sincronización ni el briefing), sin
                    // ninguna pantalla UI Toolkit encima y fuera de los botones IMGUI.
                    cuenta = NetworkManager.Instance != null && NetworkManager.Instance.GameStarted &&
                             !Rediseno.UI.PantallaUI.AlgunaVisible &&
                             !Scanner.UIBlocker.IsPointerOver(t.position.ReadValue());
                    _dedos[id] = cuenta;
                }
                held |= cuenta;
            }
            foreach (var id in _dedosSueltos) _dedos.Remove(id);

            return enMano ? held : dedos >= 2;
        }

        // ── Carga de pila recibida del server (cliente) ───────────────────────

        private void HandleCollected(byte rarityIndex, float charge)
        {
            EnsureFlashlight();
            _flashlight?.AddCharge(charge);
        }

        private void EnsureFlashlight()
        {
            if (_flashlight == null) _flashlight = FindFirstObjectByType<Flashlight>();
        }

        // ── HUD ───────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            // Estilo Mortuorium (mismo look que el resto de la UII), dentro de la matriz de
            // area segura de UIScale para que quede bien ubicado en todos los dispositivos.
            Scanner.UIScale.Begin();
            float vw = Scanner.UIScale.VirtualWidth, vh = Scanner.UIScale.VirtualHeight;

            // Con Cardboard no se puede tocar nada, pero hay que saber que lo que apuntás se
            // puede recoger. Cartel chico en cada ojo, debajo del centro de la vista.
            if (MRCardboardController.Activo)
            {
                _screenPrimaryHeld = false;
                if (_current != null && _current.ShowActionButton && !string.IsNullOrEmpty(_currentLabel) &&
                    !Rediseno.ControlMirada.Activo && Event.current.type == EventType.Repaint)
                    CartelEnOjos(_currentLabel);
                return;
            }

            // Con el control por mirada el celular está en la cabeza: no hay nada que tocar.
            if (!showActionButton || _current == null || !_current.ShowActionButton ||
                string.IsNullOrEmpty(_currentLabel) || Rediseno.ControlMirada.Activo)
            {
                _screenPrimaryHeld = false;
                return;
            }

            bool pad = GamepadManager.Instance != null && GamepadManager.Instance.IsConnected;
            bool vrBox = pad && GamepadManager.Instance.UsesMouseInput;

            string holdTime = brightHoldSeconds.ToString("0.#");
            string como = vrBox
                ? "botón A · palanca arriba: intensa · abajo: apagada"
                : pad
                ? $"botón A · mantener {holdTime} s: luz intensa"
                : $"mantener {holdTime} s: luz intensa";

            // Estilo 2.0: píldora con el degradé rojo del botón principal; mientras se
            // mantiene apretado, borde dorado (el foco/acción del rediseño).
            const float w = 340f, h = 84f;
            var btn = new Rect(vw * 0.5f - w * 0.5f, vh * 0.62f, w, h);
            Scanner.UIBlocker.AddVirtualRect(btn);   // un dedo acá lo maneja el botón, no DedoEnPantalla
            bool held = GUI.RepeatButton(btn, GUIContent.none, GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                _screenPrimaryHeld = held;
                float radio = h * 0.5f;
                MortuoriumTheme.FillRojo(btn, radio);
                if (held) MortuoriumTheme.FillR(btn, new Color(0f, 0f, 0f, 0.18f), radio);
                MortuoriumTheme.BordeR(btn, held ? MortuoriumTheme.Tan : new Color(1f, 1f, 1f, 0.12f),
                                       radio, held ? 3f : 1.5f);
            }
            GUI.Label(new Rect(btn.x, btn.y + 10f, w, 40f), _currentLabel.ToUpperInvariant(),
                      MortuoriumTheme.Estilo(MortuoriumTheme.FBebas, 32, MortuoriumTheme.Cream, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(btn.x + 16f, btn.y + 48f, w - 32f, 24f), como,
                      MortuoriumTheme.Estilo(MortuoriumTheme.FMono, 11,
                                             new Color(MortuoriumTheme.Cream.r, MortuoriumTheme.Cream.g,
                                                       MortuoriumTheme.Cream.b, 0.85f),
                                             TextAnchor.MiddleCenter));
        }
    }
}
