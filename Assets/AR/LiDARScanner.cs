using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

// Setup dedicado para iPhone Pro / iPad Pro con LiDAR.
// Hace lo que las apps de escaneo dedicadas (Polycam/Scaniverse) hacen en
// modo "live scan":
//
//   - ARMeshManager con density = 1 (maxima resolucion).
//   - Activa scene reconstruction con clasificacion en ARKit (mejora la
//     calidad de la malla y la captura de bordes/aberturas).
//   - Apaga ARPlaneManager y ARPlaneOccluder (los planos sintetizados son
//     cuadrilateros grandes que tapan aberturas como puertas y ventanas;
//     la malla LiDAR si las preserva).
//   - Visualizacion alternable entre:
//        * Occluder: invisible, escribe solo depth (para gameplay).
//        * Wireframe: muestra la malla real (verifica cobertura del scan).
//     Tap en pantalla o tecla M cicla entre ambos.
//   - HUD con conteo de chunks, vertices y triangulos.
//
// Setup en escena:
//   1. Pegalo en cualquier GameObject (idealmente el XROrigin).
//   2. Es no-op si el dispositivo no tiene LiDAR (se autodisable).
[DefaultExecutionOrder(200)]
public class LiDARScanner : MonoBehaviour
{
    public enum VisualMode { Occluder, Wireframe }

    [Header("Componentes (auto si null)")]
    [SerializeField] private XROrigin           _xrOrigin;
    [SerializeField] private ARMeshManager      _meshManager;
    [SerializeField] private ARPlaneManager     _planeManager;
    [SerializeField] private ARPlaneOccluder    _planeOccluder;
    [SerializeField] private AROcclusionManager _occlusionManager;

    [Header("Config")]
    [Tooltip("Density del mesh (0..1). LiDAR rinde mejor en 1.0.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float _meshDensity = 1f;

    [Tooltip("Apagar ARPlaneManager cuando hay LiDAR. Los planos tapan aberturas.")]
    [SerializeField] private bool _disablePlanes = true;

    [Tooltip("Activar clasificacion ARKit (walls/floors/doors/windows). Mejora la malla aunque no la dibujes coloreada.")]
    [SerializeField] private bool _enableClassification = true;

    [Header("Visualizacion")]
    [SerializeField] private VisualMode _mode = VisualMode.Occluder;
    [SerializeField] private bool _showHUD     = true;
    [SerializeField] private bool _tapToToggle = true;

    private Material _occluderMat;
    private Material _wireframeMat;

    // chunks instanciados -> renderer (para repintar al cambiar de modo)
    private readonly List<MeshRenderer> _chunkRenderers = new();
    // Throttling de cooking de MeshCollider para no destruir FPS al recibir
    // updates frecuentes de chunks. Mantiene el ultimo Time.time por collider.
    private readonly Dictionary<MeshCollider, float> _lastCookTime = new();
    private const float MeshColliderCookCooldown = 1f;
    // Layer name para que el RaycastResolver pueda priorizar la mesh LiDAR.
    public const string LiDARMeshLayerName = "LiDARMesh";
    private int _lidarMeshLayer = -1;
    private bool _lidarActive;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_xrOrigin         == null) _xrOrigin         = FindFirstObjectByType<XROrigin>();
        if (_meshManager      == null) _meshManager      = FindFirstObjectByType<ARMeshManager>();
        if (_planeManager     == null) _planeManager     = FindFirstObjectByType<ARPlaneManager>();
        if (_planeOccluder    == null) _planeOccluder    = FindFirstObjectByType<ARPlaneOccluder>();
        if (_occlusionManager == null) _occlusionManager = FindFirstObjectByType<AROcclusionManager>();
    }

