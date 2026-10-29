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
        // Botón B (derecho) — cancelar / volver. Mismo flanco que BPressedThisFrame.
        public static bool CancelDown => BPressedThisFrame;

        // ── Botón B ─────────────────────────────────────────────────────────────
        // El VR BOX NO informa cuándo se suelta la B: en cada pulsación manda bajada y
        // subida pegadas, a <1 ms (verificado con getevent en el motorola edge 30: BTN_SIDE
        // DOWN/UP con 0,2-1,1 ms entre sí, la mantengas lo que la mantengas). Android a veces
        // los junta y Unity ve sólo uno de los dos flancos — o ninguno si se compensan dentro
        // del mismo frame —, así que la B se trata como un PULSO: cualquier flanco (bajada o
        // subida) cuenta como una pulsación, y el otro flanco de la misma pulsación se
        // descarta por tiempo (AntiRebote). "Mantener la B" no existe; la luz intensa va por
        // la palanca (PalancaArriba).
        // Se lee con una InputAction y no por polling porque la acción ve CADA evento, escucha
        // todos los mouse a la vez (Android registra Mouse y Mouse1, y Mouse.current salta
        // entre ellos) y suma el escape: si la app no consume el BUTTON_BACK del mouse,
        // Android lo reenvía como la tecla "atrás".
        private static InputAction _botonB;
        // Botón A como acción (no polling de Mouse.current): ve cada evento de todos los mouse
        // aunque bajada y subida lleguen en el mismo frame. Sólo la bajada cuenta.
        private static InputAction _botonA;
        private static int _aFrame = -1;
        // Una pulsación de la A este frame (Rediseño 2.0: elegir en los menús).
        public static bool APressedThisFrame => ModoMouse && Time.frameCount == _aFrame;
        // Pulsación de A que ya usó un menú. La pausa se abre al SOLTAR la A: sin esto, elegir
        // REANUDAR (o REINTENTAR) con la A la cerraba y al soltar se volvía a abrir.
        private static int _aId, _aConsumida = -1;
        public static void ConsumirA() => _aConsumida = _aId;
        public static bool AConsumida => _aConsumida == _aId;
        private const float AntiRebote = 0.08f;
        private static int   _bFrame = -1;        // frame de la última pulsación
        private static float _bPulsoT = -10f;     // Time.unscaledTime de la última pulsación

        // Una pulsación este frame. Dura un solo frame: para el gesto de la linterna es un
        // toque (apretado un frame, suelto el siguiente).
        public static bool BHeld => BPressedThisFrame;
        public static bool BPressedThisFrame => ModoMouse && Time.frameCount == _bFrame;
        // Para el visualizador: la pulsación queda encendida un instante para que se vea.
        public static bool BVisible => ModoMouse && Time.unscaledTime - _bPulsoT < 0.15f;

        // Estadísticas para el diagnóstico del modal de Control (ControllerDiagnostics).
        public static int    BPulsaciones   { get; private set; }
        // Flancos crudos que vio Unity. Con el mando andando perfecto serían 2 por pulsación;
        // menos que eso = Android juntó los dos flancos en uno.
        public static int    BFlancos       { get; private set; }
        // Llegaron con el mando NO detectado en modo mouse: el juego las ignoró.
        public static int    BIgnoradas     { get; private set; }
        // Dispositivo/control por el que llegó la última B (p. ej. "Mouse1/backButton").
        public static string BFuente        { get; private set; } = "";

        public static void ReiniciarEstadisticas()
        {
            BPulsaciones = BFlancos = BIgnoradas = 0;
            BFuente = "";
        }

        // ── Palanca ─────────────────────────────────────────────────────────────
        // A diferencia de la B, la palanca SÍ informa que se mantiene: mientras está empujada
        // manda un delta cada ~46 ms (máx. 62 ms medido) y al soltarla deja de mandar.
        // SmoothedStick cubre esos huecos con HoldTime, así que "mantener arriba" se puede leer.
        // En la noche: TODO el hemisferio norte = luz intensa y todo el sur = apagada (con una
        // franja muerta a la altura del horizonte), así no hay que apuntar justo arriba o abajo.
        private const float FranjaMuerta = 0.3f;
        public static bool PalancaArriba => ModoMouse && SmoothedStick.y >  FranjaMuerta;
        public static bool PalancaAbajo  => ModoMouse && SmoothedStick.y < -FranjaMuerta;

        // La A apretada en este momento (el botón izquierdo del mouse que manda el VR BOX).
        // OJO: igual que la B, puede llegar con bajada y subida casi juntas y entonces nunca
        // se ve apretada entre dos frames; para no perder toques, quien la use como botón de
        // acción tiene que sumar APressedThisFrame (ver ContextActionController.PrimaryHeld).
        public static bool AHeld => ModoMouse && _botonA != null && _botonA.IsPressed();

        private static bool ModoMouse => GamepadManager.Instance != null && GamepadManager.Instance.UsesMouseInput;

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

        private static void EnsureBotonB()
        {
            if (_botonB != null) return;
            _botonB = new InputAction("VRBoxB", InputActionType.Button);
            _botonB.AddBinding("<Mouse>/backButton");
            _botonB.AddBinding("<Keyboard>/escape");
            _botonB.performed += Flanco;
            _botonB.canceled  += Flanco;
            _botonB.Enable();

            _botonA = new InputAction("VRBoxA", InputActionType.Button);
            _botonA.AddBinding("<Mouse>/leftButton");
            _botonA.performed += _ => { _aFrame = Time.frameCount; _aId++; };
            _botonA.Enable();
        }

        private static void Flanco(InputAction.CallbackContext ctx)
        {
            BFlancos++;
            if (Time.unscaledTime - _bPulsoT < AntiRebote) return;   // otro flanco de la misma pulsación
            _bFrame  = Time.frameCount;
            _bPulsoT = Time.unscaledTime;
            BPulsaciones++;
            if (!ModoMouse) BIgnoradas++;
            BFuente = $"{ctx.control.device.name}/{ctx.control.name}";
        }

        internal static void Tick()
        {
            EnsureBotonB();

            var gm = GamepadManager.Instance;
            var m  = Mouse.current;
            if (m == null || gm == null || !gm.UsesMouseInput)
            {
                Delta = Vector2.zero;
                SmoothedStick = Vector2.MoveTowards(SmoothedStick, Vector2.zero, Decay * Time.unscaledDeltaTime);
                ConfirmDown = false;
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
        }
    }
}
