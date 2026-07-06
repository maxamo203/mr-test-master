using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using Scanner;   // UIScale, UIBlocker

namespace Gamepad
{
    // Panel de pausa (overlay) que se puede abrir y navegar TANTO con el gamepad
    // como con los dedos (touch), de forma simultánea. Por ahora contiene la
    // sección "Opciones", que muestra el estado del joystick y un gamepad virtual
    // que refleja las pulsaciones en vivo. Más adelante crecerá con más cosas.
    //
    // Es solo overlay: no toca Time.timeScale (el AR/escaneo sigue corriendo
    // detrás). Toda la UI es IMGUI con el mismo patrón del resto del proyecto
    // (UIScale.Begin + coords virtuales; el tap real se detecta con EnhancedTouch
    // porque GUI.Button no dispara click confiable en iOS — ver RecalibrateButton).
    //
    // Se auto-crea junto con GamepadManager (ver GamepadManager.Bootstrap).
    [DefaultExecutionOrder(-55)]
    public class PauseMenuController : MonoBehaviour
    {
        private enum Page { Main, Options, Flashlight }

        private bool _open;
        private Page _page = Page.Main;
        private int  _focus;

        // Ítems interactivos del panel visible, en coords VIRTUALES. Se reconstruye
        // en cada OnGUI; Update los usa para navegación (foco) y hit-test de tap.
        private struct Item { public string id; public Rect rect; }
        private readonly List<Item> _items = new();
        private Rect _pauseBtnRect;

        // Sliders (parámetros de linterna): además del item focusable, guardamos las zonas
        // [-]/[+] y el valor (para editarlo tocándolo) para el hit-test de tap.
        private struct SliderHit { public string id; public Rect dec; public Rect inc; public Rect val; }
        private readonly List<SliderHit> _sliderHits = new();
        private Flashlight _fl;

        // Edición manual del valor de un slider: id en edición, teclado nativo (device) y
        // el texto actual. En editor se usa un GUI.TextField con el teclado físico.
        private string _editingSlider;
        private TouchScreenKeyboard _kb;
        private string _editText = "";

        // Navegación con gamepad: cooldowns de auto-repeat (foco vertical / ajuste horizontal).
        private float _navCooldown;
        private float _adjCooldown;

        // Estilos / texturas IMGUI.
        private static Texture2D _tex;
        private GUIStyle _btn, _btnFocus, _icon, _title, _status, _battTxt, _toggleLbl;

        private void Awake()
        {
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        }