    private IEnumerator Start()
    {
        // El subsystem de meshing no reporta soporte hasta que ARKit termina
        // de inicializar — esperamos a que la sesion este realmente tracking.
        // Damos hasta 10s; si no llega, igual intentamos.
        float tStart = Time.realtimeSinceStartup;
        while (ARSession.state != ARSessionState.SessionTracking &&
               Time.realtimeSinceStartup - tStart < 10f)
            yield return null;
        yield return null;
        yield return null;

        // Reintentar el setup hasta 5 veces (1 frame entre intentos) por si el
        // mesh subsystem tarda en aparecer aunque la sesion ya este tracking.
        bool ok = false;
        for (int attempt = 0; attempt < 5 && !ok; attempt++)
        {
            ok = TrySetupMeshManager();
            if (!ok) yield return null;
        }
        if (!ok)
        {
            Debug.Log("[LiDARScanner] Mesh no soportado (sin LiDAR). Script inactivo.");
            enabled = false;
            yield break;
        }

        _lidarActive = true;
        TrySetClassificationEnabled(_enableClassification);

        if (_occlusionManager != null)
        {
            _occlusionManager.requestedEnvironmentDepthMode    = EnvironmentDepthMode.Best;
            _occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            _occlusionManager.enabled = true;
        }

        if (_disablePlanes) HardDisablePlanes();

        Debug.Log($"[LiDARScanner] LiDAR activo. density={_meshDensity} classif={_enableClassification} mode={_mode}");
    }

    // Desactivar planos "de verdad": no alcanza con .enabled = false porque los
    // GameObjects de quads/planos que ya se crearon siguen renderizandose.
    private void HardDisablePlanes()
    {
        // 1) ARPlaneOccluder crea quads como children del propio GameObject del
        //    componente. Al apagarlo solo se detiene el Update — destruimos los
        //    children explicitamente.
        if (_planeOccluder != null)
        {
            var root = _planeOccluder.transform;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var ch = root.GetChild(i);
                if (ch != null && ch.name.StartsWith("PlaneQuad_"))
                    Destroy(ch.gameObject);
            }
            _planeOccluder.enabled = false;
        }

