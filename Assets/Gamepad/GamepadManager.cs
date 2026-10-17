using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.SceneManagement;

namespace Gamepad
{
    // Marca/tipo de gamepad detectado, para adaptar el dibujo del joystick virtual.
    public enum GamepadBrand { None, Generic, Xbox, PlayStation, Switch }

    // Snapshot de todas las pulsaciones del gamepad en un frame. Se lee en vivo
    // (ReadState) desde el visualizador. Los sticks/dpad van en rango [-1,1]
    // (x: derecha+, y: arriba+); los triggers en [0,1].
    public struct GamepadState
    {
        public Vector2 leftStick, rightStick, dpad;
        public bool south, east, west, north;   // botones de la cara (posición física)
        public bool l1, r1;                      // bumpers
        public float l2, r2;                     // triggers analógicos
        public bool l3, r3;                      // clicks de los sticks
        public bool start, select;
        public bool middle;                      // botón central / rueda (solo en modo mouse)
    }

    // Singleton global que detecta gamepads conectados por Bluetooth (emparejados a
    // nivel del SO, fuera de la app) usando el Input System nuevo. Se entera en
    // tiempo real de conexiones/desconexiones vía InputSystem.onDeviceChange y
    // expone el estado para que la UI lo muestre.
    //
    // Se auto-crea (junto con PauseMenuController) en cualquier escena vía
    // RuntimeInitializeOnLoadMethod — no hace falta wiring en el Editor. Vive en
    // DontDestroyOnLoad para sobrevivir cambios de escena.
    [DefaultExecutionOrder(-60)]
    public class GamepadManager : MonoBehaviour
    {
        public static GamepadManager Instance { get; private set; }

        // El gamepad activo (null si no hay ninguno, o si el VR BOX se detectó sólo por
        // Android: ver _vrBoxAndroid).
        public UnityEngine.InputSystem.Gamepad Current { get; private set; }
        public bool IsConnected => (Current != null && Current.added) || _vrBoxAndroid;
        // True cuando el control conectado reporta "Mouse" en el nombre (ej: VR BOX Mouse).
        // En ese modo el stick manda delta de mouse en vez de eje analógico.
        public bool UsesMouseInput => (Current != null && Current.added && EsVRBoxMouse(Current)) || _vrBoxAndroid;

        // VR BOX en modo mouse que Android tiene conectado pero Unity no registró como
        // Gamepad (conectado con la app abierta; ver AndroidVRBox). En modo mouse el input
        // llega igual por el Mouse de Unity, así que alcanza con saber que está. Se sondea
        // cada SegundosSondeo y sólo mientras Unity no vea ningún gamepad.
        private bool _vrBoxAndroid;
        private float _proximoSondeo;
        private const float SegundosSondeo = 1f;
        // Veces que cambió el gamepad activo desde que arrancó la app (diagnóstico: si sube
        // solo, el mando activo está saltando entre dispositivos).
        public int CambiosDeMando { get; private set; }

