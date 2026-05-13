using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARPlaneManager))]
public class ARPlaneOccluder : MonoBehaviour
{
    [Header("Configuración")]
    [Tooltip("Si se activa, los planos detectados se vuelven invisibles pero siguen ocluyendo objetos virtuales")]
    public bool occludeAllPlanes = true;

    [Tooltip("Tipos de planos a usar como oclusores (paredes = Vertical)")]
    public PlaneAlignmentFilter alignmentFilter = PlaneAlignmentFilter.All;

    [Tooltip("Mostrar visualmente los planos detectados (debug)")]
    public bool showPlanesForDebug = false;

    [Tooltip("Color/material a usar cuando showPlanesForDebug está activo. Si es null, se usa un material azul translúcido")]
    public Material debugMaterial;

    [Tooltip("Mostrar HUD en pantalla con info de tracking y planos detectados")]
    public bool showDebugHUD = true;

    private ARPlaneManager _planeManager;
    private ARSession _session;
    private AROcclusionManager _occlusionManager;
    private Material _occluderMat;
    private Material _debugMatRuntime;
    private int _addedCount = 0;
    private int _updatedCount = 0;

    public enum PlaneAlignmentFilter { All, HorizontalOnly, VerticalOnly }

    void Awake()
    {
        _planeManager = GetComponent<ARPlaneManager>();
        _session = FindFirstObjectByType<ARSession>();
        _occlusionManager = FindFirstObjectByType<AROcclusionManager>();

        var shader = Resources.Load<Shader>("AROccluder");
        if (shader == null) shader = Shader.Find("AR/Occluder");
        if (shader == null)
        {
            Debug.LogError("ARPlaneOccluder: shader 'AR/Occluder' no encontrado. Verificá que AROccluder.shader esté en Assets/Resources/.");
            return;
        }
        _occluderMat = new Material(shader) { name = "AROccluderMat" };
        Debug.Log("ARPlaneOccluder: shader cargado OK -> " + shader.name);

        if (_planeManager.planePrefab == null)
        {
            _planeManager.planePrefab = CreateRuntimePlanePrefab();
            Debug.Log("ARPlaneOccluder: planePrefab no asignado, se creó uno automáticamente.");
        }
    }

    GameObject CreateRuntimePlanePrefab()
    {
        var go = new GameObject("AROccluderPlane");
        go.SetActive(false);
        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _occluderMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        go.AddComponent<MeshCollider>();
        go.AddComponent<ARPlaneMeshVisualizer>();
        go.AddComponent<ARPlane>();
        return go;
    }

    Material GetDebugMaterial()
    {
        if (debugMaterial != null) return debugMaterial;
        if (_debugMatRuntime == null)
        {
            var sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Standard");
            _debugMatRuntime = new Material(sh);
            _debugMatRuntime.color = new Color(0.2f, 0.6f, 1f, 0.5f);
        }
        return _debugMatRuntime;
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
        foreach (var plane in args.added) { _addedCount++; ApplyMaterial(plane); }
        foreach (var plane in args.updated) { _updatedCount++; ApplyMaterial(plane); }
    }

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
        if (!showDebugHUD) return;

        string txt = "HUD INICIANDO...";
        try
        {
            string sessionState = ARSession.state.ToString();
            string notTracking = ARSession.notTrackingReason.ToString();
            bool pmEnabled = _planeManager != null && _planeManager.enabled;
            string detMode = _planeManager != null ? _planeManager.requestedDetectionMode.ToString() : "n/a";
            int currentPlanes = 0, verticalPlanes = 0, horizontalPlanes = 0;
            if (_planeManager != null && _planeManager.trackables != null)
            {
                foreach (var p in _planeManager.trackables)
                {
                    currentPlanes++;
                    if (p.alignment == PlaneAlignment.Vertical) verticalPlanes++;
                    else horizontalPlanes++;
                }
            }

            string occlusionInfo = "AROcclusionManager: NO";
            if (_occlusionManager != null)
            {
                string envMode = "?", occPref = "?", supported = "?";
                try { envMode = _occlusionManager.requestedEnvironmentDepthMode.ToString(); } catch { }
                try { occPref = _occlusionManager.requestedOcclusionPreferenceMode.ToString(); } catch { }
                try
                {
                    var desc = _occlusionManager.descriptor;
                    supported = desc == null ? "no-desc" : desc.environmentDepthImageSupported.ToString();
                }
                catch (System.Exception e) { supported = "ERR:" + e.GetType().Name; }
                occlusionInfo =
                    $"OcclMgr: SI ena={_occlusionManager.enabled}\n" +
                    $"  EnvDepthMode: {envMode}\n" +
                    $"  EnvDepth sup: {supported}\n" +
                    $"  OcclusionPref: {occPref}";
            }

            txt =
                $"AR Session: {sessionState}\n" +
                $"NotTracking: {notTracking}\n" +
                $"Shader: {(_occluderMat != null && _occluderMat.shader != null ? _occluderMat.shader.name : "NULL")}\n" +
                $"PlaneMgr: ena={pmEnabled} mode={detMode}\n" +
                $"PlanePrefab: {(_planeManager != null && _planeManager.planePrefab != null ? _planeManager.planePrefab.name : "NULL")}\n" +
                $"Planos: {currentPlanes} (H:{horizontalPlanes} V:{verticalPlanes})\n" +
                $"Eventos a/u: {_addedCount}/{_updatedCount}\n" +
                occlusionInfo;
        }
        catch (System.Exception e)
        {
            txt = "HUD ERROR: " + e.GetType().Name + " - " + e.Message;
        }

        var style = new GUIStyle();
        style.fontSize = 30;
        style.normal.textColor = Color.yellow;
        style.normal.background = GetHudBg();
        style.padding = new RectOffset(12, 12, 12, 12);
        style.alignment = TextAnchor.UpperLeft;
        style.wordWrap = true;

        GUI.Label(new Rect(10, 10, Screen.width - 20, 600), txt, style);
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

    void ApplyMaterial(ARPlane plane)
    {
        if (!occludeAllPlanes) return;

        var renderer = plane.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        if (!MatchesFilter(plane))
        {
            renderer.enabled = false;
            return;
        }

        renderer.enabled = true;
        Material targetMat = showPlanesForDebug ? GetDebugMaterial() : _occluderMat;
        if (renderer.sharedMaterial != targetMat)
            renderer.sharedMaterial = targetMat;

        var lineRenderer = plane.GetComponent<LineRenderer>();
        if (lineRenderer != null) lineRenderer.enabled = showPlanesForDebug;
    }

    void OnDestroy()
    {
        if (_occluderMat != null) Destroy(_occluderMat);
        if (_debugMatRuntime != null) Destroy(_debugMatRuntime);
    }
}
