using System;
using UnityEngine;

namespace Rediseno.UI
{
    // ¿El jugador está usando la app con el visor puesto? Estado de la SESIÓN (no se
    // persiste): se prende desde el botón del visor del menú y vale para todas las pantallas
    // del rediseño, que se dibujan en estéreo y se manejan con la mirada.
    public static class ModoVisor
    {
        public static bool Activo { get; private set; }
        public static event Action Cambio;

        public static void Set(bool activo)
        {
            if (activo == Activo) return;
            Activo = activo;

            // El visor es apaisado; el menú normal, vertical.
            if (activo)
            {
                Screen.autorotateToLandscapeLeft = true;
                Screen.orientation = ScreenOrientation.LandscapeLeft;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
            else
            {
                Screen.orientation = ScreenOrientation.Portrait;
                Screen.sleepTimeout = SleepTimeout.SystemSetting;
            }
            Cambio?.Invoke();
        }

        // Sacarse el visor desde cualquier lado. En partida el visor es el Cardboard de
        // MRCardboardController: hay que apagarlo a ÉL (que además vuelve la pantalla a
        // vertical). Girar la pantalla sin apagarlo dejaba la vista partida en vertical.
        public static void Salir()
        {
            if (MRCardboardController.Activo)
            {
                var mr = UnityEngine.Object.FindAnyObjectByType<MRCardboardController>();
                if (mr != null) mr.SetCardboard(false);
                Reflejar(false);
                return;
            }
            Set(false);
        }

        // Refleja el estado sin tocar la orientación (en partida la maneja
        // MRCardboardController). Ver PuenteVisor.
        internal static void Reflejar(bool activo)
        {
            if (activo == Activo) return;
            Activo = activo;
            Cambio?.Invoke();
        }

        // Al volver al menú con el visor puesto: la partida pudo dejar la pantalla vertical.
        internal static void ReaplicarOrientacion()
        {
            if (!Activo) return;
            Screen.autorotateToLandscapeLeft = true;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }
}
