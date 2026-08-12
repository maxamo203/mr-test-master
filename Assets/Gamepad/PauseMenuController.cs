using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using Scanner;   // UIScale, UIBlocker
using T = MortuoriumTheme;

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
        // Main → Opciones/Salir/Reanudar. Opciones es un hub con el volumen y las
        // subcategorías: Control (mando), Cardboard (calibración estéreo), Voz (chat de
        // voz, sólo en sesiones multijugador) y — SOLO en development build — Linterna
        // (tuning) y el toggle del Debug HUD.
        private enum Page { Main, Options, Control, Cardboard, Flashlight, Voice, DebugPanels, ARCalidad, VHS }

        public static PauseMenuController Instance { get; private set; }

        private bool _open;
        public static bool IsOpen => Instance != null && Instance._open;

        private Page _page = Page.Main;
        // "Salir al menú" pide una segunda pulsación de confirmación (corta la partida).
        private bool _confirmSalir;
        private int  _focus;
        // Al entrar a una página, enfocar el ÚLTIMO ítem (siempre "Volver"/"Reanudar") en vez
        // del primero, para no dejar resaltado el primer contenido (p. ej. "Control"). Se
        // resuelve en OnGUI, cuando ya se sabe cuántos ítems hay.
        private bool _focusLast;

        // El resaltado amarillo es el indicador de foco del GAMEPAD. Con touch no hay
        // "selección" persistente, así que solo lo mostramos si hay un mando conectado
        // (si no, todos los botones se ven grises).
        private bool ShowFocus =>
            GamepadManager.Instance != null && GamepadManager.Instance.IsConnected;

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
        private CardboardCalibrationUI _cb;

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
        private GUIStyle _btn, _icon, _title, _status, _battTxt, _toggleLbl;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
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
            DrawRect(_pauseBtnRect, new Color(0f, 0f, 0f, 0.5f));
            T.Borde(_pauseBtnRect, T.Border);
            GUI.Label(_pauseBtnRect, _open ? "X" : "II", _icon);

            if (!_open) return;

            // Overlay oscuro que bloquea la escena de atrás.
            var full = new Rect(0, 0, vw, vh);
            DrawRect(full, new Color(T.Bg.r, T.Bg.g, T.Bg.b, 0.8f));
            UIBlocker.AddVirtualRect(full);

            // El chat de voz sólo existe en una sesión multijugador: si no hay sala, ni
            // se ofrece la subcategoría (y el panel de Opciones queda más bajo).
            bool hayVoz = Voice.VoiceChatManager.SesionConVoz;

            // Panel centrado. La altura depende de la página (cada submenú tiene su alto).
            float pw = Mathf.Min(vw - 40f, 560f);
            float ph = Mathf.Min(vh - 120f, AltoPagina(hayVoz));
            var panel = new Rect((vw - pw) / 2f, (vh - ph) / 2f, pw, ph);
            DrawRect(panel, new Color(T.BgPanel.r, T.BgPanel.g, T.BgPanel.b, 0.98f));
            T.Borde(panel, T.Border);

            float pad = 24f;
            float x = panel.x + pad, w = pw - pad * 2f;
            float y = panel.y + pad;

            if (_page == Page.Main)
            {
                GUI.Label(new Rect(x, y, w, 50f), "PAUSA", _title); y += 64f;
                AddButton("opciones", new Rect(x, y, w, 64f), "OPCIONES"); y += 76f;
                AddButton("salir",    new Rect(x, y, w, 64f),
                          _confirmSalir ? "¿SEGURO? TOCÁ DE NUEVO" : "SALIR AL MENÚ"); y += 76f;
                AddButton("reanudar", new Rect(x, y, w, 64f), "REANUDAR"); y += 76f;
            }
            else if (_page == Page.Options)
            {
                // Hub de opciones: volumen + subcategorías. Linterna y Debug HUD
                // solo existen en development build.
                GUI.Label(new Rect(x, y, w, 50f), "OPCIONES", _title); y += 64f;
                AddSlider("opt_vol", new Rect(x, y, w, 60f), "Volumen",
                          GameOptions.Volumen * 100f, 0f, 100f, "{0:0}%"); y += 72f;
                AddButton("control",   new Rect(x, y, w, 64f), "CONTROL (MANDO)"); y += 76f;
                AddButton("cardboard", new Rect(x, y, w, 64f), "CARDBOARD");       y += 76f;
                AddButton("arcalidad", new Rect(x, y, w, 64f), "CALIDAD AR");      y += 76f;
                if (hayVoz) { AddButton("voz", new Rect(x, y, w, 64f), "CHAT DE VOZ"); y += 76f; }
                // US-11.1: el filtro VHS de la partida no se apaga (es atmósfera); lo
                // que el jugador decide es si además cubre los menús.
                AddToggle("vhsmenus", new Rect(x, y, w, 52f), "Filtro VHS en menús",
                          GameOptions.VhsEnMenus); y += 64f;
                if (Debug.isDebugBuild)
                {
                    AddButton("vhs",      new Rect(x, y, w, 64f), "VHS (DEV)");      y += 76f;
                    AddButton("linterna", new Rect(x, y, w, 64f), "LINTERNA (DEV)"); y += 76f;
                    AddToggle("debughud", new Rect(x, y, w, 52f), "Debug HUD (dev)",
                              DebugHud.Visible); y += 64f;
                    AddButton("dbgpaneles", new Rect(x, y, w, 64f), "PANELES DEBUG (DEV)"); y += 76f;
                }
                AddButton("volver", new Rect(x, y, w, 60f), "VOLVER");
            }
            else if (_page == Page.Control)
            {
                // Estado del mando, batería y gamepad virtual en vivo.
                GUI.Label(new Rect(x, y, w, 50f), "Control", _title); y += 60f;

                var gm = GamepadManager.Instance;
                string status = (gm != null && gm.IsConnected)
                    ? $"Joystick: {gm.DisplayName}\nTipo: {gm.Brand}   -   Estado: Conectado"
                    : "Joystick: ninguno\nConectá un mando por Bluetooth desde el sistema.";
                GUI.Label(new Rect(x, y, w, 72f), status, _status); y += 78f;

                if (gm != null && gm.IsConnected)
                {
                    bool present = gm.TryGetBattery(out float lvl);
                    DrawBattery(new Rect(x, y, w, 38f), lvl, present);
                    y += 52f;
                }

                float gh = w * 0.62f;           // proporción ~landscape
                var gpArea = new Rect(x, y, w, gh);
                DrawRect(gpArea, new Color(0.06f, 0.06f, 0.07f, 1f));
                var st = gm != null ? gm.ReadState() : default;
                GamepadVisualizer.Draw(gpArea, gm != null ? gm.Brand : GamepadBrand.None, st);
                y += gh + 16f;

                AddButton("volver", new Rect(x, y, w, 60f), "Volver");
            }
            else if (_page == Page.Cardboard)
            {
                // Calibración del estéreo Cardboard (antes en el botón "Config" de pantalla).
                GUI.Label(new Rect(x, y, w, 50f), "Cardboard", _title); y += 62f;

                var cb = GetCardboard();
                if (cb == null)
                {
                    GUI.Label(new Rect(x, y, w, 60f),
                              "El modo Cardboard no está disponible en esta escena.", _status);
                    y += 70f;
                }
                else
                {
                    // Óptica del visor: alinea las dos mitades con las lentes. No hace 3D.
                    AddSlider("cb_zoom", new Rect(x, y, w, 60f), "Zoom feed", cb.Scale,
                              CardboardCalibrationUI.ScaleMin, CardboardCalibrationUI.ScaleMax, "{0:0.00}");   y += 68f;
                    AddSlider("cb_offL", new Rect(x, y, w, 60f), "Distancia ojo izq", cb.OffsetL,
                              0f, cb.MaxOffset, "{0:0.000}"); y += 68f;
                    AddSlider("cb_offR", new Rect(x, y, w, 60f), "Distancia ojo der", cb.OffsetR,
                              0f, cb.MaxOffset, "{0:0.000}"); y += 68f;

                    // Estéreo real: rendea la escena dos veces, una por ojo. El passthrough
                    // sigue siendo mono (el celular tiene una sola cámara); lo que gana
                    // profundidad son los objetos virtuales.
                    y += 6f;
                    AddToggle("estereo3d", new Rect(x, y, w, 52f), "Visión 3D (estéreo)", cb.Estereo3D);
                    y += 58f;

                    if (cb.Estereo3D)
                    {
                        AddSlider("cb_ipd", new Rect(x, y, w, 60f), "Separación de ojos",
                                  cb.Ipd * 1000f, CardboardCalibrationUI.IpdMin * 1000f,
                                  CardboardCalibrationUI.IpdMax * 1000f, "{0:0} mm"); y += 68f;
                        // Distancia de paralaje cero: lo que esté a esta distancia cae en el
                        // mismo punto de pantalla en los dos ojos (se ve igual que en mono);
                        // lo más cerca sale hacia el jugador y lo más lejos se hunde.
                        AddSlider("cb_conv", new Rect(x, y, w, 60f), "Distancia de foco 3D",
                                  cb.Convergencia, CardboardCalibrationUI.ConvMin,
                                  CardboardCalibrationUI.ConvMax, "{0:0.0} m"); y += 68f;
                    }
                }

                y += 6f;
                AddButton("volver", new Rect(x, y, w, 60f), "Volver");
            }
            else if (_page == Page.Voice)
            {
                // Chat de voz de la sala: mute propio, volumen general y un slider de
                // volumen POR JUGADOR (los ajustes por jugador viven sólo en memoria,
                // ver GameOptions — los clientId se reasignan en cada sala).
                GUI.Label(new Rect(x, y, w, 50f), "CHAT DE VOZ", _title); y += 62f;

                var vc = Voice.VoiceChatManager.Instance;

                AddToggle("vozmic", new Rect(x, y, w, 52f),
                          "Micrófono (transmitir)", GameOptions.VozMic); y += 58f;

                // Barra de nivel del micrófono con la marca del umbral: sin esto el
                // slider de sensibilidad se ajusta a ciegas.
                DrawNivelMic(new Rect(x + 14f, y, w - 28f, 16f), vc); y += 30f;

                AddSlider("voz_vol", new Rect(x, y, w, 60f), "Volumen voces",
                          GameOptions.VozVolumen * 100f, 0f, 100f, "{0:0}%"); y += 68f;
                AddSlider("voz_sens", new Rect(x, y, w, 60f), "Sensibilidad mic",
                          GameOptions.VozSensibilidad * 100f, 0f, 100f, "{0:0}%"); y += 68f;

                GUI.Label(new Rect(x + 14f, y, w - 28f, 24f), "VOLUMEN POR JUGADOR", _status);
                y += 30f;

                if (vc == null || vc.Otros.Count == 0)
                {
                    GUI.Label(new Rect(x + 14f, y, w - 28f, 36f),
                              "No hay otros jugadores en la sala.", _status);
                    y += 40f;
                }
                else
                {
                    for (int i = 0; i < vc.Otros.Count; i++)
                    {
                        uint   id     = vc.Otros[i];
                        string nombre = Voice.VoiceChatManager.NombreDe(id);
                        if (vc.EstaHablando(id)) nombre += "  ●";   // marca de "está hablando"
                        AddSlider("voz_p" + id, new Rect(x, y, w, 60f), nombre,
                                  vc.VolumenDe(id) * 100f, 0f, 100f, "{0:0}%");
                        y += 68f;
                    }
                }

                y += 6f;
                AddButton("volver", new Rect(x, y, w, 60f), "Volver");
            }
            else if (_page == Page.ARCalidad)
            {
                // Es la opción que más pesa en batería y temperatura del teléfono.
                GUI.Label(new Rect(x, y, w, 50f), "CALIDAD AR", _title); y += 58f;

                GUI.Label(new Rect(x + 14f, y, w - 28f, 46f),
                          "Define cuánto trabaja el AR (malla del cuarto y profundidad). " +
                          "Los 60 fps no cambian: bajar de ahí marea.", _status);
                y += 54f;

                var actual = ARQuality.Actual;
                for (int i = 0; i <= 2; i++)
                {
                    var niv = (ARQuality.Nivel)i;
                    bool sel = niv == actual;
                    AddButton("arq_" + i, new Rect(x, y, w, 56f),
                              (sel ? "> " : "") + ARQuality.Nombre(niv));
                    y += 60f;
                    GUI.Label(new Rect(x + 22f, y, w - 44f, 40f), ARQuality.Descripcion(niv), _status);
                    y += 44f;
                }

                y += 6f;
                AddButton("volver", new Rect(x, y, w, 60f), "Volver");
            }
            else if (_page == Page.VHS)
            {
                // US-11.1 — ingredientes del filtro VHS por separado, para comparar en el
                // dispositivo cuál combinación queda mejor. SOLO development build: en
                // release el filtro va con los valores fijos de VHSSettings.
                if (!Debug.isDebugBuild) { _page = Page.Options; return; }

                GUI.Label(new Rect(x, y, w, 50f), "VHS (DEV)", _title); y += 58f;
                GUI.Label(new Rect(x + 14f, y, w - 28f, 40f),
                          "Se ven en partida (shader) y en menús (IMGUI, sin warp).", _status);
                y += 46f;

                // Un slider por ingrediente (0% = apagado) para poder calibrar la mezcla
                // en el dispositivo, más el multiplicador global. Ojo: el prefijo "vhs_"
                // marca slider (ver IsSlider), por eso el toggle del REC va sin guion
                // bajo, igual que "vozmic" en la página de voz.
                AddSlider("vhs_int",    new Rect(x, y, w, 60f), "Intensidad global",
                          Gameplay.VHSSettings.Intensidad * 100f, 0f, 100f, "{0:0}%"); y += 68f;
                AddSlider("vhs_scan",   new Rect(x, y, w, 60f), "Scanlines",
                          Gameplay.VHSSettings.Scanlines  * 100f, 0f, 100f, "{0:0}%"); y += 68f;
                AddSlider("vhs_grano",  new Rect(x, y, w, 60f), "Grano de cinta",
                          Gameplay.VHSSettings.Grano      * 100f, 0f, 100f, "{0:0}%"); y += 68f;
                AddSlider("vhs_bandas", new Rect(x, y, w, 60f), "Bandas de tracking",
                          Gameplay.VHSSettings.Bandas     * 100f, 0f, 100f, "{0:0}%"); y += 68f;
                AddSlider("vhs_jit",    new Rect(x, y, w, 60f), "Jitter de línea",
                          Gameplay.VHSSettings.Jitter     * 100f, 0f, 100f, "{0:0}%"); y += 68f;
                AddSlider("vhs_vin",    new Rect(x, y, w, 60f), "Viñeta",
                          Gameplay.VHSSettings.Vineta     * 100f, 0f, 100f, "{0:0}%"); y += 68f;
                AddSlider("vhs_tinte",  new Rect(x, y, w, 60f), "Tinte / desaturado",
                          Gameplay.VHSSettings.Tinte      * 100f, 0f, 100f, "{0:0}%"); y += 68f;
                AddToggle("vhsrec",     new Rect(x, y, w, 52f), "REC + fecha",
                          Gameplay.VHSSettings.Rec); y += 58f;

                y += 6f;
                AddButton("volver", new Rect(x, y, w, 60f), "Volver");
            }
            else if (_page == Page.DebugPanels)
            {
                // Un check por panel del DebugHud: mostrarlos todos juntos no entra en
                // pantalla. El estado lo persiste DebugHud en PlayerPrefs.
                if (!Debug.isDebugBuild) { _page = Page.Options; return; }

                GUI.Label(new Rect(x, y, w, 50f), "PANELES DEBUG", _title); y += 62f;

                var paneles = DebugHud.Paneles;
                if (paneles.Count == 0)
                {
                    GUI.Label(new Rect(x + 14f, y, w - 28f, 60f),
                              "El Debug HUD no está creado en esta sesión.", _status);
                    y += 66f;
                }
                else
                {
                    for (int i = 0; i < paneles.Count; i++)
                    {
                        var p = paneles[i];
                        if (p == null) continue;
                        AddToggle("dbgp_" + i, new Rect(x, y, w, 52f), p.name, p.activeSelf);
                        y += 58f;
                    }
                }

                y += 6f;
                AddButton("volver", new Rect(x, y, w, 60f), "Volver");
            }
            else // Flashlight — tuning de linterna, SOLO development build.
            {
                if (!Debug.isDebugBuild) { _page = Page.Options; return; }
                GUI.Label(new Rect(x, y, w, 50f), "LINTERNA (DEV)", _title); y += 62f;

                // Toggles de iluminación (efecto de oscurecido y malla LiDAR/AR).
                AddToggle("envlight", new Rect(x, y, w, 52f),
                          "Iluminación del entorno (tiempo real)",
                          EnvironmentLightingController.Enabled); y += 62f;
                AddToggle("meshlight", new Rect(x, y, w, 52f),
                          "Iluminar malla del entorno (LiDAR/AR)",
                          FlashlightMeshLighting.Enabled); y += 66f;

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

            // Resuelve el foco: si se pidió "enfocar el último" (al entrar a la página), ahora
            // que ya sabemos cuántos ítems hay, apuntamos a "Volver"/"Reanudar". Si no, clamp.
            if (_items.Count > 0)
            {
                if (_focusLast) { _focus = _items.Count - 1; _focusLast = false; }
                else _focus = Mathf.Clamp(_focus, 0, _items.Count - 1);
            }
        }

        // Dibuja un botón (resaltado si tiene el foco) y lo registra como ítem.
        private void AddButton(string id, Rect rect, string label)
        {
            bool focused = ShowFocus && _items.Count == _focus;
            if (focused)
            {
                DrawRect(rect, new Color(T.Tan.r, T.Tan.g, T.Tan.b, 0.14f));
                T.Borde(rect, T.Tan);
            }
            else
            {
                DrawRect(rect, new Color(0f, 0f, 0f, 0.35f));
                T.Borde(rect, id == "salir" || id == "reanudar" ? T.Red : T.Border);
            }
            GUI.Label(rect, label, _btn);
            UIBlocker.AddVirtualRect(rect);
            _items.Add(new Item { id = id, rect = rect });
        }

        // Dibuja un slider (label + valor + barra + botones [-]/[+]). Se registra como ítem
        // focusable (gamepad izq/der ajusta) y sus zonas [-]/[+] para el tap.
        private void AddSlider(string id, Rect rect, string label, float value,
                               float min, float max, string fmt)
        {
            bool focused = ShowFocus && _items.Count == _focus;
            DrawRect(rect, focused ? new Color(T.Tan.r, T.Tan.g, T.Tan.b, 0.18f)
                                   : new Color(1f, 1f, 1f, 0.05f));
            if (focused) T.Borde(rect, T.Tan);

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
            DrawRect(barBg, new Color(1f, 1f, 1f, 0.12f));
            DrawRect(new Rect(barBg.x, barBg.y, barBg.width * Mathf.Clamp01(t), barBg.height), T.Red);

            T.Borde(dec, T.Border);
            T.Borde(inc, T.Border);
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

        private CardboardCalibrationUI GetCardboard()
        {
            if (_cb == null) _cb = FindFirstObjectByType<CardboardCalibrationUI>();
            return _cb;
        }

        // Persiste la calibración de Cardboard a disco (si existe en la escena).
        private void SaveCardboard()
        {
            var cb = GetCardboard();
            if (cb != null) cb.Save();
        }

        // Alto del panel según la página. La de voz crece con la cantidad de jugadores
        // (un slider por cada uno).
        private float AltoPagina(bool hayVoz)
        {
            switch (_page)
            {
                case Page.Control:    return 820f;
                case Page.Flashlight: return 660f;
                case Page.Cardboard:
                {
                    // 3 sliders de óptica + el toggle de estéreo; con el estéreo prendido
                    // aparecen además los sliders de IPD y convergencia.
                    var cbAlto = GetCardboard();
                    return 524f + (cbAlto != null && cbAlto.Estereo3D ? 136f : 0f);
                }
                // +64 por el toggle "Filtro VHS en menús" (prod) y +76 por "VHS (DEV)".
                case Page.Options:    return (Debug.isDebugBuild ? 912f : 620f) + (hayVoz ? 76f : 0f);
                case Page.ARCalidad:  return 490f;
                // 7 sliders (global + 6 ingredientes) + el toggle del REC.
                case Page.VHS:        return 760f;
                case Page.Voice:
                {
                    var vc = Voice.VoiceChatManager.Instance;
                    int n  = vc != null ? vc.Otros.Count : 0;
                    return 428f + (n > 0 ? n * 68f : 40f);
                }
                case Page.DebugPanels:
                {
                    int n = DebugHud.Paneles.Count;
                    return 176f + (n > 0 ? n * 58f : 66f);
                }
                default: return 400f;
            }
        }

        // Barra de nivel del micrófono propio + marca roja del umbral de la VAD: si el
        // nivel no pasa la marca, el micro no transmite.
        private void DrawNivelMic(Rect r, Voice.VoiceChatManager vc)
        {
            DrawRect(r, new Color(1f, 1f, 1f, 0.10f));
            if (vc == null || !vc.MicAbierto) return;

            // El RMS de la voz vive en la parte baja del rango: se escala para que la
            // barra se mueva de forma legible (x6 ≈ voz normal llenando la barra).
            const float Escala = 6f;
            float umbral = Voice.VoiceChatManager.UmbralActual();
            float nivel  = Mathf.Clamp01(vc.NivelMic * Escala);

            DrawRect(new Rect(r.x, r.y, r.width * nivel, r.height),
                     vc.NivelMic >= umbral ? T.Tan : new Color(1f, 1f, 1f, 0.35f));
            DrawRect(new Rect(r.x + r.width * Mathf.Clamp01(umbral * Escala) - 1f,
                              r.y - 2f, 2f, r.height + 4f), T.Red);
        }

        // Los sliders llevan prefijo por familia: fl_ (linterna), cb_ (cardboard),
        // opt_ (opciones de usuario) y voz_ (chat de voz; "vozmic" es un toggle, no
        // lleva guion bajo justamente para no caer acá).
        private static bool IsSlider(string id) =>
            id != null && (id.StartsWith("fl_") || id.StartsWith("cb_") ||
                           id.StartsWith("opt_") || id.StartsWith("voz_") ||
                           id.StartsWith("vhs_"));

        // "voz_p3" → el slider de volumen del cliente 3. Los ids se arman en la página
        // de voz a partir del roster, así que son dinámicos.
        private static bool TryClientIdVoz(string id, out uint clientId)
        {
            clientId = 0;
            return id != null && id.StartsWith("voz_p") &&
                   uint.TryParse(id.Substring(5), out clientId);
        }

        // Ajusta un parámetro (dir = ±1). Paso por parámetro (grados/metros/factor).
        private void Adjust(string id, float dir)
        {
            float step;
            if (id != null && id.StartsWith("voz_")) { step = 5f; }   // todos en %
            else if (id != null && id.StartsWith("vhs_")) { step = 5f; }   // todos en %
            else switch (id)
            {
                case "fl_range":     step = 1f;     break;
                case "fl_intensity": step = 0.5f;   break;
                case "cb_zoom":      step = 0.02f;  break;
                case "cb_offL":
                case "cb_offR":      step = 0.005f; break;
                case "cb_ipd":       step = 1f;     break;   // milímetros
                case "cb_conv":      step = 0.25f;  break;   // metros
                case "opt_vol":      step = 5f;     break;   // porcentaje
                default:             step = 2f;     break;   // fl_outer / fl_inner (grados)
            }
            SetSliderValue(id, CurrentValue(id) + dir * step);
        }

        private float CurrentValue(string id)
        {
            if (TryClientIdVoz(id, out var cidGet))
            {
                var vc = Voice.VoiceChatManager.Instance;
                return vc != null ? vc.VolumenDe(cidGet) * 100f : 100f;
            }

            switch (id)
            {
                case "voz_vol":  return GameOptions.VozVolumen      * 100f;
                case "voz_sens": return GameOptions.VozSensibilidad * 100f;
                case "fl_range":     { var fl = GetFlashlight(); return fl != null ? fl.range         : 0f; }
                case "fl_outer":     { var fl = GetFlashlight(); return fl != null ? fl.outerAngleDeg : 0f; }
                case "fl_inner":     { var fl = GetFlashlight(); return fl != null ? fl.innerAngleDeg : 0f; }
                case "fl_intensity": { var fl = GetFlashlight(); return fl != null ? fl.intensity     : 0f; }
                case "cb_zoom":      { var cb = GetCardboard();  return cb != null ? cb.Scale   : 0f; }
                case "cb_offL":      { var cb = GetCardboard();  return cb != null ? cb.OffsetL : 0f; }
                case "cb_offR":      { var cb = GetCardboard();  return cb != null ? cb.OffsetR : 0f; }
                case "cb_ipd":       { var cb = GetCardboard();  return cb != null ? cb.Ipd * 1000f  : 0f; }
                case "cb_conv":      { var cb = GetCardboard();  return cb != null ? cb.Convergencia : 0f; }
                case "opt_vol":      return GameOptions.Volumen * 100f;
                case "vhs_int":      return Gameplay.VHSSettings.Intensidad * 100f;
                case "vhs_scan":     return Gameplay.VHSSettings.Scanlines  * 100f;
                case "vhs_grano":    return Gameplay.VHSSettings.Grano      * 100f;
                case "vhs_bandas":   return Gameplay.VHSSettings.Bandas     * 100f;
                case "vhs_jit":      return Gameplay.VHSSettings.Jitter     * 100f;
                case "vhs_vin":      return Gameplay.VHSSettings.Vineta     * 100f;
                case "vhs_tinte":    return Gameplay.VHSSettings.Tinte      * 100f;
            }
            return 0f;
        }

        // Setea un parámetro con su clamp (compartido por [-]/[+] y por la edición manual).
        // Los cb_ clampean dentro de CardboardCalibrationUI (setters).
        private void SetSliderValue(string id, float value)
        {
            if (TryClientIdVoz(id, out var cidSet))
            {
                Voice.VoiceChatManager.Instance?.SetVolumenDe(cidSet, Mathf.Clamp(value, 0f, 100f) / 100f);
                return;
            }

            switch (id)
            {
                case "voz_vol":  GameOptions.VozVolumen      = Mathf.Clamp(value, 0f, 100f) / 100f; break;
                case "voz_sens": GameOptions.VozSensibilidad = Mathf.Clamp(value, 0f, 100f) / 100f; break;
                case "fl_range":     { var fl = GetFlashlight(); if (fl != null) fl.range         = Mathf.Clamp(value, 0.5f, 30f); break; }
                case "fl_outer":     { var fl = GetFlashlight(); if (fl != null) fl.outerAngleDeg = Mathf.Clamp(value, 2f,   89f); break; }
                case "fl_inner":     { var fl = GetFlashlight(); if (fl != null) fl.innerAngleDeg = Mathf.Clamp(value, 0f, fl.outerAngleDeg - 1f); break; }
                case "fl_intensity": { var fl = GetFlashlight(); if (fl != null) fl.intensity     = Mathf.Clamp(value, 0f,   10f); break; }
                case "cb_zoom":      { var cb = GetCardboard(); if (cb != null) cb.Scale   = value; break; }
                case "cb_offL":      { var cb = GetCardboard(); if (cb != null) cb.OffsetL = value; break; }
                case "cb_offR":      { var cb = GetCardboard(); if (cb != null) cb.OffsetR = value; break; }
                case "cb_ipd":       { var cb = GetCardboard(); if (cb != null) cb.Ipd = value / 1000f; break; }
                case "cb_conv":      { var cb = GetCardboard(); if (cb != null) cb.Convergencia = value; break; }
                case "opt_vol":      GameOptions.Volumen = Mathf.Clamp(value, 0f, 100f) / 100f; break;
                case "vhs_int":      Gameplay.VHSSettings.Intensidad = Mathf.Clamp(value, 0f, 100f) / 100f; break;
                case "vhs_scan":     Gameplay.VHSSettings.Scanlines  = Mathf.Clamp(value, 0f, 100f) / 100f; break;
                case "vhs_grano":    Gameplay.VHSSettings.Grano      = Mathf.Clamp(value, 0f, 100f) / 100f; break;
                case "vhs_bandas":   Gameplay.VHSSettings.Bandas     = Mathf.Clamp(value, 0f, 100f) / 100f; break;
                case "vhs_jit":      Gameplay.VHSSettings.Jitter     = Mathf.Clamp(value, 0f, 100f) / 100f; break;
                case "vhs_vin":      Gameplay.VHSSettings.Vineta     = Mathf.Clamp(value, 0f, 100f) / 100f; break;
                case "vhs_tinte":    Gameplay.VHSSettings.Tinte      = Mathf.Clamp(value, 0f, 100f) / 100f; break;
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
                SetSliderValue(_editingSlider, v);
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
            bool focused = ShowFocus && _items.Count == _focus;
            DrawRect(rect, focused ? new Color(T.Tan.r, T.Tan.g, T.Tan.b, 0.18f)
                                   : new Color(1f, 1f, 1f, 0.05f));
            if (focused) T.Borde(rect, T.Tan);
            GUI.Label(new Rect(rect.x + 14f, rect.y, rect.width - 100f, rect.height), label, _toggleLbl);

            // Pill ON/OFF a la derecha (estilo prototipo).
            var pill = new Rect(rect.xMax - 76f, rect.y + (rect.height - 30f) * 0.5f, 60f, 30f);
            T.Borde(pill, value ? T.Tan : T.Border);
            GUI.Label(pill, value ? "ON" : "OFF",
                      T.Estilo(T.FMono, 13, value ? T.Tan : T.Dim, TextAnchor.MiddleCenter));

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

            // VR Box Mouse: right button abre el menú (solo si está cerrado; si está abierto
            // lo maneja el bloque de navegación más abajo para no hacer toggle+back en el mismo frame).
            if (GamepadManager.Instance != null && GamepadManager.Instance.UsesMouseInput &&
                !_open && VRBoxInput.CancelDown)
                Toggle();

            // Tap (dedos/mouse): botón de pausa siempre; ítems si está abierto.
            if (TryGetTapRelease(out var tapPx))
            {
                // Tap (px, origen abajo-izq) -> coords VIRTUALES: hay que invertir la matriz
                // de UIScale.Begin, que además de escalar traslada el origen al área segura.
                // Sin restar (sg.x, sg.y) el hit-test queda corrido en iPhone (notch/Dynamic
                // Island) y el botón de pausa —pegado al borde superior— cae dentro del notch,
                // volviéndose intocable.
                float f  = UIScale.Factor;
                var   sg = UIScale.SafeGui;
                var pv = new Vector2((tapPx.x - sg.x) / f,
                                     (Screen.height - tapPx.y - sg.y) / f); // a virtual

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

            // VR Box Mouse: navegación con VRBoxInput (ya procesado por GamepadManager.Update).
            if (GamepadManager.Instance != null && GamepadManager.Instance.UsesMouseInput)
            {
                if (_items.Count > 0)
                {
                    if (VRBoxInput.CancelDown)  { Back(); return; }
                    if (VRBoxInput.ConfirmDown) Activate(_items[_focus].id);

                    var delta = VRBoxInput.Delta;

                    _navCooldown -= Time.unscaledDeltaTime;
                    if (Mathf.Abs(delta.y) > 3f)
                    {
                        if (_navCooldown <= 0f)
                        {
                            _focus = (_focus + (delta.y > 0 ? -1 : 1) + _items.Count) % _items.Count;
                            _navCooldown = 0.18f;
                        }
                    }
                    else _navCooldown = 0f;

                    _adjCooldown -= Time.unscaledDeltaTime;
                    string focusId = _items[_focus].id;
                    if (IsSlider(focusId) && Mathf.Abs(delta.x) > 3f)
                    {
                        if (_adjCooldown <= 0f) { Adjust(focusId, delta.x > 0 ? 1f : -1f); _adjCooldown = 0.12f; }
                    }
                    else if (Mathf.Abs(delta.x) <= 3f) _adjCooldown = 0f;
                }
                return; // no procesar el bloque de gamepad en modo mouse
            }

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
            if (!_open) SaveCardboard();   // al cerrar, persistir la calibración
            _page = Page.Main;
            _confirmSalir = false;
            _focus = 0; _focusLast = true;
        }

        // Un nivel hacia atrás: submenú → Opciones, Opciones → Main, Main → cerrar.
        private void Back()
        {
            CommitEdit();
            _confirmSalir = false;
            switch (_page)
            {
                case Page.Options:
                    _page = Page.Main; _focus = 0; _focusLast = true; break;
                case Page.Cardboard:
                    SaveCardboard();
                    _page = Page.Options; _focus = 0; _focusLast = true; break;
                case Page.Control:
                case Page.Flashlight:
                case Page.Voice:
                case Page.DebugPanels:
                case Page.ARCalidad:
                case Page.VHS:
                    _page = Page.Options; _focus = 0; _focusLast = true; break;
                default: // Main
                    _open = false; break;
            }
        }

        private void Activate(string id)
        {
            // Cualquier acción que no sea "salir" cancela la confirmación pendiente.
            if (id != "salir") _confirmSalir = false;

            // Toggles dinámicos de los paneles del DebugHud ("dbgp_<índice>"). Ojo: el
            // botón que abre la página es "dbgpaneles", que NO matchea el prefijo con
            // guion bajo.
            if (id != null && id.StartsWith("dbgp_") && int.TryParse(id.Substring(5), out int iPanel))
            {
                var paneles = DebugHud.Paneles;
                if (iPanel >= 0 && iPanel < paneles.Count && paneles[iPanel] != null)
                    DebugHud.SetPanelVisible(paneles[iPanel], !paneles[iPanel].activeSelf);
                return;
            }

            switch (id)
            {
                case "opciones":  _page = Page.Options;    _focus = 0; _focusLast = true; break;
                case "control":   _page = Page.Control;    _focus = 0; _focusLast = true; break;
                case "cardboard": _page = Page.Cardboard;  _focus = 0; _focusLast = true; break;
                case "arcalidad": _page = Page.ARCalidad;  _focus = 0; _focusLast = true; break;
                case "arq_0":     ARQuality.Actual = ARQuality.Nivel.Rendimiento; break;
                case "arq_1":     ARQuality.Actual = ARQuality.Nivel.Equilibrado; break;
                case "arq_2":     ARQuality.Actual = ARQuality.Nivel.Calidad;     break;
                case "voz":       _page = Page.Voice;      _focus = 0; _focusLast = true; break;
                case "vozmic":    GameOptions.VozMic = !GameOptions.VozMic; break;
                case "estereo3d": { var cb = GetCardboard(); if (cb != null) cb.Estereo3D = !cb.Estereo3D; break; }
                case "linterna":
                    if (Debug.isDebugBuild) { _page = Page.Flashlight; _focus = 0; _focusLast = true; }
                    break;
                case "reanudar":  _open = false; SaveCardboard(); break;
                case "salir":
                    // Doble confirmación: corta la partida y vuelve al menú principal.
                    if (!_confirmSalir) { _confirmSalir = true; break; }
                    _open = false;
                    SaveCardboard();
                    SceneFlow.GoTo(SceneFlow.EscenaMenu);
                    break;
                case "volver":    Back(); break;
                case "debughud":  DebugHud.SetVisible(!DebugHud.Visible); break;
                case "dbgpaneles":
                    if (Debug.isDebugBuild) { _page = Page.DebugPanels; _focus = 0; _focusLast = true; }
                    break;
                case "envlight":  EnvironmentLightingController.Enabled = !EnvironmentLightingController.Enabled; break;
                case "meshlight": FlashlightMeshLighting.Enabled       = !FlashlightMeshLighting.Enabled;       break;

                // ── US-11.1 (VHS) ────────────────────────────────────────────
                case "vhsmenus":  GameOptions.VhsEnMenus = !GameOptions.VhsEnMenus; break;
                case "vhs":
                    if (Debug.isDebugBuild) { _page = Page.VHS; _focus = 0; _focusLast = true; }
                    break;
                case "vhsrec":    Gameplay.VHSSettings.Rec = !Gameplay.VHSSettings.Rec; break;
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

            bool android = Application.platform == RuntimePlatform.Android;

            _icon = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter, font = android ? null : T.FBebas };
            _icon.normal.textColor = T.Cream;

            _btn = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, font = android ? null : T.FBebas };
            _btn.normal.textColor = T.Cream;

            _title = new GUIStyle(GUI.skin.label) { fontSize = 32, alignment = TextAnchor.MiddleLeft, font = android ? null : T.FBebas };
            _title.normal.textColor = T.Cream;

            _status = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.UpperLeft, wordWrap = true, font = android ? null : T.FMono };
            _status.normal.textColor = T.CreamDim;

            _battTxt = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleLeft, font = android ? null : T.FMono };
            _battTxt.normal.textColor = T.Cream;

            _toggleLbl = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = true, font = android ? null : T.FMono };
            _toggleLbl.normal.textColor = T.CreamDim;
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

    }
}
