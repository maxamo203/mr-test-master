using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using T = MortuoriumTheme;

namespace Scanner
{
    // UI de captura de la imagen de referencia, con la estética Mortuorium. Se muestra
    // mientras la FSM está en Calibrating (antes de colocar objetos). El usuario:
    //   1) Ajusta un rectángulo en pantalla (lo mueve arrastrando el interior y lo
    //      redimensiona arrastrando la esquina inferior derecha).
    //   2) Toca "CAPTURAR": se toma una foto de lo que enfoca la cámara, se recorta el
    //      fragmento dentro del rectángulo y se estima su ancho físico.
    //   3) Ajusta el ANCHO REAL (slider deslizable + input numérico tipeable, sin
    //      botones +/−) y toca "CONFIRMAR": el fragmento se registra como imagen de
    //      referencia (ARImageAnchor.AddReferenceImage) y se guarda con el escaneo
    //      (CapturedReference). "RECAPTURAR" vuelve al paso 1.
    //
    // Todo el overlay se dibuja en píxeles reales (GUI.matrix identidad) para que el
    // recorte mapee 1:1 con el rectángulo en pantalla. El input se maneja con
    // EnhancedTouch (+ Mouse en editor) y hacemos el hit-test contra los rects
    // nosotros mismos (GUI.Button no dispara confiable en iOS).
    public class ReferenceCaptureUI : MonoBehaviour
    {
        [SerializeField] private ARImageAnchor _imageAnchor;
        [SerializeField] private Camera _arCamera;

        [Tooltip("Distancia (m) usada para estimar el tamaño si el raycast no toca nada.")]
        [SerializeField] private float _fallbackDistance = 1.5f;

        [Tooltip("Opacidad (0-1) del fragmento capturado como guía fantasma mientras se " +
                 "busca la zona en el entorno, para reencuadrar la cámara sobre el punto físico.")]
        [SerializeField, Range(0.1f, 0.8f)] private float _ghostAlpha = 0.35f;

        // Rango del ancho real, en cm. El SLIDER llega hasta SliderMaxCm (lo común);
        // por TEXTO se puede ingresar hasta MaxCm (más grande) si hiciera falta.
        private const float MinCm = 2f, SliderMaxCm = 100f, MaxCm = 300f;

        private enum Phase { Adjust, Confirm, Waiting }
        private enum Drag  { None, Move, ResizeBR, Slider }

        private Phase _phase = Phase.Adjust;
        private Drag  _drag  = Drag.None;
        private bool  _busy;          // durante la captura, ignoramos input
        private bool  _hideOverlay;   // oculta el overlay el frame en que capturamos

        private Rect      _sel;       // rectángulo de selección, px reales, origen arriba-izq
        private Vector2   _lastPointer;
        private Texture2D _fragment;  // fragmento capturado (pendiente de confirmar)
        private float     _widthMeters;
        private bool      _selInit;

        // Edición numérica del ancho (teclado nativo en device / TextField en editor).
        private bool  _editing;
        private string _editText = "";
        private TouchScreenKeyboard _kb;

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
            // Reflejar el teclado nativo aunque estemos "busy".
            if (_editing && _kb != null)
            {
                _editText = _kb.text;
                var st = _kb.status;
                if (st == TouchScreenKeyboard.Status.Done) CommitEdit();
                else if (st == TouchScreenKeyboard.Status.Canceled ||
                         st == TouchScreenKeyboard.Status.LostFocus) CancelEdit();
            }

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
                else if (_phase == Phase.Confirm)
                {
                    if (SliderRow().Contains(p)) { _drag = Drag.Slider; SetWidthFromSlider(p.x); }
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
                else if (_drag == Drag.ResizeBR)
                {
                    _sel.width  = Mathf.Max(60f, _sel.width  + d.x);
                    _sel.height = Mathf.Max(60f, _sel.height + d.y);
                }
                ClampSel();
            }
            else if (_phase == Phase.Confirm && _drag == Drag.Slider)
            {
                SetWidthFromSlider(p.x);
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
                if (CaptureBtn().Contains(p))        StartCoroutine(CaptureRoutine());
                else if (SinImagenBtn().Contains(p)) CalibrarSinImagen();
            }
            else if (_phase == Phase.Confirm)
            {
                if (NumBox().Contains(p))            BeginEdit();
                else if (RecaptureBtn().Contains(p)) { CommitEdit(); BackToAdjust(); }
                else if (ConfirmBtn().Contains(p))   { CommitEdit(); Confirm(); }
                else if (_editing)                   CommitEdit();   // tap afuera confirma
            }
        }

