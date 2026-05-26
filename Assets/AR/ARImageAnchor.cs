using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARTrackedImageManager))]
public class ARImageAnchor : MonoBehaviour
{
    public event Action OnImageFound;  // solo la primera vez

    public bool IsFound { get; private set; }

    // Velocidad a la que el anchor sigue las re-estimaciones del SLAM.
    // Alto = más fiel al SLAM pero con saltos bruscos visibles.
    // Bajo = más suave pero tarda más en corregir drift real.
    [SerializeField] private float _correctionSpeed = 6f;

    private ARTrackedImageManager _imageManager;
    private ARTrackedImage        _trackedImage;
    private GameObject            _anchorGO;

    private void Awake()
    {
        _imageManager         = GetComponent<ARTrackedImageManager>();
        _imageManager.enabled = false;

        var planeManager = GetComponent<ARPlaneManager>();
        if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
        if (planeManager != null) planeManager.enabled = false;
    }

    public void StartTracking()
    {
#if UNITY_EDITOR
        StartCoroutine(EditorStub());
#else
        _imageManager.enabled = true;
#endif
    }

    // ── Tracking continuo con corrección suavizada ────────────────────────

    private void Update()
    {
#if UNITY_EDITOR
        return;
#endif
        if (!_imageManager.enabled) return;

        foreach (var img in _imageManager.trackables)
        {
            if (img.trackingState != TrackingState.Tracking) continue;

            _trackedImage = img;

            if (!IsFound)
            {
                PlaceAnchorAndSpawn(img.transform);
                IsFound = true;
                OnImageFound?.Invoke();
            }
            else
            {
                // La imagen visible ES la fuente de verdad posicional.
                // Lerp suaviza los micro-saltos del refinamiento SLAM sin perder
                // la corrección real cuando la cámara se mueve mucho.
                _anchorGO.transform.position = Vector3.Lerp(
                    _anchorGO.transform.position,
                    img.transform.position,
                    _correctionSpeed * Time.deltaTime
                );
                _anchorGO.transform.rotation = Quaternion.Slerp(
                    _anchorGO.transform.rotation,
                    img.transform.rotation,
                    _correctionSpeed * Time.deltaTime
                );
                WorldOrigin.Instance.SetOrigin(_anchorGO.transform);
            }
            return; // usar solo la primera imagen tracked
        }
        // Imagen fuera de encuadre: anchor se congela en la última posición conocida
    }

    private void PlaceAnchorAndSpawn(Transform imageTransform)
    {
        _anchorGO = new GameObject("ImageAnchor");
        _anchorGO.transform.SetPositionAndRotation(imageTransform.position, imageTransform.rotation);

        WorldOrigin.Instance.SetOrigin(_anchorGO.transform);
        SpawnVisual(_anchorGO.transform);
    }

    // ── Visual ────────────────────────────────────────────────────────────

    private void SpawnVisual(Transform anchorTransform)
    {
        var root = new GameObject("AnchorVisual");
        root.transform.SetParent(anchorTransform);
        root.transform.localPosition = Vector3.up * 0.05f;
        root.transform.localRotation = Quaternion.identity;

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.SetParent(root.transform);
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

    // ── Editor stub ───────────────────────────────────────────────────────

#if UNITY_EDITOR
    private IEnumerator EditorStub()
    {
        yield return new WaitForSeconds(1f);

        var go = new GameObject("EditorAnchor");
        go.transform.position = new Vector3(0f, 0f, 1f);
        WorldOrigin.Instance.SetOrigin(go.transform);

        SpawnVisual(go.transform);
        IsFound = true;
        OnImageFound?.Invoke();
    }
#endif
}