        // ----------------------------------------------------------------- OnGUI
        private void OnGUI()
        {
            UIScale.Begin();
            EnsureStyles();
            _items.Clear();
            _sliderHits.Clear();

            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;

            // Botón de pausa, siempre visible (arriba-derecha, zona libre).
            _pauseBtnRect = new Rect(vw - 84f, 28f, 60f, 60f);
            UIBlocker.AddVirtualRect(_pauseBtnRect);
            GUI.Label(_pauseBtnRect, _open ? "X" : "II", _icon);

            if (!_open) return;

            // Overlay oscuro que bloquea la escena de atrás.
            var full = new Rect(0, 0, vw, vh);
            DrawRect(full, new Color(0f, 0f, 0f, 0.72f));
            UIBlocker.AddVirtualRect(full);

            // Panel centrado.
            float pw = Mathf.Min(vw - 40f, 560f);
            float ph = Mathf.Min(vh - 120f,
                _page == Page.Options   ? 820f :
                _page == Page.Flashlight ? 540f : 360f);
            var panel = new Rect((vw - pw) / 2f, (vh - ph) / 2f, pw, ph);
            DrawRect(panel, new Color(0.10f, 0.10f, 0.12f, 0.98f));

            float pad = 24f;
            float x = panel.x + pad, w = pw - pad * 2f;
            float y = panel.y + pad;

            if (_page == Page.Main)
            {
                GUI.Label(new Rect(x, y, w, 50f), "Pausa", _title); y += 64f;
                AddButton("opciones", new Rect(x, y, w, 64f), "Opciones"); y += 76f;
                AddButton("linterna", new Rect(x, y, w, 64f), "Linterna"); y += 76f;
                AddButton("reanudar", new Rect(x, y, w, 64f), "Reanudar"); y += 76f;
            }
            else if (_page == Page.Flashlight)
            {
                GUI.Label(new Rect(x, y, w, 50f), "Linterna", _title); y += 62f;

                var fl = GetFlashlight();
                if (fl == null)
                {
                    GUI.Label(new Rect(x, y, w, 60f), "No se encontró la linterna en la escena.", _status);
                    y += 70f;
                }
                else
                {
                    AddSlider("fl_range",     new Rect(x, y, w, 60f), "Rango",         fl.range,        0.5f, 30f, "{0:0.0} m"); y += 68f;
                    AddSlider("fl_outer",     new Rect(x, y, w, 60f), "Ángulo externo", fl.outerAngleDeg, 2f,  89f, "{0:0}°");    y += 68f;
                    AddSlider("fl_inner",     new Rect(x, y, w, 60f), "Ángulo interno", fl.innerAngleDeg, 0f,  89f, "{0:0}°");    y += 68f;
                    AddSlider("fl_intensity", new Rect(x, y, w, 60f), "Intensidad",     fl.intensity,     0f,  10f, "{0:0.0}");   y += 68f;
                }

                y += 6f;
                AddButton("volver", new Rect(x, y, w, 60f), "Volver");
            }
            else // Options
            {
                GUI.Label(new Rect(x, y, w, 50f), "Opciones", _title); y += 60f;

                // Toggle: efecto de oscurecido/linterna (DarknessOverlay) — para probar.
                AddToggle("envlight", new Rect(x, y, w, 52f),
                          "Iluminación del entorno (tiempo real)",
                          EnvironmentLightingController.Enabled);
                y += 62f;

                // Toggle: malla dinámica del entorno (AR/LiDAR) iluminada por la linterna.
                AddToggle("meshlight", new Rect(x, y, w, 52f),
                          "Iluminar malla del entorno (LiDAR/AR)",
                          FlashlightMeshLighting.Enabled);
                y += 66f;

                // Estado del joystick.
                var gm = GamepadManager.Instance;
                string status = (gm != null && gm.IsConnected)
                    ? $"Joystick: {gm.DisplayName}\nTipo: {gm.Brand}   -   Estado: Conectado"
                    : "Joystick: ninguno\nConectá un mando por Bluetooth desde el sistema.";
                GUI.Label(new Rect(x, y, w, 72f), status, _status); y += 78f;

                // Batería del mando (barrita con forma de batería + porcentaje).
                if (gm != null && gm.IsConnected)
                {
                    bool present = gm.TryGetBattery(out float lvl);
                    DrawBattery(new Rect(x, y, w, 38f), lvl, present);
                    y += 52f;
                }

                // Gamepad virtual (refleja las pulsaciones en vivo).
                float gh = w * 0.62f;           // proporción ~landscape
                var gpArea = new Rect(x, y, w, gh);
                DrawRect(gpArea, new Color(0.06f, 0.06f, 0.07f, 1f));
                var st = gm != null ? gm.ReadState() : default;
                GamepadVisualizer.Draw(gpArea, gm != null ? gm.Brand : GamepadBrand.None, st);
                y += gh + 16f;

                AddButton("volver", new Rect(x, y, w, 60f), "Volver");
            }

            // Asegura que el foco quede dentro de rango.
            if (_items.Count > 0) _focus = Mathf.Clamp(_focus, 0, _items.Count - 1);
        }

        // Dibuja un botón (resaltado si tiene el foco) y lo registra como ítem.
        private void AddButton(string id, Rect rect, string label)
        {
            bool focused = _items.Count == _focus;
            GUI.Label(rect, label, focused ? _btnFocus : _btn);
            UIBlocker.AddVirtualRect(rect);
            _items.Add(new Item { id = id, rect = rect });
        }

