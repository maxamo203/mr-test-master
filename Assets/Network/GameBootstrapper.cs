using UnityEngine;

// Punto de entrada de la sesion. Muestra botones de conexion y delega
// el flujo de lobby a ARLobbyManager + ARLobbyUI.
[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(EntityRegistry))]
public class GameBootstrapper : MonoBehaviour
{
    [Header("Conexion")]
    [SerializeField] private string _host = "127.0.0.1";
    [SerializeField] private int    _port = 7777;
    [SerializeField] private string _hostName = "";

    private NetworkManager _net;
    private LanDiscovery   _discovery;
    private bool           _started;
    private bool           _manualEntry;
    private string         _statusMsg = "";
    private Vector2        _hostsScroll;

    private void Awake()
    {
        _net       = GetComponent<NetworkManager>();
        _discovery = GetComponent<LanDiscovery>() ?? gameObject.AddComponent<LanDiscovery>();
        if (string.IsNullOrEmpty(_hostName)) _hostName = SystemInfo.deviceName;
    }

    private void Start()
    {
        // Por defecto, escanear LAN apenas arranca para mostrar hosts disponibles.
        _discovery.StartListening();
    }

    private void OnGUI()
    {
        if (_started)
        {
            if (!string.IsNullOrEmpty(_statusMsg))
            {
                GUILayout.BeginArea(new Rect(10, 10, 320, 60));
                GUILayout.Label(_statusMsg);
                GUILayout.EndArea();
            }
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 360, 420));
        GUILayout.Label("=== Multiplayer AR ===");

        // HOST: corre servidor + cliente en el mismo dispositivo.
        if (GUILayout.Button("Ser Host (servidor + jugador)", GUILayout.Width(280)))
            StartHost();

        GUILayout.Space(8);
        GUILayout.Label("Hosts en la red:");

        var hosts = _discovery.Hosts;
        _hostsScroll = GUILayout.BeginScrollView(_hostsScroll, GUILayout.Height(150), GUILayout.Width(340));
        if (hosts.Count == 0)
        {
            GUILayout.Label("  buscando...");
        }
        else
        {
            foreach (var kv in hosts)
            {
                var h     = kv.Value;
                var label = string.IsNullOrEmpty(h.Name)
                    ? $"{h.Address}:{h.GamePort}"
                    : $"{h.Name}  ({h.Address}:{h.GamePort})";
                if (GUILayout.Button(label, GUILayout.Width(320)))
                {
                    _host = h.Address;
                    _port = h.GamePort;
                    StartJoin();
                }
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(8);
        _manualEntry = GUILayout.Toggle(_manualEntry, "Ingresar IP manualmente");
        if (_manualEntry)
        {
            GUILayout.Label("IP del host:");
            _host = GUILayout.TextField(_host, GUILayout.Width(200));

            GUILayout.Label("Puerto:");
            if (int.TryParse(GUILayout.TextField(_port.ToString(), GUILayout.Width(80)), out int p))
                _port = p;

            if (GUILayout.Button("Conectar", GUILayout.Width(160)))
                StartJoin();
        }

        GUILayout.EndArea();
    }

    private void StartHost()
    {
        _net.StartServer(_port);
        // Conectarse a si mismo como cliente para tener un jugador.
        _net.StartClient("127.0.0.1", _port);

        ARLobbyManager.Instance?.BeginHostFlow();

        // Dejar de escanear (somos el host) y empezar a anunciarnos.
        _discovery.StopAll();
        _discovery.StartAdvertising(_port, _hostName);

        _statusMsg = $"Servidor en :{_port}  (anunciando como '{_hostName}')";
        _started   = true;
    }

    private void StartJoin()
    {
        _net.StartClient(_host, _port);

        ARLobbyManager.Instance?.BeginClientFlow();

        // El cliente ya no necesita seguir escaneando.
        _discovery.StopAll();

        _statusMsg = $"Conectando a {_host}:{_port}";
        _started   = true;
    }
}
