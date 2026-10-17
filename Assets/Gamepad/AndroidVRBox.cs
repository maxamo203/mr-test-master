using UnityEngine;

namespace Gamepad
{
    // Busca el VR BOX en modo mouse (@+D) directo en Android (android.view.InputDevice),
    // sin pasar por el Input System de Unity.
    //
    // Por qué: el VR BOX entra a Android como TRES nodos HID ("VR BOX Mouse", "VR BOX",
    // "VR BOX Consumer Control") que Android funde en un único InputDevice. El primero que
    // llega es el mouse (sources=MOUSE) y los de gamepad se suman milisegundos después. Si
    // el mando se conecta con la app abierta, Unity a veces lo consulta en ese instante, lo
    // ve como un mouse y no crea el Gamepad; tampoco lo re-escanea después, ni al volver de
    // segundo plano (verificado con logcat en el SM-S901E). Al abrir la app sí lo ve entero,
    // por eso "había que cerrarla y volver a abrirla". En modo mouse todo el input del VR
    // BOX llega igual por el Mouse de Unity (delta, botones; ver VRBoxInput): lo único que
    // faltaba era saber que el mando está ahí.
    //
    // Mismo criterio que GamepadManager.EsVRBoxMouse: nombre con "Mouse" y fuente GAMEPAD
    // (un mouse Bluetooth común no la tiene). Fuera de Android devuelve false.
    internal static class AndroidVRBox
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const int SOURCE_GAMEPAD = 0x00000401;

        public static bool TryFind(out string nombre)
        {
            nombre = "";
            try
            {
                using var cls = new AndroidJavaClass("android.view.InputDevice");
                int[] ids = cls.CallStatic<int[]>("getDeviceIds");
                if (ids == null) return false;

                foreach (int id in ids)
                {
                    using var dev = cls.CallStatic<AndroidJavaObject>("getDevice", id);
                    if (dev == null) continue;
                    if ((dev.Call<int>("getSources") & SOURCE_GAMEPAD) != SOURCE_GAMEPAD) continue;

                    string n = dev.Call<string>("getName");
                    if (string.IsNullOrEmpty(n) ||
                        n.IndexOf("Mouse", System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                    nombre = n;
                    return true;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[AndroidVRBox] " + e.Message);
            }
            return false;
        }
#else
        public static bool TryFind(out string nombre)
        {
            nombre = "";
            return false;
        }
#endif
    }
}
