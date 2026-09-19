using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Text;

/// <summary>
/// Runs once on startup to apply runtime configuration that can't be
/// reliably set in the Inspector (detection mode, occlusion quality, etc.)
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(ARPlaneManager))]
public class ARSessionSetup : MonoBehaviour
{
    void Awake()
    {
        // Detect floors, ceilings AND walls
        var planeManager = GetComponent<ARPlaneManager>();
        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
        if (Debug.isDebugBuild) Debug.Log($"[ARSetup] PlaneDetectionMode set to {planeManager.requestedDetectionMode}");

        // Ensure occlusion is on best quality
        var occ = GetComponentInChildren<AROcclusionManager>(includeInactive: true);
        if (occ != null)
        {
            occ.requestedEnvironmentDepthMode = EnvironmentDepthMode.Best;
            occ.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            if (Debug.isDebugBuild) Debug.Log("[ARSetup] AROcclusionManager → Best depth, environment occlusion");
        }
    }

    // The game's ARQuality.Aplicar runs on every scene load (sceneLoaded fires after Awake)
    // and turns environment depth off / to Fastest by quality level. The depth-occupancy
    // scanner is useless without it, so re-assert it here: Start runs after that handler.
    void Start()
    {
        var occ = GetComponentInChildren<AROcclusionManager>(includeInactive: true);
        if (occ == null) return;
        occ.requestedEnvironmentDepthMode = EnvironmentDepthMode.Best;
        occ.enabled = true;
        _occ = occ;
    }

    // Dev builds only: shows what the depth pipeline actually ended up with on the device,
    // since the Editor cannot run environment depth.
    AROcclusionManager _occ;
    ARCameraBackground _bg;
    readonly StringBuilder _sb = new StringBuilder();
    float _nextRefresh;
    GUIStyle _style;

    void OnGUI()
    {
        if (!Debug.isDebugBuild || _occ == null) return;
        if (Time.unscaledTime >= _nextRefresh) { _nextRefresh = Time.unscaledTime + 0.5f; Refresh(); }
        if (_style == null)
            _style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 55), normal = { textColor = Color.yellow } };
        GUI.Label(new Rect(8, Screen.height * 0.72f, Screen.width - 16, Screen.height * 0.26f), _sb.ToString(), _style);
    }

    void Refresh()
    {
        if (_bg == null) _bg = GetComponentInChildren<ARCameraBackground>(includeInactive: true);
        var mat = _bg != null ? _bg.material : null;
        var supported = _occ.descriptor?.environmentDepthImageSupported;
        bool hasTex = _occ.TryGetEnvironmentDepthTexture(out var tex);
        var cam = _bg != null ? _bg.GetComponent<Camera>() : null;

        _sb.Clear();
        _sb.Append("DEPTH req=").Append(_occ.requestedEnvironmentDepthMode)
           .Append(" cur=").Append(_occ.currentEnvironmentDepthMode)
           .Append(" on=").Append(_occ.enabled).Append(" support=").Append(supported).AppendLine();
        _sb.Append("pref=").Append(_occ.currentOcclusionPreferenceMode)
           .Append(" tex=").Append(hasTex ? tex.width + "x" + tex.height : "none").AppendLine();
        _sb.Append("bg=").Append(_bg != null ? _bg.currentRenderingMode.ToString() : "null")
           .Append(" shader=").Append(mat != null ? mat.shader.name : "null")
           .Append(" depthKw=").Append(mat != null && mat.IsKeywordEnabled("ARCORE_ENVIRONMENT_DEPTH_ENABLED"))
           .Append(" msaa=").Append(cam != null ? cam.allowMSAA + "/" + QualitySettings.antiAliasing : "?");
    }
}
