using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

// Modo Cardboard sobre AR Foundation — UN solo ARCameraBackground + composición por UI.
//
// ── Historia (para no repetir los errores) ────────────────────────────────────
// v1 (commit db7aece): dos cámaras, cada una con SU ARCameraBackground, y cada ojo mostrando
//     una MITAD DISTINTA del feed. Crasheaba al salir (dos backgrounds vivos revientan ARCore
//     en session_update) y era imposible de fusionar: el passthrough quedaba corrido un 50%
//     del ancho entre ojos mientras los virtuales se corrían los ~2 mm que corresponden.
// v2: un solo background; la cámara AR rendea TODO a una RenderTexture y esa RT se muestra
//     dos veces con distinto uvRect. Estable, pero MONO: la misma imagen en los dos ojos.
// v3 (esto): v2 + estéreo REAL, opcional (CardboardCalibrationUI.Estereo3D).
//
// ── Cómo funciona el estéreo ──────────────────────────────────────────────────
// El passthrough es mono POR FÍSICA: el celular tiene una sola cámara. Eso no se arregla.
// Lo que sí se puede es que la geometría VIRTUAL tenga disparidad. Con Estereo3D:
//
//   - La cámara AR se rendea DOS veces por frame, desplazada ±Ipd/2 sobre su eje derecho:
//     el ojo izquierdo por Render() manual a _rtL, el derecho lo rendea Unity solo a _rtR.
//     Se rendea TODO en cada pase (background incluido) a propósito: la oclusión por
//     profundidad de ARCore/ARKit la escribe el pase del background, así que separar
//     "cámara de passthrough" de "cámaras de virtuales" la rompería en Android.
//   - Cámaras PARALELAS, nunca "toe-in" (rotarlas hacia adentro para que converjan). El
//     toe-in mete disparidad VERTICAL en las esquinas y es lo que produce dolor de cabeza.
//   - Con cámaras paralelas el paralaje cero queda en el infinito. La CONVERGENCIA (la
//     distancia a la que un objeto cae en el MISMO píxel en los dos ojos) se mueve corriendo
//     el uvRect de cada ojo: un shear de proyección es exactamente un corrimiento constante
//     en pantalla, así que sale igual y no hay que pelearse con la matriz de proyección, que
//     ARCameraBackground reescribe en cada frame de cámara. Ver ConvergenciaUV().
//
// Escala del efecto: a 2 m con IPD 6.4 cm la disparidad es ~2 mm de pantalla. Si un objeto se
// ve claramente en dos lugares distintos, NO es el estéreo: es un bug de alineación.
//
// Coste: dos pases de render. Por eso en estéreo la RT baja a ARQuality.StereoScale por eje
// (~1.1x del fill rate del mono, no 2x). El piso de 60 fps del doc de diseño no se negocia.
//
// La calibración (zoom/offset por ojo, IPD, convergencia) vive en CardboardCalibrationUI
// (mismo GameObject) y se lee cada frame.
public class MRCardboardController : MonoBehaviour
{
    [Header("Referencias (se autocompletan si quedan vacías)")]
    [SerializeField] private Camera arCamera;

    public bool CardboardActive { get; private set; }

    // _rtL siempre existe. _rtR sólo en estéreo (en mono los dos ojos muestran _rtL).
    private RenderTexture      _rtL, _rtR;
    private RenderTexture      _prevTarget;
    private ARCameraBackground _bg;
    private CardboardCalibrationUI _calib;

    // Estéreo efectivamente aplicado a las RT/composición (puede diferir del toggle por un
    // frame, hasta que EnsureRT rehace las texturas).
    private bool _stereo;

    // Pose central de la cámara mientras está desplazada para un ojo. La restauramos en
    // onPostRender: entre frames, Camera.main.transform.position lo lee media base de código
    // (NetworkManager, GameDirector, PlayerNetwork) y no puede quedar corrida ±3 cm.
    private Vector3 _poseCentro;
    private bool    _poseDesplazada;

    private GameObject _canvas;
    private RawImage   _leftImg, _rightImg;

    // Orientación previa a entrar (para restaurarla tal cual al salir).
    private ScreenOrientation _prevOrientation;
    private bool _prevAutoPortrait, _prevAutoPortraitUpsideDown, _prevAutoLandscapeLeft, _prevAutoLandscapeRight;

    public void ToggleCardboardMode() => SetCardboard(!CardboardActive);

    // Compat con llamadas viejas: el IPD ahora vive en CardboardCalibrationUI (persistido).
    public void SetIPD(float meters) { if (_calib != null) _calib.Ipd = meters; }

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

        _prevTarget = arCamera.targetTexture;
        EnsureRT();          // crea las RT y engancha targetTexture

        BuildCanvas();
        UpdateEyes();