        // Dibuja un slider (label + valor + barra + botones [-]/[+]). Se registra como ítem
        // focusable (gamepad izq/der ajusta) y sus zonas [-]/[+] para el tap.
        private void AddSlider(string id, Rect rect, string label, float value,
                               float min, float max, string fmt)
        {
            bool focused = _items.Count == _focus;
            DrawRect(rect, focused ? new Color(0.95f, 0.85f, 0.20f, 0.30f)
                                   : new Color(1f, 1f, 1f, 0.07f));

            float btnW = rect.height - 12f;
            var dec = new Rect(rect.xMax - btnW * 2f - 14f, rect.y + 6f, btnW, btnW);
            var inc = new Rect(rect.xMax - btnW - 8f,       rect.y + 6f, btnW, btnW);

            // Zona del VALOR (tocable para editar) y label a la izquierda.
            float valW = 128f;
            var valRect = new Rect(dec.x - valW - 10f, rect.y + 6f, valW, rect.height - 24f);
            var lblRect = new Rect(rect.x + 14f, rect.y, valRect.x - rect.x - 16f, rect.height - 12f);

            GUI.Label(lblRect, $"{label}:", _toggleLbl);

            if (_editingSlider == id)
            {
                DrawRect(valRect, new Color(0.18f, 0.18f, 0.24f)); // fondo de campo
#if UNITY_EDITOR
                GUI.SetNextControlName("edit_" + id);
                _editText = GUI.TextField(valRect, _editText, _toggleLbl);
                GUI.FocusControl("edit_" + id);
                if (Event.current.type == EventType.KeyDown &&
                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                    CommitEdit();
#else
                GUI.Label(valRect, _editText, _toggleLbl); // el texto lo llena el teclado nativo
#endif
            }
            else
            {
                DrawRect(valRect, new Color(1f, 1f, 1f, 0.06f)); // pista visual de "tocable"
                GUI.Label(new Rect(valRect.x + 6f, valRect.y, valRect.width - 6f, valRect.height),
                          string.Format(fmt, value), _toggleLbl);
            }

            // Barra min..max (bajo el label).
            float t = Mathf.InverseLerp(min, max, value);
            var barBg = new Rect(lblRect.x, rect.yMax - 12f, lblRect.width, 5f);
            DrawRect(barBg, new Color(1f, 1f, 1f, 0.15f));
            DrawRect(new Rect(barBg.x, barBg.y, barBg.width * Mathf.Clamp01(t), barBg.height),
                     new Color(0.95f, 0.85f, 0.20f, 0.9f));

            GUI.Label(dec, "-", _btn);
            GUI.Label(inc, "+", _btn);

            UIBlocker.AddVirtualRect(rect);
            _items.Add(new Item { id = id, rect = rect });
            _sliderHits.Add(new SliderHit { id = id, dec = dec, inc = inc, val = valRect });
        }

        private Flashlight GetFlashlight()
        {
            if (_fl == null) _fl = FindFirstObjectByType<Flashlight>();
            return _fl;
        }

        private static bool IsSlider(string id) => id != null && id.StartsWith("fl_");

        // Ajusta un parámetro de la linterna (dir = ±1). Pasos por parámetro.
        private void Adjust(string id, float dir)
        {
            float step = id == "fl_range" ? 1f : id == "fl_intensity" ? 0.5f : 2f;
            SetFlashlightValue(id, CurrentValue(id) + dir * step);
        }

        private float CurrentValue(string id)
        {
            var fl = GetFlashlight();
            if (fl == null) return 0f;
            switch (id)
            {
                case "fl_range":     return fl.range;
                case "fl_outer":     return fl.outerAngleDeg;
                case "fl_inner":     return fl.innerAngleDeg;
                case "fl_intensity": return fl.intensity;
            }
            return 0f;
        }

        // Setea un parámetro con su clamp (compartido por [-]/[+] y por la edición manual).
        private void SetFlashlightValue(string id, float value)
        {
            var fl = GetFlashlight();
            if (fl == null) return;
            switch (id)
            {
                case "fl_range":     fl.range         = Mathf.Clamp(value, 0.5f, 30f); break;
                case "fl_outer":     fl.outerAngleDeg = Mathf.Clamp(value, 2f,   89f); break;
                case "fl_inner":     fl.innerAngleDeg = Mathf.Clamp(value, 0f,   fl.outerAngleDeg - 1f); break;
                case "fl_intensity": fl.intensity     = Mathf.Clamp(value, 0f,   10f); break;
            }
        }

        // ── Edición manual del valor (tocar el número) ────────────────────────
        private void BeginEdit(string id)
        {
            if (_editingSlider == id) return;
            CommitEdit(); // confirmar cualquier edición previa
            _editingSlider = id;
            _editText = CurrentValue(id).ToString("0.###", CultureInfo.InvariantCulture);
            if (TouchScreenKeyboard.isSupported)
                _kb = TouchScreenKeyboard.Open(_editText, TouchScreenKeyboardType.DecimalPad,
                                               autocorrection: false, multiline: false,
                                               secure: false, alert: false, textPlaceholder: "");
        }

        private void CommitEdit()
        {
            if (_editingSlider == null) return;
            if (float.TryParse(_editText, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                SetFlashlightValue(_editingSlider, v);
            CancelEdit();
        }

        private void CancelEdit()
        {
            _editingSlider = null;
            _editText = "";
            if (_kb != null) { _kb.active = false; _kb = null; }
        }

        // Dibuja una fila con label a la izquierda y un checkbox a la derecha. Se registra
        // como ítem (tap / South la togglean, igual que un botón).
        private void AddToggle(string id, Rect rect, string label, bool value)
        {
            bool focused = _items.Count == _focus;
            DrawRect(rect, focused ? new Color(0.95f, 0.85f, 0.20f, 0.30f)
                                   : new Color(1f, 1f, 1f, 0.07f));
            GUI.Label(new Rect(rect.x + 14f, rect.y, rect.width - 80f, rect.height), label, _toggleLbl);

            float bs = rect.height * 0.5f;
            var box = new Rect(rect.xMax - bs - 16f, rect.y + (rect.height - bs) * 0.5f, bs, bs);
            DrawRect(box, new Color(0.75f, 0.75f, 0.8f));                         // marco
            float b = Mathf.Max(2f, bs * 0.14f);
            DrawRect(new Rect(box.x + b, box.y + b, box.width - 2f * b, box.height - 2f * b),
                     value ? new Color(0.30f, 0.80f, 0.35f) : new Color(0.12f, 0.12f, 0.14f));

            UIBlocker.AddVirtualRect(rect);
            _items.Add(new Item { id = id, rect = rect });
        }

        // ---------------------------------------------------------------- Update
        private void Update()
        {
            var gp = GamepadManager.Instance != null ? GamepadManager.Instance.Current : null;

            // Abrir/cerrar con Start del gamepad.
            if (gp != null && gp.startButton.wasPressedThisFrame)
                Toggle();

            // Tap (dedos/mouse): botón de pausa siempre; ítems si está abierto.
            if (TryGetTapRelease(out var tapPx))
            {
                float f = UIScale.Factor;
                var pv = new Vector2(tapPx.x / f, (Screen.height - tapPx.y) / f); // a virtual

                if (_pauseBtnRect.Contains(pv))
                {
                    Toggle();
                }
                else if (_open)
                {
                    // Si estabas editando y el tap NO cae en ese campo, confirmar la edición.
                    if (_editingSlider != null)
                    {
                        bool onField = false;
                        for (int i = 0; i < _sliderHits.Count; i++)
                            if (_sliderHits[i].id == _editingSlider && _sliderHits[i].val.Contains(pv)) { onField = true; break; }
                        if (!onField) CommitEdit();
                    }

                    // Zonas de los sliders: valor (editar), [-] y [+]. Tienen prioridad.
                    bool handled = false;
                    for (int i = 0; i < _sliderHits.Count; i++)
                    {
                        var sh = _sliderHits[i];
                        if (sh.val.Contains(pv)) { BeginEdit(sh.id);            FocusById(sh.id); handled = true; break; }
                        if (sh.dec.Contains(pv)) { CommitEdit(); Adjust(sh.id, -1f); FocusById(sh.id); handled = true; break; }
                        if (sh.inc.Contains(pv)) { CommitEdit(); Adjust(sh.id, +1f); FocusById(sh.id); handled = true; break; }
                    }
                    if (!handled)
                        for (int i = 0; i < _items.Count; i++)
                            if (_items[i].rect.Contains(pv)) { _focus = i; Activate(_items[i].id); break; }
                }
            }

            // Teclado nativo (device): reflejar el texto y confirmar/cancelar al cerrar.
            if (_editingSlider != null && _kb != null)
            {
                _editText = _kb.text;
                var st = _kb.status;
                if (st == TouchScreenKeyboard.Status.Done) CommitEdit();
                else if (st == TouchScreenKeyboard.Status.Canceled ||
                         st == TouchScreenKeyboard.Status.LostFocus) CancelEdit();
            }

            if (!_open) return;

            // Volver/cerrar con East (B/○).
            if (gp != null && gp.buttonEast.wasPressedThisFrame) { Back(); return; }

            // Navegación de foco con dpad / stick izquierdo (con auto-repeat).
            if (gp != null && _items.Count > 0)
            {
                float dy = gp.dpad.ReadValue().y;
                if (Mathf.Abs(dy) < 0.5f) dy = gp.leftStick.ReadValue().y;

                _navCooldown -= Time.unscaledDeltaTime;
                if (Mathf.Abs(dy) > 0.5f)
                {
                    if (_navCooldown <= 0f)
                    {
                        _focus = (_focus + (dy < 0 ? 1 : -1) + _items.Count) % _items.Count;
                        _navCooldown = 0.18f;
                    }
                }
                else _navCooldown = 0f; // soltó → próximo movimiento es inmediato

                // Ajuste horizontal (izq/der) cuando el ítem con foco es un slider.
                _adjCooldown -= Time.unscaledDeltaTime;
                string focusId = _items[_focus].id;
                if (IsSlider(focusId))
                {
                    float dx = gp.dpad.ReadValue().x;
                    if (Mathf.Abs(dx) < 0.5f) dx = gp.leftStick.ReadValue().x;
                    if (Mathf.Abs(dx) > 0.5f)
                    {
                        if (_adjCooldown <= 0f) { Adjust(focusId, dx > 0 ? 1f : -1f); _adjCooldown = 0.12f; }
                    }
                    else _adjCooldown = 0f;
                }

                // Activar con South (A/✕) — para botones/toggles (los sliders usan izq/der).
                if (gp.buttonSouth.wasPressedThisFrame)
                    Activate(_items[_focus].id);
            }
        }

        private void FocusById(string id)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].id == id) { _focus = i; return; }
        }

