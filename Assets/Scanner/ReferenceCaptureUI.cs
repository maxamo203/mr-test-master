using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Scanner
{
    // UI de captura de la imagen de referencia. Se muestra mientras la FSM está
    // en Calibrating (antes de colocar objetos). El usuario:
    //   1) Ajusta un rectángulo en pantalla (lo mueve arrastrando el interior y
    //      lo redimensiona arrastrando la esquina inferior derecha).
    //   2) Toca "Capturar": se toma una foto de lo que enfoca la cámara, se
    //      recorta el fragmento dentro del rectángulo y se estima su tamaño físico.
    //   3) Confirma/ajusta el tamaño (en cm) y toca "Confirmar": el fragmento se
    //      registra como imagen de referencia (ARImageAnchor.AddReferenceImage) y
    //      queda guardado para persistirse con el escaneo (CapturedReference).
    //
    // Todo el overlay se dibuja en píxeles reales (GUI.matrix identidad) para que
    // el recorte mapee 1:1 con el rectángulo en pantalla. Igual que
    // RecalibrateButton, el input se maneja con EnhancedTouch (+ Mouse en editor)
    // y hacemos el hit-test contra los rects nosotros mismos.
    public class ReferenceCaptureUI : MonoBehaviour
    {
        [SerializeField] private ARImageAnchor _imageAnchor;
        [SerializeField] private Camera _arCamera;

        [Tooltip("Distancia (m) usada para estimar el tamaño si el raycast no toca nada.")]
        [SerializeField] private float _fallbackDistance = 1.5f;

        private enum Phase { Adjust, Confirm, Waiting }
        private enum Drag  { None, Move, ResizeBR }

        private Phase _phase = Phase.Adjust;
        private Drag  _drag  = Drag.None;
        private bool  _busy;          // durante la captura, ignoramos input
        private bool  _hideOverlay;   // oculta el overlay el frame en que capturamos

        private Rect      _sel;       // rectángulo de selección, px reales, origen arriba-izq
        private Vector2   _lastPointer;
        private Texture2D _fragment;  // fragmento capturado (pendiente de confirmar)
        private float     _widthMeters;
        private bool      _selInit;

        private static Texture2D _white;
        private GUIStyle _btnStyle, _labelStyle;

        private bool Active =>
            _imageAnchor != null &&
            ScanStateMachine.Instance != null &&
            ScanStateMachine.Instance.Current == ScannerMode.Calibrating &&
            !_imageAnchor.IsFound;

        private void Awake()
        {
            if (_imageAnchor == null) _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
            if (_arCamera == null) _arCamera = Camera.main;
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        }

        private void EnsureSelInit()
        {
            if (_selInit) return;
            float w = Screen.width  * 0.5f;
            float h = Screen.height * 0.3f;
            _sel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            _selInit = true;
        }

        // ── Input ──────────────────────────────────────────────────────────────
        private void Update()
        {
            if (!Active || _busy) return;
            EnsureSelInit();
            if (!GetPointer(out var p, out var down, out var up)) return;

            if (down)
            {
                if (_phase == Phase.Adjust)
                {
                    if (HandleRect().Contains(p)) _drag = Drag.ResizeBR;
                    else if (_sel.Contains(p))    _drag = Drag.Move;
                    else                          _drag = Drag.None;
                }
                _lastPointer = p;
                return;
            }

            if (up)
            {
                // Solo tratamos como tap de botón si no estábamos arrastrando.
                if (_drag == Drag.None) HandleButtonTap(p);
                _drag = Drag.None;
                return;
            }

            // Arrastre en curso (held).
            if (_phase == Phase.Adjust && _drag != Drag.None)
            {
                var d = p - _lastPointer;
                _lastPointer = p;
                if (_drag == Drag.Move)
                {
                    _sel.x += d.x;
                    _sel.y += d.y;
                }
                else // ResizeBR
                {
                    _sel.width  = Mathf.Max(60f, _sel.width  + d.x);
                    _sel.height = Mathf.Max(60f, _sel.height + d.y);
                }
                ClampSel();
            }
        }

        // Devuelve el puntero en px reales con origen ARRIBA-izquierda (como GUI).
        private bool GetPointer(out Vector2 pTopLeft, out bool down, out bool up)
        {
            pTopLeft = default; down = false; up = false;

            foreach (var t in ETouch.activeTouches)
            {
                pTopLeft = new Vector2(t.screenPosition.x, Screen.height - t.screenPosition.y);
                down = t.phase == UnityEngine.InputSystem.TouchPhase.Began;
                up   = t.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                       t.phase == UnityEngine.InputSystem.TouchPhase.Canceled;
                return true;
            }

            var ms = Mouse.current;
            if (ms != null && (ms.leftButton.isPressed || ms.leftButton.wasReleasedThisFrame))
            {
                var mp = ms.position.ReadValue();
                pTopLeft = new Vector2(mp.x, Screen.height - mp.y);
                down = ms.leftButton.wasPressedThisFrame;
                up   = ms.leftButton.wasReleasedThisFrame;
                return true;
            }
            return false;
        }

        private void HandleButtonTap(Vector2 p)
        {
            if (_phase == Phase.Adjust)
            {
                if (CaptureBtn().Contains(p)) StartCoroutine(CaptureRoutine());
            }
            else if (_phase == Phase.Confirm)
            {
                if (MinusBtn().Contains(p))        _widthMeters = Mathf.Max(0.02f, _widthMeters - 0.01f);
                else if (PlusBtn().Contains(p))    _widthMeters += 0.01f;
                else if (RecaptureBtn().Contains(p)) BackToAdjust();
                else if (ConfirmBtn().Contains(p)) Confirm();
            }
        }

        private void ClampSel()
        {
            _sel.width  = Mathf.Min(_sel.width,  Screen.width);
            _sel.height = Mathf.Min(_sel.height, Screen.height);
            _sel.x = Mathf.Clamp(_sel.x, 0f, Screen.width  - _sel.width);
            _sel.y = Mathf.Clamp(_sel.y, 0f, Screen.height - _sel.height);
        }

        // ── Captura ──────────────────────────────────────────────────────────────
        private IEnumerator CaptureRoutine()
        {
            _busy = true;
            _hideOverlay = true;
            // Avanzamos un frame para que el OnGUI ya no dibuje el overlay, y
            // esperamos a fin de frame para capturar el framebuffer ya renderizado.
            yield return null;
            yield return new WaitForEndOfFrame();

            var full = ScreenCapture.CaptureScreenshotAsTexture();
            _hideOverlay = false;

            // _sel está en coords arriba-izq; las texturas tienen origen abajo-izq.
            int x = Mathf.RoundToInt(_sel.x);
            int w = Mathf.RoundToInt(_sel.width);
            int h = Mathf.RoundToInt(_sel.height);
            int yBottom = full.height - Mathf.RoundToInt(_sel.y) - h;

            x = Mathf.Clamp(x, 0, full.width  - 1);
            yBottom = Mathf.Clamp(yBottom, 0, full.height - 1);
            w = Mathf.Clamp(w, 1, full.width  - x);
            h = Mathf.Clamp(h, 1, full.height - yBottom);

            var pixels = full.GetPixels(x, yBottom, w, h);
            Destroy(full);

            if (_fragment != null) Destroy(_fragment);
            _fragment = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
            _fragment.SetPixels(pixels);
            _fragment.Apply(updateMipmaps: false);

            // Punto central del rectángulo en px con origen abajo-izq (para raycast).
            var centerBL = new Vector2(_sel.x + _sel.width * 0.5f,
                                       Screen.height - (_sel.y + _sel.height * 0.5f));
            _widthMeters = EstimateWidthMeters(centerBL, w);

            _phase = Phase.Confirm;
            _busy = false;
        }

        private float EstimateWidthMeters(Vector2 centerScreenBL, int rectWidthPx)
        {
            float dist = _fallbackDistance;

            var rr = RaycastResolver.Instance;
            if (rr != null && _arCamera != null)
            {
                var hit = rr.ResolveFromScreenPoint(centerScreenBL);
                if (hit.Hit) dist = Vector3.Distance(_arCamera.transform.position, hit.Position);
            }

            float vFov = (_arCamera != null ? _arCamera.fieldOfView : 60f) * Mathf.Deg2Rad;
            float aspect = _arCamera != null ? _arCamera.aspect : (float)Screen.width / Screen.height;
            float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * aspect);
            float fullWidthAtDist = 2f * dist * Mathf.Tan(hFov * 0.5f);
            float meters = fullWidthAtDist * (rectWidthPx / (float)Screen.width);
            return Mathf.Clamp(meters, 0.02f, 5f);
        }

        private void BackToAdjust()
        {
            if (_fragment != null) { Destroy(_fragment); _fragment = null; }
            _phase = Phase.Adjust;
        }

        private void Confirm()
        {
            if (_fragment == null) return;
            CapturedReference.Set(_fragment, _widthMeters);
            _imageAnchor.AddReferenceImage(_fragment, "captura", _widthMeters);
            _fragment = null; // ahora es propiedad de CapturedReference
            _phase = Phase.Waiting;
        }

        // ── Dibujo ───────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (!Active || _hideOverlay) return;
            EnsureStyles();
            EnsureSelInit();

            // Overlay en px reales (sin la matriz de UIScale) para que coincida con el recorte.
            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;

            DrawCaptureUI();

            GUI.matrix = prev;
        }

        private void DrawCaptureUI()
        {
            if (_phase == Phase.Waiting)
            {
                DrawCenteredLabel("Buscando la zona en el entorno…");
                return;
            }

            // Rectángulo (selección o preview del fragmento).
            if (_phase == Phase.Confirm && _fragment != null)
                GUI.DrawTexture(_sel, _fragment, ScaleMode.StretchToFill);
            DrawBorder(_sel, Mathf.Max(3f, Screen.height * 0.004f), Color.white);

            if (_phase == Phase.Adjust)
            {
                DrawSolid(HandleRect(), new Color(1f, 0.8f, 0.1f, 0.9f)); // esquina para redimensionar
                DrawTopLabel("Ajustá el recuadro sobre una zona con detalle y tocá Capturar");
                GUI.Button(CaptureBtn(), "Capturar", _btnStyle);
            }
            else // Confirm
            {
                float cm = _widthMeters * 100f;
                GUI.Button(MinusBtn(), "−", _btnStyle);
                GUI.Label(WidthLabel(), $"Ancho ≈ {cm:0} cm", _labelStyle);
                GUI.Button(PlusBtn(), "+", _btnStyle);
                GUI.Button(RecaptureBtn(), "Re-capturar", _btnStyle);
                GUI.Button(ConfirmBtn(), "Confirmar", _btnStyle);
            }
        }

        // ── Layout (px reales) ───────────────────────────────────────────────────
        private float Hsz() => Mathf.Max(64f, Screen.height * 0.06f);
        private Rect HandleRect()
        {
            float s = Hsz();
            return new Rect(_sel.xMax - s, _sel.yMax - s, s, s);
        }

        private float BW => Screen.width  * 0.30f;
        private float BH => Screen.height * 0.07f;
        private float BY => Screen.height - BH - Screen.height * 0.05f;

        private Rect CaptureBtn()  => new Rect((Screen.width - BW) * 0.5f, BY, BW, BH);

        private Rect RecaptureBtn() => new Rect(Screen.width * 0.06f, BY, BW, BH);
        private Rect ConfirmBtn()   => new Rect(Screen.width * 0.94f - BW, BY, BW, BH);

        private float RowY => BY - BH - Screen.height * 0.015f;
        private Rect MinusBtn()   => new Rect(Screen.width * 0.20f - BH, RowY, BH, BH);
        private Rect PlusBtn()    => new Rect(Screen.width * 0.80f,      RowY, BH, BH);
        private Rect WidthLabel() => new Rect(Screen.width * 0.20f, RowY, Screen.width * 0.60f, BH);

        // ── Dibujo helpers ────────────────────────────────────────────────────────
        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Screen.height * 0.024f);
            if (_btnStyle == null || _btnStyle.fontSize != fs)
            {
                _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = fs, alignment = TextAnchor.MiddleCenter };
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fs, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
            }
        }

        private static Texture2D White()
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            return _white;
        }

        private static void DrawSolid(Rect r, Color c)
        {
            var prev = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, White());
            GUI.color = prev;
        }

        private static void DrawBorder(Rect r, float t, Color c)
        {
            DrawSolid(new Rect(r.x, r.y, r.width, t), c);                 // arriba
            DrawSolid(new Rect(r.x, r.yMax - t, r.width, t), c);          // abajo
            DrawSolid(new Rect(r.x, r.y, t, r.height), c);               // izq
            DrawSolid(new Rect(r.xMax - t, r.y, t, r.height), c);        // der
        }

        private void DrawTopLabel(string msg)
        {
            var r = new Rect(0, Screen.height * 0.06f, Screen.width, Screen.height * 0.06f);
            DrawSolid(r, new Color(0, 0, 0, 0.5f));
            GUI.Label(r, msg, _labelStyle);
        }

        private void DrawCenteredLabel(string msg)
        {
            var r = new Rect(0, Screen.height * 0.45f, Screen.width, Screen.height * 0.1f);
            DrawSolid(r, new Color(0, 0, 0, 0.6f));
            GUI.Label(r, msg, _labelStyle);
        }
    }
}
