using System.Text.RegularExpressions;
using UnityEngine;

namespace Gameplay
{
    // US-1.1: chequeo de versión mínima de SO al arrancar el juego (ver Historia de
    // Usuario US-1.1 en la documentación): Android por debajo de 14 o iOS por debajo
    // de 17 no son aceptados. A diferencia de los avisos de NightMenuUI (que se
    // muestran una sola vez), este se re-evalúa en cada arranque.
    //
    // Vive en su propio assembly (Mortuorium.Compatibility.asmdef) sin dependencias
    // del resto del proyecto para poder cubrir el parseo con EditMode tests
    // (Assets/Tests/EditMode/DeviceCompatibilityTests.cs) sin necesitar Play Mode ni
    // un dispositivo real.
    public static class DeviceCompatibility
    {
        public enum CompatResult { Supported, Unsupported, Unknown }

        private const int MinAndroidApi = 34;   // Android 14
        private const int MinIosMajor   = 17;

        public static CompatResult Check()
        {
#if UNITY_EDITOR
            return CompatResult.Supported;
#elif UNITY_ANDROID
            return ParseAndroid(SystemInfo.operatingSystem);
#elif UNITY_IOS
            return ParseIos(SystemInfo.operatingSystem);
#else
            // El juego solo se distribuye para Android/iOS (ver "Límites" en la
            // documentación); cualquier otra plataforma de runtime no es aceptada.
            return CompatResult.Unsupported;
#endif
        }

        // Parseo puro (sin #if de plataforma ni SystemInfo): permite testear los
        // casos borde con strings de ejemplo en vez de depender de hardware real.
        internal static CompatResult ParseAndroid(string operatingSystem)
        {
            // SystemInfo.operatingSystem en Android: "Android OS 14 / API-34 (...)".
            // Se compara el nivel de API (viene de Build.VERSION.SDK_INT) en vez del
            // "14" en texto libre: es más estable entre fabricantes.
            var m = Regex.Match(operatingSystem ?? "", @"API-(\d+)");
            if (!m.Success)
            {
                Debug.LogWarning($"DeviceCompatibility: no pude parsear el API level de '{operatingSystem}'.");
                return CompatResult.Unknown;
            }
            int api = int.Parse(m.Groups[1].Value);
            return api >= MinAndroidApi ? CompatResult.Supported : CompatResult.Unsupported;
        }

        internal static CompatResult ParseIos(string operatingSystem)
        {
            // SystemInfo.operatingSystem en iOS/iPadOS: "iOS 17.4.1" / "iPadOS 17.4.1".
            var m = Regex.Match(operatingSystem ?? "", @"(?:iOS|iPadOS)\s+(\d+)");
            if (!m.Success)
            {
                Debug.LogWarning($"DeviceCompatibility: no pude parsear la versión de '{operatingSystem}'.");
                return CompatResult.Unknown;
            }
            int major = int.Parse(m.Groups[1].Value);
            return major >= MinIosMajor ? CompatResult.Supported : CompatResult.Unsupported;
        }
    }
}
