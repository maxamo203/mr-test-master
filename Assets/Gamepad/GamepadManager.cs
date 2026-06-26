using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

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

        // El gamepad activo (null si no hay ninguno).
        public UnityEngine.InputSystem.Gamepad Current { get; private set; }
        public bool IsConnected => Current != null && Current.added;
        public GamepadBrand Brand { get; private set; } = GamepadBrand.None;
        public string DisplayName { get; private set; } = "";

        // Se disparan cuando cambia la conectividad (en tiempo real).
        public event Action OnConnected;
        public event Action OnDisconnected;

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
            // Estado inicial: puede ya haber un gamepad emparejado al arrancar.
            Refresh();
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        private void Update()
        {
            // Si el usuario tiene varios mandos, el Input System actualiza
            // Gamepad.current con la actividad. Adoptamos ese cambio sin esperar a
            // un evento de dispositivo.
            var cur = UnityEngine.InputSystem.Gamepad.current;
            if (cur != null && cur.added && cur != Current)
                Adopt(cur);
        }

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
            UnityEngine.InputSystem.Gamepad pick = UnityEngine.InputSystem.Gamepad.current;
            if (pick == null || !pick.added)
            {
                pick = null;
                foreach (var g in UnityEngine.InputSystem.Gamepad.all)
                    if (g.added) { pick = g; break; }   // el último conectado disponible
            }

            if (pick == null)
            {
                if (Current != null) Disconnect();
                return;
            }

            if (pick != Current) Adopt(pick);
        }

        private void Adopt(UnityEngine.InputSystem.Gamepad g)
        {
            bool wasConnected = IsConnected;
            Current = g;
            Brand = ClassifyBrand(g);
            DisplayName = string.IsNullOrEmpty(g.displayName) ? g.name : g.displayName;
            if (!wasConnected)
            {
                Debug.Log($"[GamepadManager] Conectado: {DisplayName} ({Brand})");
                OnConnected?.Invoke();
            }
        }

        private void Disconnect()
        {
            string prev = DisplayName;
            Current = null;
            Brand = GamepadBrand.None;
            DisplayName = "";
            Debug.Log($"[GamepadManager] Desconectado: {prev}");
            OnDisconnected?.Invoke();
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
        // gamepad conectado.
        public GamepadState ReadState()
        {
            var s = new GamepadState();
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
