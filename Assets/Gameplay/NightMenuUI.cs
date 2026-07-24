using System.Collections.Generic;
using UnityEngine;
using Scanner;   // UIScale, UIBlocker, ScanSerializer, ScannerLaunchParams
using T = MortuoriumTheme;

namespace Gameplay
{
    // Menú principal MORTUORIUM (escena inicial). Portado del prototipo HTML
    // (Assets/Prototipo Navegacion). Acá se resuelve TODO lo previo a la cámara AR:
    // modo de juego, noche y entorno; recién con todo elegido se carga SampleScene.
    //
    //   UN JUGADOR        -> [sin entorno: alerta -> escanear]
    //                        Noche -> Entorno -> CONFIRMAR -> SampleScene (sincronizar)
    //   MULTIJUGADOR      -> Crear sala / Unirse
    //       CREAR SALA    -> [sin entorno: alerta -> escanear]
    //                        Noche -> Entorno -> CONFIRMAR -> SampleScene (sala/espera)
    //       UNIRSE        -> SampleScene (autodescubrimiento / IP)
    //   ESCANEAR ENTORNO  -> lista de escaneos (editar/borrar/nuevo) -> ScannerScene
    //   OPCIONES          -> volumen + estado del mando
    //
    // La elección viaja en GameSession (Mode / SelectedNight / SelectedMap / HostPort);
    // GameBootstrapper (SampleScene) la lee para hostear o unirse.
    //
    // Wiring: componente en un GameObject de NightMenuScene, con los assets
    // NightConfig arrastrados a _nights. Las 3 escenas deben estar en Build Settings.
    public class NightMenuUI : MonoBehaviour
    {
        [Tooltip("Noches disponibles (arrastra los assets NightConfig).")]
        [SerializeField] private NightConfig[] _nights;
        [Tooltip("Escena del lobby/gameplay a cargar al continuar.")]
        [SerializeField] private string _lobbyScene = "SampleScene";

        private enum Pantalla { Menu, CrearUnirse, SinEntorno, Noches, Entorno, Escaneos, Opciones, Control }

        private Pantalla _pantalla = Pantalla.Menu;

        // Modo que se está configurando en el sub-flujo Noche->Entorno (un jugador o
        // crear sala). MultiClient no pasa por acá (se une directo).
        private GameSession.SessionMode _pendingMode = GameSession.SessionMode.SinglePlayer;

        private int _nocheSel = -1;
        private string _mapSel;                // entorno elegido para jugar (o null)
        private List<string> _scans = new();
        private readonly Dictionary<string, int>  _itemCount = new();
        private readonly Dictionary<string, bool> _hasImg    = new();
        private string _confirmarBorrar;       // nombre del escaneo a borrar (overlay Escaneos)
        private Vector2 _scroll;

        // Sección "Avanzado" (puerto) de la pantalla de entorno, sólo para host multi.
        private bool _avanzadoOpen;
        private string _portText = NetworkConfig.DefaultPort.ToString();

        // Navegación con gamepad (driver global en GamepadManager).
        private readonly Gamepad.ImguiGamepadMenu _nav = new();

        private const float Pad = 28f;

        private void Awake() => GameSession.Ensure();

        private void Update() => _nav.Update();

        // Relee la lista de escaneos guardados y cachea conteo + si tienen imagen de
        // referencia (sin ella un entorno no es jugable ni sincronizable).
        private void RefrescarScans()
        {
            _scans = ScanSerializer.ListSaved();
            _itemCount.Clear();
            _hasImg.Clear();
            foreach (var n in _scans)
            {
                var d = ScanSerializer.Load(n);
                _itemCount[n] = d == null ? 0 :
                    (d.walls?.Count ?? 0) + (d.cubes?.Count ?? 0) + (d.markers?.Count ?? 0);
                _hasImg[n] = ScanSerializer.HasRefImage(n);
            }
        }

        // ¿Hay al menos un entorno jugable (con imagen de referencia)?
        private bool HayEntornoJugable()
        {
            foreach (var n in _scans) if (_hasImg.TryGetValue(n, out var ok) && ok) return true;
            return false;
        }

        private void IrAlEscaner(string nombreEdicion)
        {
            ScannerLaunchParams.EditScanName = nombreEdicion;
            SceneFlow.GoTo(SceneFlow.EscenaEscaner);
        }

