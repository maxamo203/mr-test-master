using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

// Modo Cardboard ESTÉREO sobre AR Foundation — REWRITE a RenderTexture + composición UI.
//
// Enfoque anterior (2 ARCameraBackground + recorte por shader): crasheaba al salir (2
// backgrounds vivos, ver memoria) y NO se podía corregir el aspecto sin desfasar los
// virtuales (el recorte tocaba solo el feed, no la proyección de los objetos).
//
// Enfoque nuevo: UN solo ARCameraBackground. La cámara AR renderiza TODO (passthrough +
// virtuales + oscuridad) a una RenderTexture `_fullRT` — es el AR mono tal cual, pero a
// textura. Después mostramos esa RT DOS veces (mitad izq / der) con un Canvas Overlay + dos
// RawImage, cada una con su uvRect (zoom + offset por ojo) y su tamaño con letterbox
// (aspecto nativo, barras negras arriba/abajo). Como passthrough y virtuales viven JUNTOS en
// _fullRT, recortar/mover/letterboxear nunca los desfasa.
//
// Trade-off: passthrough y virtuales quedan MONO (misma imagen en ambos ojos, con un offset
// horizontal por ojo) — sin estéreo real de profundidad (que igual estaba en IPD=0). A cambio
// NUNCA hay 2 ARCameraBackground vivos → sin el crash. La calibración (zoom/offset por ojo)
// vive en CardboardCalibrationUI (mismo GameObject) y la leemos cada frame.
public class MRCardboardController : MonoBehaviour
{
    [Header("Referencias (se autocompletan si quedan vacías)")]
    [SerializeField] private Camera arCamera;

    public bool CardboardActive { get; private set; }

    private RenderTexture      _fullRT;
    private RenderTexture      _prevTarget;
    private ARCameraBackground _bg;
    private CardboardCalibrationUI _calib;

    private GameObject _canvas;
    private RawImage   _leftImg, _rightImg;

    // Orientación previa a entrar (para restaurarla tal cual al salir).
    private ScreenOrientation _prevOrientation;
    private bool _prevAutoPortrait, _prevAutoPortraitUpsideDown, _prevAutoLandscapeLeft, _prevAutoLandscapeRight;

    public void ToggleCardboardMode() => SetCardboard(!CardboardActive);

    // Compat: en este modo el IPD real ya no aplica (passthrough mono). No-op para no romper
    // llamadas viejas (CardboardCalibrationUI). La separación por ojo se hace con OffsetL/R.
    public void SetIPD(float meters) { }

    public void SetCardboard(bool on)
    {
        if (on == CardboardActive) return;
        if (!ResolveArCamera())
        {
            Debug.LogError("[MRCardboard] No encontré la cámara AR; no puedo entrar a modo Cardboard.");
            return;
        }
        if (on) EnterCardboard(); else ExitCardboard();
    }

    private void EnterCardboard()
    {
        // Cardboard se sostiene horizontal: fijamos landscape. Guardamos los 4 flags de
        // autorotación para devolver la orientación exacta al salir.
        _prevOrientation            = Screen.orientation;
        _prevAutoPortrait           = Screen.autorotateToPortrait;
        _prevAutoPortraitUpsideDown = Screen.autorotateToPortraitUpsideDown;
        _prevAutoLandscapeLeft      = Screen.autorotateToLandscapeLeft;
        _prevAutoLandscapeRight     = Screen.autorotateToLandscapeRight;
        Screen.orientation = ScreenOrientation.LandscapeLeft;

        _calib = GetComponent<CardboardCalibrationUI>();
        _bg    = arCamera.GetComponent<ARCameraBackground>();

        EnsureRT();
        _prevTarget = arCamera.targetTexture;
        arCamera.targetTexture = _fullRT;   // el AR mono se renderiza a la RT (background incluido)

        BuildCanvas();
        UpdateEyes();

        CardboardActive = true;
        Debug.Log("[MRCardboard] Estéreo ON (RenderTexture, 1 background).");
    }

    private void ExitCardboard()
    {
        // 1) Cámara AR de vuelta a pantalla.
        if (arCamera != null) arCamera.targetTexture = _prevTarget;

        // 2) Forzar rebuild del command buffer del ARCameraBackground: pasar de RT a pantalla
        //    deja el passthrough congelado si no se re-engancha el blit del fondo.
        if (_bg != null) { _bg.enabled = false; _bg.enabled = true; }

        // 3) Tear down del compositor y la RT.
        if (_canvas != null) { Destroy(_canvas); _canvas = null; _leftImg = _rightImg = null; }
        if (_fullRT != null) { _fullRT.Release(); Destroy(_fullRT); _fullRT = null; }

        // 4) Restaurar orientación.
        RestoreOrientation();

        CardboardActive = false;
        Debug.Log("[MRCardboard] Estéreo OFF (AR mono).");
    }