        private static bool EsVRBoxMouse(UnityEngine.InputSystem.Gamepad g)
        {
            if (g == null) return false;
            string n = string.IsNullOrEmpty(g.displayName) ? g.name : g.displayName;
            return n.IndexOf("Mouse", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public GamepadBrand Brand { get; private set; } = GamepadBrand.None;
        public string DisplayName { get; private set; } = "";

        // Se disparan cuando cambia la conectividad (en tiempo real).
        public event Action OnConnected;
        public event Action OnDisconnected;

        // Batería del mando (0..1). Se consulta cada pocos segundos porque la lectura
        // nativa es cara. _batteryPresent indica si el mando la reporta.
        private float _batteryLevel;
        private bool  _batteryPresent;
        private float _batteryTimer;

        // Crea el sistema de gamepad + menú de pausa en cualquier escena, sin
        // necesidad de arrastrar nada en el Editor. Idempotente.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("GamepadSystem");
            go.AddComponent<GamepadManager>();
            go.AddComponent<PauseMenuController>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        }

        private void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            SceneManager.sceneLoaded += OnSceneLoaded;
            // Estado inicial: puede ya haber un gamepad emparejado al arrancar.
            Refresh();
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            // Poll de respaldo: NO dependemos solo de onDeviceChange. En Android, al
            // conectar un mando por Bluetooth con el juego ya abierto, ese evento a
            // veces no dispara (antes había que reiniciar la app para verlo). Releer
            // el estado cada frame desde la lista de dispositivos lo detecta enseguida.
            // Es barato e idempotente (Refresh solo (des)adopta si realmente cambió).
            Refresh();

            // Batería: refresco periódico (la consulta nativa por JNI es costosa).
            _batteryTimer -= Time.unscaledDeltaTime;
            if (_batteryTimer <= 0f)
            {
                _batteryTimer = 5f;
                RefreshBattery();
            }

            // Lectura del pendorcho: siempre, Tick se auto-limpia si no aplica.
            VRBoxInput.Tick();
            // Diagnóstico del modal de Control: no hace nada si el modal no está abierto.
            ControllerDiagnostics.Tick();

            ActualizarCapturaPuntero();

            // Navegación con gamepad de los menús IMGUI (foco global compartido). Ver
            // ImguiGamepadMenu: GamepadManager es el driver (Tick acá, una vez por frame).
            ImguiGamepadMenu.Tick();
        }

        // ── Captura de puntero (VR BOX en modo mouse) ─────────────────────────────
        // El VR BOX mueve un cursor escondido del SO. Al llegar al borde de la pantalla el
        // cursor se frena y Mouse.delta pasa a 0: con la palanca mantenida (luz intensa) la
        // luz se "soltaba" sola al rato. Con Cursor.lockState = Locked el player de Unity pide
        // requestPointerCapture: Android deja de dibujar/mover el cursor y le entrega a la app
        // el movimiento RELATIVO (AXIS_RELATIVE_X/Y), sin bordes, y los botones dejan de
        // pasar por el sistema. Los toques con el dedo no se capturan, siguen andando.
        // Sólo en partida: en menús/lobby el cursor no molesta y no hace falta cambiar nada.
        // Android suelta la captura cuando la ventana pierde el foco (cortina de
        // notificaciones, otra app), así que al recuperarlo se vuelve a pedir.
        // Además de Cursor.lockState se pide la captura directo a la vista enfocada de la
        // Activity: con GameActivity el lockState solo no llegó a Android (dumpsys input:
        // "Pointer Capture Requested: false, seq=0" con el VR BOX detectado). La vista de
        // superficie de UnityPlayerGameActivity ya reenvía los eventos capturados a
        // onTouchEvent, así que Unity los sigue recibiendo como mouse.
        public bool QuiereCaptura => _quiereCaptura;
        // Estado REAL en Android (View.hasPointerCapture). Se consulta sólo desde el
        // diagnóstico (ConsultarCapturaAndroid), no cada frame.
        public bool PunteroCapturado { get; private set; }
        private bool _quiereCaptura;

        private bool _quiereBloqueo;

