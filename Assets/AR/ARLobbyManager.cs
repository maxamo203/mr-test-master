using System.Collections.Generic;
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
        net.OnGameStarted    += () => State = LobbyState.GameStarted;

        if (_imageAnchor != null)
            _imageAnchor.OnImageFound += OnImageFound;
    }

    // ── Llamados por GameBootstrapper ─────────────────────────────────────

    public void BeginHostFlow()
    {
        State = LobbyState.Scanning;
        _imageAnchor?.StartTracking();
    }

    public void BeginClientFlow()
    {
        State = LobbyState.Scanning;
        _imageAnchor?.StartTracking();
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
    }
}
