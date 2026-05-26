using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    public bool  IsServer      { get; private set; }
    public uint  LocalClientId { get; private set; }
    public uint  CurrentTick   { get; private set; }
    public float TickInterval  => 1f / _ticksPerSecond;
    public bool  GameStarted   { get; private set; }

    [SerializeField] private int _ticksPerSecond = 20;
    [SerializeField] public NetworkedPrefabRegistry PrefabRegistry;

    private TcpTransportServer _srv;
    private TcpTransportClient _cli;

    private uint   _nextNetId     = 1;
    private string _cloudAnchorId = null;  // null = anchor no listo todavía

    private readonly Dictionary<uint, uint> _clientToPlayer    = new();
    private readonly HashSet<uint>          _connectedClients  = new();
    private readonly HashSet<uint>          _resolvedClients   = new();

    private float _tickTimer;

    // ── Eventos ───────────────────────────────────────────────────────────

    public event Action<uint>   OnClientJoined;       // server: nuevo cliente
    public event Action<uint>   OnClientLeft;          // server: cliente desconectado
    public event Action<uint>   OnClientResolved;      // server: cliente resolvió anchor
    public event Action<string> OnAnchorIdReceived;    // client: recibió anchor ID
    public event Action         OnGameStarted;         // todos: partida arrancó

    // ── Public API ────────────────────────────────────────────────────────

    public void StartServer(int port)
    {
        IsServer = true;
        _srv     = new TcpTransportServer();
        _srv.Start(port);
    }

    public void StartClient(string host, int port)
    {
        if (_srv == null) IsServer = false;  // no sobreescribir si ya es servidor (host)
        _cli = new TcpTransportClient();
        _cli.Connect(host, port);
    }

    // Server: guardar anchor ID y enviarlo a todos los clientes conectados
    public void ServerSetAnchorId(string anchorId)
    {
        _cloudAnchorId = anchorId;
        var msg = MsgHelper.Frame(MessageType.AnchorId, new AnchorIdMsg { Id = anchorId }.Serialize());
        _srv.Broadcast(msg);
        Debug.Log($"[Server] Anchor ID broadcast: {anchorId}");
    }

    // Client: notificar al servidor que resolvió el anchor
    public void ClientSendAnchorResolved()
    {
        _cli.Send(MsgHelper.Frame(MessageType.AnchorResolved, Array.Empty<byte>()));
        Debug.Log("[Client] AnchorResolved enviado al servidor");
    }

    // Server: spawnear jugadores para todos los clientes y arrancar el juego
    public void ServerStartGame(int sorkerCount = 2)
    {
        // Spawnear jugador para cada cliente — posiciones relativas al anchor
        foreach (var clientId in _connectedClients)
        {
            if (_clientToPlayer.ContainsKey(clientId)) continue;
            var rel      = new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.2f, 0.2f));
            uint playerId = ServerSpawn(EntityTypeIds.Player, WorldOrigin.Instance.ToWorld(rel), clientId);
            _clientToPlayer[clientId] = playerId;
        }

        // Spawnear Sorkers cerca del anchor
        for (int i = 0; i < sorkerCount; i++)
        {
            var rel = new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.4f, 0.4f));
            ServerSpawn(EntityTypeIds.Sorker, WorldOrigin.Instance.ToWorld(rel), ownerClientId: 0);
        }

        GameStarted = true;
        _srv.Broadcast(MsgHelper.Frame(MessageType.StartGame, Array.Empty<byte>()));
        OnGameStarted?.Invoke();
        Debug.Log("[Server] Partida arrancada");
    }

    public uint ServerSpawn(byte typeId, Vector3 worldPosition, uint ownerClientId)
    {
        uint id  = _nextNetId++;
        var  msg = new SpawnEntityMsg
        {
            NetworkId     = id,
            TypeId        = typeId,
            OwnerClientId = ownerClientId,
            Position      = WorldOrigin.Instance.ToRelative(worldPosition), // anchor-relativo
        };
        _srv.Broadcast(MsgHelper.Frame(MessageType.SpawnEntity, msg.Serialize()));
        SpawnLocally(msg);
        return id;
    }

    public void ServerDespawn(uint networkId)
    {
        var msg = new DespawnEntityMsg { NetworkId = networkId };
        _srv.Broadcast(MsgHelper.Frame(MessageType.DespawnEntity, msg.Serialize()));
        DespawnLocally(networkId);
    }

    // ── Tick ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        _tickTimer += Time.deltaTime;
        while (_tickTimer >= TickInterval)
        {
            _tickTimer -= TickInterval;
            Tick();
        }
    }

    private void Tick()
    {
        CurrentTick++;
        if (IsServer) ServerTick();
        else          ClientTick();
    }

    // ── Server tick ───────────────────────────────────────────────────────

    private void ServerTick()
    {
        if (_srv == null) return;

        while (_srv.TryDequeueNewConnection(out var id))
            HandleClientConnected(id);

        while (_srv.TryDequeueDisconnection(out var id))
            HandleClientDisconnected(id);

        while (_srv.TryDequeue(out var msg))
            HandleServerMessage(msg);

        // Solo broadcastear estado del juego cuando la partida ya arrancó
        if (GameStarted)
            BroadcastWorldState();
    }

    private void HandleClientConnected(uint clientId)
    {
        Debug.Log($"[Server] Cliente {clientId} conectado");
        _connectedClients.Add(clientId);

        // Enviar catch-up de entidades ya existentes (en espacio anchor-relativo)
        foreach (var entity in EntityRegistry.Instance.All)
        {
            var catchup = new SpawnEntityMsg
            {
                NetworkId     = entity.NetworkId,
                TypeId        = entity.EntityTypeId,
                OwnerClientId = entity.OwnerClientId,
                Position      = WorldOrigin.Instance.ToRelative(entity.transform.position),
            };
            _srv.Send(clientId, MsgHelper.Frame(MessageType.SpawnEntity, catchup.Serialize()));
        }

        // Si el anchor ya está listo, enviarlo inmediatamente
        if (_cloudAnchorId != null)
        {
            var anchorMsg = MsgHelper.Frame(MessageType.AnchorId,
                new AnchorIdMsg { Id = _cloudAnchorId }.Serialize());
            _srv.Send(clientId, anchorMsg);
        }

        OnClientJoined?.Invoke(clientId);
    }

    private void HandleClientDisconnected(uint clientId)
    {
        Debug.Log($"[Server] Cliente {clientId} desconectado");
        _connectedClients.Remove(clientId);
        _resolvedClients.Remove(clientId);

        if (_clientToPlayer.TryGetValue(clientId, out var playerId))
        {
            ServerDespawn(playerId);
            _clientToPlayer.Remove(clientId);
        }
        _srv.RemoveClient(clientId);
        OnClientLeft?.Invoke(clientId);
    }

    private void HandleServerMessage(TcpTransportServer.IncomingMsg incoming)
    {
        switch (incoming.Type)
        {
            case MessageType.PlayerInput:
            {
                if (!GameStarted) return;
                var input = PlayerInputMsg.Deserialize(incoming.Body);
                if (!EntityRegistry.Instance.TryGet(input.NetworkId, out var entity)) return;
                if (entity.OwnerClientId == incoming.ClientId)
                    entity.ApplyInputData(input.Tick, incoming.Body);
                break;
            }
            case MessageType.AnchorResolved:
            {
                _resolvedClients.Add(incoming.ClientId);
                OnClientResolved?.Invoke(incoming.ClientId);
                Debug.Log($"[Server] Cliente {incoming.ClientId} resolvió el anchor ({_resolvedClients.Count}/{_connectedClients.Count})");
                break;
            }
        }
    }

    private void BroadcastWorldState()
    {
        var world = new WorldStateMsg { Tick = CurrentTick };
        foreach (var entity in EntityRegistry.Instance.All)
        {
            world.Entries.Add(new WorldStateMsg.Entry
            {
                NetworkId = entity.NetworkId,
                TypeId    = entity.EntityTypeId,
                Payload   = entity.SerializeState(CurrentTick),
            });
        }
        _srv.Broadcast(MsgHelper.Frame(MessageType.WorldState, world.Serialize()));
    }

    // ── Client tick ───────────────────────────────────────────────────────

    private void ClientTick()
    {
        if (_cli == null) return;

        while (_cli.TryDequeue(out var msg))
            HandleClientMessage(msg);

        if (!GameStarted) return;

        foreach (var entity in EntityRegistry.Instance.All)
        {
            if (!entity.IsOwned) continue;
            var inputBytes = entity.SerializeInput(CurrentTick);
            if (inputBytes != null)
                _cli.Send(MsgHelper.Frame(MessageType.PlayerInput, inputBytes));
        }
    }

    private void HandleClientMessage(TcpTransportClient.IncomingMsg msg)
    {
        switch (msg.Type)
        {
            case MessageType.ClientConnected:
            {
                var m = ClientConnectedMsg.Deserialize(msg.Body);
                LocalClientId = m.ClientId;
                Debug.Log($"[Client] clientId={m.ClientId}, playerId={m.PlayerNetworkId}");
                break;
            }
            case MessageType.SpawnEntity:
                SpawnLocally(SpawnEntityMsg.Deserialize(msg.Body));
                break;

            case MessageType.DespawnEntity:
                DespawnLocally(DespawnEntityMsg.Deserialize(msg.Body).NetworkId);
                break;

            case MessageType.WorldState:
            {
                var world = WorldStateMsg.Deserialize(msg.Body);
                foreach (var e in world.Entries)
                {
                    if (EntityRegistry.Instance.TryGet(e.NetworkId, out var entity))
                        entity.ApplyState(world.Tick, e.Payload);
                }
                break;
            }
            case MessageType.AnchorId:
            {
                var m = AnchorIdMsg.Deserialize(msg.Body);
                Debug.Log($"[Client] Anchor ID recibido: {m.Id}");
                OnAnchorIdReceived?.Invoke(m.Id);
                break;
            }
            case MessageType.StartGame:
            {
                GameStarted = true;
                OnGameStarted?.Invoke();
                Debug.Log("[Client] Partida arrancada");
                break;
            }
        }
    }

    // ── Entity lifecycle ──────────────────────────────────────────────────

    public void SpawnLocally(SpawnEntityMsg msg)
    {
        if (EntityRegistry.Instance.TryGet(msg.NetworkId, out _)) return;

        // msg.Position viene en espacio anchor-relativo — convertir al AR local de este dispositivo
        var  worldPos = WorldOrigin.Instance.ToWorld(msg.Position);
        var  prefab   = PrefabRegistry.Get(msg.TypeId);
        var  go       = Instantiate(prefab, worldPos, Quaternion.identity);
        var  net     = go.GetComponent<NetworkEntity>();
        bool isOwned = !IsServer && msg.OwnerClientId == LocalClientId;

        net.Initialize(msg.NetworkId, isOwned, msg.OwnerClientId);
        EntityRegistry.Instance.Register(net);

        if (!IsServer)
        {
            var ai = go.GetComponent<SorkerAI>();
            if (ai != null) ai.enabled = false;
        }

        net.OnNetworkSpawn();
        Debug.Log($"[Net] Spawned typeId={msg.TypeId} netId={msg.NetworkId} owned={isOwned}");
    }

    private void DespawnLocally(uint networkId)
    {
        if (!EntityRegistry.Instance.TryGet(networkId, out var entity)) return;
        entity.OnNetworkDespawn();
        EntityRegistry.Instance.Unregister(networkId);
        Destroy(entity.gameObject);
    }

    private void OnDestroy()
    {
        _srv?.Stop();
        _cli?.Disconnect();
    }
}
