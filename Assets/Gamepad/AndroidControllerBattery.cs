using UnityEngine;

namespace Gamepad
{
    // Lee el nivel de batería de un mando conectado usando la API nativa de Android
    // android.view.InputDevice.getBatteryState() (disponible desde API 29, que es el
    // mínimo del proyecto). El Input System de Unity NO expone batería para gamepads
    // estándar (DualShock/Xbox/Switch), así que en Android vamos por JNI.
    //
    // Escanea los input devices de tipo gamepad/joystick y devuelve la primera batería
    // "presente". Es suficiente para el caso de un único mando. En plataformas que no
    // son Android (o en el Editor) el stub devuelve false.
    internal static class AndroidControllerBattery
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Constantes de android.view.InputDevice.
        private const int SOURCE_GAMEPAD  = 0x00000401;
        private const int SOURCE_JOYSTICK = 0x01000010;

        public static bool TryGet(out float level01)
        {
            level01 = 0f;
            try
            {
                using var cls = new AndroidJavaClass("android.view.InputDevice");
                int[] ids = cls.CallStatic<int[]>("getDeviceIds");
                if (ids == null) return false;

                foreach (int id in ids)
                {
                    using var dev = cls.CallStatic<AndroidJavaObject>("getDevice", id);
                    if (dev == null) continue;

                    int sources = dev.Call<int>("getSources");
                    bool isPad = (sources & SOURCE_GAMEPAD)  == SOURCE_GAMEPAD ||
                                 (sources & SOURCE_JOYSTICK) == SOURCE_JOYSTICK;
                    if (!isPad) continue;

                    // getBatteryState() existe desde API 29; el try/catch cubre runtimes viejos.
                    using var bat = dev.Call<AndroidJavaObject>("getBatteryState");
                    if (bat == null) continue;
                    if (!bat.Call<bool>("isPresent")) continue;

                    float cap = bat.Call<float>("getCapacity"); // 0..1 (puede ser NaN)
                    if (float.IsNaN(cap) || cap < 0f) continue;

                    level01 = Mathf.Clamp01(cap);
                    return true;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[AndroidControllerBattery] " + e.Message);
            }
            return false;
        }
#else
        public static bool TryGet(out float level01)
        {
            level01 = 0f;
            return false;
        }
#endif
    }
}