        // -------------------------------------------------------------- Acciones
        private void Toggle()
        {
            CommitEdit();
            _open = !_open;
            _page = Page.Main;
            _focus = 0;
        }

        private void Back()
        {
            CommitEdit();
            if (_page == Page.Options || _page == Page.Flashlight) { _page = Page.Main; _focus = 0; }
            else _open = false;
        }

        private void Activate(string id)
        {
            switch (id)
            {
                case "opciones": _page = Page.Options;   _focus = 0; break;
                case "linterna": _page = Page.Flashlight; _focus = 0; break;
                case "reanudar": _open = false; break;
                case "volver":   _page = Page.Main;  _focus = 0; break;
                case "envlight":  EnvironmentLightingController.Enabled = !EnvironmentLightingController.Enabled; break;
                case "meshlight": FlashlightMeshLighting.Enabled       = !FlashlightMeshLighting.Enabled;       break;
            }
        }

        // Igual que RecalibrateButton: tap soltado este frame (px, origen abajo-izq).
        private bool TryGetTapRelease(out Vector2 pos)
        {
            pos = Vector2.zero;
            foreach (var t in ETouch.activeTouches)
            {
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                    t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    pos = t.screenPosition;
                    return true;
                }
            }
            var ms = Mouse.current;
            if (ms != null && ms.leftButton.wasReleasedThisFrame)
            {
                pos = ms.position.ReadValue();
                return true;
            }
            return false;
        }

