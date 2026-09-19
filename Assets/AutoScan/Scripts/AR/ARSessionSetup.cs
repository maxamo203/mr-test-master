using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

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
    }
}
