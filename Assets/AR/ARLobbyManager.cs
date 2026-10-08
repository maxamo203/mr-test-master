using System.Collections;
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
        CalibrandoManual,   // sin imagen física: el jugador ubica el 0,0 a mano (ManualCalibration)
        PlacingAnchors,     // imagen encontrada, colocando anchor points extra (opción por dispositivo)
        WaitingForClients,  // host encontró imagen, esperando clientes
        AllReady,           // cliente encontró imagen, esperando que host arranque
        GameStarted,
    }

    // Un cliente que sale de AllReady sin que arranque la noche (AJUSTAR ENTORNO, BUSCAR
    // IMAGEN) deja de estar listo: se le avisa al host para que no lo siga contando. Al
    // volver a cerrar el ajuste, AvanzarTrasSincronizar manda AnchorResolved de nuevo.
    private LobbyState _state = LobbyState.Idle;
    public LobbyState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            var previo = _state;
            _state = value;

            var net = NetworkManager.Instance;
            if (previo == LobbyState.AllReady && value != LobbyState.GameStarted &&
                net != null && !net.IsServer)
                net.ClientSendAnchorUnresolved();
        }
    }

    // Server: clientes remotos conectados y cuántos de ellos ya están listos (el host
    // NO se cuenta acá; el contador visible para todos sí lo suma — ver PublicarContador).
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
        net.OnClientUnresolved  += HandleClientUnresolved;
        net.OnClientAnchorStatus += HandleClientAnchorStatus;
        net.OnMapReceived       += HandleMapReceived;
        net.OnMapAnnounced      += HandleMapAnnounced;
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

        // Tocar el 0,0 (en partida, el LIBRO: es el visual del ancla) abre el ajuste.
        // En esta escena no hay SelectionController, así que el tap lo detecta la
        // propia ManualCalibration y acá sólo decidimos cuándo se permite: mientras
        // se sincroniza. Con la noche ya empezada NO, o un toque suelto movería el
        // mapa bajo los pies del jugador.
        var cal = ManualCalibration.Ensure();
        cal.PuedeAbrirPorTap = () => _mapaListo && EnSincronizacion;
        cal.OnTapEnOrigen += AbrirAjusteOrigen;
    }

    // Estados en los que todavía se está ubicando el entorno (la noche no arrancó).
    private bool EnSincronizacion =>
        State == LobbyState.Scanning || State == LobbyState.WaitingForClients ||
        State == LobbyState.AllReady || State == LobbyState.CalibrandoManual;

    // Abre el ajuste del 0,0 sin recentrar nada: sirve con la imagen ya detectada,
    // cuando la pose que dio el tracking quedó corrida respecto del cuarto real.
    public void AbrirAjusteOrigen()
    {
        if (!EnSincronizacion) return;
        ManualCalibration.Ensure().AbrirAjuste();
        State = LobbyState.CalibrandoManual;
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

        // Hash antes de empaquetar: en escaneos viejos AsegurarHash lo escribe en el
        // json, y así viaja dentro del .mscn y el cliente lo guarda igual.
        var hash = ScanSerializer.AsegurarHash(mapName);
        ScanLoader.LoadForDisplay(mapName, _imageAnchor);
        _mapaListo = true;
        NetworkManager.Instance.ServerSetMap(ScanPackage.Pack(mapName), hash, mapName);
    }

    // Cliente: NO arranca el tracking todavía — primero necesita recibir el mapa
    // (su imagen de referencia). El tracking arranca en HandleMapReceived.
    public void BeginClientFlow()
    {
        State = LobbyState.Scanning;
        FaseMapa(EstadoMapa.EsperandoAnuncio);   // overlay de carga hasta tener el mapa
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

    // ── Obtención del mapa (cliente) ──────────────────────────────────────
    // Desde que el cliente se conecta hasta que el mapa del host está armado en la
    // escena pasa un rato que antes era invisible (anuncio → pedido → .mscn de cientos
    // de KB por Wi-Fi → importar y reconstruir). ARLobbyUI lo muestra como overlay de
    // carga con estos datos. En el host siempre es Listo: carga su mapa sincrónico.
    public enum EstadoMapa
    {
        Listo,              // mapa armado (o host: no aplica)
        EsperandoAnuncio,   // conectado, el host todavía no dijo qué mapa se juega
        Descargando,        // MapRequest enviado, llegando el MapData
        Instalando,         // importando + reconstruyendo (un frame de hitch)
        Error,              // el .mscn llegó pero no se pudo importar
    }

    public EstadoMapa MapaCliente     { get; private set; } = EstadoMapa.Listo;
    public string     MapaNombre      { get; private set; }
    public float      MapaEsperaDesde { get; private set; }   // realtime del último cambio de fase

    // ¿Tapar la sincronización con el overlay de carga? Sólo cliente y antes de tener mapa.
    public bool MapaPendiente =>
        MapaCliente != EstadoMapa.Listo &&
        NetworkManager.Instance != null && !NetworkManager.Instance.IsServer;

    private void FaseMapa(EstadoMapa e)
    {
        MapaCliente     = e;
        MapaEsperaDesde = Time.realtimeSinceStartup;
    }

    // Botón REINTENTAR del overlay tras un import fallido: volver a pedir el .mscn.
    public void ReintentarMapa()
    {
        if (MapaCliente != EstadoMapa.Error) return;
        FaseMapa(EstadoMapa.Descargando);
        NetworkManager.Instance.ClientRequestMap();
    }

    // Hash del mapa que este cliente ya tiene cargado: un re-anuncio del mismo mapa
    // no debe recargarlo (eso reiniciaría la búsqueda de la imagen).
    private string _hashCargado;

    // Cliente: el host anunció qué mapa se juega. Si ya hay un escaneo local con ese
    // hash se usa ése; si no, se le pide el .mscn (llega por HandleMapReceived).
    private void HandleMapAnnounced(string hash, string nombre)
    {
        var net = NetworkManager.Instance;
        if (net.IsServer) return;
        if (_mapaListo && !string.IsNullOrEmpty(hash) && hash == _hashCargado) return;

        MapaNombre = nombre;
        var local = ScanSerializer.BuscarPorHash(hash);
        if (local != null)
        {
            Debug.Log($"[ARLobby] Mapa '{nombre}' ya está en el dispositivo como '{local}'; no se descarga.");
            StartCoroutine(InstalarMapa(null, local));
            return;
        }
        if (MapaCliente != EstadoMapa.Descargando) FaseMapa(EstadoMapa.Descargando);
        net.ClientRequestMap();
    }

    // Cliente: llegó el .mscn del host (porque lo pedimos). Import lo instala, o
    // devuelve el local si ya había uno con el mismo hash.
    private void HandleMapReceived(byte[] bytes)
    {
        if (NetworkManager.Instance.IsServer) return; // el host ya tiene su mapa cargado
        if (bytes == null || bytes.Length == 0) return;

        StartCoroutine(InstalarMapa(bytes, null));
    }

    // Importar + reconstruir es sincrónico y se lleva un frame entero: se difiere uno
    // para que el overlay alcance a mostrar "PREPARANDO ENTORNO" en vez de quedarse
    // congelado en la barra de descarga.
    private IEnumerator InstalarMapa(byte[] bytes, string local)
    {
        FaseMapa(EstadoMapa.Instalando);
        yield return null;

        string name = local;
        try
        {
            if (name == null) name = ScanPackage.Import(bytes);
            if (!string.IsNullOrEmpty(name)) CargarMapaCliente(name);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            name = null;
        }

        if (string.IsNullOrEmpty(name))
        {
            Debug.LogWarning("[ARLobby] No se pudo importar el mapa recibido.");
            FaseMapa(EstadoMapa.Error);
        }
    }

    // Reconstruye el mapa display-only y registra su imagen de referencia para
    // calibrar el anchor contra la misma imagen física que el host.
    private void CargarMapaCliente(string name)
    {
        ScanLoader.LoadForDisplay(name, _imageAnchor);
        _hashCargado = ScanSerializer.Load(name)?.contentHash;
        _mapaListo = true;
        State = LobbyState.Scanning;
        FaseMapa(EstadoMapa.Listo);
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
        && AnchorPendingCount == 0
        && SincronizacionPendiente == 0;

    // Clientes conectados que TODAVÍA no ubicaron el entorno (no mandaron
    // AnchorResolved). Arrancar sin ellos los deja jugando en un mapa que no está
    // donde su cuarto: ven Sorkers atravesando paredes que para ellos no existen.
    //
    // Se nota sobre todo al REINTENTAR: ReiniciarSincronizacion() vacía la lista de
    // resueltos, pero el host —que en la misma sesión AR ya está calibrado— vuelve a
    // WaitingForClients al instante, mucho antes de que los clientes recalibren.
    public int SincronizacionPendiente => Mathf.Max(0, ConnectedCount - ResolvedCount);

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
            int faltan = SincronizacionPendiente;
            if (faltan == 1) return "1 jugador todavía está ubicando el entorno";
            if (faltan > 1)  return $"{faltan} jugadores todavía están ubicando el entorno";
            return null;
        }
    }

    // ── Imagen detectada (host y cliente) ─────────────────────────────────

    private void OnImageFound()
    {
        // Sólo nos interesa la detección mientras estamos sincronizando. Si la partida
        // ya arrancó, una re-detección no puede tirarnos de vuelta al lobby.
        if (State != LobbyState.Scanning) return;
        ContinuarTrasCalibrar();
    }

    // Ya hay marco de referencia (por la imagen o a mano): sigue el flujo del lobby.
    private void ContinuarTrasCalibrar()
    {
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

    // ── Calibración manual (sin la imagen física) ─────────────────────────

    // El mapa ya está cargado en la escena (es lo único que se necesita para poder
    // ubicarlo a mano). En el cliente eso pasa recién al recibir el .mscn del host.
    private bool _mapaListo;
    public bool PuedeCalibrarManual => _mapaListo && State == LobbyState.Scanning;

    // "NO TENGO LA IMAGEN": centra el 0,0 del mapa donde apunta la mira y abre el
    // ajuste con el gizmo. Devuelve null si salió bien, o el motivo del fallo.
    public string CalibrarSinImagen()
    {
        if (!PuedeCalibrarManual) return "el entorno todavía no está cargado";

        if (!ManualCalibration.Ensure().CentrarBajoLaMira(out var error)) return error;
        State = LobbyState.CalibrandoManual;
        return null;
    }

    // Vuelve a llevar el 0,0 al punto que apunta la mira, sin salir del ajuste.
    public string RecentrarManual()
    {
        if (State != LobbyState.CalibrandoManual) return null;
        return ManualCalibration.Ensure().CentrarBajoLaMira(out var error) ? null : error;
    }

    // El jugador cerró el ajuste con LISTO. De acá en adelante es exactamente el
    // mismo camino que tras detectar la imagen (anclas primero si están activadas).
    public void CalibracionManualLista()
    {
        if (State != LobbyState.CalibrandoManual) return;
        ManualCalibration.Instance?.Listo();
        ContinuarTrasCalibrar();
    }

    // ── Reinicio de noche sin cerrar la sesión (ver Gameplay.NightTransition) ──

    // ¿Este dispositivo ya tiene el entorno ubicado en ESTA sesión AR? El anchor de la
    // imagen (y los anchor points) viven en la sesión, que el reinicio de noche NO
    // reinicia a propósito — así que si ya estaba calibrado, sigue estándolo.
    public bool YaCalibrado =>
        ManualCalibration.Calibrado ||
        (_imageAnchor != null && _imageAnchor.IsFound && _imageAnchor.CurrentAnchor != null);

    // Vuelve a la pantalla de sincronización tras reiniciar la noche. NO toca los
    // anchor points: viven en la sesión AR, que no se reinicia.
    //
    // Y tampoco tira la calibración que ya había: volver a hacer buscar la imagen en
    // cada reintento era trabajo de más para todos (y en multijugador dejaba al host
    // esperando a que los demás re-apunten una imagen que ya habían encontrado). Si el
    // entorno ya está ubicado, se sigue de largo; recalibrar queda a un botón de
    // distancia (RecalibrarConImagen / AJUSTAR ENTORNO) para cuando la pose se corrió.
    public void ReiniciarSincronizacion()
    {
        _resolvedClients.Clear();
        ResolvedCount = 0;

        // Ubicado desde la noche anterior —por la imagen o a mano—: no hay nada que
        // volver a buscar, se sigue el mismo camino que tras calibrar (anclas primero si
        // están activadas). El anchor manual sobrevive igual que el de la imagen.
        //
        // Antes, quien venía calibrado a mano ("NO TENGO LA IMAGEN") caía en UBICAR EL
        // ENTORNO en cada reintento y tenía que volver a cerrar el ajuste con LISTO para
        // poder empezar: si ese jugador era el host, parecía que "al host le exigía
        // recalibrar". Retocar sigue a un botón (AJUSTAR ENTORNO), como con la imagen.
        if (YaCalibrado)
        {
            ContinuarTrasCalibrar();
            return;
        }

        RecalibrarConImagen();
    }

    // Descarta la calibración actual y vuelve a buscar la imagen física. Es el camino
    // explícito cuando la pose quedó corrida y no alcanza con el ajuste manual.
    public void RecalibrarConImagen()
    {
        if (State == LobbyState.GameStarted) return;

        // Buscar la imagen es apuntarle con el centro de la pantalla, que en estéreo no
        // es lo que ve ninguno de los dos ojos: se vuelve a la vista mono.
        MRCardboardController.SalirSiActivo();
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
        // Sólo cuenta quien sigue en la sala: si no, listos podía superar a conectados
        // y SincronizacionPendiente (conectados − listos) tapaba a un cliente nuevo.
        if (!_connectedClients.Contains(id)) return;
        _resolvedClients.Add(id);
        ResolvedCount = _resolvedClients.Count;
    }

    private void HandleClientUnresolved(uint id)
    {
        _resolvedClients.Remove(id);
        ResolvedCount = _resolvedClients.Count;
    }

    // ── Contador de la sala (host → todos) ────────────────────────────────

    private int _jugadoresPublicados = -1, _listosPublicados = -1;

    // ¿El host mismo está listo? Llegó a WaitingForClients sólo tras ubicar el entorno
    // y cerrar sus anclas (o en partida, donde el contador ya no se muestra).
    private bool HostListo =>
        State == LobbyState.WaitingForClients || State == LobbyState.GameStarted;

    // El host lo recalcula cada frame (son dos enteros) y lo difunde SÓLO si cambió:
    // el estado del host cambia desde muchos caminos (detección, ajuste manual, anclas,
    // reinicio) y engancharse a cada uno era la forma de que el contador se trabara.
    private void Update()
    {
        var net = NetworkManager.Instance;
        if (net == null || !net.IsServer || State == LobbyState.Idle) return;

        int jugadores = 1 + ConnectedCount;
        int listos    = ResolvedCount + (HostListo ? 1 : 0);
        if (jugadores == _jugadoresPublicados && listos == _listosPublicados) return;

        _jugadoresPublicados = jugadores;
        _listosPublicados    = listos;
        net.ServerSendLobbyStatus(jugadores, listos);
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

        var cal = ManualCalibration.Instance;
        if (cal != null)
        {
            cal.OnTapEnOrigen -= AbrirAjusteOrigen;
            cal.PuedeAbrirPorTap = null;
        }

        var net = NetworkManager.Instance;
        if (net == null) return;
        net.OnClientJoined      -= HandleClientJoined;
        net.OnClientLeft        -= HandleClientLeft;
        net.OnClientResolved    -= HandleClientResolved;
        net.OnClientUnresolved  -= HandleClientUnresolved;
        net.OnClientAnchorStatus -= HandleClientAnchorStatus;
        net.OnMapReceived       -= HandleMapReceived;
        net.OnMapAnnounced      -= HandleMapAnnounced;
        net.OnNightReset        -= HandleNightReset;
        net.OnNightSurvived     -= Gameplay.NightTransition.NocheSuperada;
        net.OnNightClock        -= HandleNightClock;
        net.OnGameStarted       -= HandleGameStarted;
    }
}