        // 2) ARPlaneManager: detiene la deteccion + destruye los trackables ya
        //    creados via el helper de AR Foundation. Setear el detectionMode a
        //    None evita que se re-creen si algo los re-activa.
        if (_planeManager != null)
        {
            _planeManager.requestedDetectionMode = PlaneDetectionMode.None;
            // Destruir GameObjects de planos visibles (por si tienen prefab).
            foreach (var p in _planeManager.trackables)
                if (p != null && p.gameObject != null) p.gameObject.SetActive(false);
            _planeManager.enabled = false;
        }
    }

    private bool TrySetupMeshManager()
    {
        // ARCore (Android) no soporta scene meshing. Crear un ARMeshManager
        // ahi solo genera basura. Cortamos temprano antes de tocar nada.
        if (!PlatformSupportsMesh()) return false;

        if (_meshManager == null)
        {
            if (_xrOrigin == null) return false;
            // ARMeshManager exige ser hijo del XROrigin (no estar sobre el mismo
            // GameObject). Si no, ARFoundation tira InvalidOperationException.
            var holder = new GameObject("ARMeshHolder");
            holder.transform.SetParent(_xrOrigin.transform, worldPositionStays: false);
            _meshManager = holder.AddComponent<ARMeshManager>();
        }

        // Resolver layer LiDARMesh. Si no existe, advertir pero no fallar:
        // los chunks quedan en Default layer y el RaycastResolver caera al
        // siguiente paso de la cascada.
        _lidarMeshLayer = LayerMask.NameToLayer(LiDARMeshLayerName);
        if (_lidarMeshLayer < 0)
            Debug.LogWarning($"[LiDARScanner] Layer '{LiDARMeshLayerName}' no existe. Crearla en Project Settings > Tags and Layers para que el snap LiDAR funcione.");

        EnsureMaterials();
        EnsureMeshPrefab();

        // Habilitamos para que se cree el subsystem; si sigue null, no hay soporte.
        if (!_meshManager.enabled) _meshManager.enabled = true;
        if (_meshManager.subsystem == null)
        {
            _meshManager.enabled = false;
            return false;
        }

        _meshManager.density = _meshDensity;
        _meshManager.meshesChanged += OnMeshesChanged;
        return true;
    }

    // Solo iOS soporta scene meshing (LiDAR en iPhone/iPad Pro). En cualquier
    // otra plataforma no tiene sentido crear un ARMeshManager.
    private static bool PlatformSupportsMesh()
    {
#if UNITY_IOS
        return true;
#elif UNITY_EDITOR
        // En editor permitimos pasar para que funcione la prueba via XR Simulation
        // o Mock HMD. Si no hay subsystem, el chequeo posterior lo desactiva.
        return true;
#else
        return false;
#endif
    }

    // ── ARKit classification (solo iOS) ───────────────────────────────────
    //
    // Lo invocamos por reflexion para no tener que referenciar el assembly
    // Unity.XR.ARKit en compilacion. Asi el script compila en cualquier
    // plataforma aunque el modulo de iOS Build Support no este instalado;
    // en iOS real el subsystem es la clase ARKitMeshSubsystem y el metodo
    // SetClassificationEnabled(bool) existe.
    private void TrySetClassificationEnabled(bool on)
    {
        if (_meshManager == null || _meshManager.subsystem == null) return;

        var subsystem = _meshManager.subsystem;
        var type      = subsystem.GetType();
        if (type.Name != "ARKitMeshSubsystem") return;

        var method = type.GetMethod("SetClassificationEnabled",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (method == null)
        {
            Debug.LogWarning("[LiDARScanner] ARKitMeshSubsystem.SetClassificationEnabled no encontrado (version distinta del package).");
            return;
        }

        try
        {
            method.Invoke(subsystem, new object[] { on });
            Debug.Log($"[LiDARScanner] ARKit mesh classification = {on}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LiDARScanner] No se pudo activar classification: {e.Message}");
        }
    }

    // ── Mesh chunks ───────────────────────────────────────────────────────

    private void OnMeshesChanged(ARMeshesChangedEventArgs args)
    {
        foreach (var mf in args.added)   ConfigureChunk(mf, isNew: true);
        foreach (var mf in args.updated) ConfigureChunk(mf, isNew: false);
        // removed: ARMeshManager destruye el GO por nosotros.
    }

    private void ConfigureChunk(MeshFilter mf, bool isNew)
    {
        var mr = mf.GetComponent<MeshRenderer>();
        if (mr == null) return;
        if (!_chunkRenderers.Contains(mr)) _chunkRenderers.Add(mr);

        mr.sharedMaterial = _mode == VisualMode.Wireframe ? _wireframeMat : _occluderMat;
        mr.shadowCastingMode    = ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        // Layer LiDARMesh — el RaycastResolver lo usa para snap a geometria real.
        if (_lidarMeshLayer >= 0) mf.gameObject.layer = _lidarMeshLayer;

        // MeshCollider para Physics.Raycast. Throttle cookings a 1/seg por chunk
        // porque ARMeshManager dispara updates seguido cuando el LiDAR refina.
        var col = mf.GetComponent<MeshCollider>();
        if (col == null) col = mf.gameObject.AddComponent<MeshCollider>();

        float now = Time.time;
        bool shouldCook = isNew || !_lastCookTime.TryGetValue(col, out var last)
                                || now - last >= MeshColliderCookCooldown;
        if (shouldCook)
        {
            col.sharedMesh = mf.sharedMesh;
            _lastCookTime[col] = now;
        }
    }

    // ── Materiales y prefab ───────────────────────────────────────────────

    private void EnsureMaterials()
    {
        if (_occluderMat == null)
        {
            var sh = Resources.Load<Shader>("AROccluder") ?? Shader.Find("AR/Occluder");
            if (sh != null) _occluderMat = new Material(sh) { name = "LiDARScanner_OcclMat" };
        }

        if (_wireframeMat == null)
        {
            // Sin shader de wireframe propio, usamos Unlit semitransparente.
            // Es suficiente para verificar la cobertura del scan.
            var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            _wireframeMat = new Material(sh) { name = "LiDARScanner_WireMat" };
            var col = new Color(0.2f, 1f, 0.6f, 0.6f);
            if (_wireframeMat.HasProperty("_Color"))     _wireframeMat.color = col;
            if (_wireframeMat.HasProperty("_BaseColor")) _wireframeMat.SetColor("_BaseColor", col);
            _wireframeMat.renderQueue = 4000;
        }
    }

    private void EnsureMeshPrefab()
    {
        if (_meshManager.meshPrefab != null) return;

        var prefab = new GameObject("LiDARMeshChunk");
        prefab.SetActive(false);
        prefab.AddComponent<MeshFilter>();
        var mr = prefab.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _occluderMat;
        _meshManager.meshPrefab = prefab.GetComponent<MeshFilter>();
    }

    // ── Cambio de modo ────────────────────────────────────────────────────

    public void SetMode(VisualMode m)
    {
        _mode = m;
        var mat = m == VisualMode.Wireframe ? _wireframeMat : _occluderMat;
        for (int i = _chunkRenderers.Count - 1; i >= 0; i--)
        {
            var mr = _chunkRenderers[i];
            if (mr == null) { _chunkRenderers.RemoveAt(i); continue; }
            mr.sharedMaterial = mat;
        }
    }

    public void ToggleMode() => SetMode(_mode == VisualMode.Occluder ? VisualMode.Wireframe : VisualMode.Occluder);

    private void Update()
    {
        if (!_tapToToggle || !_lidarActive) return;

        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            // Ignorar taps sobre el HUD (esquina inferior-derecha).
            var p = Input.GetTouch(0).position;
            if (p.x > Screen.width - 540 && p.y < 180) return;
            ToggleMode();
        }
        if (Input.GetKeyDown(KeyCode.M)) ToggleMode();
    }

    // ── HUD ───────────────────────────────────────────────────────────────

    private static Texture2D _hudBg;
    private static Texture2D GetHudBg()
    {
        if (_hudBg == null) { _hudBg = new Texture2D(1,1); _hudBg.SetPixel(0,0, new Color(0,0,0,0.85f)); _hudBg.Apply(); }
        return _hudBg;
    }

    private void OnGUI()
    {
        if (!_showHUD) return;

        int chunks = 0, verts = 0, tris = 0;
        foreach (var mr in _chunkRenderers)
        {
            if (mr == null) continue;
            chunks++;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                verts += mf.sharedMesh.vertexCount;
                tris  += mf.sharedMesh.triangles.Length / 3;
            }
        }

        var style = new GUIStyle
        {
            fontSize  = 26,
            alignment = TextAnchor.LowerRight,
            wordWrap  = true,
            padding   = new RectOffset(12, 12, 12, 12),
        };
        style.normal.textColor  = new Color(1f, 0.8f, 0.2f, 1f);
        style.normal.background = GetHudBg();

        string lidar = _lidarActive ? "ON" : "OFF";
        int planeQuads = 0;
        if (_planeOccluder != null)
        {
            var t = _planeOccluder.transform;
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).name.StartsWith("PlaneQuad_")) planeQuads++;
        }
        string planeMgr = _planeManager == null ? "none" :
            $"{(_planeManager.enabled ? "ON" : "off")} mode={_planeManager.requestedDetectionMode}";
        string meshSub = _meshManager == null ? "none" :
            (_meshManager.subsystem == null ? "no-subsystem" : "subsystem-OK");

        string txt =
            $"[LiDARScanner] LiDAR={lidar}\n" +
            $"Mode: {_mode}  (tap o M para cambiar)\n" +
            $"MeshMgr: {meshSub}   density={_meshDensity}\n" +
            $"Chunks: {chunks}   Verts: {verts}   Tris: {tris}\n" +
            $"PlaneMgr: {planeMgr}   QuadsResiduales: {planeQuads}";

        GUI.Label(new Rect(Screen.width - 600, Screen.height - 200, 590, 190), txt, style);
    }

    private void OnDestroy()
    {
        if (_meshManager != null) _meshManager.meshesChanged -= OnMeshesChanged;
        if (_occluderMat  != null && _occluderMat.name.StartsWith("LiDARScanner_"))  Destroy(_occluderMat);
        if (_wireframeMat != null && _wireframeMat.name.StartsWith("LiDARScanner_")) Destroy(_wireframeMat);
    }
}
