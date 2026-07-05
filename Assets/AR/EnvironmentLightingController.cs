using UnityEngine;
using UnityEngine.SceneManagement;

// Toggle (menú Pausa > Opciones): "Iluminación del entorno (tiempo real)".
// Solo controla el DarknessOverlay (oscurece todo menos el cono de la linterna, en tiempo
// real; genérico Android + iOS). Es el efecto de "linterna de terror" — sirve para probar.
//
// La otra opción, iluminar la MALLA dinámica del entorno con LiDAR/AR, es un feature aparte:
// ver FlashlightMeshLighting.
//
// Se auto-crea (RuntimeInitializeOnLoadMethod) y persiste el estado en PlayerPrefs, así el
// toggle del menú de pausa funciona sin wiring. Resuelve el componente de cada escena en
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

    // Re-resuelve el componente de la escena actual (las refs viejas se destruyen al
    // cambiar de escena) y aplica el estado.
    private void Resolve()
    {
        _darkness = FindFirstObjectByType<DarknessOverlay>();
        Apply();
    }

    public void Toggle() => Enabled = !Enabled;

    // Genérico (Android + iOS): el DarknessOverlay oscurece todo menos el cono de la
    // linterna en tiempo real. La malla iluminada por LiDAR/AR es OTRA opción aparte
    // (ver FlashlightMeshLighting).
    public void Apply()
    {
        if (_darkness == null) _darkness = FindFirstObjectByType<DarknessOverlay>();
        if (_darkness != null) _darkness.enabled = Enabled;
    }
}
