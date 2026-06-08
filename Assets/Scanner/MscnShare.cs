using UnityEngine;

namespace Scanner
{
    // Abre la hoja de compartir del sistema con un archivo (el .MSCN exportado).
    //   - Android: Intent.ACTION_SEND con Uri via FileProvider (AndroidJavaObject,
    //     sin plugin compilado).
    //   - iOS: UIActivityViewController via el plugin Assets/Plugins/iOS/MscnNative.mm.
    public static class MscnShare
    {
        public static void Share(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogWarning("[MscnShare] path vacío.");
                return;
            }
#if UNITY_EDITOR
            Debug.Log($"[MscnShare] (editor) se compartiría: {filePath}");
#elif UNITY_ANDROID
            ShareAndroid(filePath);
#elif UNITY_IOS
            _MscnShareFile(filePath);
#else
            Debug.Log($"[MscnShare] plataforma sin share nativo: {filePath}");
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _MscnShareFile(string path);
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void ShareAndroid(string filePath)
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                string packageName = activity.Call<string>("getPackageName");
                // Authority propia (no .fileprovider) para no chocar con la que Unity
                // pueda declarar. Debe coincidir con el <provider> del AndroidManifest.
                string authority = packageName + ".mscnprovider";

                using var file = new AndroidJavaObject("java.io.File", filePath);
                using var providerClass = new AndroidJavaClass("androidx.core.content.FileProvider");
                using var uri = providerClass.CallStatic<AndroidJavaObject>("getUriForFile", activity, authority, file);

                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent");
                intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                intent.Call<AndroidJavaObject>("setType", "application/octet-stream");
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_STREAM"), uri);
                intent.Call<AndroidJavaObject>("addFlags", intentClass.GetStatic<int>("FLAG_GRANT_READ_URI_PERMISSION"));

                using var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Compartir escaneo");
                activity.Call("startActivity", chooser);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MscnShare] Falló el share en Android: {e.Message}\n" +
                               "Verificá el <provider> FileProvider en el AndroidManifest y que " +
                               "androidx.core esté disponible en el build.");
            }
        }
#endif
    }
}
