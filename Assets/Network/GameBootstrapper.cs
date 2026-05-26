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

    private NetworkManager _net;
    private bool           _started;
    private string         _statusMsg = "";

    private void Awake() => _net = GetComponent<NetworkManager>();

    private void OnGUI()
    {
        // Una vez conectado, ARLobbyUI toma el control completo
        if (_started) return;

        GUILayout.BeginArea(new Rect(10, 10, 320, 200));

        if (!_started)
        {
            GUILayout.Label("=== Multiplayer AR ===");

            GUILayout.Label("IP del host:");
            _host = GUILayout.TextField(_host, GUILayout.Width(200));

            GUILayout.Label("Puerto:");
            if (int.TryParse(GUILayout.TextField(_port.ToString(), GUILayout.Width(80)), out int p))
                _port = p;

            GUILayout.Space(6);

            // HOST: corre servidor + cliente en el mismo dispositivo
            if (GUILayout.Button("Ser Host (servidor + jugador)", GUILayout.Width(240)))
                StartHost();

            // CLIENTE: se conecta a un host existente
            if (GUILayout.Button("Unirse como Cliente", GUILayout.Width(240)))
                StartJoin();
        }
        else if (!string.IsNullOrEmpty(_statusMsg))
        {
            GUILayout.Label(_statusMsg);
        }

        GUILayout.EndArea();
    }

    private void StartHost()
    {
        _net.StartServer(_port);
        // Conectarse a si mismo como cliente para tener un jugador
        _net.StartClient("127.0.0.1", _port);

        ARLobbyManager.Instance?.BeginHostFlow();

        _statusMsg = $"Servidor en :{_port}";
        _started   = true;
    }

    private void StartJoin()
    {
        _net.StartClient(_host, _port);

        ARLobbyManager.Instance?.BeginClientFlow();

        _statusMsg = $"Conectando a {_host}:{_port}";
        _started   = true;
    }
}
