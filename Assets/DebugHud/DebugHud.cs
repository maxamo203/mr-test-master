using UnityEngine;

// HUD de debug centralizado. TODOS los datos de diagnóstico en pantalla viven
// como hijos de un único GameObject "DebugHud" (DontDestroyOnLoad), un script
// por dato — para apagar todo el debug alcanza con desactivar este GameObject
// (o un hijo puntual para apagar solo ese panel).
//
// Solo existe en development builds / editor (Debug.isDebugBuild): en una build
// release el GameObject directamente no se crea. La visibilidad se puede
// togglear en runtime desde el menú de pausa (Opciones -> Debug HUD) y persiste
// en PlayerPrefs.
//
// Hijos creados:
//   Red               (DebugRedUI)        estado de red / world origin / entidades
//   Escaner           (DebugEscanerUI)    modo FSM + posición del jugador rel. anchor
//   FuenteRaycast     (DebugRaycastUI)    fuente del raycast bajo la retícula
//   DistanciaFallback (DebugFallbackUI)   slider de distancia del raycast fallback
//   Director          (DebugDirectorUI)   fase/timers del GameDirector (solo host)
//   Baterias          (DebugBateriasUI)   estado del spawner de pilas (solo host)
//   Lidar             (DebugLidarUI)      chunks/verts/tris de la malla LiDAR
//   BenchmarkTracking (DebugBenchmarkUI)  reporte del ARTrackingBenchmark
public class DebugHud : MonoBehaviour
{
    private const string KeyVisible = "debughud_visible";

    public static DebugHud Instance { get; private set; }

    // ¿El HUD de debug está activo? (siempre false en builds release)
    public static bool Visible => Instance != null && Instance.gameObject.activeInHierarchy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!Debug.isDebugBuild) return;   // solo development build / editor
        if (Instance != null) return;

        var root = new GameObject("DebugHud");
        DontDestroyOnLoad(root);
        Instance = root.AddComponent<DebugHud>();

        Crear<DebugRedUI>(root, "Red");
        Crear<DebugEscanerUI>(root, "Escaner");
        Crear<DebugRaycastUI>(root, "FuenteRaycast");
        Crear<DebugFallbackUI>(root, "DistanciaFallback");
        Crear<DebugDirectorUI>(root, "Director");
        Crear<DebugBateriasUI>(root, "Baterias");
        Crear<DebugLidarUI>(root, "Lidar");
        Crear<DebugBenchmarkUI>(root, "BenchmarkTracking");

        root.SetActive(PlayerPrefs.GetInt(KeyVisible, 1) == 1);
    }

    private static void Crear<TComp>(GameObject root, string nombre) where TComp : Component
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(root.transform, false);
        go.AddComponent<TComp>();
    }

    public static void SetVisible(bool v)
    {
        if (Instance == null) return;
        Instance.gameObject.SetActive(v);
        PlayerPrefs.SetInt(KeyVisible, v ? 1 : 0);
    }
}

// Estilos compartidos por los paneles de debug (fondo translúcido + monospace).
internal static class DebugHudEstilos
{
    private static Texture2D _bg;
    private static GUIStyle _label;

    public static Texture2D BG()
    {
        if (_bg == null)
        {
            _bg = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
            _bg.Apply();
            _bg.hideFlags = HideFlags.HideAndDontSave;
        }
        return _bg;
    }

    public static GUIStyle Label(Color color, int size = 18)
    {
        // Un solo estilo cacheado alcanza para paneles que se dibujan de a uno;
        // el color/tamaño se pisa por llamada.
        if (_label == null)
        {
            _label = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap  = true,
                padding   = new RectOffset(10, 10, 8, 8),
            };
            _label.normal.background = BG();
        }
        _label.fontSize = size;
        _label.normal.textColor = color;
        return _label;
    }
}
