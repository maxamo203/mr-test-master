using UnityEngine;

namespace Rediseno
{
    // Ajustes de la "franja de la linterna" (Cardboard sin joystick, ver ControlMirada):
    // levantar la vista hasta cruzar una franja que rodea al jugador a cierta altura prende o
    // apaga la linterna al instante. En release valen los de fábrica; en development build se
    // ajustan desde la pausa (OPCIONES → FRANJA LINTERNA (DEV)) y quedan en PlayerPrefs.
    public static class FranjaLinternaAjustes
    {
        public const float AlturaFabrica = 32f, EsperaFabrica = 0.5f;

        // Grados por encima del horizonte donde está la franja.
        public static float Altura
        {
            get => Leer("dev_franja_altura", AlturaFabrica);
            set => Guardar("dev_franja_altura", Mathf.Clamp(value, 20f, 70f));
        }

        // Segundos después de un cambio antes de que la franja vuelva a funcionar (además hay
        // que bajar la vista por debajo de ella).
        public static float Espera
        {
            get => Leer("dev_franja_espera", EsperaFabrica);
            set => Guardar("dev_franja_espera", Mathf.Clamp(value, 0.1f, 2f));
        }

        // Mostrar en la mira la altura de la vista y el último cambio.
        public static bool MostrarDebug
        {
            get => Debug.isDebugBuild && PlayerPrefs.GetInt("dev_franja_debug", 0) == 1;
            set { if (Debug.isDebugBuild) PlayerPrefs.SetInt("dev_franja_debug", value ? 1 : 0); }
        }

        // Lecturas para el debug (las escribe ControlMirada).
        public static float AlturaVista;
        public static string UltimoCambio = "";

        private static float Leer(string clave, float fabrica) =>
            Debug.isDebugBuild ? PlayerPrefs.GetFloat(clave, fabrica) : fabrica;

        private static void Guardar(string clave, float valor)
        {
            if (Debug.isDebugBuild) PlayerPrefs.SetFloat(clave, valor);
        }
    }
}
