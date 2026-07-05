using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

// Funcionalidad opcional (menú Pausa > Opciones): "Iluminación del entorno en tiempo real".
// Cuando está ON, oscurece el espacio físico y la linterna lo revela en tiempo real por su
// cono (DarknessOverlay) — genérico para Android e iOS. En iPhones con LiDAR además activa
// la malla del ambiente (ARMeshManager) para que el efecto/oclusión respete la geometría
// real; en devices sin meshing (Android / iPhone sin LiDAR) el subsystem es null y no se
// toca (no aparecen polígonos).
//
// Se auto-crea (RuntimeInitializeOnLoadMethod) y persiste el estado en PlayerPrefs, así el
// toggle del menú de pausa funciona sin wiring. Resuelve los componentes de cada escena en
// Start y en cada carga de escena.
[DefaultExecutionOrder(120)] // después de los managers AR / AdaptiveOcclusion
public class EnvironmentLightingController : MonoBehaviour
{
    public static EnvironmentLightingController Instance { get; private set; }

    private const string PrefKey = "env_lighting_rt";

    // Estado persistido. Default ON (preserva la oscuridad de terror actual). El setter
    // guarda y re-aplica en vivo.
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(PrefKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Instance?.Apply();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("EnvironmentLighting");
        go.AddComponent<EnvironmentLightingController>();
        DontDestroyOnLoad(go);
    }

    private DarknessOverlay _darkness;
    private ARMeshManager   _mesh;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start() => Resolve();
    private void OnSceneLoaded(Scene s, LoadSceneMode m) => Resolve();

    // Re-resuelve los componentes de la escena actual (las refs viejas se destruyen al
    // cambiar de escena) y aplica el estado.
    private void Resolve()
    {
        _darkness = FindFirstObjectByType<DarknessOverlay>();
        _mesh     = FindFirstObjectByType<ARMeshManager>();
        Apply();
    }

    public void Toggle() => Enabled = !Enabled;

    public void Apply()
    {
        bool on = Enabled;

        // Genérico (Android + iOS): el DarknessOverlay hace el efecto de la linterna sobre
        // el espacio físico (oscurece todo menos el cono, en tiempo real).
        if (_darkness == null) _darkness = FindFirstObjectByType<DarknessOverlay>();
        if (_darkness != null) _darkness.enabled = on;

        // iPhone con LiDAR: activar/desactivar la malla del ambiente. subsystem != null solo
        // en devices con meshing (LiDAR). El material occluder la deja invisible (solo
        // profundidad), así da oclusión real sin dibujar polígonos.
        if (_mesh == null) _mesh = FindFirstObjectByType<ARMeshManager>();
        if (_mesh != null && _mesh.subsystem != null) _mesh.enabled = on;
    }
}
