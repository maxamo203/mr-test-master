using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Scanner;

// Modo "oclusion" para gameplay: vuelve INVISIBLES las paredes/cubos escaneados
// (las del SceneRegistry) pero que sigan TAPANDO a los Sorken que queden detras,
// y agrega un quad oclusor al nivel del piso (FloorPoint). Asi el jugador no ve
// la geometria del mapa pero los enemigos se ocultan correctamente detras de
// paredes/piso reales.
//
// Es un toggle: un check OnGUI (abajo a la izquierda) para activar/desactivar en
// vivo. Se auto-crea via RuntimeInitializeOnLoadMethod, no hay que ponerlo en la
// escena. El estado tambien se puede manejar por codigo con Enabled.
//
// Tecnica: swap del sharedMaterial de cada MeshRenderer al material "Hidden/
// SceneOccluder" (depth-only, ColorMask 0). Guarda el material original para
// restaurarlo al apagar. Ver SceneOccluder.shader para el detalle de la cola.
public class SceneOccluderMode : MonoBehaviour
{
    public static SceneOccluderMode Instance { get; private set; }

    [Tooltip("Lado del quad oclusor de piso, en metros.")]
    [SerializeField] private float _floorQuadSize = 20f;
    [SerializeField] private bool  _showToggle = true;

    private bool _enabled;
    private Material _occluderMat;
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
        get => _enabled;
        set { if (value) Apply(); else Restore(); }
    }

    public void Toggle() => Enabled = !_enabled;

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

    public void Apply()
    {
        var mat = OccluderMat();
        if (mat == null) return;

        var reg = SceneRegistry.Instance;
        if (reg != null)
        {
            foreach (var w in reg.Walls) if (w != null) ApplyTo(w.GetComponent<MeshRenderer>(), mat);
            foreach (var c in reg.Cubes) if (c != null) ApplyTo(c.GetComponent<MeshRenderer>(), mat);
        }

        EnsureFloorQuad(mat);
        if (_floorQuad != null) _floorQuad.SetActive(true);

        _enabled = true;
    }

    private void ApplyTo(MeshRenderer mr, Material mat)
    {
        if (mr == null) return;
        if (!_saved.ContainsKey(mr)) _saved[mr] = mr.sharedMaterial; // guardamos el original
        mr.sharedMaterial = mat;
    }

    public void Restore()
    {
        foreach (var kvp in _saved)
            if (kvp.Key != null) kvp.Key.sharedMaterial = kvp.Value;
        _saved.Clear();
        if (_floorQuad != null) _floorQuad.SetActive(false);
        _enabled = false;
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
        if (Instance == this) Instance = null;
    }

    // ── Toggle OnGUI ──────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!_showToggle) return;
        // Solo en gameplay (sesion multijugador), no en la escena del escaner.
        if (NetworkManager.Instance == null || !NetworkManager.Instance.InSession) return;

        var style = new GUIStyle(GUI.skin.button) { fontSize = 26, wordWrap = true };
        string label = _enabled ? "Paredes: OCULTAS (oclusor ON)" : "Paredes: visibles (oclusor OFF)";
        if (GUI.Button(new Rect(10, Screen.height - 100, 460, 88), label, style))
            Toggle();
    }
}
