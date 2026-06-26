using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if ARCORE_EXTENSIONS
using Google.XR.ARCoreExtensions;
#endif

[RequireComponent(typeof(ARAnchorManager))]
public class CloudAnchorResolver : MonoBehaviour
{
    public event Action OnResolved;
    public event Action<string> OnResolveFailed;

    public bool IsResolved { get; private set; }

    private ARAnchorManager       _anchorManager;
    private ARTrackedImageManager _imageManager;

    private void Awake()
    {
        _anchorManager = GetComponent<ARAnchorManager>();
        _imageManager  = GetComponent<ARTrackedImageManager>();
        if (_imageManager == null)
            _imageManager = FindFirstObjectByType<ARTrackedImageManager>();
    }

    public void Resolve(string cloudAnchorId)
    {
        // Host: WorldOrigin ya fue seteado al crear el anchor local
        if (WorldOrigin.Instance.IsReady)
        {
            Debug.Log("[CloudAnchorResolver] WorldOrigin ya fijado (host), resolución inmediata");
            IsResolved = true;
            OnResolved?.Invoke();
            return;
        }

        // Activar image tracking en el cliente para que ARCore tenga features que matchear
        if (_imageManager != null)
            _imageManager.enabled = true;

        StartCoroutine(ResolveCoroutine(cloudAnchorId));
    }

    private IEnumerator ResolveCoroutine(string cloudAnchorId)
    {
        Debug.Log($"[CloudAnchorResolver] Resolviendo {cloudAnchorId}...");

#if ARCORE_EXTENSIONS
        var task = _anchorManager.ResolveCloudAnchorAsync(cloudAnchorId);
        yield return new WaitUntil(() => task.IsCompleted);

        var result = task.Result;
        if (result.CloudAnchorState == CloudAnchorState.Success)
        {
            WorldOrigin.Instance.SetOrigin(result.Anchor.transform);
            SpawnAnchorVisual(result.Anchor.transform);
            if (_imageManager != null) _imageManager.enabled = false; // ya no necesario
            IsResolved = true;
            Debug.Log("[CloudAnchorResolver] Anchor resuelto correctamente");
            OnResolved?.Invoke();
        }
        else
        {
            Debug.LogError($"[CloudAnchorResolver] Error: {result.CloudAnchorState}");
            OnResolveFailed?.Invoke(result.CloudAnchorState.ToString());
        }
#else
        yield return new WaitForSeconds(1.5f);
        // En editor: usar el WorldOrigin del host (misma sesión Unity)
        // SetOrigin con self no cambia nada si ya está seteado
        if (!WorldOrigin.Instance.IsReady)
            WorldOrigin.Instance.SetOrigin(WorldOrigin.Instance.transform);
        SpawnAnchorVisual(WorldOrigin.Instance.transform);
        IsResolved = true;
        Debug.Log("[CloudAnchorResolver] [EDITOR] Anchor simulado resuelto");
        OnResolved?.Invoke();
#endif
    }

    private void SpawnAnchorVisual(Transform anchor)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.SetParent(anchor);
        sphere.transform.localPosition = Vector3.up * 0.05f;
        sphere.transform.localRotation = Quaternion.identity;
        sphere.transform.localScale    = Vector3.one * 0.1f;
        Destroy(sphere.GetComponent<Collider>());
    }
}
