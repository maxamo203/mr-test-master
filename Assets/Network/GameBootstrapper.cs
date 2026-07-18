using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections.Generic;
using Scanner;
using UnityEngine;

// Punto de entrada de la sesion multijugador. Maneja el menu de conexion:
//   - Host: elige un mapa guardado (de los escaneados en ScannerScene), arranca el
//     servidor y muestra su IP para compartirla manualmente.
//   - Cliente: ingresa la IP del host a mano y se conecta.
// El flujo de lobby AR (escanear la imagen de referencia, esperar, empezar) lo
// manejan ARLobbyManager + ARLobbyUI.
[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(EntityRegistry))]
public class GameBootstrapper : MonoBehaviour
{
    [Header("Conexion")]
    [SerializeField] private string _host = "192.168.0.1";
    [SerializeField] private int    _port = 7777;

    private enum Screen { Menu, SelectingMap, JoinEntry, Running }

    private NetworkManager       _net;
    private MRCardboardController _cardboard;
    private Screen               _screen = Screen.Menu;
    private string               _hostIp = "";
    private bool                 _isHost;
    private List<string>         _maps = new();
    private Vector2              _mapsScroll;

    // Navegación con gamepad de todos los botones del lobby (host/join/mapas/cardboard).
    private readonly Gamepad.ImguiGamepadMenu _nav = new();

    private void Awake() => _net = GetComponent<NetworkManager>();

    private void Update() => _nav.Update();

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 380, 460));
        _nav.Begin();

        switch (_screen)
        {
            case Screen.Menu:         DrawMenu();        break;
            case Screen.SelectingMap: DrawMapSelect();   break;
            case Screen.JoinEntry:    DrawJoinEntry();   break;
            case Screen.Running:      DrawRunning();     break;
        }

        _nav.End();
        GUILayout.EndArea();
    }

    // ── Pantallas ─────────────────────────────────────────────────────────

    private void DrawMenu()
    {
        GUILayout.Label("=== Multiplayer AR ===");
        GUILayout.Space(8);

        _nav.Button("Crear partida (Host)", () =>
        {
            _maps   = ScanSerializer.ListSaved();
            _screen = Screen.SelectingMap;
        }, GUILayout.Width(280), GUILayout.Height(50));
        GUILayout.Space(8);
        _nav.Button("Unirse a partida", () => _screen = Screen.JoinEntry,
                    GUILayout.Width(280), GUILayout.Height(50));
    }

    private void DrawMapSelect()
    {
        GUILayout.Label("Elegí el mapa de la partida:");
        GUILayout.Space(4);

        if (_maps.Count == 0)
        {
            GUILayout.Label("No hay mapas guardados.");
            GUILayout.Label("Escaneá uno en la ScannerScene primero.");
        }
        else
        {
            _mapsScroll = GUILayout.BeginScrollView(_mapsScroll, GUILayout.Height(300), GUILayout.Width(360));
            foreach (var name in _maps)
            {
                bool hasImg = ScanSerializer.HasRefImage(name);
                GUI.enabled = hasImg; // sin imagen de referencia no se puede sincronizar
                var label = hasImg ? name : $"{name}  (sin imagen de ref)";
                var mapName = name;   // captura por iteración para el closure
                _nav.Button(label, () => StartHost(mapName), GUILayout.Width(340), GUILayout.Height(44));
                GUI.enabled = true;
            }
            GUILayout.EndScrollView();
        }

        GUILayout.Space(6);
        _nav.Button("Volver", () => _screen = Screen.Menu, GUILayout.Width(120));
    }

    private void DrawJoinEntry()
    {
        GUILayout.Label("Unirse a una partida");
        GUILayout.Space(6);

        GUILayout.Label("IP del host:");
        _host = GUILayout.TextField(_host, GUILayout.Width(240), GUILayout.Height(34));

        GUILayout.Label("Puerto:");
        if (int.TryParse(GUILayout.TextField(_port.ToString(), GUILayout.Width(100), GUILayout.Height(34)), out int p))
            _port = p;

        GUILayout.Space(8);
        _nav.Button("Conectar", StartJoin, GUILayout.Width(200), GUILayout.Height(46));

        GUILayout.Space(6);
        _nav.Button("Volver", () => _screen = Screen.Menu, GUILayout.Width(120));
    }

    private void DrawRunning()
    {
        if (_isHost)
        {
            GUILayout.Label("=== HOST ===");
            GUILayout.Space(4);
            GUILayout.Label("Tu IP (compartila con los jugadores):");
            var ipStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = Color.cyan } };
            GUILayout.Label($"{_hostIp} : {_port}", ipStyle);
            GUILayout.Space(6);
            GUILayout.Label("Apuntá a la imagen de referencia del mapa.");
        }
        else
        {
            GUILayout.Label("=== CLIENTE ===");
            GUILayout.Label($"Conectado a {_host}:{_port}");
            GUILayout.Label("Esperando el mapa y escaneando la imagen…");
        }

        GUILayout.Space(10);
        if (_cardboard == null) _cardboard = FindFirstObjectByType<MRCardboardController>();
        if (_cardboard != null)
        {
            bool active = _cardboard.CardboardActive;
            string cbLabel = active ? "Salir de Cardboard" : "Modo Cardboard";
            // En partida, el A del control es la linterna. Dejamos el botón de Cardboard
            // touch-only (donde está, pero FUERA de la navegación con gamepad) para que
            // apretar A no toggle Cardboard además de la linterna. En el lobby (antes de
            // arrancar) sí es navegable con el joystick.
            if (_net != null && _net.GameStarted)
            {
                if (GUILayout.Button(cbLabel, GUILayout.Width(220), GUILayout.Height(46)))
                    _cardboard.ToggleCardboardMode();
            }
            else
            {
                _nav.Button(cbLabel, () => _cardboard.ToggleCardboardMode(),
                            GUILayout.Width(220), GUILayout.Height(46));
            }
        }
    }

    // ── Acciones ──────────────────────────────────────────────────────────

    private void StartHost(string mapName)
    {
        _net.StartServer(_port);
        // BeginHostFlow carga el mapa, registra su imagen de referencia y se lo pasa
        // al NetworkManager para enviarlo a cada cliente. Debe ir tras StartServer.
        // El host juega como servidor puro: ve las entidades via spawn local (server) y
        // calibra su WorldOrigin escaneando la imagen — no necesita cliente loopback
        // (sin avatares de jugador), que ademas inflaria los contadores del lobby.
        ARLobbyManager.Instance?.BeginHostFlow(mapName);

        _hostIp = LocalIPv4();
        _isHost = true;
        _screen = Screen.Running;
        Debug.Log($"[GameBootstrapper] Host en :{_port} con mapa '{mapName}'. IP {_hostIp}");
    }

    private void StartJoin()
    {
        _net.StartClient(_host, _port);
        ARLobbyManager.Instance?.BeginClientFlow();
        _isHost = false;
        _screen = Screen.Running;
        Debug.Log($"[GameBootstrapper] Uniendose a {_host}:{_port}");
    }

    // IPv4 de LAN del dispositivo (Wi-Fi). Para mostrarla y compartirla a mano.
    private static string LocalIPv4()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ua.Address))
                        return ua.Address.ToString();
                }
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[GameBootstrapper] No se pudo obtener la IP: {e.Message}"); }
        return "?.?.?.?";
    }
}
