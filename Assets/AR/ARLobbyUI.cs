using UnityEngine;

public class ARLobbyUI : MonoBehaviour
{
    private ARLobbyManager _lobby;
    private NetworkManager _net;
    private readonly Gamepad.ImguiGamepadMenu _nav = new();

    private void Start()
    {
        _lobby = ARLobbyManager.Instance;
        _net   = NetworkManager.Instance;
    }

    private void Update() => _nav.Update();

    private void OnGUI()
    {
        _nav.Begin();
        if (_lobby == null || _net == null) return;
        if (_lobby.State == ARLobbyManager.LobbyState.Idle) return;
        if (_lobby.State == ARLobbyManager.LobbyState.GameStarted) return;

        // Debajo del panel de GameBootstrapper (IP del host / estado de conexion).
        GUILayout.BeginArea(new Rect(10, 200, 360, 300));
        GUILayout.Label($"=== LOBBY ({(_net.IsServer ? "HOST" : "CLIENTE")}) ===");
        GUILayout.Label($"Estado: {_lobby.State}");
        GUILayout.Space(8);

        switch (_lobby.State)
        {
            case ARLobbyManager.LobbyState.Scanning:
                GUILayout.Label("Apuntá la cámara a la imagen de referencia.");
                GUILayout.Label("La esfera blanca aparecerá encima cuando la detecte.");
                DrawSpinner();
                break;

            case ARLobbyManager.LobbyState.WaitingForClients:
                GUILayout.Label("Imagen detectada.");
                GUILayout.Space(4);
                GUILayout.Label($"Conectados: {_lobby.ConnectedCount}");
                GUILayout.Label($"Listos:     {_lobby.ResolvedCount}");
                GUILayout.Space(8);
                _nav.Button("Empezar Partida", () => _lobby.ServerStartGame(), GUILayout.Width(180));
                break;

            case ARLobbyManager.LobbyState.AllReady:
                GUILayout.Label("Imagen detectada.");
                GUILayout.Label("Esperando que el host arranque la partida...");
                break;
        }

        _nav.End();
        GUILayout.EndArea();
    }

    private float _spinnerTime;
    private readonly string[] _spinnerFrames = { "—", "\\", "|", "/" };

    private void DrawSpinner()
    {
        _spinnerTime += Time.deltaTime;
        int frame = Mathf.FloorToInt(_spinnerTime * 6) % _spinnerFrames.Length;
        GUILayout.Label(_spinnerFrames[frame]);
    }
}
