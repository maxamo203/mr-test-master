using System.Collections.Generic;
using Gamepad;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bateries
{
    // Hub del BOTÓN PRIMARIO (A del joystick / botón en pantalla / tecla E en editor).
    // Cada frame elige, entre las acciones registradas, la disponible de mayor Priority y
    // la ejecuta al presionar. Así el mismo botón hace cosas distintas según el contexto:
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

        private readonly List<IContextAction> _actions = new();
        private IContextAction _current;
        private string         _currentLabel;
        private bool           _prevSouth;
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
            if (Instance == this) Instance = null;
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnBatteryCollected -= HandleCollected;
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
            if (PrimaryPressed()) _current?.Execute();
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

        // Botón primario: cara del gamepad, left click en modo VR Box Mouse, o E en editor.
        private bool PrimaryPressed()
        {
            var g = GamepadManager.Instance?.Current;
            bool south = g != null && (g.buttonSouth.isPressed || g.buttonEast.isPressed ||
                                       g.buttonNorth.isPressed || g.buttonWest.isPressed);

            // VR Box Mouse: el botón A/trigger manda left click de mouse.
            if (GamepadManager.Instance != null && GamepadManager.Instance.UsesMouseInput)
            {
                var mouse = Mouse.current;
                if (mouse != null) south |= mouse.leftButton.isPressed;
            }

            bool pressed = south && !_prevSouth;
            _prevSouth   = south;
#if UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) pressed = true;
#endif
            return pressed;
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
            if (!showActionButton || _current == null || !_current.ShowActionButton ||
                string.IsNullOrEmpty(_currentLabel)) return;

            bool pad = GamepadManager.Instance != null && GamepadManager.Instance.IsConnected;
            string hint = pad ? $"{_currentLabel} (A)" : _currentLabel;

            // Estilo Mortuorium (mismo look que el resto de la UII), dentro de la matriz de
            // area segura de UIScale para que quede bien ubicado en todos los dispositivos.
            Scanner.UIScale.Begin();
            float vw = Scanner.UIScale.VirtualWidth, vh = Scanner.UIScale.VirtualHeight;
            const float w = 320f, h = 68f;
            var btn = new Rect(vw * 0.5f - w * 0.5f, vh * 0.62f, w, h);
            MortuoriumTheme.Boton(null, btn, hint, primario: true,
                                  onClick: () => _current?.Execute(), fontSize: 24);
        }
    }
}
