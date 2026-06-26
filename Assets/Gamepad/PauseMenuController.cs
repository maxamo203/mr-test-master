using System.Collections.Generic;
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
        private enum Page { Main, Options }

        private bool _open;
        private Page _page = Page.Main;
        private int  _focus;

        // Ítems interactivos del panel visible, en coords VIRTUALES. Se reconstruye
        // en cada OnGUI; Update los usa para navegación (foco) y hit-test de tap.
        private struct Item { public string id; public Rect rect; }
        private readonly List<Item> _items = new();
        private Rect _pauseBtnRect;

        // Navegación con gamepad: cooldown para el auto-repeat del foco.
        private float _navCooldown;

        // Estilos / texturas IMGUI.
        private static Texture2D _tex;
        private GUIStyle _btn, _btnFocus, _icon, _title, _status, _battTxt;

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
            float ph = Mathf.Min(vh - 120f, _page == Page.Options ? 760f : 360f);
            var panel = new Rect((vw - pw) / 2f, (vh - ph) / 2f, pw, ph);
            DrawRect(panel, new Color(0.10f, 0.10f, 0.12f, 0.98f));

            float pad = 24f;
            float x = panel.x + pad, w = pw - pad * 2f;
            float y = panel.y + pad;

            if (_page == Page.Main)
            {
                GUI.Label(new Rect(x, y, w, 50f), "Pausa", _title); y += 64f;
                AddButton("opciones", new Rect(x, y, w, 64f), "Opciones"); y += 76f;
                AddButton("reanudar", new Rect(x, y, w, 64f), "Reanudar"); y += 76f;
            }
            else // Options
            {
                GUI.Label(new Rect(x, y, w, 50f), "Opciones", _title); y += 60f;

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
                    for (int i = 0; i < _items.Count; i++)
                        if (_items[i].rect.Contains(pv)) { _focus = i; Activate(_items[i].id); break; }
                }
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

                // Activar con South (A/✕).
                if (gp.buttonSouth.wasPressedThisFrame)
                    Activate(_items[_focus].id);
            }
        }

        // -------------------------------------------------------------- Acciones
        private void Toggle()
        {
            _open = !_open;
            _page = Page.Main;
            _focus = 0;
        }

        private void Back()
        {
            if (_page == Page.Options) { _page = Page.Main; _focus = 0; }
            else _open = false;
        }

        private void Activate(string id)
        {
            switch (id)
            {
                case "opciones": _page = Page.Options; _focus = 0; break;
                case "reanudar": _open = false; break;
                case "volver":   _page = Page.Main;  _focus = 0; break;
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
