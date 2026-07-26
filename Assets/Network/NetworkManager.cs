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

    // True una vez que se arrancó como host o cliente. Mientras sea false no estamos
    // en una partida (se permite navegar entre escenas — ver SceneNavUI).
    public bool  InSession     { get; private set; }

    [SerializeField] private int _ticksPerSecond = 20;
    [SerializeField] public NetworkedPrefabRegistry PrefabRegistry;

    private TcpTransportServer _srv;
    private TcpTransportClient _cli;

    private uint   _nextNetId     = 1;
    private string _cloudAnchorId = null;  // null = anchor no listo todavía
    private byte[] _mapBytes      = null;  // server: .mscn del mapa elegido, se envía al conectarse cada cliente

    // Sin avatares por ahora: el jugador ES su cámara AR, no se instancia prefab de
    // jugador. Solo se spawnean/sincronizan los Sorkens. Dejar en false.
    [SerializeField] private bool _spawnPlayers = false;

    private readonly Dictionary<uint, uint> _clientToPlayer    = new();
    private readonly HashSet<uint>          _connectedClients  = new();
    private readonly HashSet<uint>          _resolvedClients   = new();

    // Server: ultima pose anchor-relativa reportada por cada cliente (por PlayerPose).
    // El host se conoce por Camera.main aparte. Lo usa el sistema de pilas.
    private readonly Dictionary<uint, Vector3> _clientRelPos   = new();
    // Server: ultimo estado de linterna (on/off) reportado por cada cliente. Lo usa el
    // SanitySystem para drenar la cordura del jugador cuando su linterna esta apagada.
    private readonly Dictionary<uint, bool>    _clientFlashOn  = new();
    // Server: ultimo forward (aim) anchor-relativo reportado por cada cliente. Lo usa el
    // GameDirector para el test de cono del repel.
    private readonly Dictionary<uint, Vector3> _clientForward  = new();
    // Cache de la linterna local (para adjuntar su estado al PlayerPose que enviamos).
    private Flashlight _localFlashlight;

    private float _tickTimer;

    // ── Eventos ───────────────────────────────────────────────────────────

    public event Action<uint>   OnClientJoined;       // server: nuevo cliente
    public event Action<uint>   OnClientLeft;          // server: cliente desconectado
    public event Action<uint>   OnClientResolved;      // server: cliente resolvió anchor
    public event Action<string> OnAnchorIdReceived;    // client: recibió anchor ID
    public event Action         OnGameStarted;         // todos: partida arrancó
    public event Action<byte[]> OnMapReceived;         // client: recibió el .mscn del mapa
    public event Action<byte, float> OnBatteryCollected; // client: recogió pila (rarityIndex, charge)
    public event Action<float, float> OnSanityUpdated;    // client: cordura autoritativa (valor, max)
    public event Action               OnPlayerDied;        // client: fui atrapado (pantalla de muerte)

    // Server: clientes remotos conectados (no incluye al host, que es clientId 0).
    public IReadOnlyCollection<uint> ConnectedClients => _connectedClients;

    // Server: ultimo estado de linterna reportado por un cliente. false si nunca reportó
    // (el llamador decide el default; el SanitySystem asume "encendida" = no drena).
    public bool TryGetClientFlashlightOn(uint clientId, out bool on) =>
        _clientFlashOn.TryGetValue(clientId, out on);

    // Server: forward (aim) world de un cliente (convertido con el WorldOrigin actual).
    public bool TryGetClientForward(uint clientId, out Vector3 worldForward)
    {
        worldForward = Vector3.forward;
        if (_clientForward.TryGetValue(clientId, out var rel) &&
            WorldOrigin.Instance != null && WorldOrigin.Instance.IsReady)
        {
            worldForward = WorldOrigin.Instance.ToWorldDir(rel);
            return true;
        }
        return false;
    }

    // Server → cliente puntual: fue atrapado (muestra pantalla de muerte).
    public void ServerSendPlayerDied(uint clientId)
    {
        if (_srv == null) return;
        _srv.Send(clientId, MsgHelper.Frame(MessageType.PlayerDied, Array.Empty<byte>()));
    }

    // Server → cliente puntual: su cordura autoritativa (la calcula el SanitySystem).
    public void ServerSendSanity(uint clientId, float sanity, float max)
    {
        if (_srv == null) return;
        var body = new PlayerSanityMsg { Sanity = sanity, Max = max }.Serialize();
        _srv.Send(clientId, MsgHelper.Frame(MessageType.PlayerSanity, body));
    }

    // Estado de la linterna local de este dispositivo (para reportarla al server).
    public bool LocalFlashlightOn()
    {
        if (_localFlashlight == null) _localFlashlight = FindFirstObjectByType<Flashlight>();
        return _localFlashlight != null && _localFlashlight.isOn;
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void StartServer(int port)
    {
        IsServer  = true;
        InSession = true;
        _srv      = new TcpTransportServer();
        _srv.Start(port);
    }

    public void StartClient(string host, int port)
    {
        if (_srv == null) IsServer = false;  // no sobreescribir si ya es servidor (host)
        InSession = true;
        _cli = new TcpTransportClient();
        _cli.Connect(host, port);
    }

    // Server: guardar el .mscn del mapa elegido por el host. Se envía a cada cliente
    // apenas se conecta (ver HandleClientConnected) y a los ya conectados ahora.
    public void ServerSetMap(byte[] mapBytes)
    {
        _mapBytes = mapBytes;
        if (mapBytes == null || mapBytes.Length == 0)
        {
            Debug.LogWarning("[Server] ServerSetMap recibió bytes vacíos.");
            return;
        }
        if (_srv != null)
        {
            var msg = MsgHelper.Frame(MessageType.MapData, new MapDataMsg { Bytes = mapBytes }.Serialize());
            _srv.Broadcast(msg);
        }
        Debug.Log($"[Server] Mapa fijado ({mapBytes.Length} bytes).");
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

    // Client: pedir al servidor recoger una pila apuntada (el server valida cercanía).
    public void ClientSendBatteryPickup(uint batteryNetId)
    {
        if (_cli == null) return;
        var body = new Bateries.BatteryPickupMsg { NetworkId = batteryNetId }.Serialize();
        _cli.Send(MsgHelper.Frame(MessageType.BatteryPickup, body));
    }

    // Server: spawnear los Sorkens y arrancar el juego para todos.
    public void ServerStartGame(int sorkenCount = 1)
    {
        // Sin avatares: el jugador es su cámara AR. Solo spawneamos jugador-entidad si
        // _spawnPlayers está habilitado (queda para cuando se diseñe el avatar).
        if (_spawnPlayers)
        {
            foreach (var clientId in _connectedClients)
            {
                if (_clientToPlayer.ContainsKey(clientId)) continue;
                var rel      = new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.2f, 0.2f));
                uint playerId = ServerSpawn(EntityTypeIds.Player, WorldOrigin.Instance.ToWorld(rel), clientId);
                _clientToPlayer[clientId] = playerId;
            }
        }

        // El Sorken lo spawnea el GameDirector (Gameplay), en los marcadores, cuando
        // corre un intento de entrada. Aca ya no se spawnea nada de enemigo.

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
            VisibleTo     = NetworkEntity.Everyone,                          // compartida
        };
        _srv.Broadcast(MsgHelper.Frame(MessageType.SpawnEntity, msg.Serialize()));
        SpawnLocally(msg);
        return id;
    }

    // Spawn DIRIGIDO: la entidad la ve UNICAMENTE targetClientId (Arbmos = alucinacion
    // individual). Si el dueño es un cliente, el spawn/estado/despawn viajan solo a el;
    // el host siempre crea la copia local (la propia se dibuja, las de otros se simulan
    // ocultas — ver SpawnLocally). ownerClientId queda en 0 (server-authoritative).
    public uint ServerSpawnFor(uint targetClientId, byte typeId, Vector3 worldPosition)
    {
        uint id  = _nextNetId++;
        var  msg = new SpawnEntityMsg
        {
            NetworkId     = id,
            TypeId        = typeId,
            OwnerClientId = 0,
            Position      = WorldOrigin.Instance.ToRelative(worldPosition),
            VisibleTo     = targetClientId,
        };
        if (targetClientId != 0)
            _srv.Send(targetClientId, MsgHelper.Frame(MessageType.SpawnEntity, msg.Serialize()));
        SpawnLocally(msg);
        return id;
    }

    public void ServerDespawn(uint networkId)
    {
        var framed = MsgHelper.Frame(MessageType.DespawnEntity,
            new DespawnEntityMsg { NetworkId = networkId }.Serialize());

        uint visibleTo = NetworkEntity.Everyone;
        if (EntityRegistry.Instance != null && EntityRegistry.Instance.TryGet(networkId, out var e))
            visibleTo = e.VisibleTo;

        if (visibleTo == NetworkEntity.Everyone) _srv.Broadcast(framed);
        else if (visibleTo != 0)                 _srv.Send(visibleTo, framed); // dirigida a un cliente
        // visibleTo == 0 (host-only): no se envia a ningun cliente.

        DespawnLocally(networkId);
    }

    // ── Server: posiciones de jugadores y notificacion de pickup (pilas) ──────

    // Posiciones world de todos los jugadores conocidos por el server: la camara del
    // host (Camera.main) mas cada cliente (su pose relativa convertida a world con el
    // WorldOrigin actual). Lo usa el BatterySpawnManager.
    private readonly List<Vector3> _playerPosScratch = new();
    public IReadOnlyList<Vector3> ServerPlayerWorldPositions()
    {
        _playerPosScratch.Clear();
        if (!IsServer) return _playerPosScratch;

        if (Camera.main != null)
            _playerPosScratch.Add(Camera.main.transform.position);

        if (WorldOrigin.Instance != null && WorldOrigin.Instance.IsReady)
            foreach (var rel in _clientRelPos.Values)
                _playerPosScratch.Add(WorldOrigin.Instance.ToWorld(rel));

        return _playerPosScratch;
    }

    // Posicion world de un cliente puntual (para validar el pickup en el server).
    // Devuelve false si el cliente es el host (0) o si aun no reportó pose.
    public bool TryGetClientWorldPosition(uint clientId, out Vector3 world)
    {
        world = Vector3.zero;
        if (_clientRelPos.TryGetValue(clientId, out var rel) &&
            WorldOrigin.Instance != null && WorldOrigin.Instance.IsReady)
        {
            world = WorldOrigin.Instance.ToWorld(rel);
            return true;
        }
        return false;
    }

    // Server → cliente puntual: avisa que recogió una pila y cuánta carga sumar.
    public void ServerSendBatteryCollected(uint clientId, byte rarityIndex, float charge)
    {
        if (_srv == null) return;
        var body = new Bateries.BatteryCollectedMsg { RarityIndex = rarityIndex, Charge = charge }.Serialize();
        _srv.Send(clientId, MsgHelper.Frame(MessageType.BatteryCollected, body));
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

        // Enviar el mapa PRIMERO: el cliente necesita el .mscn (mapa + imagen de
        // referencia) para reconstruir el entorno y calibrar su anchor contra la misma
        // imagen física que el host. Sin esto los Sorkens no caen en el mismo lugar.
        if (_mapBytes != null && _mapBytes.Length > 0)
            _srv.Send(clientId, MsgHelper.Frame(MessageType.MapData,
                new MapDataMsg { Bytes = _mapBytes }.Serialize()));

        // Enviar catch-up de entidades ya existentes (en espacio anchor-relativo). Se
        // omiten las DIRIGIDAS a otro jugador (alucinaciones de Arbmos ajenas).
        foreach (var entity in EntityRegistry.Instance.All)
        {
            if (entity.VisibleTo != NetworkEntity.Everyone && entity.VisibleTo != clientId) continue;
            var catchup = new SpawnEntityMsg
            {
                NetworkId     = entity.NetworkId,
                TypeId        = entity.EntityTypeId,
                OwnerClientId = entity.OwnerClientId,
                Position      = WorldOrigin.Instance.ToRelative(entity.transform.position),
                VisibleTo     = entity.VisibleTo,
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
        _clientRelPos.Remove(clientId);
        _clientFlashOn.Remove(clientId);
        _clientForward.Remove(clientId);

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
            case MessageType.PlayerPose:
            {
                var pose = PlayerPoseMsg.Deserialize(incoming.Body);
                _clientRelPos[incoming.ClientId]  = pose.RelPos;
                _clientFlashOn[incoming.ClientId] = pose.FlashlightOn;
                _clientForward[incoming.ClientId] = pose.Forward;
                break;
            }
            case MessageType.BatteryPickup:
            {
                if (!GameStarted) return;
                var pick = Bateries.BatteryPickupMsg.Deserialize(incoming.Body);
                Bateries.BatterySpawnManager.Instance?.ServerHandlePickup(incoming.ClientId, pick.NetworkId);
                break;
            }
        }
    }

    private readonly List<(NetworkEntity ent, byte[] payload)> _stateScratch = new();

    private void BroadcastWorldState()
    {
        // Serializar cada entidad una sola vez.
        _stateScratch.Clear();
        foreach (var entity in EntityRegistry.Instance.All)
            _stateScratch.Add((entity, entity.SerializeState(CurrentTick)));

        // Un WorldState por cliente: entidades compartidas para todos + las DIRIGIDAS a
        // ese cliente (su alucinacion de Arbmos). Asi cada jugador ve solo lo suyo.
        foreach (var clientId in _connectedClients)
        {
            var world = new WorldStateMsg { Tick = CurrentTick };
            foreach (var (ent, payload) in _stateScratch)
            {
                if (ent.VisibleTo != NetworkEntity.Everyone && ent.VisibleTo != clientId) continue;
                world.Entries.Add(new WorldStateMsg.Entry
                {
                    NetworkId = ent.NetworkId,
                    TypeId    = ent.EntityTypeId,
                    Payload   = payload,
                });
            }
            _srv.Send(clientId, MsgHelper.Frame(MessageType.WorldState, world.Serialize()));
        }
    }

    // ── Client tick ───────────────────────────────────────────────────────

    private void ClientTick()
    {
        if (_cli == null) return;

        while (_cli.TryDequeue(out var msg))
            HandleClientMessage(msg);

        if (!GameStarted) return;

        // Reportar la pose de la camara AR al servidor (aunque no haya PlayerEntity):
        // el server necesita saber donde esta cada cliente para el sistema de pilas.
        if (Camera.main != null && WorldOrigin.Instance != null && WorldOrigin.Instance.IsReady)
        {
            var relPos  = WorldOrigin.Instance.ToRelative(Camera.main.transform.position);
            var relFwd  = WorldOrigin.Instance.ToRelativeDir(Camera.main.transform.forward);
            _cli.Send(MsgHelper.Frame(MessageType.PlayerPose,
                new PlayerPoseMsg { RelPos = relPos, FlashlightOn = LocalFlashlightOn(), Forward = relFwd }.Serialize()));
        }

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
            case MessageType.MapData:
            {
                var m = MapDataMsg.Deserialize(msg.Body);
                Debug.Log($"[Client] Mapa recibido ({m.Bytes?.Length ?? 0} bytes)");
                OnMapReceived?.Invoke(m.Bytes);
                break;
            }
            case MessageType.BatteryCollected:
            {
                var m = Bateries.BatteryCollectedMsg.Deserialize(msg.Body);
                OnBatteryCollected?.Invoke(m.RarityIndex, m.Charge);
                break;
            }
            case MessageType.PlayerSanity:
            {
                var m = PlayerSanityMsg.Deserialize(msg.Body);
                OnSanityUpdated?.Invoke(m.Sanity, m.Max);
                break;
            }
            case MessageType.PlayerDied:
            {
                OnPlayerDied?.Invoke();
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
        net.SetVisibleTo(msg.VisibleTo);
        EntityRegistry.Instance.Register(net);

        // iOS/Metal: los SkinnedMeshRenderer importados a veces traen bounds en bind-pose
        // mal calculados y el frustum culling los descarta (invisibles en device, OK en
        // editor/Android). Recalcular bounds cada frame evita el culling erróneo.
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = true;
            smr.enabled = true;
        }

        if (!IsServer)
        {
            var ai = go.GetComponent<SorkerAI>();
            if (ai != null) ai.enabled = false;
        }

        // Copia DIRIGIDA a otro jugador: el host la simula pero NO la dibuja (es la
        // alucinacion de ESE jugador). Se marca ArbmosEntity.Rendered=false antes de
        // OnNetworkSpawn (para que no cree aura/luz ni se registre como Active) y se
        // apagan renderers y luces.
        bool hideOnServer = IsServer &&
                            msg.VisibleTo != NetworkEntity.Everyone && msg.VisibleTo != 0;
        if (hideOnServer)
        {
            var arb = go.GetComponent<ArbmosEntity>();
            if (arb != null) arb.Rendered = false;
            foreach (var rend in go.GetComponentsInChildren<Renderer>(true)) rend.enabled = false;
            foreach (var lt   in go.GetComponentsInChildren<Light>(true))    lt.enabled = false;
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
