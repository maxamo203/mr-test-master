using Gamepad;
using Rediseno.UI;
using Scanner;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Rediseno.Menus
{
    // SINCRONIZACIÓN del Rediseño 2.0 (UI Toolkit): buscar la imagen de referencia, esperar
    // a los demás e INICIAR NOCHE (también la carga del mapa del host). Funciona con el
    // visor puesto: la guía de la imagen ("fantasma") queda fija en el
    // centro de cada ojo para encuadrar, y el panel va más chico y abajo, quieto en el
    // cuarto, para no tapar lo que se está buscando. Joystick: ↑↓, → elige, ← vuelve.
    //
    // AJUSTAR ENTORNO y NO TENGO LA IMAGEN llevan a la calibración a mano (PanelUbicar2).
    public class PanelSincronizacion2 : PantallaUI
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            AlCargar(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static PanelSincronizacion2 _instancia;

        private static void AlCargar(Scene s, LoadSceneMode m)
        {
            if (s.name != SceneFlow.EscenaJuego || _instancia != null) return;
            new GameObject("PanelSincronizacion2").AddComponent<PanelSincronizacion2>();
        }

        private const float AlfaFantasma = 0.35f;

        private ARLobbyManager _lobby;
        private ARImageAnchor _anchor;
        private VisualElement _tarjeta, _botones;
        private Label _titulo, _contador, _entorno, _instruccion, _estado, _aviso;
        private bool _cargandoArmado, _errorArmado;
        private ARLobbyManager.LobbyState _estadoArmado = (ARLobbyManager.LobbyState)(-1);
        private bool _puedeArmado;
        private string _error;
        private float _spin;

        protected override void Awake()
        {
            if (_instancia != null && _instancia != this) { Destroy(gameObject); return; }
            _instancia = this;
            base.Awake();
        }

        protected override void OnDestroy()
        {
            if (_instancia == this) _instancia = null;
            base.OnDestroy();
        }

        private static bool EstadoMigrado(ARLobbyManager.LobbyState s) =>
            s == ARLobbyManager.LobbyState.Scanning ||
            s == ARLobbyManager.LobbyState.WaitingForClients ||
            s == ARLobbyManager.LobbyState.AllReady;

        // ¿Esta pantalla reemplaza a la de ARLobbyUI ahora? (La lee ARLobbyUI.)
        public static bool Mostrando => _instancia != null && _instancia.EsVisible();

        protected override bool EsVisible()
        {
            if (_lobby == null) _lobby = ARLobbyManager.Instance;
            if (_lobby == null || NetworkManager.Instance == null) return false;
            if (PauseMenuController.IsOpen) return false;   // la pausa va encima, sola
            if (Gameplay.TutorialCamino.EnTransicion) return false;  // el tutorial arranca solo, detrás de su tarjeta
            return EstadoMigrado(_lobby.State);
        }

        protected override bool EnVisor => MRCardboardController.Activo;
        protected override bool CabezaPorCamara => true;
        protected override Color VeloVisor => Color.clear;    // se tiene que ver la cámara
        // Un poco por debajo de la vista y centrado en su lienzo: antes iba apoyado abajo
        // del lienzo y bajado 24°, y su borde inferior caía fuera de lo que ve cada ojo.
        protected override float AnguloPanel => 32f;
        protected override Vector2 PosicionVisor => new(0f, -12f);
        protected override VisualElement TarjetaVisor => _tarjeta;

        protected override void AlCambiarVisor(bool visor)
        {
            if (_tarjeta == null) return;
            // En pantalla: franja apoyada abajo. En el visor: tarjeta centrada en el lienzo.
            Raiz.style.justifyContent = visor ? Justify.Center : Justify.FlexEnd;
            // En pantalla: franja de abajo. En el visor: la tarjeta entera, apoyada abajo.
            _tarjeta.style.borderBottomLeftRadius = visor ? 40 : 0;
            _tarjeta.style.borderBottomRightRadius = visor ? 40 : 0;
        }

        protected override void Construir(VisualElement raiz, MenuNavegable nav)
        {
            raiz.style.justifyContent = Justify.FlexEnd;

            _tarjeta = PiezasUI.Ignorar(new VisualElement());
            _tarjeta.style.backgroundColor = new Color(0.047f, 0.039f, 0.035f, 0.9f);
            TemaUI.Radio(_tarjeta, 40);
            _tarjeta.style.borderBottomLeftRadius = 0; _tarjeta.style.borderBottomRightRadius = 0;
            TemaUI.Borde(_tarjeta, 2, TemaUI.Linea);
            TemaUI.Relleno(_tarjeta, 40, 44);
            raiz.Add(_tarjeta);

            var fila = PiezasUI.Ignorar(new VisualElement());
            fila.style.flexDirection = FlexDirection.Row;
            fila.style.justifyContent = Justify.SpaceBetween;
            fila.style.alignItems = Align.Center;
            _titulo = TemaUI.Texto("SINCRONIZACIÓN", TemaUI.Bebas, 66, TemaUI.Cream, 6f);
            fila.Add(_titulo);
            _contador = TemaUI.Texto("", TemaUI.Mono, 28, TemaUI.Muted, 2f, TextAnchor.MiddleRight);
            fila.Add(_contador);
            _tarjeta.Add(fila);

            _entorno = TemaUI.Texto("", TemaUI.Elite, 34, TemaUI.CreamDim);
            _entorno.style.marginTop = 6;
            _tarjeta.Add(_entorno);
            _instruccion = TemaUI.Texto("", TemaUI.Mono, 26, TemaUI.Muted);
            _instruccion.style.marginTop = 14;
            _tarjeta.Add(_instruccion);
            _estado = TemaUI.Texto("", TemaUI.Mono, 30, TemaUI.Tan);
            _estado.style.marginTop = 18;
            _tarjeta.Add(_estado);
            _aviso = TemaUI.Texto("", TemaUI.Mono, 24, TemaUI.Tan);
            _aviso.style.marginTop = 8;
            _tarjeta.Add(_aviso);

            _botones = PiezasUI.Ignorar(new VisualElement());
            _botones.style.marginTop = 26;
            _tarjeta.Add(_botones);
        }

        // Los botones dependen del estado: se rearman sólo cuando cambia.
        private void ArmarBotones()
        {
            _botones.Clear();
            Nav.Limpiar();
            var s = _lobby.State;
            _estadoArmado = s;
            _puedeArmado = _lobby.CanStartGame;
            _cargandoArmado = _lobby.MapaPendiente;
            _errorArmado = _lobby.MapaCliente == ARLobbyManager.EstadoMapa.Error;

            // Cliente recibiendo el escenario del host: sólo REINTENTAR si falló.
            if (_cargandoArmado)
            {
                if (_errorArmado)
                    _botones.Add(PiezasUI.Boton(Nav, "REINTENTAR", () => _lobby.ReintentarMapa()));
                return;
            }

            if (s == ARLobbyManager.LobbyState.Scanning)
            {
                _botones.Add(PiezasUI.Boton(Nav, "NO TENGO LA IMAGEN",
                    () => _error = _lobby.CalibrarSinImagen(), habilitado: _lobby.PuedeCalibrarManual));
            }
            else if (s == ARLobbyManager.LobbyState.WaitingForClients)
            {
                bool practica = Gameplay.GameSession.Instance != null && Gameplay.GameSession.Instance.EnTutorial;
                var iniciar = PiezasUI.Boton(Nav, practica ? "EMPEZAR PRÁCTICA" : "INICIAR NOCHE",
                                             () => _lobby.ServerStartGame(), habilitado: _puedeArmado);
                iniciar.style.backgroundImage = TemaUI.DegradeRojo;
                iniciar.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                _botones.Add(iniciar);
                // Tutorial: las anclas no se piden solas al sincronizar; quedan a mano acá.
                if (_lobby.PuedeAbrirAnclas)
                    _botones.Add(PiezasUI.Boton(Nav, "ANCLAS", () => { _error = null; _lobby.AbrirAnclas(); }));
                // Práctica armada en el momento: no hay imagen que buscar; se rehace el armado.
                if (Gameplay.GameSession.Instance != null && Gameplay.GameSession.Instance.ArmadoRapido)
                    _botones.Add(PiezasUI.Boton(Nav, "REHACER ARMADO", () => { _error = null; _lobby.RehacerArmado(); }));
                else
                    _botones.Add(FilaDos(
                        PiezasUI.Boton(Nav, "AJUSTAR ENTORNO", () => { _error = null; _lobby.AbrirAjusteOrigen(); }),
                        PiezasUI.Boton(Nav, "BUSCAR IMAGEN", () => { _error = null; _lobby.RecalibrarConImagen(); })));
            }
            else if (s == ARLobbyManager.LobbyState.AllReady)
            {
                _botones.Add(FilaDos(
                    PiezasUI.Boton(Nav, "AJUSTAR ENTORNO", () => { _error = null; _lobby.AbrirAjusteOrigen(); }),
                    PiezasUI.Boton(Nav, "BUSCAR IMAGEN", () => { _error = null; _lobby.RecalibrarConImagen(); })));
            }
        }

        // Botones uno debajo del otro (en los menús van siempre en vertical).
        private static VisualElement FilaDos(VisualElement a, VisualElement b)
        {
            var f = PiezasUI.Ignorar(new VisualElement());
            f.Add(a); f.Add(b);
            return f;
        }

        protected override void Animar(float dt)
        {
            if (_lobby == null) return;
            if (_anchor == null) _anchor = FindFirstObjectByType<ARImageAnchor>();
            bool cargando = _lobby.MapaPendiente;
            bool error = _lobby.MapaCliente == ARLobbyManager.EstadoMapa.Error;
            if (_lobby.State != _estadoArmado || _lobby.CanStartGame != _puedeArmado ||
                cargando != _cargandoArmado || error != _errorArmado) ArmarBotones();

            _spin += dt;
            string giro = new[] { "—", "\\", "|", "/" }[Mathf.FloorToInt(_spin * 6f) % 4];
            var sesion = Gameplay.GameSession.Instance;
            bool solo = sesion != null && sesion.SoloUnJugador;
            var net = NetworkManager.Instance;

            _contador.text = solo || net == null ? "" : $"LISTOS {net.LobbyListos}/{net.LobbyJugadores}";
            _contador.style.color = !solo && net != null && net.LobbyListos >= net.LobbyJugadores && net.LobbyJugadores > 0
                ? TemaUI.Green : TemaUI.Muted;
            bool armado = sesion != null && sesion.ArmadoRapido;
            _entorno.text = armado ? "Escenario: armado rápido"
                : sesion != null && !string.IsNullOrEmpty(sesion.SelectedMap)
                    ? $"Escenario: {sesion.SelectedMap}" : "Escenario compartido por el host";
            _instruccion.text = armado
                ? "Libro y entradas marcados. Empezá cuando quieras."
                : solo
                    ? "Apuntá a la imagen de referencia para ubicarte en tu escenario."
                    : "Todos apuntan a la imagen de referencia para compartir el mismo espacio.";

            string aviso = null;
            if (cargando)
            {
                MostrarCargaMapa(net, giro);
                return;
            }
            _titulo.text = "SINCRONIZACIÓN";
            _instruccion.style.display = DisplayStyle.Flex;
            switch (_lobby.State)
            {
                case ARLobbyManager.LobbyState.Scanning:
                {
                    bool afinando = _anchor != null && _anchor.MuestrasActuales > 0;
                    _estado.text = afinando ? $"Imagen encontrada, afinando… quedate quieto {giro}"
                                            : $"Buscando la imagen… {giro}";
                    _estado.style.color = TemaUI.Tan;
                    string a = _anchor != null ? _anchor.AvisoImagen : null;
                    if (!string.IsNullOrEmpty(a)) aviso = a + " Podés ubicar el escenario a mano o re-escanearlo.";
                    break;
                }
                case ARLobbyManager.LobbyState.WaitingForClients:
                    _estado.text = armado ? "Práctica armada · lista para empezar"
                                 : solo ? "Imagen detectada · listo para empezar" : "Imagen detectada";
                    _estado.style.color = TemaUI.Green;
                    if (!_lobby.CanStartGame) aviso = _lobby.StartBlockReason;
                    break;
                case ARLobbyManager.LobbyState.AllReady:
                    _estado.text = "Imagen detectada · esperando a que el host inicie la noche…";
                    _estado.style.color = TemaUI.Green;
                    break;
            }
            if (!string.IsNullOrEmpty(_error)) aviso = _error;
            _aviso.text = aviso ?? "";
            _aviso.style.display = string.IsNullOrEmpty(aviso) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // Cliente esperando / descargando / armando el escenario que manda el host.
        private void MostrarCargaMapa(NetworkManager net, string giro)
        {
            var fase = _lobby.MapaCliente;
            float espera = Time.realtimeSinceStartup - _lobby.MapaEsperaDesde;
            _titulo.text = fase switch
            {
                ARLobbyManager.EstadoMapa.EsperandoAnuncio => "ESPERANDO ESCENARIO",
                ARLobbyManager.EstadoMapa.Descargando      => "DESCARGANDO ESCENARIO",
                ARLobbyManager.EstadoMapa.Instalando       => "PREPARANDO ESCENARIO",
                _                                          => "NO SE PUDO CARGAR",
            };
            _entorno.text = string.IsNullOrEmpty(_lobby.MapaNombre)
                ? "Escenario compartido por el host" : $"Escenario: {_lobby.MapaNombre}";
            _instruccion.style.display = DisplayStyle.None;
            string aviso = null;
            switch (fase)
            {
                case ARLobbyManager.EstadoMapa.EsperandoAnuncio:
                    _estado.text = $"Conectado. Esperando al host… {giro}";
                    _estado.style.color = TemaUI.Tan;
                    if (espera > 8f) aviso = "Si no avanza, verificá que tengan la misma versión del juego.";
                    break;
                case ARLobbyManager.EstadoMapa.Descargando:
                {
                    int total = net != null ? net.ClientMapaBytesAnunciados : 0, leidos = 0;
                    if (net != null && net.ClientProgresoMapa(out int l, out int t)) { leidos = l; total = t; }
                    _estado.text = leidos > 0 && total > 0
                        ? $"Recibiendo el escaneo del cuarto… {Mathf.FloorToInt(100f * leidos / total)}%"
                        : $"Pidiendo el escenario al host… {giro}";
                    _estado.style.color = TemaUI.Tan;
                    if (leidos == 0 && espera > 8f) aviso = "El host no responde. Verificá que sigan en la misma red wifi.";
                    break;
                }
                case ARLobbyManager.EstadoMapa.Instalando:
                    _estado.text = $"Armando las paredes… {giro}";
                    _estado.style.color = TemaUI.Tan;
                    break;
                default:
                    _estado.text = "El escenario llegó dañado o incompleto.";
                    _estado.style.color = TemaUI.Red;
                    break;
            }
            _aviso.text = aviso ?? "";
            _aviso.style.display = string.IsNullOrEmpty(aviso) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // Guía de la imagen de referencia: en el visor, fija en el centro de cada ojo.
        protected override void DibujarEnOjo(Vector2 c, float ppd)
        {
            if (_lobby == null || _lobby.State != ARLobbyManager.LobbyState.Scanning || _lobby.MapaPendiente) return;
            Fantasma(c, 26f * ppd, Screen.height * 0.45f);
        }

        protected override void OnGUI()
        {
            base.OnGUI();
            // Sin visor: la guía en el centro de la pantalla, sobre el panel.
            if (EnVisor || !EsVisible() || Event.current.type != EventType.Repaint) return;
            if (_lobby == null || _lobby.State != ARLobbyManager.LobbyState.Scanning || _lobby.MapaPendiente) return;
            var prevM = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            // Debajo de la barra de arriba (botón de pausa) y lejos del panel de abajo.
            Fantasma(new Vector2(Screen.width * 0.5f, Screen.height * 0.36f), Screen.width * 0.5f,
                     Screen.height * 0.32f);
            GUI.matrix = prevM;
        }

        private static void Fantasma(Vector2 c, float ancho, float altoMax)
        {
            if (!CapturedReference.HasImage) return;
            var tex = CapturedReference.Texture;
            if (tex == null) return;
            float w = ancho, h = w * tex.height / Mathf.Max(1f, tex.width);
            if (h > altoMax) { h = altoMax; w = h * tex.width / Mathf.Max(1f, tex.height); }
            var r = new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h);
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, AlfaFantasma);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = new Color(TemaUI.Cream.r, TemaUI.Cream.g, TemaUI.Cream.b, 0.8f);
            float b = 2f;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, b), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - b, r.width, b), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, b, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - b, r.y, b, r.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
