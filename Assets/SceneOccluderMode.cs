using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Scanner;

// Modos de visualizacion para la geometria escaneada durante gameplay:
//   Original:          materiales semitransparentes del scanner.
//   InvisibleOccluder: paredes/cubos invisibles que solo escriben profundidad.
//   SolidDebug:        paredes/cubos opacos visibles que ocultan lo que queda detras.
//
// SolidDebug sirve para probar en PC la lectura espacial equivalente a produccion:
// solo se modifican paredes y obstaculos del SceneRegistry, nunca las entidades 3D.
//
// Es un toggle: el botón que lo activa/desactiva lo dibuja GameBootstrapper en la
// pantalla de sala (bajo el de Cardboard), llamando a Enabled/Toggle. Se auto-crea
// via RuntimeInitializeOnLoadMethod, no hay que ponerlo en la escena.
//
// Tecnica: swap del sharedMaterial de cada MeshRenderer al material "Hidden/
// SceneOccluder" (depth-only, ColorMask 0). Guarda el material original para
// restaurarlo al apagar. Ver SceneOccluder.shader para el detalle de la cola.
public class SceneOccluderMode : MonoBehaviour
{
    public enum DisplayMode
    {
        Original,
        InvisibleOccluder,
        SolidDebug,
    }

    public static SceneOccluderMode Instance { get; private set; }

    [Tooltip("Lado del quad oclusor de piso, en metros.")]
    [SerializeField] private float _floorQuadSize = 20f;

    [ColorUsage(false)]
    [SerializeField] private Color _solidDebugColor = new(0.12f, 0.14f, 0.16f, 1f);

    private DisplayMode _mode;
    private Material _occluderMat;
    private Material _solidDebugMat;
    private GameObject _floorQuad;
    // Material original de cada renderer, para restaurar al apagar.
    private readonly Dictionary<MeshRenderer, Material> _saved = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("SceneOccluderMode");
        DontDestroyOnLoad(go);
        go.AddComponent<SceneOccluderMode>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // API publica: activar/desactivar el modo (para un Toggle de UI o por codigo).
    public bool Enabled
    {
        get => _mode == DisplayMode.InvisibleOccluder;
        set { if (value) Apply(); else Restore(); }
    }

    public bool SolidDebugEnabled => _mode == DisplayMode.SolidDebug;
    public DisplayMode Mode => _mode;

    public void Toggle() => SetMode(Enabled ? DisplayMode.Original
                                            : DisplayMode.InvisibleOccluder);

    public void ToggleSolidDebug() => SetMode(SolidDebugEnabled ? DisplayMode.Original
                                                                : DisplayMode.SolidDebug);

    private Material OccluderMat()
    {
        if (_occluderMat == null)
        {
            var sh = Shader.Find("Hidden/SceneOccluder");
            if (sh == null)
            {
                Debug.LogError("[SceneOccluderMode] Shader 'Hidden/SceneOccluder' no encontrado. " +
                               "Agregalo a Project Settings > Graphics > Always Included Shaders.");
                return null;
            }
            _occluderMat = new Material(sh) { name = "SceneOccluderMat (runtime)" };
        }
        return _occluderMat;
    }

    private Material SolidDebugMat()
    {
        if (_solidDebugMat == null)
        {
            var sh = Resources.Load<Shader>("SceneSolidDebug") ?? Shader.Find("Hidden/SceneSolidDebug");
            if (sh == null)
            {
                Debug.LogError("[SceneOccluderMode] Shader 'Hidden/SceneSolidDebug' no encontrado.");
                return null;
            }
            _solidDebugMat = new Material(sh)
            {
                name = "SceneSolidDebugMat (runtime)",
                color = _solidDebugColor,
            };
        }
        return _solidDebugMat;
    }

    private void SetMode(DisplayMode mode)
    {
        if (_mode == mode) return;

        RestoreRenderers();
        if (_floorQuad != null) _floorQuad.SetActive(false);
        _mode = DisplayMode.Original;

        if (mode == DisplayMode.InvisibleOccluder)
            Apply();
        else if (mode == DisplayMode.SolidDebug)
            ApplySolidDebug();
    }

