using System.Collections.Generic;
using Scanner;
using UnityEngine;
using T = MortuoriumTheme;

// Ejecutor de la sesión en SampleScene. La elección de modo/noche/entorno ya se
// hizo en el menú principal (NightMenuUI) y viaja en GameSession. Según el modo:
//
//   SinglePlayer / MultiHost -> arranca el host con el entorno elegido y muestra la
//                               SALA (sincronizar la imagen; multi además espera a
//                               los jugadores). PanelSincronizacion2 dibuja el estado.
//   MultiClient              -> muestra UNIRSE (Rediseno.Menus.PanelUnirse2:
//                               autodescubrimiento por LAN + IP manual) y al conectar
//                               pasa a la SALA como cliente.
//
// Acá sólo se dibuja la franja de la SALA (dirección para compartir con los demás).
//
// Puerto: GameSession.HostPort (por defecto NetworkConfig.DefaultPort). El host se
// autoanuncia por LAN SÓLO en el puerto por defecto y sólo si es multijugador; con
// puerto custom los clientes deben escribir "ip:puerto" a mano.
[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(EntityRegistry))]
public class GameBootstrapper : MonoBehaviour
{
    [SerializeField] private string _host = "";
    private int _port = NetworkConfig.DefaultPort;

    private enum Pantalla { Unirse, Sala }

    private NetworkManager        _net;
    private LanDiscovery          _lan;
    private Pantalla              _pantalla = Pantalla.Unirse;
    private string                _hostIp = "";
    private List<string>          _hostIpAlt = new();   // IPs alternativas para compartir
    private bool                  _isHost;

    // Auto-host diferido al primer Update: al hostear en Start() podríamos correr
    // ANTES de que ARLobbyManager.Start se suscriba a los eventos de red (el orden
    // entre Start() no está garantizado). Esperar un frame asegura que ya lo hizo.
    private bool                  _autoHostPendiente;
    private string                _autoHostMapa;

    private const float Pad = 28f;

    public static GameBootstrapper Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        _net = GetComponent<NetworkManager>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Modelo de UNIRSE: lo dibuja Rediseno.Menus.PanelUnirse2 ──
    public bool EnUnirse => _pantalla == Pantalla.Unirse;
    public IReadOnlyDictionary<string, LanDiscovery.DiscoveredHost> Salas => _lan != null ? _lan.Hosts : null;
    public string HostManual { get => _host; set => _host = value; }
    public void ConectarA(string ip, int port) => Conectar(ip, port);
    public void ConectarManual() { ParseHostPort(_host, out var ip, out var pt); Conectar(ip, pt); }
    public void SalirUnirse() => SalirDeUnirse();

    private void Start()
    {
        var s = Gameplay.GameSession.Instance;
        // Cliente (o arranque directo de la escena en el editor): unirse a un host.
        if (s == null || s.Mode == Gameplay.GameSession.SessionMode.MultiClient)
        {
            _pantalla = Pantalla.Unirse;
            Lan().StartListening();
            return;
        }
        // Un jugador o host: hostear con el entorno ya elegido (diferido un frame).
        _autoHostMapa = s.SelectedMap;
        _autoHostPendiente = true;
    }

    // LanDiscovery vive en este mismo GameObject (se crea on-demand). Muere con la
    // escena (SceneFlow destruye el NetworkManager, que es este GameObject).
    private LanDiscovery Lan()
    {
        if (_lan == null) _lan = GetComponent<LanDiscovery>();
        if (_lan == null) _lan = gameObject.AddComponent<LanDiscovery>();
        return _lan;
    }

    private void Update()
    {
        if (_autoHostPendiente)
        {
            _autoHostPendiente = false;
            StartHost(_autoHostMapa);
        }
    }

