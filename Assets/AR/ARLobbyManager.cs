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
        PlacingAnchors,     // imagen encontrada, colocando anchor points extra (opción por dispositivo)
        WaitingForClients,  // host encontró imagen, esperando clientes
        AllReady,           // cliente encontró imagen, esperando que host arranque
        GameStarted,
    }

    public LobbyState State        { get; private set; } = LobbyState.Idle;
    public int ConnectedCount      { get; private set; }
    public int ResolvedCount       { get; private set; }

    // Clientes que todavía impiden arrancar por sus anchor points. Se mantiene
    // cacheado a propósito: CanStartGame se evalúa desde OnGUI (varias veces por
    // frame) y recorrer ConnectedClients ahí boxearía el enumerador del HashSet.
    public int AnchorPendingCount  { get; private set; }

    [SerializeField] private ARImageAnchor _imageAnchor;
    [SerializeField] private int           _sorkerCount = 1;

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
        net.OnClientJoined      += HandleClientJoined;
        net.OnClientLeft        += HandleClientLeft;
        net.OnClientResolved    += HandleClientResolved;
        net.OnClientAnchorStatus += HandleClientAnchorStatus;
        net.OnMapReceived       += HandleMapReceived;
        net.OnNightReset        += HandleNightReset;
        net.OnNightSurvived     += Gameplay.NightTransition.NocheSuperada;
        net.OnNightClock        += HandleNightClock;
        net.OnGameStarted       += HandleGameStarted;

        if (_imageAnchor == null) _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
        if (_imageAnchor != null)
            // OnImageReacquired, NO OnImageFound: éste último dispara UNA sola vez en
            // toda la sesión, así que al reiniciar la noche (RestartTracking) el lobby
            // se quedaba pegado en Scanning para siempre. OnImageReacquired incluye la
            // primera detección, así que cubre los dos casos.
            _imageAnchor.OnImageReacquired += OnImageFound;

        // Si este dispositivo va a colocar anclas, el manager tiene que existir antes
        // de que aparezca la imagen (se suscribe a OnImageReacquired).
        if (GameOptions.PuntosAncla) AnchorPointManager.Ensure();
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
        // El host tiene que saber de entrada si este cliente va a colocar anclas, para
        // no habilitar INICIAR NOCHE antes de tiempo. TcpTransportClient.Connect es
        // sincrónico, así que acá ya estamos conectados.
        ReportarAnclas();
    }

    // Manda al host el estado de los anchor points de ESTE dispositivo. No-op en el
    // host (no hay cliente); su propio estado se lee local en LocalAnchorsReady.
    public void ReportarAnclas()
    {
        var net = NetworkManager.Instance;
        if (net == null || net.IsServer) return;

        bool activada = GameOptions.PuntosAncla;
        var  mgr      = AnchorPointManager.Instance;
        net.ClientSendAnchorPointsStatus(
            activada,
            mgr != null ? mgr.Count : 0,
            !activada || (mgr != null && mgr.Listo));
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
        if (!CanStartGame)
        {
            Debug.LogWarning($"[ARLobby] ServerStartGame bloqueado: {StartBlockReason}");
            return;
        }
        NetworkManager.Instance.ServerStartGame(_sorkerCount);
        State = LobbyState.GameStarted;
    }

    public bool CanStartGame => NetworkManager.Instance != null
        && NetworkManager.Instance.IsServer
        && State == LobbyState.WaitingForClients
        && LocalAnchorsReady
        && AnchorPendingCount == 0;

    // El host también tiene que haber cerrado SUS anclas si tiene la opción activada.
    private bool LocalAnchorsReady
    {
        get
        {
            if (!GameOptions.PuntosAncla) return true;
            var mgr = AnchorPointManager.Instance;
            return mgr != null && mgr.Listo;
        }
    }

    // Motivo por el que INICIAR NOCHE está atenuado (null si se puede arrancar).
    public string StartBlockReason
    {
        get
        {
            if (CanStartGame) return null;
            if (!LocalAnchorsReady) return "colocá tus anclas y pulsá LISTO";
            if (AnchorPendingCount == 1) return "1 jugador todavía prepara sus anclas";
            if (AnchorPendingCount > 1)  return $"{AnchorPendingCount} jugadores todavía preparan sus anclas";
            return null;
        }
    }

    // ── Imagen detectada (host y cliente) ─────────────────────────────────

    private void OnImageFound()
    {
        // Sólo nos interesa la detección mientras estamos sincronizando. Si la partida
        // ya arrancó, una re-detección no puede tirarnos de vuelta al lobby.
        if (State != LobbyState.Scanning) return;

        // Con la opción activada, primero se colocan las anclas: el paso de la FSM
        // que sigue lo dispara el botón LISTO (ver AnchorPlacementDone).
        //
        // Si ya están colocadas (reinicio de noche en la MISMA sesión AR) se saltea:
        // los ARAnchor siguen vivos, así que sólo hacía falta recalibrar la imagen.
        if (GameOptions.PuntosAncla)
        {
            var anclas = AnchorPointManager.Ensure();
            if (!anclas.Listo)
            {
                anclas.AbrirColocacion();
                State = LobbyState.PlacingAnchors;
                ReportarAnclas();
                return;
            }
        }
        AvanzarTrasSincronizar();
    }

    // ── Reinicio de noche sin cerrar la sesión (ver Gameplay.NightTransition) ──

    // Vuelve a la pantalla de sincronización y re-lanza la búsqueda de la imagen. NO
    // toca los anchor points: viven en la sesión AR, que no se reinicia.
    public void ReiniciarSincronizacion()
    {
        _resolvedClients.Clear();
        ResolvedCount = 0;
        State = LobbyState.Scanning;

        // keepVisualPosition: false → la escena se mueve con el anchor nuevo, que es
        // la semántica de "recalibrar contra la imagen".
        _imageAnchor?.RestartTracking(keepVisualPosition: false);
    }

    // Cliente: el host cortó la noche.
    private void HandleNightReset()
    {
        Gameplay.NightTransition.ResetLocal();
        ReiniciarSincronizacion();
    }

    // Cliente: reloj del amanecer (el host lo escribe directo en GameDirector).
    private static void HandleNightClock(int restantes, int totales) =>
        Gameplay.NightResult.SetReloj(restantes, totales);

    private void HandleGameStarted()
    {
        // NightResult es estático y sobrevive el cambio de escena: limpiarlo acá cubre
        // también el arranque "en frío" (menú → partida), no sólo los reinicios.
        Gameplay.NightResult.LimpiarResultado();
        State = LobbyState.GameStarted;
    }

    // El jugador cerró la colocación de anclas (LISTO u OMITIR).
    public void AnchorPlacementDone()
    {
        if (State != LobbyState.PlacingAnchors) return;
        AvanzarTrasSincronizar();
        ReportarAnclas();
    }

    private void AvanzarTrasSincronizar()
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
        RecalcularAnclasPendientes();
    }

    private void HandleClientLeft(uint id)
    {
        _connectedClients.Remove(id);
        _resolvedClients.Remove(id);
        ConnectedCount = _connectedClients.Count;
        ResolvedCount  = _resolvedClients.Count;
        RecalcularAnclasPendientes();
    }

    private void HandleClientResolved(uint id)
    {
        _resolvedClients.Add(id);
        ResolvedCount = _resolvedClients.Count;
    }

    private void HandleClientAnchorStatus(uint id) => RecalcularAnclasPendientes();

    // Sólo por evento (join / leave / status), nunca por frame.
    private void RecalcularAnclasPendientes()
    {
        var net = NetworkManager.Instance;
        if (net == null || !net.IsServer) { AnchorPendingCount = 0; return; }

        int pendientes = 0;
        foreach (var id in _connectedClients)
            if (net.ClientBlocksStart(id)) pendientes++;
        AnchorPendingCount = pendientes;
    }

    private void OnDestroy()
    {
        if (_imageAnchor != null) _imageAnchor.OnImageReacquired -= OnImageFound;

        var net = NetworkManager.Instance;
        if (net == null) return;
        net.OnClientJoined      -= HandleClientJoined;
        net.OnClientLeft        -= HandleClientLeft;
        net.OnClientResolved    -= HandleClientResolved;
        net.OnClientAnchorStatus -= HandleClientAnchorStatus;
        net.OnMapReceived       -= HandleMapReceived;
        net.OnNightReset        -= HandleNightReset;
        net.OnNightSurvived     -= Gameplay.NightTransition.NocheSuperada;
        net.OnNightClock        -= HandleNightClock;
        net.OnGameStarted       -= HandleGameStarted;
    }
}
