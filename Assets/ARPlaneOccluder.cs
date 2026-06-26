using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Oclusión por planos cross-platform con persistencia y suavizado.
//
// Diseño:
//   - Para cada ARPlane detectado, creamos un Quad nativo de Unity como hijo
//     de ESTE componente (NO del plano). Esto garantiza que el quad sobrevive
//     aunque ARFoundation destruya el GameObject del plano (cuando ARKit/ARCore
//     deciden mergear planos vecinos, por ejemplo).
//   - Cuando un plano se actualiza (added/updated), guardamos su pose+size como
//     "target" y suavizamos hacia él en Update() con Lerp/Slerp.
//   - Cuando un plano se "removes" según ARFoundation, IGNORAMOS el evento. El
//     quad queda en su última pose conocida — el usuario lo quiere así para
//     mantener todo lo escaneado.
public class ARPlaneOccluder : MonoBehaviour
{
    [Header("Materiales (auto-creados si null)")]
    [SerializeField] private Material _occluderMaterial;
    [SerializeField] private Material _debugMaterial;

    [Header("Comportamiento")]
    [SerializeField] private bool _occludePlanes = true;
    [SerializeField] private bool _showDebug = false;
    [SerializeField] private bool _showHUD = true;

    [Header("Suavizado")]
    [Tooltip("Velocidad de catchup hacia la pose/tamaño objetivo. Mayor = más rápido pero más brusco.")]
    [Range(1f, 20f)]
    [SerializeField] private float _smoothSpeed = 6f;

    [Tooltip("Tamaño mínimo de plano (m) para crear su quad. Más chico = más planos persistidos.")]
    [SerializeField] private float _minPlaneSize = 0.02f;

    [Header("Filtro de alineación")]
    public PlaneAlignmentFilter alignmentFilter = PlaneAlignmentFilter.All;
    public enum PlaneAlignmentFilter { All, HorizontalOnly, VerticalOnly }

    private ARPlaneManager _planeManager;
    private AROcclusionManager _occlusionManager;
    private readonly Dictionary<TrackableId, PlaneState> _planes = new();
    private int _addedCount, _updatedCount, _removedCount, _persistedCount;

    private class PlaneState
    {
        public GameObject quad;
        public Transform  quadTr;     // cache
        public MeshRenderer renderer;
        public Vector3    targetPos;
        public Quaternion targetRot;
        public Vector2    targetSize;
        public bool       initialized; // primer update: snap sin lerp
        public bool       sourceAlive; // ARFoundation todavía trackea el plano
    }

    void Awake()
    {
        _planeManager     = GetComponent<ARPlaneManager>() ?? FindFirstObjectByType<ARPlaneManager>();
        _occlusionManager = FindFirstObjectByType<AROcclusionManager>();

        if (_planeManager == null)
        {
            Debug.LogError("[ARPlaneOccluder] No hay ARPlaneManager en la escena.");
            return;
        }

        _planeManager.planePrefab = null;
        EnsureMaterials();
    }

    void EnsureMaterials()
    {
        if (_occluderMaterial == null)
        {
            var sh = Resources.Load<Shader>("AROccluder");
            if (sh == null) sh = Shader.Find("AR/Occluder");
            if (sh != null) _occluderMaterial = new Material(sh) { name = "AROccluderMat (runtime)" };
            else Debug.LogError("[ARPlaneOccluder] Shader AR/Occluder no encontrado.");
        }

        if (_debugMaterial == null)
        {
            var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (sh != null)
            {
                _debugMaterial = new Material(sh) { name = "ARDebugMat (runtime)" };
                var col = new Color(0.2f, 0.6f, 1f, 0.7f);
                if (_debugMaterial.HasProperty("_Color"))     _debugMaterial.color = col;
                if (_debugMaterial.HasProperty("_BaseColor")) _debugMaterial.SetColor("_BaseColor", col);
                _debugMaterial.renderQueue = 4000;
            }
        }
    }

    void OnEnable()
    {
        if (_planeManager != null)
            _planeManager.trackablesChanged.AddListener(OnPlanesChanged);
    }

    void OnDisable()
    {
        if (_planeManager != null)
            _planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
    }

    void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        foreach (var plane in args.added)   { _addedCount++;   RegisterOrUpdate(plane); }
        foreach (var plane in args.updated) { _updatedCount++; RegisterOrUpdate(plane); }

