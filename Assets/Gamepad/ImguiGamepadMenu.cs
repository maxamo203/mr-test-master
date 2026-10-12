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
        private static readonly Rediseno.RepeticionNav _rep = new();

        // Rediseño 2.0: "volver" pedido con el mando (← de la ruedita del VR BOX, ← o B/○ del
        // gamepad). Cada pantalla vieja que tenga a dónde volver se suscribe. Sólo se dispara
        // con Rediseno.Flag prendido; apagado, todo queda como antes.
        public static event Action PedidoVolver;

        // El botón "volver" (MortuoriumTheme.BotonVolver) que se dibujó en el último pase.
        // Si nadie está suscripto a PedidoVolver, la ← lo acciona: así toda pantalla vieja
        // con botón de volver lo hereda sin tocarla.
        private static Action _volverDelPase;
        public static void RegistrarVolver(Action volver) => _volverDelPase = volver;

        private static readonly Color Highlight = new(0.95f, 0.85f, 0.20f);

        private static bool ShowFocus =>
            GamepadManager.Instance != null && GamepadManager.Instance.IsConnected;

        // ¿El PRÓXIMO botón que se dibuje tiene el foco del gamepad? Para menús con
        // dibujo custom (MortuoriumTheme) que pintan su propio resaltado (borde
        // dorado) en vez del tint de backgroundColor.
        public static bool NextHasFocus => ShowFocus && _drawIndex == _focus;

        // Driver: al inicio de CADA pass de OnGUI (lo llama GamepadManager, que corre antes que los
        // menús) resetea el índice; en Repaint arranca de cero la acumulación del frame.
        public static void BeginPass()
        {
            _drawIndex = 0;
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                _frame.Clear();
                _volverDelPase = null;   // lo vuelve a registrar el BotonVolver de este pase
            }
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
            if (_frame.Count != _lastCount)   // cambió el set (pantalla nueva)
            {
                _lastCount = _frame.Count;
                _focus = PrimerFoco();
            }
            if (_frame.Count > 0) _focus = Mathf.Clamp(_focus, 0, _frame.Count - 1);

            var gm = GamepadManager.Instance;
            var gp = gm != null ? gm.Current : null;
            bool useMouse = gm != null && gm.UsesMouseInput;

            if ((gp == null && !useMouse) || _frame.Count == 0) return;

            float dy;
            float dx = 0f;
            bool confirm;
            bool volver = false;

            if (useMouse)
            {
                // VR BOX en modo mouse: delta.y del stick → navegación vertical.
                // Umbral 3 px/frame, escala 12 px/frame = eje completo.
                float rawY = VRBoxInput.Delta.y;
                dy      = Mathf.Abs(rawY) > 3f ? Mathf.Clamp(rawY / 12f, -1f, 1f) : 0f;
                float rawX = VRBoxInput.Delta.x;
                dx      = Mathf.Abs(rawX) > 3f ? Mathf.Clamp(rawX / 12f, -1f, 1f) : 0f;
                // Rediseño 2.0: eje suavizado (el crudo llega a pulsos y entre pulsos parece
                // soltado, lo que hacía que mantener la ruedita recorriera todo el menú).
                if (Rediseno.Flag.Activo) { dy = VRBoxInput.SmoothedStick.y; dx = VRBoxInput.SmoothedStick.x; }
                // Rediseño 2.0: del VR BOX sólo cuenta la ruedita (→ elige), no la A.
                confirm = !Rediseno.Flag.Activo && VRBoxInput.ConfirmDown;
            }
            else
            {
                dy = gp.dpad.ReadValue().y;
                if (Mathf.Abs(dy) < 0.5f) dy = gp.leftStick.ReadValue().y;
                confirm = gp.buttonSouth.wasPressedThisFrame;
                Vector2 h = gp.dpad.ReadValue();
                dx = Mathf.Abs(h.x) >= 0.5f ? h.x : gp.leftStick.ReadValue().x;
                volver = gp.buttonEast.wasPressedThisFrame;
            }

            // Rediseño 2.0: en todos los menús → elige y ← vuelve, de a un golpe (hay que
            // volver al centro para repetir). Así toda la app se maneja con el joystick.
            // Con el bloqueo compartido entre pantallas (Rediseno.BloqueoNav): después de cada
            // acción hay que soltar la palanca y esperar un instante, así el empuje que eligió
            // no se lee otra vez en la pantalla siguiente.
            if (Rediseno.Flag.Activo)
            {
                bool libre = Rediseno.BloqueoNav.Libre(dx, dy);
                if (libre && Mathf.Abs(dx) > 0.6f && Mathf.Abs(dx) > Mathf.Abs(dy))
                {
                    if (dx > 0f) confirm = true; else volver = true;
                }
                if (!libre) { confirm = false; volver = false; }

                if (volver)
                {
                    Rediseno.BloqueoNav.TrasAccion();
                    if (PedidoVolver != null) { AudioManager.Sonar(c => c.uiVolver); PedidoVolver(); return; }
                    if (_volverDelPase != null) { _volverDelPase(); return; }   // ya suena solo
                }
                if (confirm) Rediseno.BloqueoNav.TrasAccion();
            }

            if (Rediseno.Flag.Activo)
            {
                // Repetición tipo teclado: un paso, pausa, ritmo fijo.
                int paso = _rep.Tick(Mathf.Abs(dy) >= Mathf.Abs(dx) ? dy : 0f, Time.unscaledDeltaTime);
                if (paso != 0) MoveFocus(paso > 0 ? -1 : 1);
            }
            else
            {
                _navCd -= Time.unscaledDeltaTime;
                if (Mathf.Abs(dy) > 0.5f)
                {
                    if (_navCd <= 0f) { MoveFocus(dy < 0 ? 1 : -1); _navCd = 0.18f; }
                }
                else _navCd = 0f;
            }

            if (confirm && _focus >= 0 && _focus < _frame.Count && _frame[_focus].enabled)
                _frame[_focus].action?.Invoke();
        }

        // Foco inicial de una pantalla nueva: la primera opción habilitada que NO sea el
        // botón "volver" (es lo primero que se dibuja, arriba a la izquierda, y con el foco ahí
        // un → de más te devolvía a la pantalla anterior).
        private static int PrimerFoco()
        {
            if (!Rediseno.Flag.Activo) return 0;
            for (int i = 0; i < _frame.Count; i++)
                if (_frame[i].enabled && _frame[i].action != _volverDelPase) return i;
            return 0;
        }

        private static void MoveFocus(int dir)
        {
            int n = _frame.Count;
            for (int s = 0; s < n; s++)
            {
                _focus = (_focus + dir + n) % n;
                // El tic va sólo cuando el foco ATERRIZA en un ítem, no en cada vuelta del
                // bucle que saltea los deshabilitados. Corre desde Tick() (una vez por
                // frame, fuera de OnGUI) y con el cooldown de _navCd, así que no se repite.
                if (_frame[_focus].enabled) { AudioManager.Sonar(c => c.uiNavegar); return; }
            }
        }
    }
}