        private void SetWidthFromSlider(float px)
        {
            var row = SliderRow();
            float t = Mathf.Clamp01((px - row.x) / row.width);
            _widthMeters = Mathf.Lerp(MinCm, SliderMaxCm, t) / 100f;
        }

        private void ClampSel()
        {
            _sel.width  = Mathf.Min(_sel.width,  Screen.width);
            _sel.height = Mathf.Min(_sel.height, Screen.height);
            _sel.x = Mathf.Clamp(_sel.x, 0f, Screen.width  - _sel.width);
            _sel.y = Mathf.Clamp(_sel.y, 0f, Screen.height - _sel.height);
        }

        // ── Edición numérica del ancho ───────────────────────────────────────────
        private void BeginEdit()
        {
            if (_editing) return;
            _editing = true;
            _editText = (_widthMeters * 100f).ToString("0", CultureInfo.InvariantCulture);
            if (TouchScreenKeyboard.isSupported)
                _kb = TouchScreenKeyboard.Open(_editText, TouchScreenKeyboardType.NumberPad,
                                               autocorrection: false, multiline: false,
                                               secure: false, alert: false, textPlaceholder: "");
        }

        private void CommitEdit()
        {
            if (!_editing) return;
            if (float.TryParse(_editText, NumberStyles.Float, CultureInfo.InvariantCulture, out var cm))
                _widthMeters = Mathf.Clamp(cm, MinCm, MaxCm) / 100f;
            CancelEdit();
        }

        private void CancelEdit()
        {
            _editing = false;
            _editText = "";
            if (_kb != null) { _kb.active = false; _kb = null; }
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
            return Mathf.Clamp(meters, MinCm / 100f, MaxCm / 100f);
        }

        // ── Escape: calibrar sin la imagen ───────────────────────────────────────
        // El jugador no tiene (o perdió) la imagen física. Centramos el 0,0 del mapa
        // en el punto que está apuntando y pasamos a la fase de ajuste manual, donde
        // lo acomoda con el gizmo. Ver ManualCalibration.
        private string _errorSinImagen;

        private void CalibrarSinImagen()
        {
            var fsm = ScanStateMachine.Instance;
            if (fsm == null) return;

            if (!ManualCalibration.Ensure().CentrarBajoLaMira(out _errorSinImagen)) return;

            _errorSinImagen = null;
            fsm.SetMode(ScannerMode.Origin_Adjust);
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

            // Overlay en px reales (sin la matriz de UIScale) para que coincida con el recorte.
            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;

            // Franjas fuera del área segura en negro (cámara visible sólo adentro).
            T.FillOutsideSafeArea(T.Bg);

            DrawCaptureUI();

            GUI.matrix = prev;
        }

        private int FsBtn   => Mathf.RoundToInt(Screen.height * 0.024f);
        private int FsLabel => Mathf.RoundToInt(Screen.height * 0.017f);
        private int FsNum    => Mathf.RoundToInt(Screen.height * 0.026f);

