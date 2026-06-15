using System.Collections.Generic;
using Scanner;
using UnityEngine;

public class ARLobbyManager : MonoBehaviour
{
    public static ARLobbyManager Instance { get; private set; }

    public enum LobbyState
    {
        Idle,               // antes de conectar
        Scanning,           // buscando la imagen de referencia
        WaitingForClients,  // host encontró imagen, esperando clientes
        AllReady,           // cliente encontró imagen, esperando que host arranque
        GameStarted,
    }

    public LobbyState State        { get; private set; } = LobbyState.Idle;
    public int ConnectedCount      { get; private set; }
    public int ResolvedCount       { get; private set; }

    [SerializeField] private ARImageAnchor _imageAnchor;
    [SerializeField] private int           _sorkerCount = 2;

    private readonly HashSet<uint> _connectedClients = new();
    private readonly HashSet<uint> _resolvedClients  = new();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        var net = NetworkManager.Instance;
        net.OnClientJoined   += HandleClientJoined;
        net.OnClientLeft     += HandleClientLeft;
        net.OnClientResolved += HandleClientResolved;
        net.OnMapReceived    += HandleMapReceived;
        net.OnGameStarted    += () => State = LobbyState.GameStarted;

        if (_imageAnchor == null) _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
        if (_imageAnchor != null)
            _imageAnchor.OnImageFound += OnImageFound;
    }

    // ── Llamados por GameBootstrapper ─────────────────────────────────────

    // Host: carga el mapa elegido (display-only) — esto registra su imagen de
    // referencia en AR y arranca la búsqueda — y lo empaqueta para enviárselo a los
    // clientes al conectarse. Al detectar la imagen física, WorldOrigin calibra.
    public void BeginHostFlow(string mapName)
    {
        State = LobbyState.Scanning;

        if (string.IsNullOrEmpty(mapName))
        {
            Debug.LogWarning("[ARLobby] BeginHostFlow sin mapa: el host no comparte mapa.");
            _imageAnchor?.StartTracking();
            return;
        }

        ScanLoader.LoadForDisplay(mapName, _imageAnchor);
        NetworkManager.Instance.ServerSetMap(ScanPackage.Pack(mapName));
    }

    // Cliente: NO arranca el tracking todavía — primero necesita recibir el mapa
    // (su imagen de referencia). El tracking arranca en HandleMapReceived.
    public void BeginClientFlow()
    {
        State = LobbyState.Scanning;
    }

    // Cliente: llegó el .mscn del host. Lo importamos, reconstruimos el mapa
    // display-only y registramos su imagen de referencia para calibrar el anchor
    // contra la misma imagen física que el host.
    private void HandleMapReceived(byte[] bytes)
    {
        if (NetworkManager.Instance.IsServer) return; // el host ya tiene su mapa cargado
        if (bytes == null || bytes.Length == 0) return;

        var name = ScanPackage.Import(bytes);
        if (string.IsNullOrEmpty(name))
        {
            Debug.LogWarning("[ARLobby] No se pudo importar el mapa recibido.");
            return;
        }
        ScanLoader.LoadForDisplay(name, _imageAnchor);
        State = LobbyState.Scanning;
        Debug.Log($"[ARLobby] Mapa '{name}' cargado; apuntá a la imagen para sincronizar.");
    }

    // ── Arrancar partida ──────────────────────────────────────────────────

    public void ServerStartGame()
    {
        if (!NetworkManager.Instance.IsServer) return;
        NetworkManager.Instance.ServerStartGame(_sorkerCount);
        State = LobbyState.GameStarted;
    }

    public bool CanStartGame => NetworkManager.Instance.IsServer
        && State == LobbyState.WaitingForClients;

    // ── Imagen detectada (host y cliente) ─────────────────────────────────

    private void OnImageFound()
    {
        if (NetworkManager.Instance.IsServer)
        {
            State = LobbyState.WaitingForClients;
        }
        else
        {
            NetworkManager.Instance.ClientSendAnchorResolved();
            State = LobbyState.AllReady;
        }
    }

    // ── Handlers de red ───────────────────────────────────────────────────

    private void HandleClientJoined(uint id)
    {
        _connectedClients.Add(id);
        ConnectedCount = _connectedClients.Count;
    }

    private void HandleClientLeft(uint id)
    {
        _connectedClients.Remove(id);
        _resolvedClients.Remove(id);
        ConnectedCount = _connectedClients.Count;
        ResolvedCount  = _resolvedClients.Count;
    }

    private void HandleClientResolved(uint id)
    {
        _resolvedClients.Add(id);
        ResolvedCount = _resolvedClients.Count;
    }

    private void OnDestroy()
    {
        var net = NetworkManager.Instance;
        if (net == null) return;
        net.OnClientJoined   -= HandleClientJoined;
        net.OnClientLeft     -= HandleClientLeft;
        net.OnClientResolved -= HandleClientResolved;
        net.OnMapReceived    -= HandleMapReceived;
    }
}