        // No procesamos args.removed: queremos que los quads persistan en su última
        // pose conocida aunque ARFoundation/ARKit decidan dejar de trackear el plano.
        foreach (var kvp in args.removed)
        {
            _removedCount++;
            if (_planes.TryGetValue(kvp.Key, out var st))
            {
                st.sourceAlive = false;
                _persistedCount++;
            }
        }
    }

    bool MatchesFilter(ARPlane plane)
    {
        switch (alignmentFilter)
        {
            case PlaneAlignmentFilter.HorizontalOnly:
                return plane.alignment == PlaneAlignment.HorizontalUp || plane.alignment == PlaneAlignment.HorizontalDown;
            case PlaneAlignmentFilter.VerticalOnly:
                return plane.alignment == PlaneAlignment.Vertical;
            default:
                return true;
        }
    }

    void RegisterOrUpdate(ARPlane plane)
    {
        if (!MatchesFilter(plane)) return;

        var size = plane.size;
        if (size.x < _minPlaneSize || size.y < _minPlaneSize) return;

        if (!_planes.TryGetValue(plane.trackableId, out var st) || st == null || st.quad == null)
        {
            st = new PlaneState();
            st.quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            st.quad.name = $"PlaneQuad_{plane.trackableId}";
            Destroy(st.quad.GetComponent<Collider>());

            // Parent a NOSOTROS, no al plano. Sobrevive aunque el ARPlane sea destruido.
            st.quadTr = st.quad.transform;
            st.quadTr.SetParent(transform, worldPositionStays: false);
            // Quad nativo está en XY (normal +Z). ARPlane está en XZ (normal +Y).
            // Para alinearlos vamos a aplicar la rotación del plano y luego rotar 90° en X
            // mediante la rotación interna del mesh. Más simple: dejamos la rotación final
            // en quad.transform.rotation directamente, computada como plane.rotation * Euler(90,0,0).

            st.renderer = st.quad.GetComponent<MeshRenderer>();
            st.renderer.shadowCastingMode    = ShadowCastingMode.Off;
            st.renderer.receiveShadows       = false;
            st.renderer.lightProbeUsage      = LightProbeUsage.Off;
            st.renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            _planes[plane.trackableId] = st;
        }

        // Actualizamos targets — el snap/lerp pasa en Update().
        st.targetPos  = plane.transform.position;
        st.targetRot  = plane.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
        st.targetSize = size;
        st.sourceAlive = true;

        // Aplicar material según el toggle actual.
        Material target = _showDebug ? _debugMaterial : _occluderMaterial;
        if (target != null && st.renderer.sharedMaterial != target)
            st.renderer.sharedMaterial = target;

        st.quad.SetActive(_occludePlanes || _showDebug);

        // Primer update: snap directo para evitar que el quad arranque desde origen.
        if (!st.initialized)
        {
            st.quadTr.SetPositionAndRotation(st.targetPos, st.targetRot);
            st.quadTr.localScale = new Vector3(st.targetSize.x, st.targetSize.y, 1f);
            st.initialized = true;
        }
    }

    void Update()
    {
        float t = 1f - Mathf.Exp(-_smoothSpeed * Time.deltaTime); // exponencial estable a cualquier FPS

        foreach (var st in _planes.Values)
        {
            if (st.quad == null || !st.initialized) continue;

            // Lerp pose
            st.quadTr.position = Vector3.Lerp(st.quadTr.position, st.targetPos, t);
            st.quadTr.rotation = Quaternion.Slerp(st.quadTr.rotation, st.targetRot, t);

            // Lerp scale (en 2D porque Z siempre es 1)
            var cur = st.quadTr.localScale;
            float sx = Mathf.Lerp(cur.x, st.targetSize.x, t);
            float sy = Mathf.Lerp(cur.y, st.targetSize.y, t);
            st.quadTr.localScale = new Vector3(sx, sy, 1f);
        }
    }

    void OnDestroy()
    {
        foreach (var st in _planes.Values)
            if (st != null && st.quad != null) Destroy(st.quad);
        _planes.Clear();

        if (_occluderMaterial != null && _occluderMaterial.name.Contains("(runtime)")) Destroy(_occluderMaterial);
        if (_debugMaterial    != null && _debugMaterial.name.Contains("(runtime)"))    Destroy(_debugMaterial);
    }

    // ── HUD ───────────────────────────────────────────────────────────────

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

        int activeTracked = 0, vert = 0, horiz = 0;
        if (_planeManager != null && _planeManager.trackables != null)
        {
            foreach (var p in _planeManager.trackables)
            {
                activeTracked++;
                if (p.alignment == PlaneAlignment.Vertical) vert++;
                else horiz++;
            }
        }

        int persisted = 0;
        foreach (var st in _planes.Values) if (!st.sourceAlive) persisted++;

        string occInfo = "OccMgr: NO";
        if (_occlusionManager != null)
        {
            string sup = "?";
            try
            {
                var desc = _occlusionManager.descriptor;
                sup = desc == null ? "no-desc" : desc.environmentDepthImageSupported.ToString();
            }
            catch { sup = "ERR"; }
            occInfo = $"OccMgr EnvDepth: {_occlusionManager.requestedEnvironmentDepthMode} sup={sup}";
        }

        string txt =
            $"AR Session: {ARSession.state}\n" +
            $"PlaneMgr ena={(_planeManager != null && _planeManager.enabled)} mode={(_planeManager != null ? _planeManager.requestedDetectionMode.ToString() : "n/a")}\n" +
            $"Trackeados ahora: {activeTracked} (H:{horiz} V:{vert})\n" +
            $"Quads totales: {_planes.Count}  (persistidos: {persisted})\n" +
            $"Eventos a/u/r: {_addedCount}/{_updatedCount}/{_removedCount}\n" +
            $"occludePlanes={_occludePlanes}  showDebug={_showDebug}  smooth={_smoothSpeed:F1}\n" +
            $"Occluder: {(_occluderMaterial != null ? _occluderMaterial.shader.name : "NULL")}\n" +
            $"Debug:    {(_debugMaterial != null ? _debugMaterial.shader.name : "NULL")}\n" +
            occInfo;

        var style = new GUIStyle
        {
            fontSize = 28,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            padding = new RectOffset(12, 12, 12, 12)
        };
        style.normal.textColor = Color.yellow;
        style.normal.background = GetHudBg();

        GUI.Label(new Rect(10, 10, Screen.width - 20, 750), txt, style);
    }
}