        private void ActualizarCapturaPuntero()
        {
            // Son dos cosas distintas:
            //  - La captura de ANDROID (requestPointerCapture): el cursor escondido deja de
            //    existir y llega el movimiento relativo, sin bordes. No toca los toques de
            //    pantalla. Con el rediseño va en TODO el juego (menús incluidos): si no, la
            //    ruedita se trababa al llegar el cursor al borde también en los menús.
            //  - El BLOQUEO de Unity (Cursor.lockState): con el cursor bloqueado Unity no le
            //    manda toques al IMGUI y los botones viejos (GUI.Button) quedaban muertos. Va
            //    sólo en la partida con Cardboard, donde no se toca nada viejo.
            var muerte = Gameplay.LocalDeath.Instance;
            bool pantallaFinal = (muerte != null && muerte.PresentationReady) || Gameplay.NightResult.Sobrevivio;
            bool partidaConVisor = UsesMouseInput && !pantallaFinal && MRCardboardController.Activo &&
                                   NetworkManager.Instance != null && NetworkManager.Instance.GameStarted;
            // Captura de Android SIEMPRE que el VR BOX esté detectado, en toda la app y con o sin
            // el rediseño: sin ella, el cursor escondido llega al borde y la ruedita se traba
            // (y aparece el cursor). No afecta los toques con el dedo.
            bool captura = UsesMouseInput;

            if (partidaConVisor != _quiereBloqueo)
            {
                _quiereBloqueo = partidaConVisor;
                Cursor.lockState = partidaConVisor ? CursorLockMode.Locked : CursorLockMode.None;
                // Al cambiar el bloqueo Unity pide/suelta la captura de Android por su cuenta: al
                // desbloquear (p. ej. al morir con Cardboard) la soltaba y, como acá seguía
                // "queriéndola", nunca se volvía a pedir — el cursor escondido llegaba al borde
                // y aparecía. Se vuelve a pedir un instante después (Unity la suelta async).
                if (captura) _repedirCapturaEn = Time.unscaledTime + 0.25f;
            }
            if (_repedirCapturaEn > 0f && Time.unscaledTime >= _repedirCapturaEn)
            {
                _repedirCapturaEn = 0f;
                if (captura) PedirCapturaAndroid(true);
            }
            // Vigilante: Android suelta la captura por su cuenta (cambio de bloqueo, notificación,
            // teclado de pantalla, otra vista con foco) sin avisar siempre. Cada medio segundo se
            // fija si la sigue teniendo y, si no, la vuelve a pedir. Sólo con el VR BOX.
            if (captura && _quiereCaptura && Time.unscaledTime >= _vigilarCapturaEn)
            {
                _vigilarCapturaEn = Time.unscaledTime + 0.5f;
                AsegurarCapturaAndroid();
            }
            if (captura == _quiereCaptura) return;
            _quiereCaptura = captura;
            PedirCapturaAndroid(captura);
        }

        private float _repedirCapturaEn;
        private float _vigilarCapturaEn;

        // Pide la captura sólo si se perdió. No con el teclado de pantalla abierto (el foco es
        // del campo de texto: la captura le quedaría a él) ni sin foco de la app (Android la
        // ignora; se reintenta en el próximo chequeo).
        private void AsegurarCapturaAndroid()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Application.isFocused || TouchScreenKeyboard.visible) return;
            ConVistaEnfocada(v =>
            {
                bool tiene = v.Call<bool>("hasPointerCapture");
                PunteroCapturado = tiene;
                if (!tiene) v.Call("requestPointerCapture");
            });
#endif
        }

        private void OnApplicationFocus(bool foco)
        {
            // Android suelta la captura al perder el foco (cortina, otra app): se vuelve a pedir.
            if (foco && _quiereCaptura) PedirCapturaAndroid(true);
        }

        private static void PedirCapturaAndroid(bool capturar)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ConVistaEnfocada(v => v.Call(capturar ? "requestPointerCapture" : "releasePointerCapture"));
#endif
        }

        public void ConsultarCapturaAndroid()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ConVistaEnfocada(v => PunteroCapturado = v.Call<bool>("hasPointerCapture"));