        // El pase del ojo izquierdo va en onBeforeRender: corre después de todos los
        // LateUpdate (DarknessOverlay reposiciona su quad ahí) y después de que el pose
        // driver actualizó la cámara, así que los dos ojos usan la pose más fresca.
        Application.onBeforeRender += RenderOjoIzquierdo;
        Camera.onPostRender        += OnCamaraPostRender;

        CardboardActive = true;
        Debug.Log($"[MRCardboard] Cardboard ON (estéreo={_stereo}).");
    }

    private void ExitCardboard()
    {
        Application.onBeforeRender -= RenderOjoIzquierdo;
        Camera.onPostRender        -= OnCamaraPostRender;
        RestaurarPose();

        // 1) Cámara AR de vuelta a pantalla.
        if (arCamera != null) arCamera.targetTexture = _prevTarget;

        // 2) Forzar rebuild del command buffer del ARCameraBackground: pasar de RT a pantalla
        //    deja el passthrough congelado si no se re-engancha el blit del fondo.
        if (_bg != null) { _bg.enabled = false; _bg.enabled = true; }

        // 3) Tear down del compositor y las RT.
        if (_canvas != null) { Destroy(_canvas); _canvas = null; _leftImg = _rightImg = null; }
        LiberarRTs();

        // 4) Restaurar orientación.
        RestoreOrientation();

        CardboardActive = false;
        Debug.Log("[MRCardboard] Cardboard OFF (AR mono a pantalla).");
    }

    // ── RenderTextures ────────────────────────────────────────────────────────

    // Crea/redimensiona las RT al tamaño de pantalla actual. Se llama también en LateUpdate
    // porque al fijar landscape, Screen.width/height tardan uno o más frames en actualizarse,
    // y porque el jugador puede prender/apagar el estéreo o cambiar la calidad en caliente.
    private void EnsureRT()
    {
        bool stereo = _calib != null && _calib.EstereoActivo;

        // Escala de render según ARQuality: cada ojo muestra la MITAD del ancho de la RT, así
        // que rendear a resolución nativa completa es fill rate que nunca se llega a ver. En
        // estéreo se rendea dos veces, así que además se aplica StereoScale.
        float s = Mathf.Clamp(ARQuality.RenderScale, 0.4f, 1f);
        if (stereo) s *= ARQuality.StereoScale;
        int w = Mathf.Max(2, Mathf.RoundToInt(Screen.width  * s));
        int h = Mathf.Max(2, Mathf.RoundToInt(Screen.height * s));

        if (_rtL != null && _rtL.width == w && _rtL.height == h && stereo == _stereo) return;

        LiberarRTs();
        _stereo = stereo;
        _rtL = NuevaRT(w, h, "CardboardRT_L");
        if (stereo) _rtR = NuevaRT(w, h, "CardboardRT_R");

        // Fuera del pase por ojo la cámara apunta al ojo DERECHO: en estéreo ese es el que
        // rendea Unity sola, y en mono _rtR es null y va todo a _rtL.
        if (arCamera  != null) arCamera.targetTexture = stereo ? _rtR : _rtL;
        if (_leftImg  != null) _leftImg.texture  = _rtL;
        if (_rightImg != null) _rightImg.texture = stereo ? _rtR : _rtL;
    }

    // wrapMode Clamp: la convergencia puede empujar el uvRect un pelo fuera de [0,1] y con
    // Repeat (el default de RenderTexture) el borde de un ojo aparecería del otro lado.
    private RenderTexture NuevaRT(int w, int h, string nombre)
    {
        var rt = new RenderTexture(w, h, 24) { name = nombre, wrapMode = TextureWrapMode.Clamp };
        rt.Create();
        return rt;
    }

    private void LiberarRTs()
    {
        if (arCamera != null && (arCamera.targetTexture == _rtL || arCamera.targetTexture == _rtR))
            arCamera.targetTexture = null;
        if (_rtL != null) { _rtL.Release(); Destroy(_rtL); _rtL = null; }
        if (_rtR != null) { _rtR.Release(); Destroy(_rtR); _rtR = null; }
    }

    // ── Pase por ojo ──────────────────────────────────────────────────────────

    // Ojo izquierdo: Render() manual a _rtL con la cámara desplazada -Ipd/2. Después deja la
    // cámara desplazada +Ipd/2 apuntando a _rtR y el render automático de Unity hace el ojo
    // derecho. La pose se restaura en OnCamaraPostRender (dispara para los dos pases).
    private void RenderOjoIzquierdo()
    {
        if (!CardboardActive || arCamera == null) return;
        RestaurarPose();                       // por si el render automático no llegó a correr
        if (!_stereo || _rtR == null) { arCamera.targetTexture = _rtL; return; }

        var     t      = arCamera.transform;
        Vector3 centro = t.position;
        Vector3 der    = t.right;
        float   mitad  = _calib.Ipd * 0.5f;

        _poseCentro = centro; _poseDesplazada = true;
        t.position = centro - der * mitad;
        arCamera.targetTexture = _rtL;
        arCamera.Render();

        // Re-armamos el desplazamiento del ojo derecho desde `centro` y no desde t.position:
        // así da igual si el postRender del pase manual ya restauró la pose o no.
        _poseCentro = centro; _poseDesplazada = true;
        t.position = centro + der * mitad;
        arCamera.targetTexture = _rtR;
    }

    private void OnCamaraPostRender(Camera cam)
    {
        if (cam == arCamera) RestaurarPose();
    }

    private void RestaurarPose()
    {
        if (!_poseDesplazada) return;
        _poseDesplazada = false;
        if (arCamera != null) arCamera.transform.position = _poseCentro;
    }

    // ── Composición ───────────────────────────────────────────────────────────

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

        _leftImg  = NewEye("LeftEye",  _rtL);
        _rightImg = NewEye("RightEye", _rtR != null ? _rtR : _rtL);
    }

    private RectTransform NewChild(string n)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);
        return (RectTransform)go.transform;
    }

    private RawImage NewEye(string n, RenderTexture tex)
    {
        var rt = NewChild(n);
        var img = rt.gameObject.AddComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        rt.anchorMin = rt.anchorMax = Vector2.zero;   // origen en la esquina inf-izq (px)
        rt.pivot = new Vector2(0.5f, 0.5f);
        return img;
    }

    private void LateUpdate()
    {
        if (!CardboardActive) return;
        EnsureRT();     // por si cambió la resolución, la calidad o el toggle de estéreo
        UpdateEyes();
    }

    private void UpdateEyes()
    {
        if (_leftImg == null || _rightImg == null || _rtL == null) return;

        float sx = 0.77f, offL = 0.064f, offR = 0.183f;
        if (_calib != null) { sx = _calib.Scale; offL = _calib.OffsetL; offR = _calib.OffsetR; }
        sx = Mathf.Clamp(sx, 0.1f, 1f);

        // Paralaje cero a la distancia de convergencia: se corre la ventana del ojo izquierdo
        // hacia la derecha y la del derecho hacia la izquierda, la misma cantidad.
        float conv = ConvergenciaUV();

        float W = Screen.width, H = Screen.height, halfW = W * 0.5f;
        float rtAspect    = (float)_rtL.width / Mathf.Max(1, _rtL.height);
        float sliceAspect = sx * rtAspect;   // aspecto de la porción de RT que ve cada ojo

        ApplyEye(_leftImg,  offL + conv, sx, new Vector2(W * 0.25f, H * 0.5f), halfW, H, sliceAspect);
        ApplyEye(_rightImg, offR - conv, sx, new Vector2(W * 0.75f, H * 0.5f), halfW, H, sliceAspect);
    }

    // Corrimiento de uv que se suma al ojo izquierdo y se resta al derecho para que un punto a
    // `Convergencia` metros caiga en el MISMO píxel en los dos ojos.
    //
    // Con la cámara desplazada `e` sobre su eje derecho, un punto que está justo al frente de
    // la pose central a distancia D cae en ndc_x = -P00*e/D, o sea u = 0.5 - 0.5*P00*e/D. Con
    // e = -+Ipd/2 la diferencia entre ojos es 0.5*P00*Ipd/D; se reparte mitad y mitad.
    // P00 = proyección[0,0] = 1/tan(fovH/2), la que AR Foundation arma con las intrínsecas.
    private float ConvergenciaUV()
    {
        if (!_stereo || arCamera == null || _calib == null) return 0f;
        float d   = Mathf.Max(0.1f, _calib.Convergencia);
        float p00 = Mathf.Abs(arCamera.projectionMatrix.m00);
        return 0.25f * p00 * _calib.Ipd / d;
    }

    // uvRect recorta la RT (zoom sx + offset por ojo). El tamaño en pantalla preserva el
    // aspecto de esa porción dentro de la mitad (availW x availH) → letterbox sin estirar.
    private void ApplyEye(RawImage img, float offX, float sx, Vector2 center,
                          float availW, float availH, float sliceAspect)
    {
        // El margen deja pasar el corrimiento de convergencia aunque la calibración esté al
        // tope (con zoom 1 el offset útil es 0). Lo que se salga de la RT se ve como el píxel
        // del borde estirado, no como la imagen envuelta (wrapMode Clamp).
        const float kMargen = 0.06f;
        float tope = Mathf.Max(0f, 1f - sx);
        offX = Mathf.Clamp(offX, -kMargen, tope + kMargen);
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