    private void OnGUI()
    {
        // Debajo de la pausa no se dibuja la franja de sala.
        if (Gamepad.PauseMenuController.IsOpen) return;
        // UNIRSE lo dibuja Rediseno.Menus.PanelUnirse2 (estéreo con Cardboard).
        if (_pantalla != Pantalla.Sala) return;

        UIScale.Begin();
        float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;

        // En la SALA la cámara queda visible dentro del área segura y sólo se ennegrecen
        // las franjas de afuera (notch / home indicator).
        T.FillOutsideSafeArea(T.Bg);

        // Con el visor la franja de sala quedaba partida entre los ojos y no se puede
        // leer; salir del visor está en la pausa.
        if (!MRCardboardController.Activo) DrawSala(vw, vh);
    }

    private void SalirDeUnirse()
    {
        _lan?.StopAll();
        SceneFlow.GoTo(SceneFlow.EscenaMenu);
    }

    // "ip" o "ip:puerto" -> ip + puerto (por defecto NetworkConfig.DefaultPort).
    private static void ParseHostPort(string text, out string ip, out int port)
    {
        ip   = (text ?? "").Trim();
        port = NetworkConfig.DefaultPort;
        int c = ip.LastIndexOf(':');
        if (c > 0 && int.TryParse(ip.Substring(c + 1).Trim(), out int p) && p > 0 && p < 65536)
        {
            port = p;
            ip   = ip.Substring(0, c).Trim();
        }
    }

    // ── SALA (host / cliente conectado) ───────────────────────────────────

    private void DrawSala(float vw, float vh)
    {
        // La cámara AR queda visible: solo franjas con gradiente arriba/abajo.
        // PanelSincronizacion2 dibuja el estado de sincronización + INICIAR debajo.
        T.Gradiente(new Rect(0, 0, vw, 220f), 0.85f, haciaAbajo: true);

        bool solo = Gameplay.GameSession.Instance != null &&
                    Gameplay.GameSession.Instance.SoloUnJugador;

        // Cabecera. En modo un jugador NO mostramos la dirección de sala (no hay
        // nadie a quien compartírsela) ni título: la cabecera queda vacía.
        if (solo && _isHost)
        {
            // Sin "TU NOCHE"; sólo se rotula la práctica.
            if (Gameplay.GameSession.Instance.EnTutorial)
                GUI.Label(new Rect(Pad, 14f, vw - Pad * 2f, 34f), "PRÁCTICA",
                          T.Estilo(T.FBebas, 24, T.Cream));
        }
        else if (_isHost)
        {
            GUI.Label(new Rect(Pad, 14f, vw - Pad * 2f, 34f), "SALA DE HOST",
                      T.Estilo(T.FBebas, 24, T.Cream));

            var caja = new Rect(Pad, 52f, vw - Pad * 2f, 64f);
            T.Borde(caja, T.Border);
            GUI.Label(new Rect(caja.x + 14f, caja.y + 8f, caja.width - 28f, 18f),
                      "DIRECCIÓN DE SALA", T.Estilo(T.FMono, 10, T.Dim));
            GUI.Label(new Rect(caja.x + 14f, caja.y + 26f, caja.width - 28f, 30f),
                      $"{_hostIp} : {_port}", T.Estilo(T.FMono, 18, T.Tan));

            // Si hay varias IPs (p.ej. WiFi + otra red), ofrecemos las alternativas:
            // la principal es la mejor estimación, pero puede no ser la red por la
            // que están conectados los demás.
            if (_hostIpAlt.Count > 0)
                GUI.Label(new Rect(Pad, 122f, vw - Pad * 2f, 34f),
                          "¿No te encuentran? probá: " + string.Join("  ·  ", _hostIpAlt),
                          T.Estilo(T.FMono, 10, T.Dim, TextAnchor.UpperLeft, wrap: true));
            else
                GUI.Label(new Rect(Pad, 122f, vw - Pad * 2f, 34f),
                          "Compartí la dirección con los demás jugadores",
                          T.Estilo(T.FMono, 11, T.Muted, TextAnchor.UpperLeft, wrap: true));
        }
        else
        {
            GUI.Label(new Rect(Pad, 14f, vw - Pad * 2f, 34f), "UNIDO A LA SALA",
                      T.Estilo(T.FBebas, 24, T.Cream));

            var caja = new Rect(Pad, 52f, vw - Pad * 2f, 64f);
            T.Borde(caja, T.Border);
            GUI.Label(new Rect(caja.x + 14f, caja.y + 8f, caja.width - 28f, 18f),
                      "CONECTADO A", T.Estilo(T.FMono, 10, T.Dim));
            GUI.Label(new Rect(caja.x + 14f, caja.y + 26f, caja.width - 28f, 30f),
                      $"{_host} : {_port}", T.Estilo(T.FMono, 18, T.Tan));
        }

        // MODO CARDBOARD y PAREDES ya no se dibujan acá: viven en el menú de pausa
        // (PauseMenuController) para no ocupar la pantalla todo el tiempo.
    }

