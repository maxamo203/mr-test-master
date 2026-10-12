using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gameplay
{
    // Selecciona qué catálogo de noches (y qué progreso) usar.
    //   - Editor: menú Mortuorium (EditorPrefs, por máquina).
    //   - Development build: por defecto DEV, pero se puede cambiar EN EL JUEGO desde
    //     OPCIONES → DESARROLLO del menú principal (PlayerPrefs, por dispositivo), para
    //     probar en un build dev con los datos de prod.
    //   - Release: siempre PROD; el override de PlayerPrefs ni se lee.
    // La preferencia es local y no se guarda en escenas ni se commitea.
    public static class PlayDataProfile
    {
        private const string EditorPrefKey = "Mortuorium.PlayDataProfile.Production";
        private const string DevBuildPrefKey = "dev_perfil_noches_prod";

        public static bool UseProduction
        {
            get
            {
#if UNITY_EDITOR
                return EditorPrefs.GetBool(EditorPrefKey, false);
#else
                return !Debug.isDebugBuild || PlayerPrefs.GetInt(DevBuildPrefKey, 0) == 1;
#endif
            }
            set
            {
#if UNITY_EDITOR
                EditorPrefs.SetBool(EditorPrefKey, value);
#else
                if (!Debug.isDebugBuild) return;
                PlayerPrefs.SetInt(DevBuildPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
#endif
            }
        }

        public static bool UseDevelopment => !UseProduction;
        public static string DisplayName => UseProduction ? "PROD" : "DEV";
    }
}
