using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARTrackedImageManager))]
public class ARImageAnchor : MonoBehaviour
{
    public event Action OnImageFound;       // solo la PRIMERA vez (compat con consumidores actuales)
    public event Action OnImageReacquired;  // cada vez que se (re)detecta la imagen, incluida la primera

    public bool IsFound { get; private set; }
    public Transform CurrentAnchor => _anchor != null ? _anchor.transform : null;

    [SerializeField] private ARAnchorManager _anchorManager;

    private ARTrackedImageManager _imageManager;
    private ARPlaneManager        _planeManager;
    private ARAnchor              _anchor;
    private GameObject            _anchorVisual;
    private bool                  _foundEverFired;

    private void Awake()
    {
        _imageManager         = GetComponent<ARTrackedImageManager>();
        _imageManager.enabled = false;

        if (_anchorManager == null) _anchorManager = FindFirstObjectByType<ARAnchorManager>();

        _planeManager = GetComponent<ARPlaneManager>();
        if (_planeManager == null) _planeManager = FindFirstObjectByType<ARPlaneManager>();
        if (_planeManager != null) _planeManager.enabled = false;
    }

    public void StartTracking()
    {
#if UNITY_EDITOR
        StartCoroutine(EditorStub());
#else
        _imageManager.enabled = true;
#endif
    }

    // Vuelve a entrar en modo "buscando imagen". El anchor viejo se destruye
    // pero los hijos de WorldOrigin se preservan (las posiciones anchor-relativas
    // se mantienen porque WorldOrigin.SetOrigin re-parentea sin tocar localPos).
    public void RestartTracking()
    {
        IsFound = false;

        if (_anchorVisual != null) Destroy(_anchorVisual);
        _anchorVisual = null;

        if (_anchor != null) Destroy(_anchor.gameObject);
        _anchor = null;

#if UNITY_EDITOR
        StartCoroutine(EditorStub());
#else
        _imageManager.enabled = true;
#endif
        Debug.Log("[ARImageAnchor] RestartTracking — buscando imagen otra vez.");
    }

    private void Update()
    {
#if UNITY_EDITOR
        return;
#endif
        if (!_imageManager.enabled || IsFound) return;

        foreach (var img in _imageManager.trackables)
        {
            if (img.trackingState != TrackingState.Tracking) continue;

            PlaceAnchorAndSpawn(img.transform);
            IsFound = true;

            if (!_foundEverFired)
            {
                _foundEverFired = true;
                OnImageFound?.Invoke();
            }
            OnImageReacquired?.Invoke();

            _imageManager.enabled = false;
            return;
        }
    }

    private void PlaceAnchorAndSpawn(Transform imageTransform)
    {
        var anchorGO = new GameObject("ImageAnchor");
        anchorGO.transform.SetPositionAndRotation(imageTransform.position, imageTransform.rotation);
        _anchor = anchorGO.AddComponent<ARAnchor>();

        if (_planeManager != null) _planeManager.enabled = true;

        WorldOrigin.Instance.SetOrigin(_anchor.transform);
        SpawnVisual(_anchor.transform);
    }

    private void SpawnVisual(Transform anchorTransform)
    {
        _anchorVisual = new GameObject("AnchorVisual");
        _anchorVisual.transform.SetParent(anchorTransform);
        _anchorVisual.transform.localPosition = Vector3.up * 0.05f;
        _anchorVisual.transform.localRotation = Quaternion.identity;

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.SetParent(_anchorVisual.transform);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale    = Vector3.one * 0.1f;
        Destroy(sphere.GetComponent<Collider>());

        var mini = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mini.transform.SetParent(sphere.transform);
        mini.transform.localPosition = new Vector3(0.7f, 0f, 0f);
        mini.transform.localScale    = Vector3.one * 0.35f;
        Destroy(mini.GetComponent<Collider>());
        var mat = mini.GetComponent<Renderer>().material;
        mat.color = Color.red;
        mat.SetColor("_BaseColor", Color.red);
    }

#if UNITY_EDITOR
    private IEnumerator EditorStub()
    {
        yield return new WaitForSeconds(1f);

        var go = new GameObject("EditorAnchor");
        go.transform.position = new Vector3(0f, 0f, 1f);
        WorldOrigin.Instance.SetOrigin(go.transform);

        SpawnVisual(go.transform);
        IsFound = true;
        if (!_foundEverFired)
        {
            _foundEverFired = true;
            OnImageFound?.Invoke();
        }
        OnImageReacquired?.Invoke();
    }
#endif
}
