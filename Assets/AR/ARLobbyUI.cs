using Scanner;
using UnityEngine;
using T = MortuoriumTheme;

// UI del lobby AR (SampleScene) con la estética del prototipo: la pantalla de
// SINCRONIZACIÓN (todos apuntan la cámara a la imagen de referencia del mapa)
// y el briefing de la noche cuando el host arranca la partida.
//
// Se dibuja ENCIMA de la cámara AR y DEBAJO/JUNTO al panel de sala de
// GameBootstrapper (que ocupa la franja superior). La lógica de estados vive en
// ARLobbyManager; acá solo se presenta.
public class ARLobbyUI : MonoBehaviour
{
    [Header("Briefing de la noche")]
    [Tooltip("Segundos que el título de la noche permanece en pantalla al arrancar.")]
    [SerializeField] private float _briefingDuration = 3f;

    private ARLobbyManager _lobby;
    private NetworkManager _net;
    private readonly Gamepad.ImguiGamepadMenu _nav = new();

    // Briefing: cartel de intro (NOCHE X + texto) que entra deslizándose desde la
    // izquierda con fade, se sostiene y sale con fade. Sin overlay ni botón; dura
    // _briefingDuration y se descarta solo.
    private bool  _briefingArmado;   // ya detectamos el arranque de la partida
    private bool  _briefingDone;     // terminó (ya no se dibuja)
    private float _briefingT;        // segundos transcurridos

    // Tiempos de la animación de entrada/salida (dentro de la duración total).
    private const float InDur = 0.5f, OutDur = 0.5f, SlidePx = 60f;

    private const float Pad = 28f;

    private void Start()
    {
        _lobby = ARLobbyManager.Instance;
        _net   = NetworkManager.Instance;
    }

    private void Update()
    {
        _nav.Update();

        // Timer del briefing (en Update para no depender de los múltiples pasos de
        // OnGUI por frame). Arranca al entrar a GameStarted.
        if (_lobby != null && _lobby.State == ARLobbyManager.LobbyState.GameStarted && !_briefingDone)
        {
            if (!_briefingArmado) { _briefingArmado = true; _briefingT = 0f; }
            _briefingT += Time.deltaTime;
            if (_briefingT >= Mathf.Max(0.1f, _briefingDuration)) _briefingDone = true;
        }
    }