        // --------------------------------------------------------------- Estilos
        private void DrawRect(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, _tex);
            GUI.color = Color.white;
        }

        private void EnsureStyles()
        {
            if (_tex == null)
            {
                _tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _tex.SetPixel(0, 0, Color.white);
                _tex.Apply();
                _tex.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_btn != null) return;

            _icon = new GUIStyle(GUI.skin.box)
            {
                fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
            };
            _icon.normal.textColor = Color.white;

            _btn = new GUIStyle(GUI.skin.button)
            {
                fontSize = 24, alignment = TextAnchor.MiddleCenter
            };
            _btnFocus = new GUIStyle(_btn) { fontStyle = FontStyle.Bold };
            _btnFocus.normal.textColor = Color.black;
            _btnFocus.normal.background = SolidTex(new Color(0.95f, 0.85f, 0.20f));
            _btnFocus.hover.background = _btnFocus.normal.background;

            _title = new GUIStyle { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _title.normal.textColor = Color.white;

            _status = new GUIStyle { fontSize = 20, alignment = TextAnchor.UpperLeft, wordWrap = true };
            _status.normal.textColor = new Color(0.85f, 0.85f, 0.9f);

            _battTxt = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _battTxt.normal.textColor = Color.white;

            _toggleLbl = new GUIStyle { fontSize = 22, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            _toggleLbl.normal.textColor = Color.white;
        }

        // Dibuja una batería (cuerpo + terminal) con relleno proporcional y el
        // porcentaje numérico al lado. Si el mando no reporta batería, muestra "N/D".
        private void DrawBattery(Rect area, float level01, bool present)
        {
            float h = area.height;
            float bodyW = h * 1.9f;
            var body = new Rect(area.x, area.y, bodyW, h);
            var tip  = new Rect(body.xMax, area.y + h * 0.30f, h * 0.16f, h * 0.40f);

            // Marco + terminal + fondo interno.
            DrawRect(body, new Color(0.55f, 0.55f, 0.60f));
            DrawRect(tip,  new Color(0.55f, 0.55f, 0.60f));
            float b = Mathf.Max(2f, h * 0.10f);   // grosor del marco
            var inner = new Rect(body.x + b, body.y + b, body.width - 2 * b, body.height - 2 * b);
            DrawRect(inner, new Color(0.12f, 0.12f, 0.14f));

            string label;
            if (present)
            {
                float lvl = Mathf.Clamp01(level01);
                Color fill = lvl > 0.5f ? new Color(0.30f, 0.80f, 0.35f)
                           : lvl > 0.2f ? new Color(0.95f, 0.80f, 0.20f)
                                        : new Color(0.90f, 0.30f, 0.30f);
                DrawRect(new Rect(inner.x, inner.y, inner.width * lvl, inner.height), fill);
                label = Mathf.RoundToInt(lvl * 100f) + "%";
            }
            else
            {
                label = "N/D";   // el mando no reporta batería (USB / no soportado)
            }

            GUI.color = Color.white;
            GUI.Label(new Rect(tip.xMax + h * 0.4f, area.y, area.width - bodyW, h), label, _battTxt);
        }

        private static Texture2D SolidTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }
    }
}