    // Crea/redimensiona la RT al tamaño de pantalla actual. Se llama también en LateUpdate
    // porque al fijar landscape, Screen.width/height tardan uno o más frames en actualizarse.
    private void EnsureRT()
    {
        int w = Mathf.Max(2, Screen.width), h = Mathf.Max(2, Screen.height);
        if (_fullRT != null && _fullRT.width == w && _fullRT.height == h) return;

        if (_fullRT != null)
        {
            if (arCamera != null && arCamera.targetTexture == _fullRT) arCamera.targetTexture = null;
            _fullRT.Release();
            Destroy(_fullRT);
        }
        _fullRT = new RenderTexture(w, h, 24) { name = "CardboardFullRT" };
        _fullRT.Create();

        if (CardboardActive && arCamera != null) arCamera.targetTexture = _fullRT;
        if (_leftImg  != null) _leftImg.texture  = _fullRT;
        if (_rightImg != null) _rightImg.texture = _fullRT;
    }

    private void BuildCanvas()
    {
        _canvas = new GameObject("CardboardCanvas");
        var canvas = _canvas.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;   // encima de todo (menos IMGUI, que queda arriba a propósito)

        // Fondo negro: cubre la pantalla y hace de barras de letterbox.
        var bg = NewChild("BG");
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = Color.black;
        bgImg.raycastTarget = false;
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = Vector2.zero; bg.offsetMax = Vector2.zero;

        _leftImg  = NewEye("LeftEye");
        _rightImg = NewEye("RightEye");
    }

    private RectTransform NewChild(string n)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);
        return (RectTransform)go.transform;
    }

    private RawImage NewEye(string n)
    {
        var rt = NewChild(n);
        var img = rt.gameObject.AddComponent<RawImage>();
        img.texture = _fullRT;
        img.raycastTarget = false;
        rt.anchorMin = rt.anchorMax = Vector2.zero;   // origen en la esquina inf-izq (px)
        rt.pivot = new Vector2(0.5f, 0.5f);
        return img;
    }

    private void LateUpdate()
    {
        if (!CardboardActive) return;
        EnsureRT();     // por si la resolución cambió al asentarse el landscape
        UpdateEyes();
    }

    private void UpdateEyes()
    {
        if (_leftImg == null || _rightImg == null || _fullRT == null) return;

        float sx = 0.77f, offL = 0.064f, offR = 0.183f;
        if (_calib != null) { sx = _calib.Scale; offL = _calib.OffsetL; offR = _calib.OffsetR; }
        sx = Mathf.Clamp(sx, 0.1f, 1f);

        float W = Screen.width, H = Screen.height, halfW = W * 0.5f;
        float rtAspect    = (float)_fullRT.width / Mathf.Max(1, _fullRT.height);
        float sliceAspect = sx * rtAspect;   // aspecto de la porción de RT que ve cada ojo

        ApplyEye(_leftImg,  offL, sx, new Vector2(W * 0.25f, H * 0.5f), halfW, H, sliceAspect);
        ApplyEye(_rightImg, offR, sx, new Vector2(W * 0.75f, H * 0.5f), halfW, H, sliceAspect);
    }

    // uvRect recorta la RT (zoom sx + offset por ojo). El tamaño en pantalla preserva el
    // aspecto de esa porción dentro de la mitad (availW x availH) → letterbox sin estirar.
    private void ApplyEye(RawImage img, float offX, float sx, Vector2 center,
                          float availW, float availH, float sliceAspect)
    {
        offX = Mathf.Clamp(offX, 0f, Mathf.Max(0f, 1f - sx));
        img.uvRect = new Rect(offX, 0f, sx, 1f);

        float w = availW, h = availW / Mathf.Max(1e-4f, sliceAspect);
        if (h > availH) { h = availH; w = availH * sliceAspect; }   // encajar por alto si sobra

        var rt = img.rectTransform;
        rt.sizeDelta        = new Vector2(w, h);
        rt.anchoredPosition = center;
    }

    private bool ResolveArCamera()
    {
        if (arCamera != null) return true;
        var mgr = FindFirstObjectByType<ARCameraManager>();
        if (mgr != null) arCamera = mgr.GetComponent<Camera>();
        if (arCamera == null) arCamera = Camera.main;
        return arCamera != null;
    }

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

    private void OnDisable()
    {
        if (CardboardActive) ExitCardboard();
    }
}
