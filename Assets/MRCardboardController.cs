using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Android;
using UnityEngine.InputSystem.XR;


public class MRCardboardController : MonoBehaviour
{
    public GyroscopeTracking gyroTracking;

    [Header("Referencias")]
    public Camera arCamera;
    public ARCameraBackground arCameraBackground;
    public ARCameraManager arCameraManager;

    [Header("Webcam Settings")]
    public int camWidth = 1280;
    public int camHeight = 720;

    private WebCamTexture _webcamTex;
    private GameObject _bgQuad;
    private Material _bgMat;
    private bool _cardboardMode = false;

    void Awake()
    {
        enabled = false; // Cardboard mode not used in this AR build
    }

    void Start()
    {
        RequestCameraPermission();
    }

    void RequestCameraPermission()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += (perm) => InitWebcam();
            callbacks.PermissionDenied += (perm) =>
                Debug.LogWarning("Permiso de cámara denegado");
            Permission.RequestUserPermission(Permission.Camera, callbacks);
        }
        else
        {
            InitWebcam();
        }
#endif
    }

    void InitWebcam()
    {
        Debug.Log("Dispositivos de cámara disponibles: " + WebCamTexture.devices.Length);
        string backCam = "";
        foreach (var dev in WebCamTexture.devices)
        {
            Debug.Log("Cámara: " + dev.name + " frontal: " + dev.isFrontFacing);
            if (!dev.isFrontFacing) { backCam = dev.name; break; }
        }
        
        if (string.IsNullOrEmpty(backCam))
        {
            Debug.LogError("No se encontró cámara trasera");
            return;
        }
        
        _webcamTex = new WebCamTexture(backCam, camWidth, camHeight, 30);
        Debug.Log("WebcamTexture creada: " + backCam);
    }

    public void ToggleCardboardMode()
    {
        if (!enabled) return;
        if (_webcamTex == null)
        {
            Debug.LogError("WebcamTexture es null - reintentando init");
            InitWebcam();
            if (_webcamTex == null)
            {
                Debug.LogError("No se pudo inicializar la webcam");
                return;
            }
        }
        
        _cardboardMode = !_cardboardMode;
        if (_cardboardMode)
            StartCoroutine(EnterCardboardMode());
        else
            ExitCardboardMode();
    }

    IEnumerator EnterCardboardMode()
    {
        // Detener la sesión AR completamente antes de tocar la cámara
        var arSession = FindFirstObjectByType<ARSession>();
        if (arSession != null)
        {
            arSession.Reset();
            arSession.enabled = false;
        }

        arCameraBackground.enabled = false;
        arCameraManager.enabled = false;
        
        var trackedPoseDriver = arCamera.GetComponent<TrackedPoseDriver>();
        if (trackedPoseDriver != null) trackedPoseDriver.enabled = false;

        // Esperar más tiempo para que ARCore libere sus recursos
        yield return new WaitForSeconds(0.8f);

        if (_webcamTex != null && !_webcamTex.isPlaying)
            _webcamTex.Play();

        CreateBackgroundQuad();
        SetupStereoCamera();

        if (gyroTracking != null) gyroTracking.StartTracking();

        Debug.Log("Modo Cardboard ON");
    }

    private Camera _rightEyeCamera;

    void SetupStereoCamera()
    {
        // Ojo izquierdo — la cámara existente cubre mitad izquierda
        arCamera.rect = new Rect(0f, 0f, 0.5f, 1f);

        // Ojo derecho — nueva cámara que cubre mitad derecha
        GameObject rightEyeObj = new GameObject("RightEyeCamera");
        rightEyeObj.transform.SetParent(arCamera.transform);
        rightEyeObj.transform.localPosition = new Vector3(0.064f, 0f, 0f); // IPD ~64mm
        rightEyeObj.transform.localRotation = Quaternion.identity;

        _rightEyeCamera = rightEyeObj.AddComponent<Camera>();
        _rightEyeCamera.CopyFrom(arCamera);
        _rightEyeCamera.rect = new Rect(0.5f, 0f, 0.5f, 1f);
        _rightEyeCamera.depth = arCamera.depth + 1;

        // Duplicar el quad de fondo para el ojo derecho
        if (_bgQuad != null)
        {
            GameObject bgRight = Instantiate(_bgQuad);
            bgRight.name = "CardboardBackgroundRight";
            bgRight.transform.SetParent(rightEyeObj.transform);
            bgRight.transform.localPosition = _bgQuad.transform.localPosition;
            bgRight.transform.localScale = _bgQuad.transform.localScale;
            bgRight.transform.localRotation = Quaternion.identity;
            bgRight.GetComponent<Renderer>().material = _bgMat;
        }
    }

    void ExitCardboardMode()
    {
        if (gyroTracking != null) gyroTracking.StopTracking();

        arCamera.rect = new Rect(0f, 0f, 1f, 1f);

        if (_rightEyeCamera != null)
        {
            Destroy(_rightEyeCamera.gameObject);
            _rightEyeCamera = null;
        }

        if (_webcamTex != null && _webcamTex.isPlaying)
            _webcamTex.Stop();
        if (_bgQuad != null) { Destroy(_bgQuad); _bgQuad = null; }
        if (_bgMat != null) { Destroy(_bgMat); _bgMat = null; }

        var trackedPoseDriver = arCamera.GetComponent<TrackedPoseDriver>();
        if (trackedPoseDriver != null) trackedPoseDriver.enabled = true;

        arCameraManager.enabled = true;
        arCameraBackground.enabled = true;

        var arSession = FindFirstObjectByType<ARSession>();
        if (arSession != null) arSession.enabled = true;

        Debug.Log("Modo AR normal ON");
    }

    void CreateBackgroundQuad()
    {
        _bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _bgQuad.name = "CardboardBackground";
        Destroy(_bgQuad.GetComponent<Collider>());
        _bgQuad.transform.SetParent(arCamera.transform);
        float dist = arCamera.farClipPlane * 0.9f;
        float h = 2f * dist * Mathf.Tan(arCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * arCamera.aspect;
        _bgQuad.transform.localPosition = new Vector3(0, 0, dist);
        _bgQuad.transform.localScale = new Vector3(w, h, 1f);
        _bgQuad.transform.localRotation = Quaternion.identity;
        _bgMat = new Material(Shader.Find("Unlit/Texture"));
        _bgMat.mainTexture = _webcamTex;
        _bgQuad.GetComponent<Renderer>().material = _bgMat;
        _bgQuad.GetComponent<Renderer>().sortingOrder = -100;
    }

    void OnDestroy()
    {
        if (_webcamTex != null && _webcamTex.isPlaying) _webcamTex.Stop();
        if (_bgMat != null) Destroy(_bgMat);
    }
}