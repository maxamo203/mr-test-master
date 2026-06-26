using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

#if ARCORE_EXTENSIONS
using Google.XR.ARCoreExtensions;
#endif

[RequireComponent(typeof(ARAnchorManager))]
public class CloudAnchorHost : MonoBehaviour
{
    public event Action<string> OnAnchorHosted;
    public event Action<string> OnHostingFailed;
    public event Action         OnAnchorPlaced;

    public FeatureMapQuality CurrentQuality { get; private set; } = FeatureMapQuality.Insufficient;
    public bool IsAnchored { get; private set; }
    public bool IsHosting  { get; private set; }

    private ARAnchorManager       _anchorManager;
    private ARTrackedImageManager _imageManager;
    private ARAnchor              _localAnchor;
    private bool                  _scanning;

    private void Awake()
    {
        _anchorManager = GetComponent<ARAnchorManager>();
        _imageManager  = GetComponent<ARTrackedImageManager>();
        if (_imageManager == null)
            _imageManager = FindFirstObjectByType<ARTrackedImageManager>();
    }

    public void StartScanning()
    {
        _scanning = true;
#if UNITY_EDITOR
        StartCoroutine(EditorAutoPlace());
#else
        if (_imageManager != null)
            _imageManager.trackedImagesChanged += OnTrackedImagesChanged;
#endif
    }

    public void HostAnchor()
    {
        if (_localAnchor == null || IsHosting) return;
        StartCoroutine(HostCoroutine());
    }

    // ── Detección de imagen ───────────────────────────────────────────────

    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
    {
        if (IsAnchored || !_scanning) return;

        ARTrackedImage target = null;
        foreach (var img in args.added)
            if (img.trackingState == TrackingState.Tracking) { target = img; break; }

        if (target == null)
            foreach (var img in args.updated)
                if (img.trackingState == TrackingState.Tracking) { target = img; break; }

        if (target != null)
            StartCoroutine(PlaceAnchorOnImage(target));
    }

    private IEnumerator PlaceAnchorOnImage(ARTrackedImage img)
    {
        if (IsAnchored) yield break;
        IsAnchored = true;

        // Esfera visual parented a la imagen → sigue el tracking y aparece encima
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.SetParent(img.transform);
        sphere.transform.localPosition = Vector3.up * 0.05f; // 5 cm sobre la imagen
        sphere.transform.localRotation = Quaternion.identity;
        sphere.transform.localScale    = Vector3.one * 0.1f;
        Destroy(sphere.GetComponent<Collider>());

        // Usar la pose de la imagen como origen compartido
        var pose = new Pose(img.transform.position, img.transform.rotation);
        WorldOrigin.Instance.SetOrigin(img.transform);

#if ARCORE_EXTENSIONS
        // Crear anchor via manager (AR Foundation 6.x)
        var task = _anchorManager.TryAddAnchorAsync(pose);
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Result.status.IsSuccess())
        {
            _localAnchor = task.Result.value;
        }
        else
        {
            Debug.LogWarning($"[CloudAnchorHost] TryAddAnchorAsync falló: {task.Result.status}, usando pose directa");
            var go = new GameObject("LocalAnchor");
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            _localAnchor = go.AddComponent<ARAnchor>();
        }
        // Cambiar WorldOrigin al anchor real — esto es lo que el cliente va a resolver
        WorldOrigin.Instance.SetOrigin(_localAnchor.transform);
#else
        yield return null;
        var anchorGO = new GameObject("LocalAnchor");
        anchorGO.transform.SetPositionAndRotation(pose.position, pose.rotation);
        _localAnchor = anchorGO.AddComponent<ARAnchor>();
        WorldOrigin.Instance.SetOrigin(_localAnchor.transform);
#endif

        OnAnchorPlaced?.Invoke();
        Debug.Log($"[CloudAnchorHost] Anchor colocado en imagen: {pose.position}");
    }

    // ── Calidad del feature map ───────────────────────────────────────────

    private void Update()
    {
        if (!_scanning || !IsAnchored || Camera.main == null) return;

#if ARCORE_EXTENSIONS
        CurrentQuality = _anchorManager.EstimateFeatureMapQualityForHosting(
            new Pose(Camera.main.transform.position, Camera.main.transform.rotation));
#else
        CurrentQuality = FeatureMapQuality.Good;
#endif
    }

    // ── Upload a Cloud Anchors ────────────────────────────────────────────

    private IEnumerator HostCoroutine()
    {
        IsHosting = true;
        Debug.Log("[CloudAnchorHost] Subiendo anchor a la nube...");

#if ARCORE_EXTENSIONS
        var task = _anchorManager.HostCloudAnchorAsync(_localAnchor, ttlDays: 1);
        yield return new WaitUntil(() => task.IsCompleted);

        var result = task.Result;
        if (result.CloudAnchorState == CloudAnchorState.Success)
        {
            Debug.Log($"[CloudAnchorHost] Anchor hosteado: {result.CloudAnchorId}");
            OnAnchorHosted?.Invoke(result.CloudAnchorId);
        }
        else
        {
            Debug.LogError($"[CloudAnchorHost] Error: {result.CloudAnchorState}");
            OnHostingFailed?.Invoke(result.CloudAnchorState.ToString());
        }
#else
        yield return new WaitForSeconds(1f);
        string fakeId = $"EDITOR_ANCHOR_{UnityEngine.Random.Range(1000, 9999)}";
        Debug.Log($"[CloudAnchorHost] [EDITOR] Anchor simulado: {fakeId}");
        OnAnchorHosted?.Invoke(fakeId);
#endif
        IsHosting = false;
    }

    // ── Editor stub ───────────────────────────────────────────────────────

#if UNITY_EDITOR
    private IEnumerator EditorAutoPlace()
    {
        yield return new WaitForSeconds(1f);

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position   = Vector3.forward;
        sphere.transform.localScale = Vector3.one * 0.1f;
        Destroy(sphere.GetComponent<Collider>());

        var go = new GameObject("LocalAnchor");
        go.transform.position = Vector3.forward;
        _localAnchor = go.AddComponent<ARAnchor>();
        WorldOrigin.Instance.SetOrigin(go.transform);

        IsAnchored = true;
        OnAnchorPlaced?.Invoke();
        Debug.Log("[CloudAnchorHost] [EDITOR] Anchor simulado colocado");
    }
#endif

    private void OnDestroy()
    {
#if !UNITY_EDITOR
        if (_imageManager != null)
            _imageManager.trackedImagesChanged -= OnTrackedImagesChanged;
#endif
    }
}

#if !ARCORE_EXTENSIONS
public enum FeatureMapQuality { Insufficient, Sufficient, Good }
public enum CloudAnchorState  { Success, ErrorInternal, ErrorNotAuthorized, TaskInProgress }
#endif