    public void Apply()
    {
        var mat = OccluderMat();
        if (mat == null) return;

        ApplySceneMaterial(mat);

        EnsureFloorQuad(mat);
        if (_floorQuad != null) _floorQuad.SetActive(true);

        _mode = DisplayMode.InvisibleOccluder;
    }

    // Disponible exclusivamente en Editor/development build. A diferencia del modo
    // invisible, muestra las superficies opacas y deja que el depth buffer decida que
    // paredes, obstaculos y entidades realmente son visibles desde la camara.
    public void ApplySolidDebug()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) return;

        var solid = SolidDebugMat();
        var floorOccluder = OccluderMat();
        if (solid == null || floorOccluder == null) return;

        ApplySceneMaterial(solid);
        // No dibujamos un piso artificial gris: conserva la oclusion inferior sin
        // reemplazar visualmente el entorno real capturado por la camara.
        EnsureFloorQuad(floorOccluder);
        if (_floorQuad != null) _floorQuad.SetActive(true);
        _mode = DisplayMode.SolidDebug;
    }

    private void ApplySceneMaterial(Material material)
    {
        var reg = SceneRegistry.Instance;
        if (reg == null) return;

        foreach (var w in reg.Walls)
            if (w != null) ApplyTo(w.GetComponent<MeshRenderer>(), material);
        foreach (var c in reg.Cubes)
            if (c != null) ApplyTo(c.GetComponent<MeshRenderer>(), material);
    }

    private void ApplyTo(MeshRenderer mr, Material mat)
    {
        if (mr == null) return;
        if (!_saved.ContainsKey(mr)) _saved[mr] = mr.sharedMaterial; // guardamos el original
        mr.sharedMaterial = mat;
    }

    public void Restore()
    {
        RestoreRenderers();
        if (_floorQuad != null) _floorQuad.SetActive(false);
        _mode = DisplayMode.Original;
    }

    private void RestoreRenderers()
    {
        foreach (var kvp in _saved)
            if (kvp.Key != null) kvp.Key.sharedMaterial = kvp.Value;
        _saved.Clear();
    }

    // Quad oclusor horizontal al nivel del piso (FloorPoint). Hijo de WorldOrigin
    // para que siga al anchor en recalibraciones, igual que el resto del mapa.
    private void EnsureFloorQuad(Material mat)
    {
        if (FloorPoint.Instance == null || WorldOrigin.Instance == null) return;

        if (_floorQuad == null)
        {
            _floorQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _floorQuad.name = "FloorOccluder";
            // Sin collider: solo oclusion visual (igual que ARPlaneOccluder).
            var col = _floorQuad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var mr = _floorQuad.GetComponent<MeshRenderer>();
            mr.sharedMaterial        = mat;
            mr.shadowCastingMode     = ShadowCastingMode.Off;
            mr.receiveShadows        = false;
            mr.lightProbeUsage       = LightProbeUsage.Off;
            mr.reflectionProbeUsage  = ReflectionProbeUsage.Off;
        }

        // El Quad nativo esta en XY (normal +Z); lo rotamos para que quede
        // horizontal. Cull Off en el shader => la orientacion exacta no importa.
        var tr = _floorQuad.transform;
        tr.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
        tr.localPosition = FloorPoint.Instance.LocalPosition;
        tr.localRotation = Quaternion.Euler(90f, 0f, 0f);
        tr.localScale    = new Vector3(_floorQuadSize, _floorQuadSize, 1f);
    }

    private void OnDestroy()
    {
        if (_floorQuad != null) Destroy(_floorQuad);
        if (_occluderMat != null && _occluderMat.name.Contains("(runtime)")) Destroy(_occluderMat);
        if (_solidDebugMat != null && _solidDebugMat.name.Contains("(runtime)")) Destroy(_solidDebugMat);
        if (Instance == this) Instance = null;
    }
}
