using UnityEngine;

namespace Rediseno
{
    // Auto-repetición de la navegación ↑↓ como la de un teclado: un paso al empujar, una
    // pausa larga, y después pasos a ritmo fijo mientras se mantenga. Sin esto, mantener la
    // palanca recorría todas las opciones en un instante.
    //
    // Con la ruedita del VR BOX hay que pasarle el eje SUAVIZADO (VRBoxInput.SmoothedStick):
    // el crudo llega a pulsos cada ~46 ms y entre pulso y pulso parece soltado, lo que
    // reiniciaba la espera y movía otra vez en cada pulso.
    public sealed class RepeticionNav
    {
        public const float PrimeraEspera = 0.45f;
        public const float Ritmo         = 0.30f;

        private bool  _sostenido;
        private float _t;

        // Devuelve +1 / -1 cuando hay que dar un paso en el sentido del eje, 0 si no.
        public int Tick(float eje, float dt)
        {
            float a = Mathf.Abs(eje);
            if (a < 0.5f)
            {
                if (a < 0.4f) _sostenido = false;   // histéresis tolerante al drift de la ruedita
                return 0;
            }
            int dir = eje > 0f ? 1 : -1;
            if (!_sostenido)
            {
                _sostenido = true;
                _t = PrimeraEspera;
                return dir;
            }
            _t -= dt;
            if (_t > 0f) return 0;
            _t = Ritmo;
            return dir;
        }
    }
}
