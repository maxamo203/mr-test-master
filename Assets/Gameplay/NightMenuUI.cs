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
        [Tooltip("Noches de PROD (arrastra los assets de Nights/prod). Se usan en release.")]
        [SerializeField] private NightConfig[] _nights;
        [Tooltip("Noches de DEV (Nights/dev). SOLO se usan en development build; en release " +
                 "se ignoran y se usan las de prod.")]
        [SerializeField] private NightConfig[] _devNights;
        [Tooltip("Escena del lobby/gameplay a cargar al continuar.")]
        [SerializeField] private string _lobbyScene = "SampleScene";

        // Set de noches activo: en development build usa las DEV (si hay); en release,
        // siempre las de prod. Asi las noches dev nunca llegan a una build de release.
        private NightConfig[] Nights =>
            (Debug.isDebugBuild && _devNights != null && _devNights.Length > 0) ? _devNights : _nights;

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

        // US-1.1: chequeo de versión mínima de SO (Android 14 / iOS 17). A diferencia
        // de los avisos de abajo, éste se re-evalúa en cada Awake, no se persiste.
        private bool _versionIncompatible;

        // US-1.1: aviso (una sola vez) de que no se pudo determinar la versión del SO
        // (DeviceCompatibility.CompatResult.Unknown). No bloquea, solo recomienda.
        private bool _mostrarAvisoVersionDesconocida;

        // US-1.5: aviso (una sola vez) de que iOS trackea con mucho menos drift que
        // Android. Ver Riesgos Identificados en la documentación.
        private bool _mostrarAvisoIos;

        // Sección "Avanzado" (puerto) de la pantalla de entorno, sólo para host multi.
        private bool _avanzadoOpen;
        private string _portText = NetworkConfig.DefaultPort.ToString();

        // Navegación con gamepad (driver global en GamepadManager).
        private readonly Gamepad.ImguiGamepadMenu _nav = new();

        private const float Pad = 28f;

        private void Awake()
        {
            GameSession.Ensure();

            var compat = DeviceCompatibility.Check();
            _versionIncompatible = compat == DeviceCompatibility.CompatResult.Unsupported;
            _mostrarAvisoVersionDesconocida = compat == DeviceCompatibility.CompatResult.Unknown &&
                                               !GameOptions.AvisoVersionDesconocidaVisto;
            //_versionIncompatible = true;
            //_mostrarAvisoVersionDesconocida = true;

            _mostrarAvisoIos = Application.platform == RuntimePlatform.Android && !GameOptions.AvisoIosVisto;
            //_mostrarAvisoIos = true;
        }

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

            if (_versionIncompatible)
            {
                DrawBloqueoVersion(vw, vh);
            }
            else if (_mostrarAvisoVersionDesconocida)
            {
                DrawAvisoVersionDesconocida(vw, vh);
            }
            else if (_mostrarAvisoIos)
            {
                DrawAvisoIos(vw, vh);
            }
            else if (_confirmarBorrar != null)
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
            // Título grande: el tamaño escala con el ancho para que "MORTUORIUM"
            // ocupe casi todo el ancho, igual que el wordmark del splash (así la
            // transición splash -> menú no salta de tamaño/posición).
            int titleSize = Mathf.RoundToInt(vw * 0.20f);
            float titleY  = vh * 0.26f;
            float titleH  = titleSize * 1.15f;
            T.TituloGlitch(new Rect(0, titleY, vw, titleH), "MORTUORIUM", titleSize);
            GUI.Label(new Rect(0, titleY + titleH + 4f, vw, 26f), "El ritual no debe parar",
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

            var nights = Nights;
            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "ELEGÍ LA NOCHE",
                      T.Estilo(T.FBebas, 28, T.Cream));

            int total = Mathf.Max(6, nights != null ? nights.Length : 0);
            float gap = 12f;
            float cw = (vw - Pad * 2f - gap) / 2f;
            float ch = 86f;
            float y0 = 150f;

            for (int i = 0; i < total; i++)
            {
                int col = i % 2, fila = i / 2;
                var r = new Rect(Pad + col * (cw + gap), y0 + fila * (ch + gap), cw, ch);

                bool existe = nights != null && i < nights.Length && nights[i] != null;
                if (existe && !NightProgress.Desbloqueada(i))
                {
                    // Configurada pero todavía no ganada la anterior: candado real.
                    T.Fill(r, new Color(1f, 1f, 1f, 0.02f));
                    T.Borde(r, T.BorderDim);
                    GUI.Label(new Rect(r.x, r.y + 12f, r.width, 34f), (i + 1).ToString(),
                              T.Estilo(T.FBebas, 26, T.Disabled, TextAnchor.MiddleCenter));
                    T.Candado(new Rect(r.center.x - 8f, r.y + 46f, 16f, 18f), T.Disabled);
                    GUI.Label(new Rect(r.x, r.y + 66f, r.width, 16f), "bloqueada",
                              T.Estilo(T.FMono, 9, T.Disabled, TextAnchor.MiddleCenter));
                    continue;
                }
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

            bool haySel = _nocheSel >= 0 && nights != null && _nocheSel < nights.Length &&
                          nights[_nocheSel] != null && NightProgress.Desbloqueada(_nocheSel);
            if (haySel)
            {
                string nombre = string.IsNullOrEmpty(nights[_nocheSel].displayName)
                    ? $"NOCHE {_nocheSel + 1}" : nights[_nocheSel].displayName;
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
            s.SelectedNight = Nights[_nocheSel];
            s.SelectedMap   = _mapSel;
            // Catálogo + índice: dejan pasar a la siguiente noche desde la partida,
            // sin volver acá (ver Gameplay.NightTransition).
            s.Nights        = Nights;
            s.NightIndex    = _nocheSel;
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

        // US-1.1: bloqueo duro por versión de SO no soportada (Android < 14 / iOS < 17).
        // A diferencia de los avisos de abajo, no tiene forma de descartarse: no hay
        // nada jugable que ofrecer, así que la única acción posible es salir.
        private void DrawBloqueoVersion(float vw, float vh)
        {
            T.Fill(new Rect(0, 0, vw, vh), new Color(0f, 0f, 0f, 0.85f));

            float pw = vw - 56f, ph = 284f;
            var panel = new Rect((vw - pw) / 2f, (vh - ph) / 2f, pw, ph);
            T.Panel(panel, T.BgPanel, T.Red);

            const float iconSize = 60f;
            MortuoriumIcons.Draw(new Rect(panel.center.x - iconSize / 2f, panel.y + 14f, iconSize, iconSize),
                                  MortuoriumIcons.Icon.Pentagrama);

            GUI.Label(new Rect(panel.x + 22f, panel.y + 82f, pw - 44f, 30f),
                      "VERSIÓN NO COMPATIBLE", T.Estilo(T.FBebas, 20, T.Cream));
            GUI.Label(new Rect(panel.x + 22f, panel.y + 118f, pw - 44f, 90f),
                      "Lo sentimos, la versión de su dispositivo no es compatible con el juego.",
                      T.Estilo(T.FElite, 14, T.CreamDim, TextAnchor.UpperLeft, wrap: true));

            T.Boton(_nav, new Rect(panel.x + 22f, panel.yMax - 62f, pw - 44f, 46f),
                    "SALIR", true, SalirDelJuego, fontSize: 16, textColor: T.Red);
        }

        private static void SalirDelJuego()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // US-1.1: aviso (una sola vez) de que no se pudo determinar con certeza la
        // versión del SO. No bloquea: solo recomienda actualizar para evitar
        // inestabilidad o problemas de rendimiento no cubiertos por las pruebas.
        private void DrawAvisoVersionDesconocida(float vw, float vh)
        {
            T.Fill(new Rect(0, 0, vw, vh), new Color(0f, 0f, 0f, 0.7f));

            float pw = vw - 56f, ph = 314f;
            var panel = new Rect((vw - pw) / 2f, (vh - ph) / 2f, pw, ph);
            T.Panel(panel, T.BgPanel, T.Tan);

            const float iconSize = 60f;
            MortuoriumIcons.Draw(new Rect(panel.center.x - iconSize / 2f, panel.y + 14f, iconSize, iconSize),
                                  MortuoriumIcons.Icon.Pentagrama);

            GUI.Label(new Rect(panel.x + 22f, panel.y + 82f, pw - 44f, 30f),
                      "NO PUDIMOS VERIFICAR TU VERSIÓN", T.Estilo(T.FBebas, 18, T.Cream));
            GUI.Label(new Rect(panel.x + 22f, panel.y + 118f, pw - 44f, 130f),
                      "No pudimos determinar la versión de tu sistema operativo. Es posible que " +
                      "experimentes inestabilidad o problemas de rendimiento. Para la mejor " +
                      "experiencia recomendamos Android 14 o superior, o iOS 17 o superior.",
                      T.Estilo(T.FElite, 13, T.CreamDim, TextAnchor.UpperLeft, wrap: true));

            T.Boton(_nav, new Rect(panel.x + 22f, panel.yMax - 62f, pw - 44f, 46f),
                    "ENTENDIDO", true, CerrarAvisoVersionDesconocida, fontSize: 16);
        }

        private void CerrarAvisoVersionDesconocida()
        {
            _mostrarAvisoVersionDesconocida = false;
            GameOptions.AvisoVersionDesconocidaVisto = true;
        }

        // US-1.5: aviso de compatibilidad, una sola vez por dispositivo Android (la
        // deriva/re-localización del entorno escaneado es mucho menor en iOS; ver
        // "Riesgos Identificados" en la documentación). No bloquea seguir en Android.
        private void DrawAvisoIos(float vw, float vh)
        {
            T.Fill(new Rect(0, 0, vw, vh), new Color(0f, 0f, 0f, 0.7f));

            float pw = vw - 56f, ph = 314f;
            var panel = new Rect((vw - pw) / 2f, (vh - ph) / 2f, pw, ph);
            T.Panel(panel, T.BgPanel, T.Tan);

            const float iconSize = 60f;
            MortuoriumIcons.Draw(new Rect(panel.center.x - iconSize / 2f, panel.y + 14f, iconSize, iconSize),
                                  MortuoriumIcons.Icon.Pentagrama);

            GUI.Label(new Rect(panel.x + 22f, panel.y + 82f, pw - 44f, 30f),
                      "MEJOR EXPERIENCIA EN iOS", T.Estilo(T.FBebas, 20, T.Cream));
            GUI.Label(new Rect(panel.x + 22f, panel.y + 118f, pw - 44f, 130f),
                      "En dispositivos iOS el seguimiento de realidad aumentada es más estable " +
                      "y sufre mucho menos deriva espacial que en Android. Podés seguir jugando " +
                      "en este dispositivo sin problema, pero si tenés un iPhone a mano vas a " +
                      "tener la mejor experiencia posible.",
                      T.Estilo(T.FElite, 13, T.CreamDim, TextAnchor.UpperLeft, wrap: true));

            T.Boton(_nav, new Rect(panel.x + 22f, panel.yMax - 62f, pw - 44f, 46f),
                    "ENTENDIDO", true, CerrarAvisoIos, fontSize: 16);
        }

        private void CerrarAvisoIos()
        {
            _mostrarAvisoIos = false;
            GameOptions.AvisoIosVisto = true;
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

            // Anchor points extra: opción por dispositivo (ver AnchorPointManager).
            T.FilaToggle(_nav, new Rect(Pad, y, vw - Pad * 2f, 56f),
                         "PUNTOS DE ANCLAJE",
                         "colocá anclas en tu cuarto antes de empezar (menos deriva)",
                         GameOptions.PuntosAncla,
                         () => GameOptions.PuntosAncla = !GameOptions.PuntosAncla);
            y += 68f;

            // Detección automática de paredes: experimental, opt-in (ver AutoWallBuilder).
            T.FilaToggle(_nav, new Rect(Pad, y, vw - Pad * 2f, 56f),
                         "PARED AUTOMÁTICA (BETA)",
                         "en el escáner, sugiere paredes detectadas por la cámara para confirmar/ajustar",
                         GameOptions.EscaneoAutoBeta,
                         () => GameOptions.EscaneoAutoBeta = !GameOptions.EscaneoAutoBeta);
            y += 68f;

            // US-11.1: el filtro VHS en la PARTIDA es parte de la atmósfera y no se
            // apaga; sobre los menús es opcional (gusto y legibilidad).
            T.FilaToggle(_nav, new Rect(Pad, y, vw - Pad * 2f, 56f),
                         "FILTRO VHS EN MENÚS",
                         "el grano y las líneas de cinta también sobre esta pantalla",
                         GameOptions.VhsEnMenus,
                         () => GameOptions.VhsEnMenus = !GameOptions.VhsEnMenus);
            y += 68f;

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
            y += 64f;

            // ── Herramientas de desarrollo ────────────────────────────────────
            // Sólo en development build (en release ni se dibujan ni se compilan las
            // llamadas): tocar la progresión a mano no puede llegar al jugador.
            if (!Debug.isDebugBuild) return;

            var nights = Nights;
            int total  = nights != null ? nights.Length : 0;

            GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 20f), "DESARROLLO", T.Estilo(T.FMono, 11, T.Red));
            y += 22f;
            GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 20f),
                      $"progreso: {NightProgress.Desbloqueadas} de {total} noches desbloqueadas",
                      T.Estilo(T.FMono, 11, T.CreamDim));
            y += 26f;

            float bw = (vw - Pad * 2f - 10f) * 0.5f;
            T.Boton(_nav, new Rect(Pad, y, bw, 46f), "DESBLOQUEAR TODAS", primario: false,
                    () => NightProgress.DesbloquearTodas(total), enabled: total > 0, fontSize: 13);
            T.Boton(_nav, new Rect(Pad + bw + 10f, y, bw, 46f), "BORRAR PROGRESO", primario: false,
                    NightProgress.BorrarProgreso, fontSize: 13, textColor: T.Red);
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
