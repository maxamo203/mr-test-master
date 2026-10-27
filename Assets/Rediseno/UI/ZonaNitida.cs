using UnityEngine;

namespace Rediseno.UI
{
    // Las lentes del visor sólo enfocan bien cerca de su eje: la composición estéreo no
    // pre-corrige la distorsión (como hace el SDK de Cardboard), así que lejos del centro
    // de cada ojo todo se ve borroso. Con la pantalla fija a la cabeza no se puede bajar
    // la mirada hacia lo de abajo: hay que traerlo a esta zona. Grados respecto del
    // frente (+ arriba).
    public static class ZonaNitida
    {
        // Radio, alrededor del eje de cada lente, que se ve nítido. Ajustar en el dispositivo.
        public const float Radio = 18f;
        // Suavizado del deslizamiento (1/s, exponencial).
        public const float Velocidad = 10f;

        // Cuánto subir una tarjeta cuyo borde inferior cae a `bordeInferior` grados para
        // que no pase de -Radio. Nunca la baja.
        public static float SubirTarjeta(float bordeInferior) => Mathf.Max(0f, -Radio - bordeInferior);

        // Los menús, en cambio, se deslizan para que la opción enfocada quede SIEMPRE en el
        // centro de la lente (deslizamiento = -centroFoco, en PantallaUI): con una banda de
        // tolerancia las últimas opciones quedaban en su borde, todavía fuera de foco.
        // Menú principal (PantallaUI.PisoVisor): su última opción no baja de este ángulo bajo
        // el centro, así casi todo el menú queda a la altura de la vista o arriba. Ajustar en
        // el dispositivo: más chico = menú más alto.
        public const float PisoMenu = 12f;

        // Píxeles de pantalla por grado en cada ojo (misma convención que PantallaUI:
        // media pantalla = Cabeza.MitadVista * 2 grados).
        public static float PixelesPorGrado => Screen.width * 0.5f / (Cabeza.MitadVista * 2f);
    }
}
