using UnityEngine;
using UnityEngine.XR.ARFoundation;

// Modo Cardboard ESTÉREO sobre AR Foundation. A diferencia de la versión anterior
// (que apagaba AR y usaba WebCamTexture + giroscopio, lo que rompía el tracking de
// imagen / multijugador y daba la vista rotada), acá AR queda PRENDIDO:
//   - El ojo izquierdo es la cámara AR existente, recortada a la mitad izquierda.
//   - El ojo derecho es una cámara hija con offset de IPD que cubre la mitad derecha;
//     ambas muestran el passthrough de AR (cada una con su ARCameraBackground) y se
//     mueven con la pose de AR (TrackedPoseDriver del padre).
// Así se conserva el tracking 6DoF + imagen + la sincronización del multijugador, y la
// orientación la maneja AR Foundation (no hay conversión de giroscopio que se desalinee
// en landscape). Cardboard se usa en landscape, así que al entrar bloqueamos esa
// orientación.
public class MRCardboardController : MonoBehaviour
{
    [Header("Referencias (se autocompletan si quedan vacías)")]
    [SerializeField] private Camera arCamera;

    [Header("Estéreo")]
    [Tooltip("Distancia interpupilar en metros (~64 mm).")]
    [SerializeField] private float _ipd = 0.064f;

    public bool CardboardActive { get; private set; }

    private Camera           _rightEye;
    private ScreenOrientation _prevOrientation;
    private bool             _prevAutorotate;

    public void ToggleCardboardMode() => SetCardboard(!CardboardActive);

    public void SetCardboard(bool on)
    {
        if (on == CardboardActive) return;
        if (!ResolveArCamera())
        {
            Debug.LogError("[MRCardboard] No encontré la cámara AR; no puedo entrar a modo Cardboard.");
            return;
        }

        if (on) EnterCardboard();
        else    ExitCardboard();
    }

    private void EnterCardboard()
    {
        // Cardboard se sostiene horizontal: fijamos landscape para que las dos mitades
        // queden lado a lado correctamente.
        _prevOrientation = Screen.orientation;
        _prevAutorotate  = Screen.autorotateToLandscapeLeft;
        Screen.orientation = ScreenOrientation.LandscapeLeft;

        // Ojo izquierdo = cámara AR, mitad izquierda.
        arCamera.rect = new Rect(0f, 0f, 0.5f, 1f);

        // Ojo derecho = cámara hija con offset de IPD, mitad derecha. Hija de la cámara
        // AR ⇒ hereda la pose del TrackedPoseDriver; el offset en X local separa los ojos.
        var rightObj = new GameObject("RightEyeCamera");
        rightObj.transform.SetParent(arCamera.transform, worldPositionStays: false);
        rightObj.transform.localPosition = new Vector3(_ipd, 0f, 0f);
        rightObj.transform.localRotation = Quaternion.identity;

        _rightEye = rightObj.AddComponent<Camera>();
        _rightEye.CopyFrom(arCamera);          // mismo FOV / clip / clearFlags / cullingMask
        _rightEye.rect  = new Rect(0.5f, 0f, 0.5f, 1f);
        _rightEye.depth = arCamera.depth + 1;

        // Passthrough de AR también en el ojo derecho. ARCameraBackground encuentra el
        // ARCameraManager de la escena y hace el blit del feed en su viewport.
        rightObj.AddComponent<ARCameraBackground>();

        CardboardActive = true;
        Debug.Log("[MRCardboard] Estéreo ON (AR sigue activo).");
    }

    private void ExitCardboard()
    {
        if (_rightEye != null) { Destroy(_rightEye.gameObject); _rightEye = null; }

        if (arCamera != null) arCamera.rect = new Rect(0f, 0f, 1f, 1f);

        Screen.orientation = _prevAutorotate ? ScreenOrientation.AutoRotation : _prevOrientation;

        CardboardActive = false;
        Debug.Log("[MRCardboard] Estéreo OFF (AR mono).");
    }

    // Busca la cámara AR (la que tiene ARCameraManager) si no fue asignada en el Inspector.
    private bool ResolveArCamera()
    {
        if (arCamera != null) return true;
        var mgr = FindFirstObjectByType<ARCameraManager>();
        if (mgr != null) arCamera = mgr.GetComponent<Camera>();
        if (arCamera == null) arCamera = Camera.main;
        return arCamera != null;
    }

    private void OnDisable()
    {
        if (CardboardActive) ExitCardboard();
    }
}
