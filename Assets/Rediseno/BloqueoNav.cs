using UnityEngine;

namespace Rediseno
{
    // Bloqueo compartido por TODOS los menús (nuevos y viejos) para que una acción del mando
    // no se arrastre a la pantalla siguiente. Sin esto, elegir con la palanca → (o volver
    // con ←) cambiaba de pantalla con la palanca todavía empujada, y la pantalla nueva leía
    // ese mismo empuje como otra acción: se volvía atrás solo.
    //
    // Después de cada elegir/volver hay que (1) soltar la palanca hasta el centro y (2) que
    // pase un instante. Recién ahí el eje horizontal vuelve a valer.
    public static class BloqueoNav
    {
        private const float Espera = 0.35f;
        // La ruedita barata "tiembla" (drift): manda deltas chicos aunque esté soltada, y
        // con un umbral de centro muy fino el bloqueo no se liberaba NUNCA (→ dejaba de
        // elegir). Umbral más tolerante + un tope: pasado ese tiempo se libera igual.
        private const float UmbralCentro = 0.4f;
        private const float MaxEsperaCentro = 0.8f;
        private static float _hasta, _desde;
        private static bool  _esperaCentro;

        // Llamar cada vez que una entrada del mando elige, vuelve o cambia de pantalla.
        public static void TrasAccion()
        {
            _hasta = Time.unscaledTime + Espera;
            _desde = Time.unscaledTime;
            _esperaCentro = true;
        }

        // ¿Puede el eje horizontal disparar elegir/volver ahora? Llamar cada frame con los
        // ejes actuales (-1..1): también es lo que detecta la vuelta al centro.
        public static bool Libre(float dx, float dy)
        {
            float m = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            // Soltada, o temblando por debajo de "empujar" (0,6) durante un rato. Empujada a
            // fondo a propósito NO se libera nunca: si no, mantenerla volvía a disparar.
            if (_esperaCentro &&
                (m < UmbralCentro || (m < 0.6f && Time.unscaledTime - _desde > MaxEsperaCentro)))
                _esperaCentro = false;
            return !_esperaCentro && Time.unscaledTime >= _hasta;
        }
    }
}
