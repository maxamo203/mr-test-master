using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gameplay
{
    // Selecciona qué catálogo y progreso usar mientras se prueba desde el Editor.
    // Fuera del Editor conserva el comportamiento esperado: development build usa
    // DEV y cualquier build release usa PROD. La preferencia es local y no se
    // guarda en escenas ni se commitea.
    public static class PlayDataProfile
    {
        private const string EditorPrefKey = "Mortuorium.PlayDataProfile.Production";

        public static bool UseProduction
        {
            get
            {
#if UNITY_EDITOR
                return EditorPrefs.GetBool(EditorPrefKey, false);
#else
                return !Debug.isDebugBuild;
#endif
            }
            set
            {
#if UNITY_EDITOR
                EditorPrefs.SetBool(EditorPrefKey, value);
#endif
            }
        }

        public static bool UseDevelopment => !UseProduction;
        public static string DisplayName => UseProduction ? "PROD" : "DEV";
    }
}