        private void DrawCaptureUI()
        {
            float sw = Screen.width, sh = Screen.height;

            if (_phase == Phase.Waiting)
            {
                DrawGhostOverlay();

                var r = new Rect(0, sh * 0.45f, sw, sh * 0.1f);
                T.Fill(r, new Color(0f, 0f, 0f, 0.6f));
                GUI.Label(r, "Buscando la zona en el entorno…",
                          T.Estilo(T.FElite, FsBtn, T.Cream, TextAnchor.MiddleCenter));
                return;
            }

            // Rectángulo (selección o preview del fragmento).
            if (_phase == Phase.Confirm && _fragment != null)
                GUI.DrawTexture(_sel, _fragment, ScaleMode.StretchToFill);
            T.Borde(_sel, T.Cream, Mathf.Max(3f, sh * 0.004f));

            if (_phase == Phase.Adjust)
            {
                // Esquina para redimensionar (tan) + guía de las 4 esquinas.
                T.Fill(HandleRect(), T.Tan);

                DrawTopLabel("Ajustá el recuadro sobre una zona con detalle y tocá CAPTURAR");
                DrawBtn(CaptureBtn(), "CAPTURAR", primario: true);

                // Retícula mínima al centro: el escape de abajo ancla el mapa donde
                // apunta, así que hay que ver a dónde se está apuntando.
                DrawMira();
                DrawBtn(SinImagenBtn(), "NO TENGO LA IMAGEN", primario: false);
                if (!string.IsNullOrEmpty(_errorSinImagen))
                {
                    var e = SinImagenBtn();
                    GUI.Label(new Rect(e.x, e.y - Screen.height * 0.035f, e.width, Screen.height * 0.03f),
                              _errorSinImagen,
                              T.Estilo(T.FMono, FsLabel, T.Red, TextAnchor.MiddleCenter));
                }
            }
            else // Confirm
            {
                DrawTopLabel("Ajustá el ancho real de la imagen y confirmá");

                // Panel inferior para leer los controles sobre la cámara/preview.
                float panelTop = SliderRow().y - sh * 0.05f;
                T.Fill(new Rect(0, panelTop, sw, sh - panelTop), new Color(T.Bg.r, T.Bg.g, T.Bg.b, 0.82f));

                // Etiqueta + slider + input numérico (sin botones +/−).
                var row = SliderRow();
                GUI.Label(new Rect(row.x, row.y - sh * 0.032f, sw - row.x * 2f, sh * 0.03f),
                          "ANCHO REAL DE LA IMAGEN",
                          T.Estilo(T.FMono, FsLabel, T.Muted, TextAnchor.LowerLeft));

                DrawSlider(row);
                DrawNumBox(NumBox());

                DrawBtn(RecaptureBtn(), "RECAPTURAR", primario: false);
                DrawBtn(ConfirmBtn(),   "CONFIRMAR",  primario: true);
            }
        }

        // Slider deslizable (dibujo; el drag lo maneja Update por EnhancedTouch).
        private void DrawSlider(Rect row)
        {
            // El thumb se clampea al final si el valor (por texto) supera el máx del slider.
            float t = Mathf.InverseLerp(MinCm, SliderMaxCm, _widthMeters * 100f);
            var track = new Rect(row.x, row.y + row.height * 0.5f - 3f, row.width, 6f);
            T.Fill(track, T.BorderDim);
            T.Fill(new Rect(track.x, track.y, track.width * t, track.height), T.Red);

            var thumb = new Rect(row.x + row.width * t - 9f, row.y + row.height * 0.5f - 16f, 18f, 32f);
            T.Fill(thumb, T.Cream);
            T.Borde(thumb, T.Red, 2f);
        }

        private void DrawNumBox(Rect box)
        {
            T.Fill(box, T.BgField);
            T.Borde(box, _editing ? T.Tan : T.Border);
            var inner = new Rect(box.x + 10f, box.y, box.width - 20f, box.height);
            var st = T.Estilo(T.FMono, FsNum, T.Cream, TextAnchor.MiddleCenter);

            if (_editing)
            {
#if UNITY_EDITOR
                // En editor se tipea con el teclado físico vía TextField.
                GUI.SetNextControlName("wcm");
                _editText = GUI.TextField(inner, _editText, st);
                GUI.FocusControl("wcm");
                if (Event.current.type == EventType.KeyDown &&
                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                    CommitEdit();
#else
                // En device el texto lo llena el teclado nativo (TouchScreenKeyboard).
                GUI.Label(inner, $"{_editText} cm", st);
#endif
            }
            else
            {
                GUI.Label(inner, $"{_widthMeters * 100f:0} cm", st);
            }
        }

        // Fragmento capturado, semi-transparente, en el mismo rectángulo donde se tomó:
        // guía para reencuadrar la cámara sobre el punto físico exacto mientras
        // ARImageAnchor intenta reengancharlo. _sel no se mueve en esta fase (no hay
        // drag activo), así que queda fijo como referencia en pantalla.
        private void DrawGhostOverlay()
        {
            if (!CapturedReference.HasImage) return;

            var prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, _ghostAlpha);
            GUI.DrawTexture(_sel, CapturedReference.Texture, ScaleMode.StretchToFill);
            GUI.color = prevColor;

            T.Borde(_sel, T.Cream, Mathf.Max(2f, Screen.height * 0.003f));
        }

        // ── Layout (px reales) ───────────────────────────────────────────────────
        private float Hsz() => Mathf.Max(64f, Screen.height * 0.06f);
        private Rect HandleRect()
        {
            float s = Hsz();
            return new Rect(_sel.xMax - s, _sel.yMax - s, s, s);
        }

