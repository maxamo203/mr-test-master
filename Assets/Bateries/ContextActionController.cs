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

    // Hub del BOTÓN PRIMARIO (A del joystick / botón en pantalla / tecla E en editor).
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

        private void Update()
        {
            ResolveCurrent();
            UpdatePrimaryGesture();
        }

        private void UpdatePrimaryGesture()
        {
            EnsureFlashlight();
            bool wasPressed = _primaryGesture.IsPressed;
            PrimaryGestureEvent gesture = _primaryGesture.Tick(
                PrimaryHeld(), Time.unscaledDeltaTime, brightHoldSeconds);

            if (!wasPressed && _primaryGesture.IsPressed)
                _pressedAction = _current;

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

        private void CancelPrimaryGesture()
        {
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

            // VR Box Mouse: el botón A/trigger manda left click de mouse.
            if (GamepadManager.Instance != null && GamepadManager.Instance.UsesMouseInput)
            {
                var mouse = Mouse.current;
                if (mouse != null) held |= mouse.leftButton.isPressed;
            }

#if UNITY_EDITOR
            if (Keyboard.current != null) held |= Keyboard.current.eKey.isPressed;
#endif
            return held || _screenPrimaryHeld;
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

            if (!showActionButton || _current == null || !_current.ShowActionButton ||
                string.IsNullOrEmpty(_currentLabel))
            {
                _screenPrimaryHeld = false;
                return;
            }

            bool pad = GamepadManager.Instance != null && GamepadManager.Instance.IsConnected;
            string holdTime = brightHoldSeconds.ToString("0.#");
            string hint = pad
                ? $"{_currentLabel} (A) · mantener {holdTime} s: intensa"
                : $"{_currentLabel}\nMantener {holdTime} s: luz intensa";
            const float w = 360f, h = 82f;
            var btn = new Rect(vw * 0.5f - w * 0.5f, vh * 0.62f, w, h);
            bool held = GUI.RepeatButton(btn, hint);
            if (Event.current.type == EventType.Repaint)
                _screenPrimaryHeld = held;
        }
    }
}