#else
            PunteroCapturado = Cursor.lockState == CursorLockMode.Locked;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Corre en el hilo de UI de Android (las vistas sólo se tocan desde ahí). La captura
        // la tiene que pedir la vista con foco; si no hay, la decorView.
        // La Activity se busca una vez (el vigilante llama esto dos veces por segundo).
        private static AndroidJavaObject _actividad;
        private static bool _avisoFalla;

        private static void ConVistaEnfocada(System.Action<AndroidJavaObject> accion)
        {
            try
            {
                if (_actividad == null)
                {
                    using var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    _actividad = up.GetStatic<AndroidJavaObject>("currentActivity");
                }
                var act = _actividad;
                act.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        var foco = act.Call<AndroidJavaObject>("getCurrentFocus");
                        if (foco != null) { using (foco) accion(foco); return; }
                        using var ventana = act.Call<AndroidJavaObject>("getWindow");
                        using var decor = ventana.Call<AndroidJavaObject>("getDecorView");
                        accion(decor);
                    }
                    catch (System.Exception e) { AvisarFalla(e); }
                }));
            }
            catch (System.Exception e) { AvisarFalla(e); }
        }

        // Una sola vez: con el vigilante, un error repetido llenaría el log dos veces por segundo.
        private static void AvisarFalla(System.Exception e)
        {
            if (_avisoFalla) return;
            _avisoFalla = true;
            Debug.LogWarning($"[GamepadManager] Captura de puntero: {e.Message}");
        }
