using Scanner;
using UnityEngine;
using T = MortuoriumTheme;

// Briefing de la noche (SampleScene): el cartel NOCHE X + texto que entra al arrancar la
// partida. Las pantallas previas a la noche (sincronización, carga del mapa, anclas, ubicar
// el entorno) las dibuja Rediseno.Menus (PanelSincronizacion2 / PanelAnclas2 / PanelUbicar2);
// la lógica de estados vive en ARLobbyManager.
//
// Mientras hay lobby también pinta de negro las franjas fuera del área segura: la cámara
// se ve sólo dentro, igual que en el resto de las pantallas sobre la cámara AR.
public class ARLobbyUI : MonoBehaviour
{
    [Header("Briefing de la noche")]
    [Tooltip("Segundos que el título de la noche permanece en pantalla al arrancar.")]
    [SerializeField] private float _briefingDuration = 3f;

    private ARLobbyManager _lobby;
    private NetworkManager _net;

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
        if (_lobby == null) return;

        // Al salir de GameStarted (reinicio de noche sin cerrar la sesión) rearmamos el
        // briefing: si no, el cartel de la noche siguiente no volvía a mostrarse nunca.
        if (_lobby.State != ARLobbyManager.LobbyState.GameStarted)
        {
            _briefingArmado = false;
            _briefingDone   = false;
            _briefingT      = 0f;
            return;
        }

        // Timer del briefing (en Update para no depender de los múltiples pasos de
        // OnGUI por frame). Arranca al entrar a GameStarted.
        if (!_briefingDone)
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
        // Con la pausa abierta no se dibuja nada: debajo de la pausa parecía parte de ella.
        if (Gamepad.PauseMenuController.IsOpen) return;
        // Tutorial pasando de paso o reintentando: arranca solo, detrás de su tarjeta.
        if (Gameplay.TutorialCamino.EnTransicion && _lobby.State != ARLobbyManager.LobbyState.GameStarted) return;

        UIScale.Begin();
        float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;

        // Franjas fuera del área segura en negro (cámara visible sólo dentro).
        T.FillOutsideSafeArea(T.Bg);

        if (_lobby.State == ARLobbyManager.LobbyState.GameStarted && !_briefingDone)
            DrawBriefing(vw, vh);
    }

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

        // Con Cardboard: una copia por ojo (a pantalla completa quedaba partida entre los
        // dos). Mismo contenido, centrado en cada ojo y más angosto.
        if (MRCardboardController.Activo)
        {
            BriefingPorOjo(night, slide);
            GUI.color = prevColor;
            return;
        }

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

    private void BriefingPorOjo(Gameplay.NightConfig night, float slide)
    {
        var prevM = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        float s = Screen.height / 1080f;
        string nombre = night != null
            ? (string.IsNullOrEmpty(night.displayName) ? night.name : night.displayName)
            : "LA NOCHE COMIENZA";
        string texto = night != null && !string.IsNullOrEmpty(night.briefing)
            ? night.briefing
            : "Sobreviví hasta el amanecer. El ritual no debe parar.";
        Rediseno.UI.OjosVisor.Centros(out var centroIzq, out var centroDer);
        for (int ojo = 0; ojo < 2; ojo++)
        {
            float cx = (ojo == 0 ? centroIzq : centroDer).x;
            float w = Screen.width * 0.36f, x = cx - w * 0.5f + slide * s;
            float y = Screen.height * 0.28f;
            GUI.Label(new Rect(x, y, w, 40f * s), "NOCHE",
                      T.Estilo(T.FBebas, Mathf.RoundToInt(30 * s), T.Red));
            y += 42f * s;
            GUI.Label(new Rect(x, y, w, 90f * s), nombre.ToUpperInvariant(),
                      T.Estilo(T.FBebas, Mathf.RoundToInt(72 * s), T.Cream));
            y += 96f * s;
            GUI.Label(new Rect(x, y, w, 240f * s), texto,
                      T.Estilo(T.FElite, Mathf.RoundToInt(26 * s), T.CreamDim, TextAnchor.UpperLeft, wrap: true));
        }
        GUI.matrix = prevM;
    }

    // Easing suave para la entrada (desaceleración).
    private static float EaseOut(float k) => 1f - (1f - k) * (1f - k);
}

