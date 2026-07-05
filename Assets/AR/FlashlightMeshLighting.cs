using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

// Opción (Pausa > Opciones): "Iluminar malla del entorno".
// Genera en tiempo real la malla del ambiente por AR y la ilumina con la linterna
// (material Custom/FlashlightLitMesh, que revela la superficie donde apunta el cono).
//
//   - iPhone con LiDAR: usa la scene reconstruction de ARKit (ARMeshManager) → malla real.
//   - Android / iPhone sin LiDAR: ARCore no soporta meshing (subsystem == null), así que
//     esta opción queda inactiva en esos devices (no rompe nada). La generación de malla
//     por profundidad (ARCore Depth) queda como extensión futura.
//
// Se auto-crea y persiste el estado en PlayerPrefs; el toggle del menú de pausa la maneja.
// Usa el ARMeshManager de la escena si existe (o crea uno bajo el XROrigin). OJO: si otro
// sistema (AdaptiveOcclusion) también maneja el ARMeshManager, dejá activo solo uno.
[DefaultExecutionOrder(130)]
public class FlashlightMeshLighting : MonoBehaviour
{
    public static FlashlightMeshLighting Instance { get; private set; }

    private const string PrefKey = "env_mesh_light";

    // Opt-in: apagado por defecto.
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(PrefKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Instance?.Apply();
        }
    }

    [Header("Calidad")]
    [Tooltip("Densidad/detalle de la malla (0..1). 1 = máximo. Triángulos más chicos, más " +
             "detalle (y más CPU/GPU). NO cambia la cobertura: los huecos son de zonas no " +
             "escaneadas / fuera de rango / superficies difíciles (vidrio, espejos, negro).")]
    [Range(0.1f, 1f)] [SerializeField] private float meshDensity = 1f;

    [Tooltip("Clasificación ARKit de la malla (pared/piso/etc). Mejora un poco el cierre y " +
             "la calidad de la malla. Solo iPhone con LiDAR.")]
    [SerializeField] private bool enableClassification = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("FlashlightMeshLighting");
        go.AddComponent<FlashlightMeshLighting>();
        DontDestroyOnLoad(go);
    }

    private ARMeshManager _mesh;
    private Material       _litMat;
    private readonly List<MeshRenderer> _chunks = new();
    private bool _wired;

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

    private void Resolve()
    {
        // Las refs de la escena anterior se destruyeron; re-resolver y re-aplicar.
        if (_wired && _mesh != null) _mesh.meshesChanged -= OnMeshesChanged;
        _mesh  = FindFirstObjectByType<ARMeshManager>();
        _wired = false;
        _chunks.Clear();
        Apply();
    }

    public void Toggle() => Enabled = !Enabled;

    public void Apply()
    {
        if (!Enabled) { Teardown(); return; }
        if (!EnsureMesh()) return; // device sin meshing (Android / sin LiDAR)

        _mesh.density = meshDensity;
        if (!_mesh.enabled) _mesh.enabled = true;
        if (!_wired) { _mesh.meshesChanged += OnMeshesChanged; _wired = true; }
    }

    private void Teardown()
    {
        // Solo tocamos el ARMeshManager si NOSOTROS lo activamos (_wired). Así, con la
        // opción apagada, no clobbereamos un mesh manager que maneje otro sistema.
        if (_mesh != null && _wired)
        {
            _mesh.meshesChanged -= OnMeshesChanged;
            _wired = false;
            try { _mesh.DestroyAllMeshes(); } catch { /* sin subsystem: nada que destruir */ }
            _mesh.enabled = false;
        }
        _chunks.Clear();
    }

    // Asegura ARMeshManager + material + prefab. Devuelve false si el device no soporta
    // meshing (subsystem == null → Android / iPhone sin LiDAR).
    private bool EnsureMesh()
    {
        if (_mesh == null)
        {
            var xr = FindFirstObjectByType<XROrigin>();
            if (xr == null) return false;
            // ARMeshManager debe ser hijo del XROrigin (no del mismo GO que otra cosa).
            var holder = new GameObject("FlashlightMeshHolder");
            holder.transform.SetParent(xr.transform, worldPositionStays: false);
            _mesh = holder.AddComponent<ARMeshManager>();
        }

        if (!_mesh.enabled) _mesh.enabled = true;
        if (_mesh.subsystem == null) { _mesh.enabled = false; return false; }

        EnsureMaterial();
        EnsurePrefab();
        TrySetClassificationEnabled(enableClassification);
        return true;
    }

    // Activa la clasificación de la malla en ARKit por reflexión (para no referenciar el
    // assembly Unity.XR.ARKit en compilación; el subsystem real es ARKitMeshSubsystem).
    private void TrySetClassificationEnabled(bool on)
    {
        if (_mesh == null || _mesh.subsystem == null) return;
        var subsystem = _mesh.subsystem;
        if (subsystem.GetType().Name != "ARKitMeshSubsystem") return;

        var method = subsystem.GetType().GetMethod("SetClassificationEnabled",
            BindingFlags.Public | BindingFlags.Instance);
        if (method == null) return;
        try { method.Invoke(subsystem, new object[] { on }); }
        catch (Exception e) { Debug.LogWarning($"[MeshLight] classification: {e.Message}"); }
    }

    private void EnsureMaterial()
    {
        if (_litMat != null) return;
        var sh = Resources.Load<Shader>("FlashlightLitMesh") ?? Shader.Find("Custom/FlashlightLitMesh");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        _litMat = new Material(sh) { name = "FlashlightLitMesh (runtime)" };
    }

    private void EnsurePrefab()
    {
        if (_mesh.meshPrefab != null) return;
        var prefab = new GameObject("FlashlightMeshChunk");
        prefab.SetActive(false);
        prefab.AddComponent<MeshFilter>();
        var mr = prefab.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _litMat;
        _mesh.meshPrefab = prefab.GetComponent<MeshFilter>();
    }

    private void OnMeshesChanged(ARMeshesChangedEventArgs args)
    {
        foreach (var mf in args.added)   Configure(mf);
        foreach (var mf in args.updated) Configure(mf);
        // removed: el ARMeshManager destruye el GO por nosotros.
    }

    private void Configure(MeshFilter mf)
    {
        var mr = mf.GetComponent<MeshRenderer>();
        if (mr == null) return;
        mr.sharedMaterial       = _litMat;
        mr.shadowCastingMode    = ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        if (!_chunks.Contains(mr)) _chunks.Add(mr);
    }
}
