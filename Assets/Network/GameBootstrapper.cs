using System.Collections.Generic;
using Scanner;
using UnityEngine;
using T = MortuoriumTheme;

// Ejecutor de la sesión en SampleScene. La elección de modo/noche/entorno ya se
// hizo en el menú principal (NightMenuUI) y viaja en GameSession. Según el modo:
//
//   SinglePlayer / MultiHost -> arranca el host con el entorno elegido y muestra la
//                               SALA (sincronizar la imagen; multi además espera a
//                               los jugadores). ARLobbyUI dibuja el estado encima.
//   MultiClient              -> muestra UNIRSE (autodescubrimiento por LAN + IP
//                               manual) y al conectar pasa a la SALA como cliente.
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
    private MRCardboardController _cardboard;
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

    // Navegación con gamepad de todos los botones del lobby.
    private readonly Gamepad.ImguiGamepadMenu _nav = new();

    private const float Pad = 28f;

    private void Awake() => _net = GetComponent<NetworkManager>();

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
        _nav.Update();
        if (_autoHostPendiente)
        {
            _autoHostPendiente = false;
            StartHost(_autoHostMapa);
        }
    }

    private void OnGUI()
    {
        UIScale.Begin();
        _nav.Begin();

        float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;

        // UNIRSE tapa la cámara (fondo opaco); en la SALA la cámara queda visible
        // para sincronizar la imagen de referencia.
        if (_pantalla != Pantalla.Sala)
        {
            var full = new Rect(0, 0, vw, vh);
            T.Fill(full, T.Bg);
            UIBlocker.AddVirtualRect(full);
        }

        switch (_pantalla)
        {
            case Pantalla.Unirse: DrawUnirse(vw, vh); break;
            case Pantalla.Sala:   DrawSala(vw, vh);   break;
        }

        _nav.End();
    }

    // ── UNIRSE (cliente) ──────────────────────────────────────────────────

    private void DrawUnirse(float vw, float vh)
    {
        T.BotonVolver(_nav, SalirDeUnirse);

        GUI.Label(new Rect(Pad, 90f, vw - Pad * 2f, 40f), "UNIRSE A SALA",
                  T.Estilo(T.FBebas, 28, T.Cream));

        float y = 132f;

        // Salas encontradas automáticamente en la red (sólo hosts en el puerto por
        // defecto se autoanuncian). Tocar una se conecta directo.
        var hosts = _lan != null ? _lan.Hosts : null;
        int encontradas = hosts != null ? hosts.Count : 0;

        GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 22f),
                  encontradas > 0 ? "salas en tu red:" : "buscando salas en tu red…",
                  T.Estilo(T.FElite, 12, T.Dim));
        y += 28f;

        if (hosts != null)
        {
            foreach (var h in hosts.Values)
            {
                var fila = new Rect(Pad, y, vw - Pad * 2f, 52f);
                var host = h;   // captura por iteración
                T.Celda(_nav, fila, primario: false,
                        () => Conectar(host.Address, host.GamePort));
                string nombre = string.IsNullOrEmpty(host.Name) ? "Sala" : host.Name;
                GUI.Label(new Rect(fila.x + 14f, fila.y + 6f, fila.width - 28f, 22f), nombre,
                          T.Estilo(T.FMono, 14, T.Cream));
                GUI.Label(new Rect(fila.x + 14f, fila.y + 28f, fila.width - 28f, 18f),
                          $"{host.Address} : {host.GamePort}", T.Estilo(T.FMono, 11, T.Dim));
                y += 60f;
            }
        }

        y += 8f;
        // IP manual (para host con puerto custom, u otra red). Si no ponés puerto,
        // se asume el por defecto.
        GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 20f), "…o ingresá la IP del host",
                  T.Estilo(T.FMono, 11, T.Muted));
        y += 24f;
        _host = T.CampoTexto(new Rect(Pad, y, vw - Pad * 2f, 50f), _host, "192.168.1.42");
        y += 56f;
        GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 18f),
                  $"si no ponés puerto se usa el {NetworkConfig.DefaultPort} (o escribí ip:puerto)",
                  T.Estilo(T.FMono, 10, T.Dim));
        y += 30f;

        T.Boton(_nav, new Rect(Pad, y, vw - Pad * 2f, 56f), "CONECTAR", primario: true,
                () => { ParseHostPort(_host, out var ip, out var pt); Conectar(ip, pt); });
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
        // ARLobbyUI dibuja el estado de sincronización + INICIAR debajo de esto.
        T.Gradiente(new Rect(0, 0, vw, 220f), 0.85f, haciaAbajo: true);

        bool solo = Gameplay.GameSession.Instance != null &&
                    Gameplay.GameSession.Instance.SoloUnJugador;

        // Cabecera. En modo un jugador NO mostramos la dirección de sala (no hay
        // nadie a quien compartírsela): solo el título de la noche.
        float yBotones;
        if (solo && _isHost)
        {
            GUI.Label(new Rect(Pad, 14f, vw - Pad * 2f, 34f), "TU NOCHE",
                      T.Estilo(T.FBebas, 24, T.Cream));
            yBotones = 64f;
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
                          "¿no te encuentran? probá: " + string.Join("  ·  ", _hostIpAlt),
                          T.Estilo(T.FMono, 10, T.Dim, TextAnchor.UpperLeft, wrap: true));
            else
                GUI.Label(new Rect(Pad, 122f, vw - Pad * 2f, 34f),
                          "compartí la dirección con los demás jugadores",
                          T.Estilo(T.FMono, 11, T.Muted, TextAnchor.UpperLeft, wrap: true));
            yBotones = 164f;
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
            yBotones = 132f;
        }

        // Botones apilados: Cardboard y (debajo) el toggle del oclusor de paredes.
        const float bw = 230f, bh = 44f, gap = 10f;
        bool enPartida = _net != null && _net.GameStarted;

        // Toggle de Cardboard (igual criterio que antes: en partida, touch-only
        // para que el botón A del mando quede libre para la linterna).
        if (_cardboard == null) _cardboard = FindFirstObjectByType<MRCardboardController>();
        if (_cardboard != null)
        {
            bool active = _cardboard.CardboardActive;
            string cbLabel = active ? "SALIR DE CARDBOARD" : "MODO CARDBOARD";
            var r = new Rect(Pad, yBotones, bw, bh);
            UIBlocker.AddVirtualRect(r);
            T.Boton(enPartida ? null : _nav, r, cbLabel, primario: false,
                    () => _cardboard.ToggleCardboardMode(), fontSize: 14);
            yBotones += bh + gap;
        }

        // Oclusor de paredes: ocultar/mostrar la geometría escaneada durante la
        // partida (antes lo dibujaba SceneOccluderMode abajo a la izquierda). Solo
        // tiene sentido con la partida arrancada (ya hay mapa/Sorken).
        var occ = SceneOccluderMode.Instance;
        if (occ != null && enPartida)
        {
            string label = occ.Enabled ? "PAREDES: OCULTAS" : "PAREDES: VISIBLES";
            var r = new Rect(Pad, yBotones, bw, bh);
            UIBlocker.AddVirtualRect(r);
            T.Boton(null, r, label, primario: false, () => occ.Toggle(), fontSize: 14);
        }
    }

    // ── Acciones ──────────────────────────────────────────────────────────

    private void StartHost(string mapName)
    {
        var s = Gameplay.GameSession.Ensure();
        _port = s.HostPort;
        _net.StartServer(_port);
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