        // Arranca el sub-flujo Noche->Entorno para el modo dado (un jugador o host).
        private void ConfigurarPartida(GameSession.SessionMode mode)
        {
            _pendingMode = mode;
            RefrescarScans();
            if (!HayEntornoJugable()) { _pantalla = Pantalla.SinEntorno; return; }
            _nocheSel = -1;
            _mapSel = null;
            _pantalla = Pantalla.Noches;
        }

        // ------------------------------------------------------------ OnGUI
        private void OnGUI()
        {
            UIScale.Begin();
            _nav.Begin();

            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;
            // Fondo opaco en TODA la pantalla (incluye fuera del área segura), para
            // que no se cuele la escena 3D detrás por el notch / home indicator.
            T.FillScreen(T.Bg);
            UIBlocker.AddVirtualRect(new Rect(0, 0, vw, vh));

            if (_confirmarBorrar != null)
            {
                DrawConfirmarBorrar(vw, vh);
            }
            else
            {
                switch (_pantalla)
                {
                    case Pantalla.Menu:        DrawMenu(vw, vh);        break;
                    case Pantalla.CrearUnirse: DrawCrearUnirse(vw, vh); break;
                    case Pantalla.SinEntorno:  DrawSinEntorno(vw, vh);  break;
                    case Pantalla.Noches:      DrawNoches(vw, vh);      break;
                    case Pantalla.Entorno:     DrawEntorno(vw, vh);     break;
                    case Pantalla.Escaneos:    DrawEscaneos(vw, vh);    break;
                    case Pantalla.Opciones:    DrawOpciones(vw, vh);    break;
                    case Pantalla.Control:     DrawControl(vw, vh);     break;
                }
            }

            _nav.End();
        }

        // ------------------------------------------------------- pantallas
        private void DrawMenu(float vw, float vh)
        {
            T.TituloGlitch(new Rect(0, vh * 0.20f, vw, 70f), "MORTUORIUM", 58);
            GUI.Label(new Rect(0, vh * 0.20f + 74f, vw, 26f), "El ritual no debe parar",
                      T.Estilo(T.FElite, 14, T.Muted, TextAnchor.MiddleCenter));

            float bw = vw - Pad * 2f, bh = 56f, x = Pad;
            float y = vh - 44f - (bh + 14f) * 4f - 24f;

            T.Boton(_nav, new Rect(x, y, bw, bh), "UN JUGADOR", primario: true,
                    () => ConfigurarPartida(GameSession.SessionMode.SinglePlayer), fontSize: 22);
            y += bh + 14f;
            T.Boton(_nav, new Rect(x, y, bw, bh), "MULTIJUGADOR", false,
                    () => _pantalla = Pantalla.CrearUnirse);
            y += bh + 14f;
            T.Boton(_nav, new Rect(x, y, bw, bh), "ESCANEAR ENTORNO", false, OnEscanear);
            y += bh + 14f;
            T.Boton(_nav, new Rect(x, y, bw, bh), "OPCIONES", false, () => _pantalla = Pantalla.Opciones);

            GUI.Label(new Rect(0, vh - 36f, vw, 20f), "build interna · equipo 112",
                      T.Estilo(T.FMono, 10, T.Disabled, TextAnchor.MiddleCenter));
        }

        private void OnEscanear()
        {
            RefrescarScans();
            if (_scans.Count == 0) IrAlEscaner(null);
            else _pantalla = Pantalla.Escaneos;
        }

        private void DrawCrearUnirse(float vw, float vh)
        {
            T.BotonVolver(_nav, () => _pantalla = Pantalla.Menu);

            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "SESIÓN COMPARTIDA",
                      T.Estilo(T.FBebas, 28, T.Cream));

            float y = 170f;
            DrawCard(new Rect(Pad, y, vw - Pad * 2f, 84f), "CREAR SALA (HOST)",
                     "tu dispositivo aloja la sesión", primario: false,
                     () => ConfigurarPartida(GameSession.SessionMode.MultiHost));
            y += 100f;
            DrawCard(new Rect(Pad, y, vw - Pad * 2f, 84f), "UNIRSE (CLIENTE)",
                     "buscar una sala o ingresar la IP", primario: false, UnirseComoCliente);
        }

        // Cliente: no elige noche ni entorno (los define/comparte el host). Va directo
        // a SampleScene, donde GameBootstrapper muestra el autodescubrimiento / IP.
        private void UnirseComoCliente()
        {
            GameSession.Ensure().Mode = GameSession.SessionMode.MultiClient;
            SceneFlow.GoTo(_lobbyScene);
        }

