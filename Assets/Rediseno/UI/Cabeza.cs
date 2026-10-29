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
        // Mitad del campo visual de cada ojo en el visor (PantallaUI dibuja con 90° por ojo).
        public const float MitadVista = 45f;

        // ¿Algún menú lo está usando? (modo "quieto en el cuarto" con el visor). La cuenta
        // regresiva de "poné el celular en el visor" va sólo al empezar a usarlo, no en cada
        // pantalla nueva.
        public static bool EnUso { get; private set; }

        // ¿Ya se pasó por la cuenta regresiva con este visor puesto? Sobrevive a los cambios de
        // escena (la partida apaga el sensor: ahí manda la cámara AR) y se olvida recién al
        // sacarse el visor (ModoVisor). Así volver al menú después de morir no la repite.
        public static bool YaAcomodado { get; private set; }
        public static void OlvidarAcomodo() => YaAcomodado = false;

        public static void Habilitar(bool on)
        {
            EnUso = on;
            if (!on) CuentaRegresiva = 0f;
            var s = AttitudeSensor.current;
            if (s == null) return;
            if (on && !s.enabled) InputSystem.EnableDevice(s);
            else if (!on && s.enabled) InputSystem.DisableDevice(s);
        }

        // Toma la orientación actual como "al frente". Con cuenta regresiva: espera unos
        // segundos para que el jugador meta el celular en el visor antes de fijarla.
        public static void Recentrar(bool conCuentaRegresiva = false)
        {
            if (conCuentaRegresiva) { CuentaRegresiva = 3f; YaAcomodado = true; return; }
            _tieneRef = TryLeer(out var q);
            Yaw = 0f;
            Pitch = 0f;
            if (_tieneRef)
            {
                // La altura no se recentra (es contra el horizonte): queda la real, así la
                // pantalla no pasa un frame por el medio de la vista antes de ir a su lugar.
                var v = Camara(q);
                _rumboRef = Rumbo(v, _rumboRef);
                Pitch = Mathf.Asin(Mathf.Clamp(v.z, -1f, 1f)) * Mathf.Rad2Deg;
            }
#if UNITY_EDITOR
            _yawEd = _pitchEd = 0f;
#endif
        }

        // limitePos / limiteNeg: hasta qué yaw la pantalla todavía se ve asomada (ver ArrastrarAlBorde).
        public static void Tick(float dt, float limitePos, float limiteNeg)
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

            // Mientras se vea más que un borde de la pantalla, queda quieta en el cuarto. Si
            // girás hasta dejarla atrás, no salta ni se pierde: se queda ASOMADA por el borde
            // del campo visual y te acompaña, así siempre se ve que hay un menú y al girar de
            // vuelta aparece enseguida (antes se recentraba de golpe cada 1,5 s; después se
            // iba entera afuera). Mirando casi vertical el rumbo no está definido: no se arrastra.
            if (Mathf.Abs(v.z) <= 0.97f) Yaw = ArrastrarAlBorde(Yaw, limitePos, limiteNeg, ref _rumboRef);
        }

        // Pantalla "a la rastra": `yaw` es hacia dónde mirás respecto de la pantalla (+ = miraste
        // a su derecha, la pantalla queda a tu izquierda). Pasado el límite de ese lado (de la
        // pantalla ya sólo se ve un borde), se corre la referencia para dejarla justo ahí,
        // asomada por el borde de la vista. Devuelve el yaw corregido. También lo usa PantallaUI
        // con la cámara AR; los límites los calcula PantallaUI.LimitesArrastre.
        public static float ArrastrarAlBorde(float yaw, float limitePos, float limiteNeg, ref float referencia)
        {
            if (yaw > limitePos)  { referencia += yaw - limitePos; return limitePos; }
            if (yaw < -limiteNeg) { referencia += yaw + limiteNeg; return -limiteNeg; }
            return yaw;
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