        private float BtnH => Mathf.Max(52f, Screen.height * 0.07f);
        private float BtnY => Screen.height - BtnH - Screen.height * 0.05f;

        // Adjust: un botón CAPTURAR centrado.
        private Rect CaptureBtn() => new Rect(Screen.width * 0.25f, BtnY, Screen.width * 0.5f, BtnH);

        // Adjust: escape "no tengo la imagen", arriba de CAPTURAR y más chico (es la
        // salida secundaria; el camino bueno sigue siendo capturar la referencia).
        private float SinImagenH => BtnH * 0.72f;
        private Rect SinImagenBtn() => new Rect(Screen.width * 0.18f,
                                                BtnY - SinImagenH - Screen.height * 0.018f,
                                                Screen.width * 0.64f, SinImagenH);

        // Confirm: RECAPTURAR (izq) + CONFIRMAR (der).
        private Rect RecaptureBtn() => new Rect(Screen.width * 0.06f, BtnY, Screen.width * 0.42f, BtnH);
        private Rect ConfirmBtn()   => new Rect(Screen.width * 0.52f, BtnY, Screen.width * 0.42f, BtnH);

        // Fila del ancho: slider (izq) + input numérico (der), arriba de los botones.
        private float RowH => Mathf.Max(48f, Screen.height * 0.06f);
        private float RowY => BtnY - RowH - Screen.height * 0.03f;
        private Rect SliderRow() => new Rect(Screen.width * 0.06f, RowY, Screen.width * 0.56f, RowH);
        private Rect NumBox()    => new Rect(Screen.width * 0.66f, RowY, Screen.width * 0.28f, RowH);

        // ── Dibujo helpers ────────────────────────────────────────────────────────
        // Botón temático (solo visual; el tap lo dispara HandleButtonTap por EnhancedTouch).
        private void DrawBtn(Rect r, string label, bool primario)
        {
            T.Fill(r, primario ? new Color(T.Red.r, T.Red.g, T.Red.b, 0.20f)
                               : new Color(0f, 0f, 0f, 0.5f));
            T.Borde(r, primario ? T.Red : T.Border);
            GUI.Label(r, label, T.Estilo(T.FBebas, FsBtn, T.Cream, TextAnchor.MiddleCenter));
        }

        // Cruz fina en el centro exacto de la pantalla: es el punto que usa
        // "NO TENGO LA IMAGEN" para anclar el 0,0 del mapa.
        private void DrawMira()
        {
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float r = Screen.height * 0.022f, g = r * 0.3f, w = Mathf.Max(2f, Screen.height * 0.002f);
            var c = new Color(T.Cream.r, T.Cream.g, T.Cream.b, 0.75f);
            T.Fill(new Rect(cx - r, cy - w * 0.5f, r - g, w), c);
            T.Fill(new Rect(cx + g, cy - w * 0.5f, r - g, w), c);
            T.Fill(new Rect(cx - w * 0.5f, cy - r, w, r - g), c);
            T.Fill(new Rect(cx - w * 0.5f, cy + g, w, r - g), c);
        }

        private void DrawTopLabel(string msg)
        {
            // Wrap a 2 líneas: sin esto, con MiddleCenter y una sola línea, mensajes
            // largos como "ajustá el recuadro..." se recortaban simétrico en los dos
            // extremos (ej. "Está el recuadro... CAPTURAR" -> "stá el recuadro...
            // CAPTUR") en vez de bajar a una segunda línea.
            //
            // Este overlay dibuja en píxeles reales (GUI.matrix identidad, ver OnGUI),
            // pero el botón de pausa (PauseMenuController, esquina sup-derecha) usa
            // coordenadas virtuales de UIScale — convertimos su borde inferior a
            // píxeles reales para no superponernos.
            float pauseBottomReal = Scanner.SafeArea.Top + (28f + 60f) * UIScale.Factor + 8f;
            var r = new Rect(0, pauseBottomReal, Screen.width, Screen.height * 0.065f);
            T.Fill(r, new Color(0f, 0f, 0f, 0.5f));
            // Blanco puro solo en Android: ahí T.CreamDim se ve apagado/gris (mismo
            // motivo que el resto de los ajustes de esta rama — ver MortuoriumTheme).
            var color = Application.platform == RuntimePlatform.Android ? Color.white : T.CreamDim;
            GUI.Label(r, msg, T.Estilo(T.FElite, FsLabel, color, TextAnchor.MiddleCenter, wrap: true));
        }
    }
}
