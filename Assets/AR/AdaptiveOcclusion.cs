using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Detecta las capacidades de oclusión del dispositivo y elige automáticamente
// la mejor estrategia disponible:
//
//   1. Mesh         (iPhone Pro con LiDAR) — malla real del ambiente +
//                   AROcclusionManager con EnvDepth para pixel-perfect.
//   2. EnvDepth     (Android con ARCore Depth API) — oclusión por profundidad
//                   per-pixel sin malla. ARCameraBackground la integra al renderer
//                   automáticamente.
//   3. Planes       (iPhone normal sin LiDAR, Android sin Depth) — fallback al
//                   ARPlaneOccluder existente con quads parentados.
//
// Setup en escena:
//   - AROcclusionManager en la cámara AR (cualquier EnvDepthMode — lo ajustamos)
//   - ARPlaneManager + ARPlaneOccluder en algún GO
//   - ARMeshManager OPCIONAL: si no existe, se agrega en runtime cuando hace falta
//
// Asignar este script en cualquier GameObject de la escena.
[DefaultExecutionOrder(100)] // Después de los managers AR
public class AdaptiveOcclusion : MonoBehaviour
{
    public enum Strategy { Unknown, Planes, EnvironmentDepth, Mesh }

    [Header("Componentes (auto-resuelto si null)")]
    [SerializeField] private AROcclusionManager _occlusionManager;
    [SerializeField] private ARPlaneManager     _planeManager;
    [SerializeField] private ARPlaneOccluder    _planeOccluder;
    [SerializeField] private ARMeshManager      _meshManager;

    [Header("Mesh occluder (LiDAR)")]
    [Tooltip("Material para chunks de malla. Si null, usa AR/Occluder.")]
    [SerializeField] private Material _meshOccluderMaterial;
    [Tooltip("Densidad de la malla. Más alto = mejor calidad, más CPU/GPU. LiDAR rinde bien en 1.0.")]
    [Range(0f, 1f)]
    [SerializeField] private float _meshDensity = 1f;

    [Header("Preferencias")]
    [Tooltip("Si true, prioriza ARMeshManager (LiDAR). Si false, solo usa EnvDepth aunque haya LiDAR.")]
    [SerializeField] private bool _preferMeshWhenLidar = true;
    [Tooltip("Mostrar HUD con la estrategia elegida.")]
    [SerializeField] private bool _showHUD = true;

    public Strategy ChosenStrategy { get; private set; } = Strategy.Unknown;
    public string DiagnosticInfo { get; private set; } = "Inicializando...";

    private void Awake()
    {
        if (_occlusionManager == null) _occlusionManager = FindFirstObjectByType<AROcclusionManager>();
        if (_planeManager     == null) _planeManager     = FindFirstObjectByType<ARPlaneManager>();
        if (_planeOccluder    == null) _planeOccluder    = FindFirstObjectByType<ARPlaneOccluder>();
        if (_meshManager      == null) _meshManager      = FindFirstObjectByType<ARMeshManager>();
    }

    private IEnumerator Start()
    {
        // Esperamos a que ARSession esté inicializada para que los descriptors
        // de los subsystems reporten capacidades reales del dispositivo.
        yield return new WaitUntil(() =>
            ARSession.state == ARSessionState.SessionInitializing ||
            ARSession.state == ARSessionState.SessionTracking ||
            ARSession.state == ARSessionState.Ready);

        // Damos un frame más por seguridad para que los descriptors se poblen.
        yield return null;

        bool envDepthSupported = CheckEnvDepthSupport();
        bool meshSupported     = CheckMeshSupport();

        if (meshSupported && _preferMeshWhenLidar)
        {
            ChosenStrategy = Strategy.Mesh;
            ApplyMeshStrategy();
        }
        else if (envDepthSupported)
        {
            ChosenStrategy = Strategy.EnvironmentDepth;
            ApplyEnvDepthStrategy();
        }
        else
        {
            ChosenStrategy = Strategy.Planes;
            ApplyPlanesStrategy();
        }

        DiagnosticInfo =
            $"Strategy: {ChosenStrategy}\n" +
            $"EnvDepth supported: {envDepthSupported}\n" +
            $"Mesh supported: {meshSupported}\n" +
            $"OS reports: {SystemInfo.deviceModel}";

        Debug.Log($"[AdaptiveOcclusion] {DiagnosticInfo.Replace('\n', ' ')}");
    }

    // ── Detección de capacidades ─────────────────────────────────────────

    private bool CheckEnvDepthSupport()
    {
        if (_occlusionManager == null) return false;
        var desc = _occlusionManager.descriptor;
        if (desc == null) return false;
        return desc.environmentDepthImageSupported == Supported.Supported;
    }

