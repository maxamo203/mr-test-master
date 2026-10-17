using UnityEngine;
using UnityEngine.InputSystem;

namespace Rediseno.UI
{
    // Hacia dónde mira la cabeza en los menús con visor, en grados respecto de "al frente"
    // (el momento del último recentrado). En los menús no hay sesión AR (gastaría batería
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

        private static Quaternion _ref = Quaternion.identity;
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
            _tieneRef = TryLeer(out _ref);
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
            if (!_tieneRef) { _tieneRef = TryLeer(out _ref); return; }
            if (!TryLeer(out var q)) return;

            // Dirección de la cámara (−Z del celular) en el marco de referencia.
            Vector3 v = (Quaternion.Inverse(_ref) * q) * Vector3.back;
            if (InputSystem.settings.compensateForScreenOrientation)
            {
                // Ya compensado a la pantalla apaisada: arriba = +Y, derecha = +X.
                Yaw = Mathf.Atan2(v.x, -v.z) * Mathf.Rad2Deg;
                Pitch = Mathf.Asin(Mathf.Clamp(v.y, -1f, 1f)) * Mathf.Rad2Deg;
            }
            else
            {
                // Ejes del celular en LandscapeLeft: arriba = +X, derecha = −Y.
                Yaw = Mathf.Atan2(-v.y, -v.z) * Mathf.Rad2Deg;
                Pitch = Mathf.Asin(Mathf.Clamp(v.x, -1f, 1f)) * Mathf.Rad2Deg;
            }

            // Si mirás muy lejos de la pantalla un rato (te diste vuelta, el sensor derivó),
            // la pantalla vuelve a ponerse delante.
            if (Mathf.Abs(Yaw) > anguloPanel * 0.5f + 35f || Mathf.Abs(Pitch) > 55f)
            {
                _lejosT += dt;
                if (_lejosT > 1.5f) { _lejosT = 0f; Recentrar(); }
            }
            else _lejosT = 0f;
        }

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