        private void DrawCard(Rect r, string titulo, string subtitulo, bool primario, System.Action onClick)
        {
            T.Celda(_nav, r, primario, onClick);
            GUI.Label(new Rect(r.x + 20f, r.y + 16f, r.width - 40f, 32f), titulo,
                      T.Estilo(T.FBebas, 24, T.Cream));
            GUI.Label(new Rect(r.x + 20f, r.y + 50f, r.width - 40f, 20f), subtitulo,
                      T.Estilo(T.FMono, 12, T.Dim));
        }

        // Alerta de "falta un entorno" (un jugador o crear sala). Vuelve al origen
        // del sub-flujo y ofrece ir a escanear.
        private void DrawSinEntorno(float vw, float vh)
        {
            T.BotonVolver(_nav, VolverDeSubflujo);

            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "FALTA UN ENTORNO",
                      T.Estilo(T.FBebas, 28, T.Cream));
            GUI.Label(new Rect(Pad, 140f, vw - Pad * 2f, 120f),
                      "no hay ningún entorno escaneado (con imagen de referencia) todavía. " +
                      "el ritual necesita conocer tu espacio real antes de empezar a jugar.",
                      T.Estilo(T.FElite, 14, T.CreamDim, TextAnchor.UpperLeft, wrap: true));