    // ── Acciones ──────────────────────────────────────────────────────────

    private void StartHost(string mapName)
    {
        var s = Gameplay.GameSession.Ensure();
        // En un jugador no hay clientes externos ni descubrimiento LAN, por lo que no
        // corresponde competir por el puerto fijo 7777. El puerto 0 le pide a Windows
        // uno efimero libre; en multijugador se conserva exactamente el elegido.
        int requestedPort = s.SoloUnJugador ? 0 : s.HostPort;
        _port = _net.StartServer(requestedPort);
        // BeginHostFlow carga el mapa, registra su imagen de referencia y se lo pasa
        // al NetworkManager para enviarlo a cada cliente. Debe ir tras StartServer.
        // El host juega como servidor puro: ve las entidades via spawn local (server) y
        // calibra su WorldOrigin escaneando la imagen — no necesita cliente loopback
        // (sin avatares de jugador), que ademas inflaria los contadores del lobby.
        ARLobbyManager.Instance?.BeginHostFlow(mapName);
        s.SelectedMap = mapName;

        // IP "real" de LAN para compartir (evita adaptadores virtuales / VPN) + las
        // alternativas por si la principal no es la de la red esperada.
        var ips = LanAddress.AllLanIPv4();
        _hostIp = ips.Count > 0 ? ips[0] : "?.?.?.?";
        _hostIpAlt = ips.Count > 1 ? ips.GetRange(1, ips.Count - 1) : new List<string>();

        // Autoanunciarse por LAN SÓLO en el puerto por defecto y sólo si es una sala
        // real (multijugador). Con puerto custom no se anuncia: los clientes deben
        // escribir ip:puerto a mano.
        if (s.Mode == Gameplay.GameSession.SessionMode.MultiHost && _port == NetworkConfig.DefaultPort)
        {
            string nombre = string.IsNullOrEmpty(mapName) ? "Sala Mortuorium" : $"Sala · {mapName}";
            Lan().StartAdvertising(_port, nombre);
        }

        _isHost = true;
        _pantalla = Pantalla.Sala;
        Debug.Log($"[GameBootstrapper] Host en :{_port} con mapa '{mapName}'. IP {_hostIp}");
    }

    // Conecta como cliente a ip:port (usado por el autodescubrimiento y por la IP
    // manual). Detiene la escucha de descubrimiento antes de conectar.
    private void Conectar(string ip, int port)
    {
        if (string.IsNullOrEmpty(ip)) return;
        _lan?.StopAll();
        _host = ip;
        _port = port;
        _net.StartClient(ip, port);
        ARLobbyManager.Instance?.BeginClientFlow();
        _isHost = false;
        // Un cliente siempre es multijugador (por si venía de una sesión solo).
        Gameplay.GameSession.Ensure().Mode = Gameplay.GameSession.SessionMode.MultiClient;
        _pantalla = Pantalla.Sala;
        Debug.Log($"[GameBootstrapper] Uniendose a {ip}:{port}");
    }
}