#endif

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => ImguiGamepadMenu.ResetEscena();

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is UnityEngine.InputSystem.Gamepad)) return;

            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Enabled:
                case InputDeviceChange.Reconnected:
                case InputDeviceChange.Removed:
                case InputDeviceChange.Disabled:
                case InputDeviceChange.Disconnected:
                    Refresh();
                    break;
            }
        }

        // Recalcula el gamepad activo a partir de los conectados.
        private void Refresh()
        {
            // El VR BOX en modo mouse manda siempre: Gamepad.current salta al último gamepad
            // que mandó input, y si el SO registra otro gamepad además del "… Mouse", seguirlo
            // apagaba UsesMouseInput a ratos y con eso la B de la linterna.
            UnityEngine.InputSystem.Gamepad pick = null;
            foreach (var g in UnityEngine.InputSystem.Gamepad.all)
                if (g.added && EsVRBoxMouse(g)) { pick = g; break; }

            if (pick == null) pick = UnityEngine.InputSystem.Gamepad.current;
            if (pick == null || !pick.added)
            {
                pick = null;
                foreach (var g in UnityEngine.InputSystem.Gamepad.all)
                    if (g.added) { pick = g; break; }   // el último conectado disponible
            }

            if (pick == null)
            {
                if (Current != null) Disconnect();
                SondearVRBoxAndroid();
                return;
            }

            if (pick != Current) Adopt(pick);
        }

        // Unity no ve ningún gamepad: ¿Android tiene un VR BOX en modo mouse? Barato (unas
        // pocas llamadas JNI por segundo) y sólo corre en ese caso.
        private void SondearVRBoxAndroid()
        {
            if (Time.unscaledTime < _proximoSondeo) return;
            _proximoSondeo = Time.unscaledTime + SegundosSondeo;

            bool hay = AndroidVRBox.TryFind(out string nombre);
            if (hay == _vrBoxAndroid) return;
            if (!hay) { Disconnect(); return; }

            _vrBoxAndroid = true;
            CambiosDeMando++;
            Brand = GamepadBrand.Generic;
            DisplayName = nombre;
            AlConectar("Android");
        }

        private void Adopt(UnityEngine.InputSystem.Gamepad g)
        {
            bool wasConnected = IsConnected;
            _vrBoxAndroid = false;   // Unity ya lo ve: manda su Gamepad
            Current = g;
            CambiosDeMando++;
            Brand = ClassifyBrand(g);
            DisplayName = string.IsNullOrEmpty(g.displayName) ? g.name : g.displayName;
            if (!wasConnected) AlConectar("Input System");
        }

        private void AlConectar(string via)
        {
            Debug.Log($"[GamepadManager] Conectado: {DisplayName} ({Brand}, vía {via})");
            RefreshBattery();
            // En modo mouse el pendorcho mueve un cursor en Android; lo ocultamos y
            // lo centramos para que tarde más en llegar al borde de pantalla.
            if (UsesMouseInput)
            {
                Cursor.visible = false;
                var m = Mouse.current;
                if (m != null) m.WarpCursorPosition(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            }
            OnConnected?.Invoke();
        }

        private void Disconnect()
        {
            string prev = DisplayName;
            Current = null;
            _vrBoxAndroid = false;
            Brand = GamepadBrand.None;
            DisplayName = "";
            _batteryPresent = false;
            _batteryLevel = 0f;
            Cursor.visible = true;
            Debug.Log($"[GamepadManager] Desconectado: {prev}");
            OnDisconnected?.Invoke();
        }

        // Nivel de batería del mando, 0..1. Devuelve false si el mando no la reporta
        // (muchos gamepads vía USB/algunos por BT no exponen batería).
        public bool TryGetBattery(out float level01)
        {
            level01 = _batteryLevel;
            return _batteryPresent && IsConnected;
        }

        // Lee la batería: primero por el Input System (usage BatteryStrength, raro en
        // gamepads), y si no, por la API nativa de Android (InputDevice.getBatteryState).
        private void RefreshBattery()
        {
            _batteryPresent = false;
            _batteryLevel = 0f;
            if (!IsConnected) return;

            var bat = Current?.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("batteryLevel");
            if (bat != null)
            {
                _batteryLevel = Mathf.Clamp01(bat.ReadValue());
                _batteryPresent = true;
                return;
            }

            if (AndroidControllerBattery.TryGet(out float lvl))
            {
                _batteryLevel = lvl;
                _batteryPresent = true;
            }
        }

        // Clasifica la marca según el tipo de layout y la descripción del device.
        private static GamepadBrand ClassifyBrand(UnityEngine.InputSystem.Gamepad g)
        {
            string t = g.GetType().Name;                // p. ej. XInputControllerWindows
            string layout = g.layout ?? "";
            var d = g.description;
            string hay = $"{t} {layout} {d.product} {d.manufacturer} {d.deviceClass}".ToLowerInvariant();

            if (hay.Contains("xinput") || hay.Contains("xbox"))
                return GamepadBrand.Xbox;
            if (hay.Contains("dualshock") || hay.Contains("dualsense") ||
                hay.Contains("sony") || hay.Contains("playstation"))
                return GamepadBrand.PlayStation;
            if (hay.Contains("switch") || hay.Contains("nintendo") || hay.Contains("pro controller"))
                return GamepadBrand.Switch;
            return GamepadBrand.Generic;
        }

        // Snapshot de las pulsaciones actuales. Default (todo en cero) si no hay
        // gamepad conectado. En modo mouse el pendorcho manda eventos de ratón, así
        // que los mapeamos a las entradas equivalentes del gamepad virtual.
        public GamepadState ReadState()
        {
            var s = new GamepadState();
            if (UsesMouseInput)
            {
                s.leftStick = VRBoxInput.SmoothedStick;
                s.east = VRBoxInput.BVisible;  // botón B (ver VRBoxInput: todos los mouse + escape)
                // A = back de Android, C = vol+, D = vol- → interceptados por el SO, siempre false
                return s;
            }

            var g = Current;
            if (g == null || !g.added) return s;

            s.leftStick  = g.leftStick.ReadValue();
            s.rightStick = g.rightStick.ReadValue();
            s.dpad       = g.dpad.ReadValue();
            s.south  = g.buttonSouth.isPressed;
            s.east   = g.buttonEast.isPressed;
            s.west   = g.buttonWest.isPressed;
            s.north  = g.buttonNorth.isPressed;
            s.l1     = g.leftShoulder.isPressed;
            s.r1     = g.rightShoulder.isPressed;
            s.l2     = g.leftTrigger.ReadValue();
            s.r2     = g.rightTrigger.ReadValue();
            s.l3     = g.leftStickButton.isPressed;
            s.r3     = g.rightStickButton.isPressed;
            s.start  = g.startButton.isPressed;
            s.select = g.selectButton.isPressed;
            return s;
        }
    }
}
