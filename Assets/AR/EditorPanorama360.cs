#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Fondo panorámico 360 para el PLAY MODE DEL EDITOR. En el editor no hay feed de cámara
// AR (las subsistemas están stubbeados, ver ARImageAnchor.EditorStub), así que la escena
// se juega contra un fondo vacío y los efectos de pantalla completa — el filtro VHS de
// US-11.1 y la distorsión de US-11.2 — no se pueden evaluar: sin imagen detrás no hay
// nada que granular, distorsionar ni teñir. Este componente pone una equirectangular
// (Assets/Editor/Panorama360/hotel360.jpg) como skybox y manda a la cámara a limpiar
// contra él, de modo que el GrabPass tenga contenido real para trabajar.
//
// TIER: editor-only. El archivo entero está dentro de #if UNITY_EDITOR, así que no se
// compila en NINGÚN build (ni development ni release), y la imagen vive bajo
// Assets/Editor/ — Unity nunca incluye assets de una carpeta Editor en el build. En el
// dispositivo el fondo es, como siempre, la cámara real.
//
// Se prende/apaga desde el menú Mortuorium > Fondo 360 en Play (ver
// Assets/Editor/MortuoriumPanorama360Menu.cs); el estado vive en EditorPrefs, o sea que
// es de esta máquina y no se commitea.
public class EditorPanorama360 : MonoBehaviour
{
    public const string KeyActivo = "mortuorium_fondo360";

    // Se busca por nombre y no por ruta fija: si la imagen se mueve o se reemplaza por
    // otra panorámica, sigue apareciendo sin tocar código.
    private const string NombreTextura = "hotel360";

    public static bool Activo
    {
        get => EditorPrefs.GetBool(KeyActivo, true);
        set => EditorPrefs.SetBool(KeyActivo, value);
    }

    private Material _mat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!Activo) return;
        var go = new GameObject("EditorPanorama360");
        DontDestroyOnLoad(go);
        go.AddComponent<EditorPanorama360>();
    }

    private void Awake()
    {
        var tex = CargarPanoramica();
        if (tex == null)
        {
            Debug.LogWarning($"EditorPanorama360: no encontré la textura '{NombreTextura}'. " +
                             "Poné una equirectangular en Assets/Editor/Panorama360/.");
            enabled = false;
            return;
        }

        var shader = Shader.Find("Skybox/Panoramic");
        if (shader == null)
        {
            Debug.LogWarning("EditorPanorama360: falta el shader 'Skybox/Panoramic'.");
            enabled = false;
            return;
        }

        // Material en memoria: no hace falta un .mat en disco (que sí podría terminar
        // referenciado por una escena y colarse en un build).
        _mat = new Material(shader) { name = "EditorPanorama360Mat", hideFlags = HideFlags.HideAndDontSave };
        _mat.SetTexture("_MainTex", tex);
        _mat.SetFloat("_Mapping",   1f);   // Latitude Longitude Layout (equirectangular)
        _mat.SetFloat("_ImageType", 0f);   // 360 grados
        _mat.SetFloat("_Layout",    0f);   // sin estéreo (una sola imagen)
        _mat.SetFloat("_Exposure",  1f);

        RenderSettings.skybox = _mat;
        DynamicGI.UpdateEnvironment();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Los cambios de RenderSettings son de play mode y se revierten al salir, pero cada
    // escena trae los suyos: hay que volver a ponerlo al cargar.
    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (_mat == null) return;
        RenderSettings.skybox = _mat;
        DynamicGI.UpdateEnvironment();
    }

    // La cámara AR se resuelve/reemplaza durante la sesión (y Cardboard rehace su
    // composición), así que en vez de setearla una vez se re-chequea. Es un Update por
    // frame que SOLO existe en el editor — mismo criterio que el tuning en vivo de
    // ArbmosSmokeAura.
    private void Update()
    {
        var cam = Camera.main;
        if (cam != null && cam.clearFlags != CameraClearFlags.Skybox)
            cam.clearFlags = CameraClearFlags.Skybox;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (RenderSettings.skybox == _mat) RenderSettings.skybox = null;
        if (_mat != null) DestroyImmediate(_mat);
    }

    private static Texture CargarPanoramica()
    {
        foreach (var guid in AssetDatabase.FindAssets($"{NombreTextura} t:Texture2D"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var tex  = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
        }
        return null;
    }
}
#endif