            T.Boton(_nav, new Rect(Pad, vh - 44f - 58f, vw - Pad * 2f, 58f),
                    "ESCANEAR ENTORNO", primario: true, () => IrAlEscaner(null));
        }

        // Vuelve al punto de partida del sub-flujo (menú para un jugador, crear/unirse
        // para host).
        private void VolverDeSubflujo()
        {
            _pantalla = _pendingMode == GameSession.SessionMode.MultiHost
                ? Pantalla.CrearUnirse : Pantalla.Menu;
        }

        private void DrawNoches(float vw, float vh)
        {
            T.BotonVolver(_nav, VolverDeSubflujo);

            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "ELEGÍ LA NOCHE",
                      T.Estilo(T.FBebas, 28, T.Cream));

            int total = Mathf.Max(6, _nights != null ? _nights.Length : 0);
            float gap = 12f;
            float cw = (vw - Pad * 2f - gap) / 2f;
            float ch = 86f;
            float y0 = 150f;

            for (int i = 0; i < total; i++)
            {
                int col = i % 2, fila = i / 2;
                var r = new Rect(Pad + col * (cw + gap), y0 + fila * (ch + gap), cw, ch);

                bool existe = _nights != null && i < _nights.Length && _nights[i] != null;
                if (!existe)
                {
                    // Celda "bloqueada": decorativa, sin interacción (paridad visual
                    // con el prototipo cuando hay menos de 6 noches configuradas).
                    T.Fill(r, new Color(1f, 1f, 1f, 0.02f));
                    T.Borde(r, T.BorderDim);
                    GUI.Label(new Rect(r.x, r.y + 12f, r.width, 34f), (i + 1).ToString(),
                              T.Estilo(T.FBebas, 26, T.Disabled, TextAnchor.MiddleCenter));
                    // Ícono de candado (en vez del texto "bloqueada").
                    T.Candado(new Rect(r.center.x - 8f, r.y + 50f, 16f, 18f), T.Disabled);
                    continue;
                }

                int idx = i;   // captura por iteración para el closure
                bool sel = _nocheSel == i;
                T.Celda(_nav, r, primario: false, () => _nocheSel = idx,
                        bordeOverride: sel ? T.Red : (Color?)null,
                        fillOverride: sel ? new Color(T.Red.r, T.Red.g, T.Red.b, 0.14f) : (Color?)null);
                GUI.Label(new Rect(r.x, r.y + 12f, r.width, 34f), (i + 1).ToString(),
                          T.Estilo(T.FBebas, 26, T.Cream, TextAnchor.MiddleCenter));
                GUI.Label(new Rect(r.x, r.y + 50f, r.width, 18f), sel ? "elegida" : "disponible",
                          T.Estilo(T.FMono, 10, sel ? T.Tan : T.Dim, TextAnchor.MiddleCenter));
            }

            bool haySel = _nocheSel >= 0 && _nights != null && _nocheSel < _nights.Length &&
                          _nights[_nocheSel] != null;
            if (haySel)
            {
                string nombre = string.IsNullOrEmpty(_nights[_nocheSel].displayName)
                    ? $"NOCHE {_nocheSel + 1}" : _nights[_nocheSel].displayName;
                GUI.Label(new Rect(Pad, vh - 44f - 58f - 30f, vw - Pad * 2f, 24f), nombre,
                          T.Estilo(T.FElite, 13, T.Muted, TextAnchor.MiddleCenter));
                T.Boton(_nav, new Rect(Pad, vh - 44f - 58f, vw - Pad * 2f, 58f),
                        "CONTINUAR", primario: true, () => _pantalla = Pantalla.Entorno);
            }
        }

        private void DrawEntorno(float vw, float vh)
        {
            T.BotonVolver(_nav, () => _pantalla = Pantalla.Noches);

            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "ELEGÍ EL ENTORNO",
                      T.Estilo(T.FBebas, 28, T.Cream));
            GUI.Label(new Rect(Pad, 132f, vw - Pad * 2f, 22f),
                      "seleccioná un escaneo guardado para esta noche",
                      T.Estilo(T.FElite, 12, T.Dim));

            // "Avanzado" (puerto) anclado al borde inferior, arriba del CONFIRMAR.
            // Sólo para host multijugador (en un jugador el puerto no importa).
            bool host = _pendingMode == GameSession.SessionMode.MultiHost;
            float confirmH = 58f;
            float avanzadoH = !host ? 0f : (_avanzadoOpen ? 112f : 40f);
            float avanzadoY = vh - 44f - confirmH - 14f - avanzadoH;

            float listaY = 170f;
            float listaBottom = (host ? avanzadoY : vh - 44f - confirmH - 14f) - 12f;

            float rowH = 60f, gap = 10f;
            var viewport = new Rect(Pad, listaY, vw - Pad * 2f, Mathf.Max(60f, listaBottom - listaY));
            float contentH = _scans.Count * (rowH + gap);

            _scroll = GUI.BeginScrollView(viewport, _scroll,
                                          new Rect(0, 0, viewport.width - 16f, Mathf.Max(contentH, viewport.height)));
            float y = 0f;
            foreach (var name in _scans)
            {
                float w = viewport.width - 16f;
                bool ok = _hasImg.TryGetValue(name, out var hi) && hi;   // jugable = con imagen
                var fila = new Rect(0, y, w, rowH);
                var mapName = name;   // captura por iteración

                bool sel = _mapSel == name;
                T.Celda(_nav, fila, primario: false, () => _mapSel = mapName, enabled: ok,
                        bordeOverride: sel ? T.Red : (Color?)null,
                        fillOverride: sel ? new Color(T.Red.r, T.Red.g, T.Red.b, 0.14f) : (Color?)null);
                GUI.Label(new Rect(fila.x + 14f, y, w - 160f, rowH),
                          ok ? name : $"{name}  (sin imagen de ref)",
                          T.Estilo(T.FMono, 14, ok ? T.Cream : T.Disabled));
                GUI.Label(new Rect(fila.x + 14f, y, w - 28f, rowH),
                          $"{(_itemCount.TryGetValue(name, out var c) ? c : 0)} elementos",
                          T.Estilo(T.FMono, 11, T.Dim, TextAnchor.MiddleRight));
                y += rowH + gap;
            }
            GUI.EndScrollView();

            if (host) DrawAvanzadoPuerto(vw, avanzadoY);

            bool puedeConfirmar = !string.IsNullOrEmpty(_mapSel);
            T.Boton(_nav, new Rect(Pad, vh - 44f - confirmH, vw - Pad * 2f, confirmH),
                    "CONFIRMAR", primario: true, Confirmar, enabled: puedeConfirmar);
        }

        // Sección "Avanzado" (host): puerto TCP. Se dibuja desde 'y' hacia abajo.
        private void DrawAvanzadoPuerto(float vw, float y)
        {
            float w = vw - Pad * 2f;
            var header = new Rect(Pad, y, w, 28f);
            UIBlocker.AddVirtualRect(header);
            bool foco = Gamepad.ImguiGamepadMenu.NextHasFocus;
            if (foco) T.Fill(header, new Color(T.Tan.r, T.Tan.g, T.Tan.b, 0.12f));
            _nav.Button(header, $"AVANZADO {(_avanzadoOpen ? "(-)" : "(+)")}",
                        T.Estilo(T.FMono, 12, foco ? T.Cream : T.Muted, TextAnchor.MiddleLeft),
                        () => _avanzadoOpen = !_avanzadoOpen);
            y += 32f;

            if (!_avanzadoOpen) return;

            GUI.Label(new Rect(Pad, y, 120f, 20f), "PUERTO", T.Estilo(T.FMono, 11, T.Muted));
            y += 22f;
            _portText = T.CampoTexto(new Rect(Pad, y, 150f, 46f), _portText,
                                     NetworkConfig.DefaultPort.ToString());
            bool custom = PuertoElegido() != NetworkConfig.DefaultPort;
            GUI.Label(new Rect(Pad + 162f, y, w - 162f, 46f),
                      custom ? "puerto custom: NO se autoanuncia; los demás deben escribir ip:puerto"
                             : "por defecto: se autoanuncia por LAN (los demás lo encuentran solos)",
                      T.Estilo(T.FMono, 10, custom ? T.Tan : T.Dim, TextAnchor.UpperLeft, wrap: true));
        }

        // Puerto válido del campo (o el por defecto si está vacío/inválido).
        private int PuertoElegido() =>
            int.TryParse((_portText ?? "").Trim(), out int p) && p > 0 && p < 65536
                ? p : NetworkConfig.DefaultPort;

        private void Confirmar()
        {
            if (string.IsNullOrEmpty(_mapSel)) return;
            var s = GameSession.Ensure();
            s.Mode          = _pendingMode;
            s.SelectedNight = _nights[_nocheSel];
            s.SelectedMap   = _mapSel;
            s.HostPort      = _pendingMode == GameSession.SessionMode.MultiHost
                ? PuertoElegido() : NetworkConfig.DefaultPort;
            SceneFlow.GoTo(_lobbyScene);
        }

        // ── Gestión de escaneos (desde "ESCANEAR ENTORNO") ────────────────────
        private void DrawEscaneos(float vw, float vh)
        {
            T.BotonVolver(_nav, () => _pantalla = Pantalla.Menu);

            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "ESCANEO DE ENTORNO",
                      T.Estilo(T.FBebas, 28, T.Cream));
            GUI.Label(new Rect(Pad, 132f, vw - Pad * 2f, 22f),
                      "continuá editando un escaneo guardado o empezá uno nuevo",
                      T.Estilo(T.FElite, 12, T.Dim));

            float rowH = 64f, gap = 10f;
            float listaY = 170f;
            float listaH = vh - listaY - 44f - 58f - 20f;
            var viewport = new Rect(Pad, listaY, vw - Pad * 2f, listaH);
            float contentH = _scans.Count * (rowH + gap);

            _scroll = GUI.BeginScrollView(viewport, _scroll,
                                          new Rect(0, 0, viewport.width - 16f, Mathf.Max(contentH, listaH)));
            float y = 0f;
            foreach (var nombre in _scans)
            {
                float w = viewport.width - 16f;
                var fila  = new Rect(0, y, w, rowH);
                var zonaBorrar = new Rect(fila.xMax - 78f, y + 8f, 70f, rowH - 16f);
                var zonaAbrir  = new Rect(0, y, w - 86f, rowH);

                var n = nombre;   // captura por iteración
                T.Borde(fila, T.Border);
                T.Celda(_nav, zonaAbrir, primario: false, () => IrAlEscaner(n));
                GUI.Label(new Rect(fila.x + 14f, y, w - 240f, rowH), n,
                          T.Estilo(T.FMono, 15, T.Cream));
                GUI.Label(new Rect(fila.x + 14f, y, zonaBorrar.x - fila.x - 22f, rowH),
                          $"{(_itemCount.TryGetValue(n, out var c) ? c : 0)} elementos · editar",
                          T.Estilo(T.FMono, 11, T.Dim, TextAnchor.MiddleRight));

                T.Celda(_nav, zonaBorrar, primario: false, () => _confirmarBorrar = n,
                        bordeOverride: T.Red);
                GUI.Label(zonaBorrar, "borrar", T.Estilo(T.FMono, 11, T.Red, TextAnchor.MiddleCenter));

                y += rowH + gap;
            }
            GUI.EndScrollView();

            T.Boton(_nav, new Rect(Pad, vh - 44f - 58f, vw - Pad * 2f, 58f),
                    "+ NUEVO ESCANEO", primario: true, () => IrAlEscaner(null));
        }

        private void DrawConfirmarBorrar(float vw, float vh)
        {
            T.Fill(new Rect(0, 0, vw, vh), new Color(0f, 0f, 0f, 0.7f));

            float pw = vw - 56f, ph = 190f;
            var panel = new Rect((vw - pw) / 2f, (vh - ph) / 2f, pw, ph);
            T.Panel(panel, T.BgPanel, T.Red);

            GUI.Label(new Rect(panel.x + 22f, panel.y + 20f, pw - 44f, 30f),
                      $"¿ELIMINAR \"{_confirmarBorrar}\"?", T.Estilo(T.FBebas, 20, T.Cream));
            GUI.Label(new Rect(panel.x + 22f, panel.y + 56f, pw - 44f, 22f),
                      "esta acción no se puede deshacer", T.Estilo(T.FMono, 12, T.Muted));

            float bw = (pw - 44f - 10f) / 2f;
            T.Boton(_nav, new Rect(panel.x + 22f, panel.yMax - 70f, bw, 48f),
                    "CANCELAR", false, () => _confirmarBorrar = null, fontSize: 16);
            T.Boton(_nav, new Rect(panel.x + 22f + bw + 10f, panel.yMax - 70f, bw, 48f),
                    "ELIMINAR", true, () =>
                    {
                        ScanSerializer.Delete(_confirmarBorrar);
                        _confirmarBorrar = null;
                        RefrescarScans();
                    }, fontSize: 16);
        }

        private void DrawOpciones(float vw, float vh)
        {
            T.BotonVolver(_nav, () => _pantalla = Pantalla.Menu);

            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "OPCIONES",
                      T.Estilo(T.FBebas, 28, T.Cream));

            float y = 160f;

            // Volumen maestro (persistente).
            int volPct = Mathf.RoundToInt(GameOptions.Volumen * 100f);
            GUI.Label(new Rect(Pad, y, vw - Pad * 2f - 70f, 22f), "VOLUMEN",
                      T.Estilo(T.FMono, 13, T.CreamDim));
            GUI.Label(new Rect(vw - Pad - 70f, y, 70f, 22f), $"{volPct}%",
                      T.Estilo(T.FMono, 13, T.Tan, TextAnchor.MiddleRight));
            y += 26f;
            float nuevoVol = T.Slider(new Rect(Pad, y, vw - Pad * 2f, 30f),
                                      GameOptions.Volumen, 0f, 1f);
            if (!Mathf.Approximately(nuevoVol, GameOptions.Volumen)) GameOptions.Volumen = nuevoVol;
            y += 54f;

            // Estado del mando + botón para ir al visualizador.
            var gm = Gamepad.GamepadManager.Instance;
            string estado = (gm != null && gm.IsConnected)
                ? $"joystick: {gm.DisplayName}\nestado: conectado"
                : "joystick: ninguno\nconectá un mando por Bluetooth desde el sistema";
            var caja = new Rect(Pad, y, vw - Pad * 2f, 56f);
            T.Borde(caja, T.Border);
            GUI.Label(new Rect(caja.x + 14f, caja.y + 8f, caja.width - 28f, caja.height - 16f),
                      estado, T.Estilo(T.FMono, 12, gm != null && gm.IsConnected ? T.CreamDim : T.Muted, TextAnchor.UpperLeft, wrap: true));
            y += 68f;
            T.Boton(_nav, new Rect(Pad, y, vw - Pad * 2f, 52f), "CONTROL (MANDO)", false,
                    () => _pantalla = Pantalla.Control);
        }

        private void DrawControl(float vw, float vh)
        {
            T.BotonVolver(_nav, () => _pantalla = Pantalla.Opciones);

            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "CONTROL",
                      T.Estilo(T.FBebas, 28, T.Cream));

            var gm = Gamepad.GamepadManager.Instance;
            string status = (gm != null && gm.IsConnected)
                ? $"joystick: {gm.DisplayName}  ·  {gm.Brand}"
                : "ningún mando conectado";
            GUI.Label(new Rect(Pad, 136f, vw - Pad * 2f, 24f), status,
                      T.Estilo(T.FMono, 12, gm != null && gm.IsConnected ? T.Green : T.Muted));

            float y = 168f;
            if (gm != null && gm.IsConnected)
            {
                bool present = gm.TryGetBattery(out float lvl);
                string bat = present ? $"batería: {Mathf.RoundToInt(lvl * 100f)}%" : "batería: N/D";
                GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 22f), bat,
                          T.Estilo(T.FMono, 12, T.Muted));
                y += 30f;
            }

            float gpH = (vw - Pad * 2f) * 0.62f;
            var gpArea = new Rect(Pad, y, vw - Pad * 2f, gpH);
            T.Fill(gpArea, new Color(0.06f, 0.06f, 0.07f, 1f));
            var st = gm != null ? gm.ReadState() : default;
            Gamepad.GamepadVisualizer.Draw(gpArea, gm != null ? gm.Brand : Gamepad.GamepadBrand.None, st);
        }
    }
}
