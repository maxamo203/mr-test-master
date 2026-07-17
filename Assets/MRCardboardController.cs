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

    [Header("Recorte lateral de cámara")]
    [Tooltip("Material con _CropOffsetX = 0 (ojo izquierdo ve la mitad izquierda del feed).")]
    [SerializeField] private Material _cropLeft;
    [Tooltip("Material con _CropOffsetX = 0.5 (ojo derecho ve la mitad derecha del feed).")]
    [SerializeField] private Material _cropRight;

    public bool CardboardActive { get; private set; }

    private Camera            _rightEye;
    private ARCameraBackground _leftBg;

    // Estado de orientación previo a entrar a Cardboard, para restaurarlo tal cual al salir.
    private ScreenOrientation  _prevOrientation;
    private bool _prevAutoPortrait, _prevAutoPortraitUpsideDown, _prevAutoLandscapeLeft, _prevAutoLandscapeRight;

    public void ToggleCardboardMode() => SetCardboard(!CardboardActive);

    // Permite ajustar el IPD en vivo desde CardboardCalibrationUI.
    public void SetIPD(float meters)
    {
        _ipd = meters;
        if (_rightEye != null)
            _rightEye.transform.localPosition = new Vector3(_ipd, 0f, 0f);
    }

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
        // queden lado a lado correctamente. Guardamos TODOS los flags de autorotación
        // (no solo uno) para poder devolver la orientación exacta al salir.
        _prevOrientation            = Screen.orientation;
        _prevAutoPortrait           = Screen.autorotateToPortrait;
        _prevAutoPortraitUpsideDown = Screen.autorotateToPortraitUpsideDown;
        _prevAutoLandscapeLeft      = Screen.autorotateToLandscapeLeft;
        _prevAutoLandscapeRight     = Screen.autorotateToLandscapeRight;
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

        // Passthrough de AR en el ojo derecho.
        var rightBg = rightObj.AddComponent<ARCameraBackground>();

        // Recorte lateral: cada ojo recibe su mitad del feed de cámara para
        // evitar la doble imagen idéntica que marea con el headset.
        if (_cropLeft != null && _cropRight != null)
        {
            _leftBg = arCamera.GetComponent<ARCameraBackground>();
            if (_leftBg != null)
            {
                _leftBg.useCustomMaterial = true;
                _leftBg.customMaterial    = _cropLeft;
            }
            rightBg.useCustomMaterial = true;
            rightBg.customMaterial    = _cropRight;
        }

        CardboardActive = true;
        Debug.Log("[MRCardboard] Estéreo ON (AR sigue activo).");
    }

    private void ExitCardboard()
    {
        // 1) Fuera el ojo derecho (y su ARCameraBackground) antes de tocar el izquierdo.
        if (_rightEye != null) { Destroy(_rightEye.gameObject); _rightEye = null; }

        // 2) Viewport completo de nuevo para el ojo izquierdo.
        if (arCamera != null) arCamera.rect = new Rect(0f, 0f, 1f, 1f);

        // 3) Restaura el material por defecto del ojo izquierdo y FUERZA a ARCameraBackground a
        //    reconstruir su command buffer. Síntoma sin esto: al salir, la sesión sigue
        //    trackeando (los objetos virtuales se mueven) pero el passthrough queda CONGELADO,
        //    porque el blit del fondo no se re-renderiza tras salir del split/custom-material.
        //    Toggle enabled = OnDisable/OnEnable → re-engancha el command buffer al viewport nuevo.
        var leftBg = _leftBg != null
            ? _leftBg
            : (arCamera != null ? arCamera.GetComponent<ARCameraBackground>() : null);
        if (leftBg != null)
        {
            leftBg.useCustomMaterial = false;
            leftBg.customMaterial    = null;
            leftBg.enabled = false;
            leftBg.enabled = true;
        }
        _leftBg = null;

        // 4) Devuelve la orientación tal como estaba (si no, el celular queda trabado en landscape).
        RestoreOrientation();

        CardboardActive = false;
        Debug.Log("[MRCardboard] Estéreo OFF (AR mono).");
    }

    // Restaura los flags de autorotación y la orientación previos a entrar a Cardboard.
    // Primero los flags (para que AutoRotation sepa qué orientaciones permitir) y después la
    // orientación: si la app permitía autorotar, volvemos a AutoRotation; si estaba fija
    // (p. ej. portrait bloqueado), restauramos esa orientación concreta.
    private void RestoreOrientation()
    {
        Screen.autorotateToPortrait           = _prevAutoPortrait;
        Screen.autorotateToPortraitUpsideDown = _prevAutoPortraitUpsideDown;
        Screen.autorotateToLandscapeLeft      = _prevAutoLandscapeLeft;
        Screen.autorotateToLandscapeRight     = _prevAutoLandscapeRight;

        bool anyAuto = _prevAutoPortrait || _prevAutoPortraitUpsideDown ||
                       _prevAutoLandscapeLeft || _prevAutoLandscapeRight;
        Screen.orientation = anyAuto ? ScreenOrientation.AutoRotation : _prevOrientation;
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