    private void OnGUI()
    {
        if (_lobby == null || _net == null) return;
        if (_lobby.State == ARLobbyManager.LobbyState.Idle) return;

        UIScale.Begin();
        _nav.Begin();

        float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;

        // Franjas fuera del área segura en negro (cámara visible sólo dentro).
        T.FillOutsideSafeArea(T.Bg);

        if (_lobby.State == ARLobbyManager.LobbyState.GameStarted)
        {
            if (!_briefingDone) DrawBriefing(vw, vh);
            _nav.End();
            return;
        }

        // ── COLOCACIÓN DE ANCLAS (pantalla propia, con retícula) ──────────────
        if (_lobby.State == ARLobbyManager.LobbyState.PlacingAnchors)
        {
            DrawColocarAnclas(vw, vh);
            _nav.End();
            return;
        }

        // ── SINCRONIZACIÓN (encima de la cámara, debajo de la franja de sala) ──
        bool solo = Gameplay.GameSession.Instance != null &&
                    Gameplay.GameSession.Instance.SoloUnJugador;

        T.Gradiente(new Rect(0, vh - 320f, vw, 320f), 0.9f, haciaAbajo: false);

        float y = vh - 290f;
        GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 36f), "SINCRONIZACIÓN",
                  T.Estilo(T.FBebas, 26, T.Cream));
        y += 38f;

        string entorno = Gameplay.GameSession.Instance != null &&
                         !string.IsNullOrEmpty(Gameplay.GameSession.Instance.SelectedMap)
            ? $"entorno: {Gameplay.GameSession.Instance.SelectedMap}"
            : "entorno compartido por el host";
        GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 22f), entorno,
                  T.Estilo(T.FElite, 13, T.CreamDim));
        y += 26f;

        // El texto de sincronización se personaliza: en un jugador no hay "todos
        // los jugadores" ni "mismo espacio virtual" (no hay con quién compartirlo).
        GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 40f),
                  solo
                      ? "apuntá la cámara a la imagen de referencia para ubicarte " +
                        "en tu espacio escaneado."
                      : "todos los jugadores deben apuntar la cámara a la imagen de " +
                        "referencia para ubicarse en el mismo espacio virtual.",
                  T.Estilo(T.FMono, 11, T.Muted, TextAnchor.UpperLeft, wrap: true));
        y += 48f;

        switch (_lobby.State)
        {
            case ARLobbyManager.LobbyState.Scanning:
                GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 24f),
                          $"buscando la imagen… {Spinner()}",
                          T.Estilo(T.FMono, 13, T.Tan));
                break;

            case ARLobbyManager.LobbyState.WaitingForClients:
            {
                // El contador de conectados/listos solo aplica a multijugador.
                GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 24f),
                          solo
                              ? "imagen detectada · listo para empezar"
                              : $"imagen detectada · conectados {_lobby.ConnectedCount} · listos {_lobby.ResolvedCount}",
                          T.Estilo(T.FMono, 12, T.Green));

                bool puede = _lobby.CanStartGame;
                if (!puede)
                {
                    string motivo = _lobby.StartBlockReason;
                    if (!string.IsNullOrEmpty(motivo))
                        GUI.Label(new Rect(Pad, vh - 44f - 56f - 24f, vw - Pad * 2f, 22f), motivo,
                                  T.Estilo(T.FMono, 11, T.Tan));
                }
                T.Boton(_nav, new Rect(Pad, vh - 44f - 56f, vw - Pad * 2f, 56f),
                        "INICIAR NOCHE", primario: true, () => _lobby.ServerStartGame(),
                        enabled: puede);
                break;
            }

            case ARLobbyManager.LobbyState.AllReady:
                GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 24f), "imagen detectada",
                          T.Estilo(T.FMono, 12, T.Green));
                GUI.Label(new Rect(Pad, y + 26f, vw - Pad * 2f, 24f),
                          "esperando a que el host inicie la noche…",
                          T.Estilo(T.FMono, 12, T.Muted));
                break;
        }

        _nav.End();
    }

    // ── Colocación de anchor points (opción por dispositivo) ──────────────
    // Retícula al centro + panel inferior. El jugador camina por su cuarto, apunta a
    // superficies y coloca anclas; con LISTO cierra y arranca la corrección de deriva.
    private void DrawColocarAnclas(float vw, float vh)
    {
        var mgr = AnchorPointManager.Instance;
        // Nunca deberíamos llegar acá sin manager (ARLobbyManager lo crea antes de
        // entrar al estado), pero si pasa lo creamos y dibujamos al frame siguiente:
        // quedarse sin UI acá dejaría al jugador sin poder arrancar la partida.
        if (mgr == null) { AnchorPointManager.Ensure(); return; }

        // Retícula: cruz fina en el centro de la pantalla virtual.
        const float R = 20f, G = 6f, W = 2f;
        float cx = vw * 0.5f, cy = vh * 0.5f;
        var reticColor = mgr.CardboardBloquea ? T.Dim : T.Cream;
        T.Fill(new Rect(cx - R, cy - W * 0.5f, R - G, W), reticColor);
        T.Fill(new Rect(cx + G, cy - W * 0.5f, R - G, W), reticColor);
        T.Fill(new Rect(cx - W * 0.5f, cy - R, W, R - G), reticColor);
        T.Fill(new Rect(cx - W * 0.5f, cy + G, W, R - G), reticColor);

        T.Gradiente(new Rect(0, vh - 400f, vw, 400f), 0.9f, haciaAbajo: false);
        UIBlocker.AddVirtualRect(new Rect(0, vh - 400f, vw, 400f));

        float y = vh - 370f;
        GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 36f), "PUNTOS DE ANCLAJE",
                  T.Estilo(T.FBebas, 26, T.Cream));
        y += 38f;

        GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 44f),
                  "recorré tu cuarto y colocá anclas apuntando a paredes o muebles. " +
                  "cuantas más y más repartidas, menos se corre el mapa.",
                  T.Estilo(T.FMono, 11, T.Muted, TextAnchor.UpperLeft, wrap: true));
        y += 50f;

        string estado = mgr.Count < AnchorPointManager.MinAnclas
            ? $"anclas: {mgr.Count} / {AnchorPointManager.MaxAnclas}  ·  faltan {AnchorPointManager.MinAnclas - mgr.Count}"
            : $"anclas: {mgr.Count} / {AnchorPointManager.MaxAnclas}";
        GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 22f), estado,
                  T.Estilo(T.FMono, 13, mgr.PuedeCerrar ? T.Green : T.Tan));
        y += 24f;

        // Motivo del último rechazo (o el hint de Cardboard, que gana).
        string aviso = mgr.CardboardBloquea ? "salí de Cardboard para poder apuntar" : _errorAnclas;
        if (!string.IsNullOrEmpty(aviso))
            GUI.Label(new Rect(Pad, y, vw - Pad * 2f, 22f), aviso, T.Estilo(T.FMono, 11, T.Red));

        // Los botones se dibujan SIEMPRE (deshabilitados en vez de ocultos): el foco
        // del mando es una lista global y se resetea si cambia la cantidad de items.
        float by = vh - 44f - 56f;
        T.Boton(_nav, new Rect(Pad, by, vw - Pad * 2f, 56f), "COLOCAR ANCLA", primario: true,
                () =>
                {
                    _errorAnclas = mgr.TryColocar(out var err) ? null : err;
                    ReportarAnclas();
                },
                enabled: mgr.PuedeColocar && !mgr.CardboardBloquea);

        float bw = (vw - Pad * 2f - 10f) * 0.5f;
        by -= 52f;
        T.Boton(_nav, new Rect(Pad, by, bw, 46f), "DESHACER", primario: false,
                () => { mgr.DeshacerUltima(); _errorAnclas = null; ReportarAnclas(); },
                enabled: mgr.Count > 0, fontSize: 16);
        T.Boton(_nav, new Rect(Pad + bw + 10f, by, bw, 46f), "LISTO", primario: false,
                () => { mgr.MarcarListo(); _errorAnclas = null; _lobby.AnchorPlacementDone(); },
                enabled: mgr.PuedeCerrar, fontSize: 16);

        // Escape: en un cuarto sin superficies trackeables no se llega al mínimo y el
        // jugador quedaría sin poder arrancar la partida.
        by -= 40f;
        T.Boton(_nav, new Rect(Pad, by, vw - Pad * 2f, 34f), "OMITIR ANCLAS", primario: false,
                () => { mgr.Omitir(); _errorAnclas = null; _lobby.AnchorPlacementDone(); },
                fontSize: 13, textColor: T.Muted);
    }

    private string _errorAnclas;

    private void ReportarAnclas() => _lobby.ReportarAnclas();

    // ── Briefing de la noche (cartel de intro, sin overlay ni botón) ──────
    // Entra deslizándose desde la izquierda con fade, se sostiene y sale con fade.
    // Se dibuja directamente sobre la cámara.
    private void DrawBriefing(float vw, float vh)
    {
        float dur = Mathf.Max(0.1f, _briefingDuration);
        float t   = _briefingT;

        // Alpha y desplazamiento en X según la fase (entrada / sostenido / salida).
        float alpha, slide;
        if (t < InDur)
        {
            float k = Mathf.Clamp01(t / InDur);
            alpha = k;
            slide = -SlidePx * (1f - EaseOut(k));   // llega desde la izquierda
        }
        else if (t > dur - OutDur)
        {
            alpha = Mathf.Clamp01((dur - t) / OutDur);
            slide = 0f;
        }
        else { alpha = 1f; slide = 0f; }

        var night = Gameplay.GameSession.Instance != null
            ? Gameplay.GameSession.Instance.SelectedNight : null;

        // GUI.color multiplica el alpha de todo lo dibujado (incluido el texto).
        var prevColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);

        float x = Pad + slide;
        float y = vh * 0.30f;
        GUI.Label(new Rect(x, y, vw - Pad * 2f, 24f), "NOCHE",
                  T.Estilo(T.FBebas, 16, T.Red));
        y += 28f;

        // El cliente no conoce la config de la noche (la define el host).
        string nombre = night != null
            ? (string.IsNullOrEmpty(night.displayName) ? night.name : night.displayName)
            : "LA NOCHE COMIENZA";
        GUI.Label(new Rect(x, y, vw - Pad * 2f, 70f), nombre.ToUpperInvariant(),
                  T.Estilo(T.FBebas, 48, T.Cream));
        y += 84f;

        string briefing = night != null && !string.IsNullOrEmpty(night.briefing)
            ? night.briefing
            : "Sobreviví hasta el amanecer. El ritual no debe parar.";
        GUI.Label(new Rect(x, y, vw - Pad * 2f, 140f), briefing,
                  T.Estilo(T.FElite, 14, T.CreamDim, TextAnchor.UpperLeft, wrap: true));

        GUI.color = prevColor;
    }

    // Easing suave para la entrada (desaceleración).
    private static float EaseOut(float k) => 1f - (1f - k) * (1f - k);

    private float _spinnerTime;
    private static readonly string[] _spinnerFrames = { "—", "\\", "|", "/" };

    private string Spinner()
    {
        _spinnerTime += Time.deltaTime;
        return _spinnerFrames[Mathf.FloorToInt(_spinnerTime * 6) % _spinnerFrames.Length];
    }
}
