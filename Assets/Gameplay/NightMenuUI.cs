using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
    //   OPCIONES          -> volumen + visor + estado del mando (-> CONTROL)
    //
    // Este componente es el MODELO del flujo: las pantallas las dibuja Rediseno.Menus
    // (UI Toolkit) leyendo PantallaActual / AvisoActual. Sólo CONTROL (diagnóstico del
    // mando) se sigue dibujando acá, en IMGUI.
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

        // Set de noches activo: el selector Mortuorium > Datos en Play permite probar
        // DEV o PROD desde el Editor. Fuera del Editor, una development build usa DEV
        // y una release siempre usa PROD.
        private NightConfig[] Nights =>
            (PlayDataProfile.UseDevelopment && _devNights != null && _devNights.Length > 0)
                ? _devNights
                : _nights;

        public enum Pantalla { Menu, CrearUnirse, SinEntorno, Noches, Entorno, Escaneos, Opciones, Control, Tutoriales }

        public Pantalla PantallaActual { get; private set; } = Pantalla.Menu;

        // Modo que se está configurando en el sub-flujo Noche->Entorno (un jugador o
        // crear sala). MultiClient no pasa por acá (se une directo).
        private GameSession.SessionMode _pendingMode = GameSession.SessionMode.SinglePlayer;

        private int _nocheSel = -1;
        private string _mapSel;                // entorno elegido para jugar (o null)
        private List<string> _scans = new();
        private readonly Dictionary<string, int>  _itemCount = new();
        private readonly Dictionary<string, bool> _hasImg    = new();
        // Práctica elegida en TUTORIALES mientras se elige su escenario (Ninguno = noche
        // normal) y, para esa práctica, los escaneos que no le sirven con su motivo.
        private TutorialTipo _tutorialPendiente;
        private bool _caminoPendiente;
        private readonly Dictionary<string, string> _noSirve = new();

        // US-1.1: chequeo de versión mínima de SO (Android 14 / iOS 17). A diferencia
        // de los avisos de abajo, éste se re-evalúa en cada Awake, no se persiste.
        private bool _versionIncompatible;

        // US-1.1: aviso (una sola vez) de que no se pudo determinar la versión del SO
        // (DeviceCompatibility.CompatResult.Unknown). No bloquea, solo recomienda.
        private bool _mostrarAvisoVersionDesconocida;

        // US-1.5: aviso (una sola vez) de que iOS trackea con mucho menos drift que
        // Android. Ver Riesgos Identificados en la documentación.
        private bool _mostrarAvisoIos;

        // Navegación con gamepad de la pantalla CONTROL (driver global en GamepadManager).
        private readonly Gamepad.ImguiGamepadMenu _nav = new();

        private const float Pad = 28f;

        public static NightMenuUI Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            GameSession.Ensure();

            var compat = DeviceCompatibility.Check();
            _versionIncompatible = compat == DeviceCompatibility.CompatResult.Unsupported;
            _mostrarAvisoVersionDesconocida = compat == DeviceCompatibility.CompatResult.Unknown &&
                                               !GameOptions.AvisoVersionDesconocidaVisto;
            //_versionIncompatible = true;
            //_mostrarAvisoVersionDesconocida = true;

            _mostrarAvisoIos = Application.platform == RuntimePlatform.Android && !GameOptions.AvisoIosVisto;
            //_mostrarAvisoIos = true;

            // Volvemos de una partida que se cortó sola (el host salió o se cayó la red).
            if (HayAvisoSalida) AudioManager.Sonar(c => c.uiAlerta);

            // Volvemos de una práctica (terminada o abandonada desde la pausa): se suelta
            // y se vuelve a la lista de TUTORIALES, que es de donde salió.
            var sesion = GameSession.Instance;
            if (sesion.EnTutorial)
            {
                sesion.Tutorial = TutorialTipo.Ninguno;
                sesion.Camino   = false;
                PantallaActual  = Pantalla.Tutoriales;
            }

            // Volvemos del escáner abierto desde una práctica (¿DÓNDE JUGÁS? / FALTA UN
            // ESCENARIO): se retoma esa práctica en vez de caer en el menú principal, con el
            // escenario recién guardado/editado ya elegido si le sirve.
            var tutorial = _tutorialTrasEscaner;
            bool camino = _caminoTrasEscaner;
            _tutorialTrasEscaner = TutorialTipo.Ninguno;
            _caminoTrasEscaner = false;
            if (tutorial != TutorialTipo.Ninguno)
            {
                IniciarPractica(tutorial, camino);
                var escaneado = SaveLoadUI.NombreEdicion;
                if (PantallaActual == Pantalla.Entorno && !string.IsNullOrEmpty(escaneado) && EsJugable(escaneado))
                    _mapSel = escaneado;
            }
        }

        private void OnEnable()  => Gamepad.ImguiGamepadMenu.PedidoVolver += Volver;
        private void OnDisable() => Gamepad.ImguiGamepadMenu.PedidoVolver -= Volver;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Modelo de las pantallas de Rediseno.Menus (MenuPrincipal2, MenusFlujoJuego…) ──
        // ¿Qué pantalla se ve, sin ningún aviso encima? Cada pantalla nueva se muestra
        // cuando coincide con la suya.
        public bool SinAvisos =>
            !_versionIncompatible && !HayAvisoSalida &&
            !_mostrarAvisoVersionDesconocida && !_mostrarAvisoIos && !_confirmarEscaner;
        public bool MostrandoPrincipal => SinAvisos && PantallaActual == Pantalla.Menu;
        public bool Mostrando(Pantalla p) => SinAvisos && PantallaActual == p;
        public void VolverPantalla()     => Volver();
        public void AbrirControl()       => PantallaActual = Pantalla.Control;

        // Catálogo de noches dev/prod (sólo dev build). Al cambiarlo se suelta la noche
        // elegida: su índice es del catálogo anterior.
        public void UsarNochesProd(bool prod)
        {
            PlayDataProfile.UseProduction = prod;
            _nocheSel = -1;
        }

        // Flujo de juego para las pantallas nuevas (noches / escenario / sin escenario).
        public NightConfig[] Catalogo => Nights;
        public int NocheElegida => _nocheSel;
        public string EscenarioElegido => _mapSel;
        public IReadOnlyList<string> Escenarios => _scans;
        public bool EsJugable(string escena) =>
            _hasImg.TryGetValue(escena, out var ok) && ok && !_noSirve.ContainsKey(escena);
        // Por qué no se puede elegir ese escenario (null si se puede).
        public string MotivoNoJugable(string escena) =>
            !(_hasImg.TryGetValue(escena, out var ok) && ok) ? "Sin imagen de referencia: no se puede jugar"
            : _noSirve.TryGetValue(escena, out var motivo) ? motivo : null;
        public TutorialTipo TutorialPendiente => _tutorialPendiente;
        // ¿Lo pendiente es el camino del TUTORIAL (pasos encadenados) y no una práctica suelta?
        public bool CaminoPendiente => _caminoPendiente;
        public int ElementosDe(string escena) => _itemCount.TryGetValue(escena, out var n) ? n : 0;
        public bool CreandoSala => _pendingMode == GameSession.SessionMode.MultiHost;

        // Puerto de la sala (sección AVANZADO de ¿DÓNDE JUGÁS?, sólo al crear sala). Vacío o
        // inválido = el por defecto. Con uno custom la sala NO se autoanuncia por LAN: los
        // demás tienen que escribir ip:puerto (ver GameBootstrapper.StartHost).
        private string _portText = NetworkConfig.DefaultPort.ToString();
        public string PuertoTexto { get => _portText; set => _portText = value; }
        public int PuertoElegido =>
            int.TryParse((_portText ?? "").Trim(), out int p) && p > 0 && p < 65536
                ? p : NetworkConfig.DefaultPort;

        // CONTINUAR · NOCHE N del menú nuevo: directo a elegir escenario con esa noche puesta
        // (con el visor, cada paso de más cuesta). Sin escenario jugable cae en "falta".
        public void AbrirContinuar(int noche)
        {
            ConfigurarPartida(GameSession.SessionMode.SinglePlayer);
            if (PantallaActual != Pantalla.Noches) return;
            var n = Nights;
            if (n != null && noche >= 0 && noche < n.Length && n[noche] != null && NightProgress.Desbloqueada(noche))
            {
                SeleccionarNoche(noche);
                _entornoDesdeContinuar = true;
            }
        }

        // ¿Se llegó a ¿DÓNDE JUGÁS? con CONTINUAR (salteando la grilla de noches)? Entonces
        // "volver" regresa al menú principal, de donde se vino, y no a la grilla.
        private bool _entornoDesdeContinuar;

        public void SeleccionarNoche(int i)
        {
            _entornoDesdeContinuar = false;
            _nocheSel = i;
            PreseleccionarEscenario();
            PantallaActual = Pantalla.Entorno;
        }

        public void SeleccionarEscenario(string escena) { if (EsJugable(escena)) _mapSel = escena; }
        public void ConfirmarPartida() => Confirmar();
        public void ElegirOtraNoche()  => PantallaActual = Pantalla.Noches;
        public void EscanearNuevo()    => PedirEscaner(null);
        public void EditarEscenario(string escena) => PedirEscaner(escena);

        // El escaneo es lo único que se hace SIN el visor (con el celular en la
        // mano y tocando la pantalla). Con el visor puesto, ir al escáner primero lo avisa
        // (AvisoMenu.EscanearSinVisor): aceptar saca del visor y sigue; volver deja todo como
        // estaba. Guardar el escaneo vuelve al menú, donde el visor se prende de nuevo.
        private bool _confirmarEscaner;
        private string _escanerPendiente;

        private void PedirEscaner(string nombre)
        {
            if (Rediseno.UI.ModoVisor.Activo)
            {
                _escanerPendiente = nombre;
                _confirmarEscaner = true;
                return;
            }
            IrAlEscaner(nombre);
        }

        public void EscanearSinVisor()
        {
            _confirmarEscaner = false;
            Rediseno.UI.ModoVisor.Set(false);
            IrAlEscaner(_escanerPendiente);
        }
        public void CrearSala() => ConfigurarPartida(GameSession.SessionMode.MultiHost);
        public void Unirse()    => UnirseComoCliente();

        // El último escenario jugado si sigue siendo jugable; si no, el primero que lo sea.
        private void PreseleccionarEscenario()
        {
            if (!string.IsNullOrEmpty(_mapSel) && EsJugable(_mapSel)) return;
            var ultimo = GameSession.Instance != null ? GameSession.Instance.SelectedMap : null;
            if (!string.IsNullOrEmpty(ultimo) && EsJugable(ultimo)) { _mapSel = ultimo; return; }
            foreach (var n in _scans) if (EsJugable(n)) { _mapSel = n; return; }
        }
        public int TotalNoches => Mathf.Max(1, Nights != null ? Nights.Length : 0);
        public void AbrirJugar()         => ConfigurarPartida(GameSession.SessionMode.SinglePlayer);
        public void AbrirMultijugador()  { if (BuildVariant.Multijugador) PantallaActual = Pantalla.CrearUnirse; }
        // MIS ESCENARIOS se abre aunque esté vacío (ahí ofrece NUEVO ESCANEO): ir directo
        // al escáner sorprendía, y con el visor puesto lo sacaba.
        public void AbrirEscenarios()    { RefrescarScans(); PantallaActual = Pantalla.Escaneos; }
        public void BorrarEscenario(string escena) { ScanSerializer.Delete(escena); RefrescarScans(); }

        // Avisos modales del menú, en orden de precedencia (el mismo que Volver). Los dibuja
        // Rediseno.Menus.AvisoMenu2.
        public enum AvisoMenu { Ninguno, VersionIncompatible, Salida, VersionDesconocida, Ios, EscanearSinVisor }
        public AvisoMenu AvisoActual =>
            _versionIncompatible            ? AvisoMenu.VersionIncompatible :
            HayAvisoSalida                  ? AvisoMenu.Salida :
            _mostrarAvisoVersionDesconocida ? AvisoMenu.VersionDesconocida :
            _mostrarAvisoIos                ? AvisoMenu.Ios :
            _confirmarEscaner               ? AvisoMenu.EscanearSinVisor : AvisoMenu.Ninguno;
        public string TextoAvisoSalida => GameSession.Instance != null ? GameSession.Instance.AvisoSalida : null;
        // El bloqueo por versión no se cierra (Volver tampoco lo hace): sólo SALIR.
        public void CerrarAviso() => Volver();
        public static void Salir() => SalirDelJuego();
        public void AbrirOpciones()      => PantallaActual = Pantalla.Opciones;
        public void AbrirTutoriales()
        {
            _tutorialPendiente = TutorialTipo.Ninguno;
            _caminoPendiente = false;
            PantallaActual = Pantalla.Tutoriales;
        }

        // Arranca una práctica: un jugador, fuera del catálogo (NightIndex = -1: no
        // desbloquea noches ni toca récords). Todas se juegan en un escenario escaneado, que
        // se elige en ¿DÓNDE JUGÁS?; sin escenario jugable caen en "falta". Sólo en
        // development build SORKEN y VELETH ofrecen además ARMAR EN EL MOMENTO (sin escaneo,
        // ArmadoRapido): entonces van a ¿DÓNDE JUGÁS? aunque no haya escenario.
        public void IniciarTutorial(TutorialTipo tipo) => IniciarPractica(tipo, camino: false);

        // El camino del TUTORIAL desde ese paso: al superarlo sigue el próximo en la misma
        // sesión (TutorialCamino). El escenario tiene que servir para todos los que faltan.
        public void IniciarCamino(TutorialTipo desde) => IniciarPractica(desde, camino: true);

        private void IniciarPractica(TutorialTipo tipo, bool camino)
        {
            _caminoPendiente = camino;
            if (Tutoriales.Config(tipo) == null)
            {
                Debug.LogError($"[NightMenu] Falta la NightConfig del tutorial {tipo} en Resources/Tutoriales.");
                return;
            }
            _pendingMode = GameSession.SessionMode.SinglePlayer;
            _tutorialPendiente = tipo;
            _nocheSel = -1;
            RefrescarScans();
            if (!PuedeArmarEnElMomento && !HayEntornoJugable())
            {
                AudioManager.Sonar(c => c.uiAlerta);
                PantallaActual = Pantalla.SinEntorno;
                return;
            }
            if (!string.IsNullOrEmpty(_mapSel) && !EsJugable(_mapSel)) _mapSel = null;
            PreseleccionarEscenario();
            PantallaActual = Pantalla.Entorno;
        }

        // ARMAR EN EL MOMENTO de ¿DÓNDE JUGÁS? (SORKEN / VELETH): la práctica sin escaneo.
        // Herramienta de testeo, sólo en development build: sin escaneo la oclusión depende
        // de la profundidad del celular (bien con LiDAR, floja en Android). En release las
        // prácticas se juegan siempre en un escenario escaneado.
        public bool PuedeArmarEnElMomento => Debug.isDebugBuild && !_caminoPendiente && ArmadoRapido.Usa(_tutorialPendiente);
        public void ArmarEnElMomento()
        {
            if (PuedeArmarEnElMomento) ArrancarTutorial(_tutorialPendiente, null);
        }

        private void Update()
        {
            // Red de seguridad de la demo: ningún camino debería llevar a la pantalla de
            // sesión compartida con el botón oculto, pero si alguno apareciera, cae al menú
            // en vez de dejar entrar al multijugador.
            if (!BuildVariant.Multijugador && PantallaActual == Pantalla.CrearUnirse)
                PantallaActual = Pantalla.Menu;

            _nav.Update();
            HandleBackButton();
        }

        // Botón/gesto "atrás" de Android: el Input System nuevo lo reporta como
        // Key.Escape del teclado virtual. Replica la MISMA navegación que el botón
        // "volver" de cada pantalla (overlays primero).
        private void HandleBackButton()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

            // A veces la B del VR BOX llega como "atrás" (escape). En CONTROL eso te sacaba
            // del diagnóstico justo mientras probabas la B; ahí se sale con el botón volver.
            if (PantallaActual == Pantalla.Control && Gamepad.GamepadManager.Instance != null &&
                Gamepad.GamepadManager.Instance.UsesMouseInput) return;

            // Las pantallas nuevas manejan su propio "atrás".
            if (Rediseno.UI.PantallaUI.AlgunaVisible) return;
            Volver();
        }

        // Un paso atrás, overlays primero (mismo orden que AvisoActual). Lo usan el atrás
        // de Android, el ← del mando (ImguiGamepadMenu.PedidoVolver) y las pantallas nuevas.
        private void Volver()
        {
            // Bloqueo por versión no compatible: a propósito no tiene botón de
            // volver (no se puede omitir), así que el back tampoco hace nada acá.
            if (_versionIncompatible) return;
            if (HayAvisoSalida) { CerrarAvisoSalida(); return; }
            if (_mostrarAvisoVersionDesconocida) { CerrarAvisoVersionDesconocida(); return; }
            if (_mostrarAvisoIos) { CerrarAvisoIos(); return; }
            if (_confirmarEscaner) { _confirmarEscaner = false; return; }

            switch (PantallaActual)
            {
                case Pantalla.CrearUnirse: PantallaActual = Pantalla.Menu; break;
                case Pantalla.SinEntorno:
                case Pantalla.Noches:      VolverDeSubflujo(); break;
                case Pantalla.Entorno:
                    if (_tutorialPendiente != TutorialTipo.Ninguno) { AbrirTutoriales(); break; }
                    PantallaActual = _entornoDesdeContinuar ? Pantalla.Menu : Pantalla.Noches;
                    _entornoDesdeContinuar = false;
                    break;
                case Pantalla.Escaneos:    PantallaActual = Pantalla.Menu; break;
                case Pantalla.Opciones:    PantallaActual = Pantalla.Menu; break;
                case Pantalla.Control:     PantallaActual = Pantalla.Opciones; break;
                case Pantalla.Tutoriales:  PantallaActual = Pantalla.Menu; break;
                // Pantalla.Menu: pantalla raíz, sin destino — no forzamos salir de
                // la app, dejamos que el SO maneje el back (minimizar).
            }
        }

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
            _noSirve.Clear();
            if (_tutorialPendiente == TutorialTipo.Ninguno) return;
            // En el camino del TUTORIAL el escenario tiene que servir también para los pasos
            // que siguen (p.ej. el Sorken necesita una puerta o ventana marcada).
            int desde = _caminoPendiente ? Mathf.Max(0, TutorialCamino.Indice(_tutorialPendiente)) : 0;
            foreach (var n in _scans)
            {
                var data = ScanSerializer.Load(n);
                string motivo = null;
                if (_caminoPendiente)
                    for (int i = desde; i < TutorialCamino.Pasos.Length && motivo == null; i++)
                        motivo = Tutoriales.MotivoNoSirve(TutorialCamino.Pasos[i], data);
                else
                    motivo = Tutoriales.MotivoNoSirve(_tutorialPendiente, data);
                if (motivo != null) _noSirve[n] = motivo;
            }
        }

        // ¿Hay al menos un entorno jugable (con imagen de referencia y, en una práctica,
        // que le sirva a esa práctica)?
        private bool HayEntornoJugable()
        {
            foreach (var n in _scans) if (EsJugable(n)) return true;
            return false;
        }

        // Práctica desde la que se fue al escáner (Ninguno si no). Estático: sobrevive al
        // viaje ida y vuelta de escenas; lo consume Awake.
        private static TutorialTipo _tutorialTrasEscaner;
        private static bool _caminoTrasEscaner;

        private void IrAlEscaner(string nombreEdicion)
        {
            ScannerLaunchParams.EditScanName = nombreEdicion;
            _tutorialTrasEscaner = _tutorialPendiente;
            _caminoTrasEscaner = _caminoPendiente;
            SceneFlow.GoTo(SceneFlow.EscenaEscaner);
        }

        // Arranca el sub-flujo Noche->Entorno para el modo dado (un jugador o host).
        private void ConfigurarPartida(GameSession.SessionMode mode)
        {
            _pendingMode = mode;
            _tutorialPendiente = TutorialTipo.Ninguno;
            _caminoPendiente = false;
            RefrescarScans();
            // Falta un entorno escaneado: es un callejón sin salida, se avisa con el
            // sonido de alerta y no con el de confirmación que ya sonó al tocar el botón.
            if (!HayEntornoJugable())
            {
                AudioManager.Sonar(c => c.uiAlerta);
                PantallaActual = Pantalla.SinEntorno;
                return;
            }
            _nocheSel = -1;
            _mapSel = null;
            PantallaActual = Pantalla.Noches;
        }

        // ------------------------------------------------------------ OnGUI
        // Todas las pantallas y avisos del menú los dibuja Rediseno.Menus (UI Toolkit); acá
        // sólo queda CONTROL (diagnóstico del mando), que no tiene versión nueva.
        private void OnGUI()
        {
            if (PantallaActual != Pantalla.Control || Rediseno.UI.PantallaUI.AlgunaVisible) return;
            // Todavía no sabe dibujarse en estéreo: se sale del visor en vez de dejar la
            // vista partida.
            if (Rediseno.UI.ModoVisor.Activo) Rediseno.UI.ModoVisor.Set(false);

            UIScale.Begin();
            _nav.Begin();

            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;
            // Fondo opaco en TODA la pantalla (incluye fuera del área segura), para
            // que no se cuele la escena 3D detrás por el notch / home indicator.
            T.FillScreen(T.Bg);
            UIBlocker.AddVirtualRect(new Rect(0, 0, vw, vh));

            DrawControl(vw, vh);

            _nav.End();
        }

        // Cliente: no elige noche ni entorno (los define/comparte el host). Va directo
        // a SampleScene, donde GameBootstrapper muestra el autodescubrimiento / IP.
        private void UnirseComoCliente()
        {
            var s = GameSession.Ensure();
            s.Mode = GameSession.SessionMode.MultiClient;

            // Soltar la noche de una partida anterior de ESTE dispositivo: GameSession es
            // DontDestroyOnLoad y su NightIndex sobrevive. NightTransition.NocheSuperada
            // asume que un cliente lo tiene en -1 para no mover su progresión; si quedaba
            // el índice de la noche que jugó solo antes, sobrevivir la noche del host le
            // desbloqueaba una noche que nunca jugó.
            s.SelectedNight = null;
            s.NightIndex    = -1;

            SceneFlow.GoTo(_lobbyScene);
        }

        // Vuelve al punto de partida del sub-flujo (menú para un jugador, crear/unirse
        // para host).
        private void VolverDeSubflujo()
        {
            if (_tutorialPendiente != TutorialTipo.Ninguno) { AbrirTutoriales(); return; }
            PantallaActual = _pendingMode == GameSession.SessionMode.MultiHost
                ? Pantalla.CrearUnirse : Pantalla.Menu;
        }

        private void Confirmar()
        {
            if (string.IsNullOrEmpty(_mapSel)) return;
            var s = GameSession.Ensure();
            if (_tutorialPendiente != TutorialTipo.Ninguno)
            {
                ArrancarTutorial(_tutorialPendiente, _mapSel);
                return;
            }
            s.Mode          = _pendingMode;
            s.SelectedNight = Nights[_nocheSel];
            s.SelectedMap   = _mapSel;
            // Catálogo + índice: dejan pasar a la siguiente noche desde la partida,
            // sin volver acá (ver Gameplay.NightTransition).
            s.Nights        = Nights;
            s.NightIndex    = _nocheSel;
            s.HostPort      = _pendingMode == GameSession.SessionMode.MultiHost
                ? PuertoElegido : NetworkConfig.DefaultPort;
            SceneFlow.GoTo(_lobbyScene);
        }

        // Carga SampleScene con esa práctica. mapa null = armado rápido (sin escaneo).
        private void ArrancarTutorial(TutorialTipo tipo, string mapa)
        {
            var s = GameSession.Ensure();
            s.Mode          = GameSession.SessionMode.SinglePlayer;
            s.Tutorial      = tipo;
            s.Camino        = _caminoPendiente;
            s.SelectedNight = Tutoriales.Config(tipo);
            s.SelectedMap   = mapa;
            // El camino termina ofreciendo JUGAR NOCHE 1 en la misma sesión: necesita el
            // catálogo. NightIndex sigue en -1 mientras dura el tutorial.
            s.Nights        = _caminoPendiente ? Nights : null;
            s.NightIndex    = -1;
            s.HostPort      = NetworkConfig.DefaultPort;
            SceneFlow.GoTo(_lobbyScene);
        }

        private static void SalirDelJuego()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void CerrarAvisoVersionDesconocida()
        {
            _mostrarAvisoVersionDesconocida = false;
            GameOptions.AvisoVersionDesconocidaVisto = true;
        }

        private void CerrarAvisoIos()
        {
            _mostrarAvisoIos = false;
            GameOptions.AvisoIosVisto = true;
        }

        // Aviso de que la partida multijugador se cortó sola: el host cerró la sala o se
        // perdió la conexión (lo deja NetworkManager en GameSession.AvisoSalida antes de
        // volver al menú). Se muestra una sola vez.
        private static bool HayAvisoSalida =>
            GameSession.Instance != null && !string.IsNullOrEmpty(GameSession.Instance.AvisoSalida);

        private void CerrarAvisoSalida()
        {
            if (GameSession.Instance != null) GameSession.Instance.AvisoSalida = null;
        }

        private void DrawControl(float vw, float vh)
        {
            T.BotonVolver(_nav, () => PantallaActual = Pantalla.Opciones);

            GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "CONTROL",
                      T.Estilo(T.FBebas, 28, T.Cream));

            var gm = Gamepad.GamepadManager.Instance;
            string status = (gm != null && gm.IsConnected)
                ? $"Joystick: {gm.DisplayName}  ·  {gm.Brand}"
                : "Ningún mando conectado";
            GUI.Label(new Rect(Pad, 136f, vw - Pad * 2f, 26f), status,
                      T.Estilo(T.FMono, 15, gm != null && gm.IsConnected ? T.Green : T.Muted));

            float y = 168f;
            if (gm != null && gm.IsConnected)
            {
                bool present = gm.TryGetBattery(out float lvl);
                string bat = present ? $"Batería: {Mathf.RoundToInt(lvl * 100f)}%" : "Batería: N/D";
                GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 24f), bat,
                          T.Estilo(T.FMono, 15, T.Muted));
                y += 30f;
            }

            // Mando a la izquierda y diagnóstico a la derecha si entra (landscape); si no, uno
            // debajo del otro achicando el mando para dejarle lugar al diagnóstico.
            float ancho = vw - Pad * 2f;
            bool lado = ancho >= 900f;
            float gpW = lado ? Mathf.Min(ancho * 0.5f - 12f, (vh - y - 20f) / 0.62f)
                             : Mathf.Min(ancho, Mathf.Max(200f, (vh - y - ControllerDiagAlto - 40f) / 0.62f));
            float gpH = gpW * 0.62f;
            var gpArea = new Rect(Pad, y, gpW, gpH);
            T.Fill(gpArea, new Color(0.06f, 0.06f, 0.07f, 1f));
            var st = gm != null ? gm.ReadState() : default;
            Gamepad.GamepadVisualizer.Draw(gpArea, gm != null ? gm.Brand : Gamepad.GamepadBrand.None, st);

            var diag = lado ? new Rect(Pad + gpW + 24f, y, ancho - gpW - 24f, ControllerDiagAlto)
                            : new Rect(Pad, y + gpH + 16f, ancho, ControllerDiagAlto);
            Gamepad.ControllerDiagnostics.Draw(diag);
        }

        private static float ControllerDiagAlto => Gamepad.ControllerDiagnostics.Alto;
    }
}
