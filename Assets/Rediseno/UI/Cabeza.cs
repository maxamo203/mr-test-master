using UnityEngine;
using UnityEngine.InputSystem;

namespace Rediseno.UI
{
    // Hacia dónde mira la cabeza en los menús con visor, en grados. Yaw: respecto del rumbo
    // del último recentrado. Pitch: respecto del HORIZONTE (gravedad), no de cómo tenías la
    // cabeza al recentrar: así la pantalla aparece siempre a la altura de los ojos aunque en
    // ese momento estuvieras mirando el piso (antes quedaba anclada abajo y había que bajar
    // la cabeza, forzando el cuello). En los menús no hay sesión AR (gastaría batería
    // para nada), así que sale del sensor de orientación del celular (3 grados de libertad
    // alcanzan para apuntar a una pantalla flotante).
    //
    // Se asume el visor apaisado con la parte de arriba del celular a la izquierda
    // (ScreenOrientation.LandscapeLeft, lo que fija ModoVisor). En el Editor no hay sensor:
    // se mira con las flechas del teclado.
    public static class Cabeza
    {
        public static float Yaw   { get; private set; }   // + derecha
        public static float Pitch { get; private set; }   // + arriba

        public static bool  EnCuentaRegresiva => CuentaRegresiva > 0f;
        public static float CuentaRegresiva { get; private set; }

        private static float _rumboRef;   // rumbo (grados, en el plano horizontal) de "al frente"
        private static bool _tieneRef;
        private static float _lejosT;

        public static void Habilitar(bool on)
        {
            var s = AttitudeSensor.current;
            if (s == null) return;
            if (on && !s.enabled) InputSystem.EnableDevice(s);
            else if (!on && s.enabled) InputSystem.DisableDevice(s);
        }

        // Toma la orientación actual como "al frente". Con cuenta regresiva: espera unos
        // segundos para que el jugador meta el celular en el visor antes de fijarla.
        public static void Recentrar(bool conCuentaRegresiva = false)
        {
            if (conCuentaRegresiva) { CuentaRegresiva = 3f; return; }
            _tieneRef = TryLeer(out var q);
            if (_tieneRef) _rumboRef = Rumbo(Camara(q), _rumboRef);
            Yaw = Pitch = 0f;
#if UNITY_EDITOR
            _yawEd = _pitchEd = 0f;
#endif
        }

        public static void Tick(float dt, float anguloPanel)
        {
            if (CuentaRegresiva > 0f)
            {
                CuentaRegresiva -= dt;
                if (CuentaRegresiva <= 0f) { CuentaRegresiva = 0f; Recentrar(); }
                Yaw = Pitch = 0f;
                return;
            }

#if UNITY_EDITOR
            if (AttitudeSensor.current == null || !_tieneRef) { TickEditor(dt); return; }
#endif
            if (!_tieneRef) { Recentrar(); return; }
            if (!TryLeer(out var q)) return;

            // Dirección de la cámara en el marco del mundo del sensor (Z = arriba).
            Vector3 v = Camara(q);
            Pitch = Mathf.Asin(Mathf.Clamp(v.z, -1f, 1f)) * Mathf.Rad2Deg;
            Yaw = Mathf.DeltaAngle(_rumboRef, Rumbo(v, _rumboRef + Yaw));

            // Si mirás muy lejos de la pantalla un rato (te diste vuelta, el sensor derivó),
            // la pantalla vuelve a ponerse delante.
            if (Mathf.Abs(Yaw) > anguloPanel * 0.5f + 35f || Mathf.Abs(Pitch) > 55f)
            {
                _lejosT += dt;
                if (_lejosT > 1.5f) { _lejosT = 0f; Recentrar(); }
            }
            else _lejosT = 0f;
        }

        // La orientación del sensor (Android: rotation vector; iOS: CoreMotion) lleva del marco
        // del celular al del mundo, con Z vertical hacia arriba en los dos. La cámara mira por
        // −Z del celular. La compensación de pantalla apaisada del Input System gira alrededor
        // de ese mismo eje (value * q), así que no cambia esta dirección.
        private static Vector3 Camara(Quaternion q) => q * Vector3.back;

        // Rumbo en el plano horizontal (+ hacia la derecha). Mirando casi derecho arriba o abajo
        // no está definido: se mantiene el anterior.
        private static float Rumbo(Vector3 v, float anterior) =>
            Mathf.Abs(v.z) > 0.97f ? anterior : Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;

        private static bool TryLeer(out Quaternion q)
        {
            var s = AttitudeSensor.current;
            if (s == null || !s.enabled) { q = Quaternion.identity; return false; }
            q = s.attitude.ReadValue();
            return q != new Quaternion(0f, 0f, 0f, 0f);
        }

#if UNITY_EDITOR
        private static float _yawEd, _pitchEd;
        private static void TickEditor(float dt)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            const float vel = 40f;
            if (kb.leftArrowKey.isPressed)  _yawEd   -= vel * dt;
            if (kb.rightArrowKey.isPressed) _yawEd   += vel * dt;
            if (kb.upArrowKey.isPressed)    _pitchEd += vel * dt;
            if (kb.downArrowKey.isPressed)  _pitchEd -= vel * dt;
            Yaw = _yawEd; Pitch = _pitchEd;
        }
#endif
    }
}
