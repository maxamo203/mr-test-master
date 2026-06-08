using System.IO;
using UnityEngine;

namespace Scanner
{
    // Recibe un archivo .MSCN cuando el SO abre la app con "abrir con":
    //   - Android: lee el Uri del intent de lanzamiento (file:// o content://) via
    //     ContentResolver y lo importa.
    //   - iOS: el plugin MscnNative.mm llama UnitySendMessage("MscnReceiver",
    //     "OnFileReceived", <path>).
    //
    // Importa los bytes (ScanPackage.Import los guarda en disco) y luego dispara la
    // carga a escena (ScanLoader). Si la escena todavia no esta lista, deja el name
    // pendiente y reintenta en Update. Singleton DontDestroyOnLoad; lo crea
    // ScannerSceneBootstrap.
    public class MscnReceiver : MonoBehaviour
    {
        public static MscnReceiver Instance { get; private set; }

        private string _pendingLoadName;

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("MscnReceiver");
            go.AddComponent<MscnReceiver>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

#if UNITY_IOS && !UNITY_EDITOR
            // iOS "abrir con": Unity entrega la URL del archivo por deep link.
            // absoluteURL trae la URL del cold-start; deepLinkActivated, las de warm.
            Application.deepLinkActivated += OnDeepLink;
            if (!string.IsNullOrEmpty(Application.absoluteURL)) OnDeepLink(Application.absoluteURL);
#endif
        }

        private void OnDestroy()
        {
#if UNITY_IOS && !UNITY_EDITOR
            Application.deepLinkActivated -= OnDeepLink;
#endif
        }

        // Recibe una URL de archivo (file:///…/x.mscn) desde el deep link de iOS.
        private void OnDeepLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            string path = url;
            try { if (url.StartsWith("file://")) path = new System.Uri(url).LocalPath; }
            catch { /* dejamos path como vino */ }
            OnFileReceived(path);
        }

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            TryConsumeAndroidIntent();
#endif
        }

        // Llamado por el plugin iOS con el path del archivo recibido (en Inbox/).
        public void OnFileReceived(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                byte[] data = File.ReadAllBytes(path);
                HandleImportedBytes(data);
                try { File.Delete(path); } catch { /* Inbox read-only a veces */ }
            }
            catch (System.Exception e) { Debug.LogWarning($"[MscnReceiver] OnFileReceived: {e.Message}"); }
        }

        private void HandleImportedBytes(byte[] data)
        {
            if (!ScanPackage.LooksValid(data)) return; // no es un .MSCN: ignorar
            var name = ScanPackage.Import(data);
            if (string.IsNullOrEmpty(name)) return;
            _pendingLoadName = name;
            TryLoadPending();
        }

        private void Update()
        {
            if (_pendingLoadName != null) TryLoadPending();
        }

        private void TryLoadPending()
        {
            if (_pendingLoadName == null) return;
            if (SceneRegistry.Instance == null) return; // escena aun no lista; reintenta
            var anchor = Object.FindFirstObjectByType<ARImageAnchor>();
            if (anchor == null) return;

            // CRITICO: al abrir via archivo (cold-start), la carga re-registra la
            // imagen de referencia en la sesion AR. Hacerlo antes de que la sesion
            // este inicializada crashea de forma intermitente. Esperamos a que la
            // sesion AR este levantada (Update reintenta hasta entonces).
            var arState = UnityEngine.XR.ARFoundation.ARSession.state;
            if (arState != UnityEngine.XR.ARFoundation.ARSessionState.SessionInitializing &&
                arState != UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking)
                return;

            if (ScanLoader.Load(_pendingLoadName, anchor))
            {
                Debug.Log($"[MscnReceiver] Escaneo importado y cargado: '{_pendingLoadName}'.");
                _pendingLoadName = null;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void OnApplicationFocus(bool focus)
        {
            if (focus) TryConsumeAndroidIntent();
        }

        private void TryConsumeAndroidIntent()
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                if (intent == null) return;

                // VIEW ("abrir con") => getData(); SEND ("compartir a") => EXTRA_STREAM.
                var uri = intent.Call<AndroidJavaObject>("getData");
                bool fromSend = false;
                if (uri == null)
                {
                    using var ic = new AndroidJavaClass("android.content.Intent");
                    uri = intent.Call<AndroidJavaObject>("getParcelableExtra", ic.GetStatic<string>("EXTRA_STREAM"));
                    fromSend = uri != null;
                }
                if (uri == null) return;

                byte[] data = null;
                using (uri)
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var stream = resolver.Call<AndroidJavaObject>("openInputStream", uri))
                {
                    if (stream != null)
                    {
                        // Copiamos el stream a un temporal con android.os.FileUtils
                        // (API 29+, nuestro minSdk) y lo leemos con C#. Evita un loop
                        // byte-a-byte por JNI (lentisimo).
                        var tmp = Path.Combine(Application.temporaryCachePath, "import." + ScanPackage.Extension);
                        using (var fos = new AndroidJavaObject("java.io.FileOutputStream", tmp))
                        using (var fileUtils = new AndroidJavaClass("android.os.FileUtils"))
                        {
                            fileUtils.CallStatic<long>("copy", stream, fos);
                            fos.Call("flush");
                            fos.Call("close");
                        }
                        stream.Call("close");
                        if (File.Exists(tmp))
                        {
                            data = File.ReadAllBytes(tmp);
                            try { File.Delete(tmp); } catch { }
                        }
                    }
                }

                // Limpiamos el intent para no reimportar al volver a foco.
                intent.Call<AndroidJavaObject>("setData", (AndroidJavaObject)null);
                if (fromSend)
                {
                    using var ic2 = new AndroidJavaClass("android.content.Intent");
                    intent.Call("removeExtra", ic2.GetStatic<string>("EXTRA_STREAM"));
                }

                if (data != null) HandleImportedBytes(data);
            }
            catch (System.Exception e) { Debug.LogWarning($"[MscnReceiver] intent: {e.Message}"); }
        }
#endif
    }
}
