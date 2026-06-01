using UnityEngine;

namespace Scanner
{
    // Escalado uniforme para toda la UI IMGUI (OnGUI) del scanner.
    //
    // IMGUI dibuja en píxeles crudos, así que en pantallas de alta resolución
    // (p. ej. un iPhone moderno) los paneles y textos se ven diminutos y casi no
    // se pueden tocar. Para que la UI se vea IGUAL en todos los dispositivos —
    // ocupando siempre la misma fracción de pantalla — cada OnGUI llama a
    // UIScale.Begin() al entrar: eso aplica una GUI.matrix que escala todo
    // respecto de una resolución de diseño de referencia.
    //
    // Regla de uso dentro de un OnGUI escalado:
    //   - Llamar UIScale.Begin() como primera línea.
    //   - Posicionar usando UIScale.VirtualWidth / VirtualHeight en lugar de
    //     Screen.width / Screen.height. Los tamaños (w, h, fontSize) se quedan en
    //     "unidades de diseño" y el escalado los agranda/achica solo.
    //
    // Como TODOS los paneles comparten el mismo Factor y el mismo espacio virtual,
    // siguen alineados entre sí en cualquier dispositivo.
    public static class UIScale
    {
        // Resolución de diseño (portrait). Los tamaños hardcodeados de los paneles
        // están pensados a ~esta escala. Bajá estos números si querés la UI más
        // grande; subilos si la querés más chica.
        public const float ReferenceWidth  = 420f;
        public const float ReferenceHeight = 1080f;

        // Límites para no romper layouts en pantallas extremas.
        private const float MinFactor = 0.5f;
        private const float MaxFactor = 4f;

        // Factor de escala uniforme. Tomamos el menor de los dos ratios para que la
        // UI entre completa sin importar el aspect ratio del dispositivo.
        public static float Factor =>
            Mathf.Clamp(
                Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight),
                MinFactor, MaxFactor);

        // Dimensiones "virtuales" (pre-escala) en las que hay que dibujar.
        public static float VirtualWidth  => Screen.width  / Factor;
        public static float VirtualHeight => Screen.height / Factor;

        // Llamar al principio de cada OnGUI que quiera escalarse.
        public static void Begin()
        {
            float s = Factor;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        }
    }
}
