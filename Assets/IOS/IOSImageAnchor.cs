using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Setup standalone para iOS / ARKit.
// Detecta una imagen una sola vez, crea un ARAnchor real en su pose y
// spawnea las esferas como hijas. ARKit mantiene el anchor fijo en el mundo
// real aunque el usuario mueva el teléfono — sin re-escaneo.
//
// Setup en escena:
//   - XR Origin (AR) con AR Camera
//   - AR Session
//   - GameObject con ARTrackedImageManager (referenceLibrary asignada, maxNumberOfMovingImages = 1)
//   - GameObject con ARAnchorManager
//   - Este script en cualquier GameObject (o en el mismo del ARTrackedImageManager)
[DisallowMultipleComponent]
public class IOSImageAnchor : MonoBehaviour
{
    [Header("AR Managers (auto-resuelto si se deja vacío)")]
    [SerializeField] private ARTrackedImageManager _imageManager;
    [SerializeField] private ARAnchorManager       _anchorManager;
    [SerializeField] private ARPlaneManager        _planeManager;
    [SerializeField] private AROcclusionManager    _occlusionManager;

    [Header("Visual del anchor")]
    [Tooltip("Altura de las esferas sobre la imagen, en metros.")]
    [SerializeField] private float _visualHeight = 0.05f;
    [Tooltip("Diámetro de la esfera principal, en metros.")]
    [SerializeField] private float _mainSphereSize = 0.1f;

    public bool IsFound { get; private set; }

    private ARAnchor _anchor;

    private void Awake()
    {
        if (_imageManager    == null) _imageManager    = FindFirstObjectByType<ARTrackedImageManager>();
        if (_anchorManager   == null) _anchorManager   = FindFirstObjectByType<ARAnchorManager>();
        if (_planeManager    == null) _planeManager    = FindFirstObjectByType<ARPlaneManager>();
        if (_occlusionManager == null) _occlusionManager = FindFirstObjectByType<AROcclusionManager>();

        if (_imageManager == null)
            Debug.LogError("[IOSImageAnchor] No hay ARTrackedImageManager en la escena.");
        if (_anchorManager == null)
            Debug.LogError("[IOSImageAnchor] No hay ARAnchorManager en la escena.");

        // Deshabilitamos planos durante la detección para que ARKit priorice
        // la imagen. Lo re-habilitamos en cuanto el anchor queda establecido.
        if (_planeManager != null) _planeManager.enabled = false;

        // AROcclusionManager: en iPhones Pro con LiDAR activa oclusión por
        // profundidad real (mejor que la basada en planos). En iPhones sin
        // LiDAR, ARKit ignora silenciosamente EnvironmentDepth.
        if (_occlusionManager != null)
        {
            _occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Best;
            _occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            _occlusionManager.enabled = true;
        }
    }

    private void OnEnable()
    {
        if (_imageManager != null) _imageManager.enabled = true;
    }

    private void Update()
    {
        if (IsFound || _imageManager == null || !_imageManager.enabled) return;

        foreach (var img in _imageManager.trackables)
        {
            if (img.trackingState != TrackingState.Tracking) continue;

            PlaceAnchorAndSpawn(img.transform);
            IsFound = true;

            // Una vez creado el anchor real, no necesitamos seguir
            // re-estimando la pose de la imagen (esa era la fuente de drift).
            _imageManager.enabled = false;
            return;
        }
    }

    private void PlaceAnchorAndSpawn(Transform imageTransform)
    {
        var anchorGO = new GameObject("ImageAnchor_iOS");
        anchorGO.transform.SetPositionAndRotation(imageTransform.position, imageTransform.rotation);

        // AddComponent<ARAnchor>() registra el anchor en ARKit.
        // A partir de acá, ARKit lo mantiene fijo respecto al mapa SLAM.
        _anchor = anchorGO.AddComponent<ARAnchor>();

        // Ahora que el anchor está establecido, re-habilitamos la detección
        // de planos para que ARPlaneOccluder pueda generar los occluders.
        if (_planeManager != null) _planeManager.enabled = true;

        SpawnVisual(_anchor.transform);
        Debug.Log($"[IOSImageAnchor] Anchor creado en {anchorGO.transform.position}");
    }

    private void SpawnVisual(Transform anchorTransform)
    {
        var root = new GameObject("AnchorVisual");
        root.transform.SetParent(anchorTransform, worldPositionStays: false);
        root.transform.localPosition = Vector3.up * _visualHeight;
        root.transform.localRotation = Quaternion.identity;

        var main = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        main.transform.SetParent(root.transform, worldPositionStays: false);
        main.transform.localPosition = Vector3.zero;
        main.transform.localScale    = Vector3.one * _mainSphereSize;
        Destroy(main.GetComponent<Collider>());

        var satellite = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        satellite.transform.SetParent(main.transform, worldPositionStays: false);
        satellite.transform.localPosition = new Vector3(0.7f, 0f, 0f);
        satellite.transform.localScale    = Vector3.one * 0.35f;
        Destroy(satellite.GetComponent<Collider>());

        var mat = satellite.GetComponent<Renderer>().material;
        mat.color = Color.red;
        mat.SetColor("_BaseColor", Color.red); // URP
    }
}