    private bool CheckMeshSupport()
    {
        // ARMeshManager.subsystem es null si el dispositivo no soporta meshing.
        // En iOS solo está disponible con LiDAR; en Android prácticamente nunca.
        if (_meshManager == null)
        {
            // Probamos a agregar uno temporal para preguntarle al subsystem.
            var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin == null) return false;
            var probe = xrOrigin.gameObject.AddComponent<ARMeshManager>();
            bool supported = probe.subsystem != null;
            // Si no es soportado, lo destruimos. Si sí, lo guardamos para usarlo.
            if (supported) _meshManager = probe;
            else Destroy(probe);
            return supported;
        }
        return _meshManager.subsystem != null;
    }

    // ── Estrategias ──────────────────────────────────────────────────────

    private void ApplyMeshStrategy()
    {
        // EnvDepth pixel-perfect para integración con virtual content.
        if (_occlusionManager != null)
        {
            _occlusionManager.requestedEnvironmentDepthMode    = EnvironmentDepthMode.Best;
            _occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            _occlusionManager.enabled = true;
        }

        // Mesh real del ambiente (LiDAR).
        EnsureMeshManager();
        if (_meshManager != null)
        {
            _meshManager.density = _meshDensity;
            _meshManager.enabled = true;
        }

        // Apagamos planos: no aportan nada extra cuando tenés mesh real.
        if (_planeOccluder != null) _planeOccluder.enabled = false;
        if (_planeManager  != null) _planeManager.enabled  = false;
    }

    private void ApplyEnvDepthStrategy()
    {
        // EnvDepth: ARCameraBackground lo integra al pipeline automáticamente.
        if (_occlusionManager != null)
        {
            _occlusionManager.requestedEnvironmentDepthMode    = EnvironmentDepthMode.Best;
            _occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            _occlusionManager.enabled = true;
        }

        // Apagamos mesh manager si existía.
        if (_meshManager != null) _meshManager.enabled = false;

        // Planos siguen activos por si los necesitás para raycast/spawn, pero
        // el occluder de planos se apaga (la oclusión la hace EnvDepth).
        if (_planeManager  != null) _planeManager.enabled  = true;
        if (_planeOccluder != null) _planeOccluder.enabled = false;
    }

    private void ApplyPlanesStrategy()
    {
        // Sin LiDAR/Depth: caemos a la oclusión por planos clásica.
        if (_occlusionManager != null)
        {
            _occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
        }
        if (_meshManager   != null) _meshManager.enabled   = false;
        if (_planeManager  != null) _planeManager.enabled  = true;
        if (_planeOccluder != null) _planeOccluder.enabled = true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void EnsureMeshManager()
    {
        if (_meshManager == null) return;

        // ARMeshManager necesita un prefab con MeshFilter + MeshRenderer.
        // Si no se asignó uno, lo creamos en runtime con el material occluder.
        if (_meshManager.meshPrefab == null)
        {
            if (_meshOccluderMaterial == null)
            {
                var sh = Resources.Load<Shader>("AROccluder") ?? Shader.Find("AR/Occluder");
                if (sh != null) _meshOccluderMaterial = new Material(sh) { name = "MeshOccluderMat (runtime)" };
            }

            var prefab = new GameObject("MeshChunk");
            prefab.SetActive(false);
            prefab.AddComponent<MeshFilter>();
            var mr = prefab.AddComponent<MeshRenderer>();
            if (_meshOccluderMaterial != null) mr.sharedMaterial = _meshOccluderMaterial;
            mr.shadowCastingMode    = ShadowCastingMode.Off;
            mr.receiveShadows       = false;
            mr.lightProbeUsage      = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // ARMeshManager espera MeshFilter — los chunks de mesh se le
            // asignan al MeshFilter.sharedMesh internamente por el manager.
            _meshManager.meshPrefab = prefab.GetComponent<MeshFilter>();
        }
    }

    // ── HUD ──────────────────────────────────────────────────────────────

    static Texture2D _hudBgTex;
    static Texture2D GetHudBg()
    {
        if (_hudBgTex == null)
        {
            _hudBgTex = new Texture2D(1, 1);
            _hudBgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.85f));
            _hudBgTex.Apply();
        }
        return _hudBgTex;
    }

    void OnGUI()
    {
        if (!_showHUD) return;

        var style = new GUIStyle
        {
            fontSize = 26,
            alignment = TextAnchor.UpperRight,
            wordWrap = true,
            padding = new RectOffset(12, 12, 12, 12)
        };
        style.normal.textColor = new Color(0.4f, 1f, 0.6f, 1f);
        style.normal.background = GetHudBg();

        GUI.Label(new Rect(Screen.width - 520, 10, 500, 200),
                  $"[AdaptiveOcclusion]\n{DiagnosticInfo}", style);
    }
}
