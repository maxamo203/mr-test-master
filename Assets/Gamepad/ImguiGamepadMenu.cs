using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamepad
{
    // Navegación con gamepad de menús IMGUI, con UN foco GLOBAL que abarca los botones de TODOS
    // los menús visibles en el frame. Así, p. ej., en el lobby del host se navega entre el toggle
    // "Modo Cardboard" (GameBootstrapper) y "Empezar Partida" (ARLobbyUI) como una sola lista.
    //
    // Cada menú tiene un campo `ImguiGamepadMenu _nav` y dibuja sus botones con `_nav.Button(...)`
    // en su OnGUI. El estado de navegación es ESTÁTICO (compartido) y lo maneja GamepadManager:
    //   - GamepadManager.OnGUI (execution order bajo, corre ANTES que los menús) → BeginPass()
    //   - GamepadManager.Update → Tick()
    // Begin/End/Update de instancia quedaron no-op (compat con el call-site de los menús).
    //
    // Click de mouse/touch invoca la Action al toque; el gamepad (dpad/stick vertical + South) la
    // invoca en Tick(). El foco se resalta solo si hay mando conectado. Ítems con GUI.enabled=false
    // se saltean al navegar y no se activan.
    public class ImguiGamepadMenu
    {
        // ── API de instancia (los menús la usan; el estado real es estático/global) ──
        public void Begin()  { }
        public void End()    { }
        public void Update() { }

        public bool Button(string label, Action onClick, params GUILayoutOption[] options)
            => DoButtonLayout(label, onClick, options);

        public bool Button(Rect rect, string label, GUIStyle style, Action onClick)
            => DoButtonRect(rect, label, style, onClick);

        // ── Estado global compartido entre todos los menús ──
        private struct Item { public Action action; public bool enabled; }
        private static readonly List<Item> _frame = new();   // botones dibujados este frame (se arma en Repaint)
        private static int   _drawIndex;                      // índice corriente dentro del pass (para el highlight)
        private static int   _focus;
        private static int   _lastCount = -1;
        private static float _navCd;

        private static readonly Color Highlight = new(0.95f, 0.85f, 0.20f);

        private static bool ShowFocus =>
            GamepadManager.Instance != null && GamepadManager.Instance.IsConnected;

        // Driver: al inicio de CADA pass de OnGUI (lo llama GamepadManager, que corre antes que los
        // menús) resetea el índice; en Repaint arranca de cero la acumulación del frame.
        public static void BeginPass()
        {
            _drawIndex = 0;
            if (Event.current != null && Event.current.type == EventType.Repaint) _frame.Clear();
        }

        private static bool DoButtonLayout(string label, Action onClick, GUILayoutOption[] options)
        {
            bool focused = ShowFocus && _drawIndex == _focus;
            var prev = GUI.backgroundColor;
            if (focused) GUI.backgroundColor = Highlight;
            bool clicked = GUILayout.Button(label, options);
            GUI.backgroundColor = prev;
            if (clicked) onClick?.Invoke();
            if (Event.current.type == EventType.Repaint)
                _frame.Add(new Item { action = onClick, enabled = GUI.enabled });
            _drawIndex++;
            return clicked;
        }

        private static bool DoButtonRect(Rect rect, string label, GUIStyle style, Action onClick)
        {
            bool focused = ShowFocus && _drawIndex == _focus;
            var prev = GUI.backgroundColor;
            if (focused) GUI.backgroundColor = Highlight;
            bool clicked = GUI.Button(rect, label, style);
            GUI.backgroundColor = prev;
            if (clicked) onClick?.Invoke();
            if (Event.current.type == EventType.Repaint)
                _frame.Add(new Item { action = onClick, enabled = GUI.enabled });
            _drawIndex++;
            return clicked;
        }

        // Driver: 1 vez por frame (GamepadManager.Update). Navega y activa; usa la lista que quedó
        // armada en el Repaint del frame anterior.
        public static void Tick()
        {
            if (_frame.Count != _lastCount) { _focus = 0; _lastCount = _frame.Count; }  // cambió el set
            if (_frame.Count > 0) _focus = Mathf.Clamp(_focus, 0, _frame.Count - 1);

            var gp = GamepadManager.Instance != null ? GamepadManager.Instance.Current : null;
            if (gp == null || _frame.Count == 0) return;

            float dy = gp.dpad.ReadValue().y;
            if (Mathf.Abs(dy) < 0.5f) dy = gp.leftStick.ReadValue().y;

            _navCd -= Time.unscaledDeltaTime;
            if (Mathf.Abs(dy) > 0.5f)
            {
                if (_navCd <= 0f) { MoveFocus(dy < 0 ? 1 : -1); _navCd = 0.18f; }
            }
            else _navCd = 0f;

            if (gp.buttonSouth.wasPressedThisFrame &&
                _focus >= 0 && _focus < _frame.Count && _frame[_focus].enabled)
                _frame[_focus].action?.Invoke();
        }

        private static void MoveFocus(int dir)
        {
            int n = _frame.Count;
            for (int s = 0; s < n; s++)
            {
                _focus = (_focus + dir + n) % n;
                if (_frame[_focus].enabled) return;
            }
        }
    }
}
