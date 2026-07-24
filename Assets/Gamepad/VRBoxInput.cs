using UnityEngine;
using UnityEngine.InputSystem;

namespace Gamepad
{
    // Input del pendorcho (VR BOX) en modo mouse (@+D): el stick manda Mouse.current.delta,
    // los botones de cara mandan left/right button del mouse.
    // Centraliza la lectura para que no haya accesos directos a Mouse.current dispersos.
    // GamepadManager.Update() la actualiza cada frame cuando UsesMouseInput es true.
    public static class VRBoxInput
    {
        // Delta crudo del stick este frame, en píxeles. (0,0) cuando no hay movimiento.
        public static Vector2 Delta         { get; private set; }
        // Stick suavizado para el visualizador: snappea al desplazamiento cuando llega
        // delta y mantiene la posición durante HoldTime antes de decaer a cero.
        public static Vector2 SmoothedStick { get; private set; }
        // Botón A (izquierdo) — confirmar / acción principal.
        public static bool ConfirmDown      { get; private set; }
        // Botón B (derecho) — cancelar / volver.
        public static bool CancelDown       { get; private set; }

        // Umbral mínimo de delta (px/frame) para considerar el stick en movimiento.
        private const float Threshold = 3f;
        // px/frame equivalente a eje = 1.
        private const float Scale = 12f;
        // Tiempo que mantiene la posición luego del último delta antes de empezar a decaer.
        // Cubre los frames sin delta entre pulsos del pendorcho (que manda a su propia Hz).
        private const float HoldTime = 0.15f;
        // Velocidad de decaimiento del stick cuando el usuario soltó la ruedita (unidades/s).
        private const float Decay = 6f;

        private static float _holdTimer;

        internal static void Tick()
        {
            var gm = GamepadManager.Instance;
            var m  = Mouse.current;
            if (m == null || gm == null || !gm.UsesMouseInput)
            {
                Delta = Vector2.zero;
                SmoothedStick = Vector2.MoveTowards(SmoothedStick, Vector2.zero, Decay * Time.unscaledDeltaTime);
                ConfirmDown = CancelDown = false;
                _holdTimer = 0f;
                return;
            }

            Delta = m.delta.ReadValue();

            if (Delta.magnitude > Threshold)
            {
                // Llegó delta: actualizar posición y resetear el timer de hold.
                SmoothedStick = Vector2.ClampMagnitude(Delta / Scale, 1f);
                _holdTimer = HoldTime;
            }
            else if (_holdTimer > 0f)
            {
                // Dentro del hold: mantener la posición aunque no llegue delta este frame.
                _holdTimer -= Time.unscaledDeltaTime;
            }
            else
            {
                // Fuera del hold: decaer hacia cero (stick soltado).
                SmoothedStick = Vector2.MoveTowards(SmoothedStick, Vector2.zero, Decay * Time.unscaledDeltaTime);
            }

            ConfirmDown = m.leftButton.wasPressedThisFrame;  // click de la ruedita (sin confirmar)
            CancelDown  = m.backButton.wasPressedThisFrame;  // botón B
        }
    }
}
