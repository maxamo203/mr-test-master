using UnityEngine;

// Opciones de usuario persistentes (PlayerPrefs) aplicadas globalmente.
// Volumen maestro, anchor points y ajustes del chat de voz; pensado para crecer
// (brillo, confort, etc.).
public static class GameOptions
{
    private const string KeyVolumen     = "opt_volumen";
    private const string KeyVolMusica   = "opt_volumen_musica";
    private const string KeyVolEfectos  = "opt_volumen_efectos";
    private const string KeyPuntosAncla = "opt_puntos_ancla";
    private const string KeyEscaneoAutoBeta = "opt_escaneo_auto_beta";
    private const string KeyVozMic      = "opt_voz_mic";
    private const string KeyVozVolumen  = "opt_voz_volumen";
    private const string KeyVozSens     = "opt_voz_sensibilidad";
    private const string KeyCalidadAR   = "opt_calidad_ar";
    private const string KeyAvisoIos    = "opt_aviso_ios_visto";
    private const string KeyVhsMenus    = "opt_vhs_menus";
    private const string KeyAvisoVerDesc = "opt_aviso_version_desconocida_visto";

    // Volumen maestro 0..1 (AudioListener.volume). Persiste entre sesiones.
    public static float Volumen
    {
        get => PlayerPrefs.GetFloat(KeyVolumen, 1f);
        set
        {
            float v = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyVolumen, v);
            AudioListener.volume = v;
        }
    }

    // ── Canales de audio (aparte del maestro de arriba) ───────────────────────
    // Volumen maestro = AudioListener.volume (Volumen, arriba). Estos dos son
    // multiplicadores que aplica AudioManager al reproducir cada sonido, así el
    // jugador puede bajar la música sin perder las pistas sonoras del juego (que en
    // un juego de terror son información: de dónde viene el Sorken, la alerta del
    // libro). El chat de voz queda FUERA de los dos: tiene su propio VozVolumen,
    // para poder bajar el juego y seguir escuchando a los compañeros.

    // No hace falta avisarle a nadie al cambiarlos: AudioManager los lee en cada Update
    // (música y capa de tensión) y en cada disparo (efectos), así que mover el slider se
    // oye en el acto.

    // Volumen de la música y la capa ambiental de tensión (0..1).
    public static float VolumenMusica
    {
        get => PlayerPrefs.GetFloat(KeyVolMusica, 1f);
        set => PlayerPrefs.SetFloat(KeyVolMusica, Mathf.Clamp01(value));
    }

    // Volumen de todo lo que no es música ni voz: entidades, linterna, libro, UI (0..1).
    public static float VolumenEfectos
    {
        get => PlayerPrefs.GetFloat(KeyVolEfectos, 1f);
        set => PlayerPrefs.SetFloat(KeyVolEfectos, Mathf.Clamp01(value));
    }

    // ¿Este dispositivo coloca anchor points extra antes de empezar la partida?
    // Es una decisión POR DISPOSITIVO (el cuarto y el tracking son de cada uno),
    // no algo que el host imponga. Ver AnchorPointManager. Default: apagada.
    public static bool PuntosAncla
    {
        get => PlayerPrefs.GetInt(KeyPuntosAncla, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(KeyPuntosAncla, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // WIP — desconectado a propósito, ver AutoWallBuilder.cs (Assets/Scanner/):
    // el componente no está pegado a ningún GameObject de ninguna escena todavía
    // (no se probó en dispositivo), así que hoy el botón/toggle no harían nada si
    // se mostraran. Interruptor maestro: en false, ni el toggle de Opciones ni el
    // botón "PARED AUTO (BETA)" de la botonera del escáner se dibujan — el resto
    // del código (FSM, AutoWallBuilder, GameOptions.EscaneoAutoBeta) queda intacto
    // para retomarlo. Para reactivar: 1) cablear AutoWallBuilder a ScannerRoot en
    // ScannerScene.unity (ver comentario de ScannerSceneBootstrap), 2) probar la
    // detección real en un Android con ARCore, 3) poner esto en true.
    public const bool AutoWallScanBetaEnabled = false;

    // ¿Está habilitado el modo BETA de detección automática de paredes (ARCore
    // detecta planos verticales y el jugador confirma/descarta cada uno)? Opt-in
    // y apagado por defecto: es experimental, así que solo aparece el botón en la
    // botonera del escáner (ver ReticleController) si esto está activo. Ver
    // también AutoWallScanBetaEnabled arriba — mientras esté en false, esta
    // opción ni siquiera se muestra en Opciones.
    public static bool EscaneoAutoBeta
    {
        get => PlayerPrefs.GetInt(KeyEscaneoAutoBeta, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(KeyEscaneoAutoBeta, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // ── Chat de voz (sólo tiene efecto en sesiones multijugador) ──────────────
    // El volumen POR JUGADOR no se guarda acá a propósito: los clientId se reasignan
    // en cada sala, así que persistirlos haría que el ajuste caiga en otra persona.
    // Vive en memoria dentro de Voice.VoiceChatManager mientras dura la sesión.

    // ¿Transmite el micrófono? Apagarlo es el "mute" del propio jugador.
    public static bool VozMic
    {
        get => PlayerPrefs.GetInt(KeyVozMic, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(KeyVozMic, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // Volumen general de las voces (0..1), aparte del volumen maestro del juego.
    public static float VozVolumen
    {
        get => PlayerPrefs.GetFloat(KeyVozVolumen, 1f);
        set
        {
            PlayerPrefs.SetFloat(KeyVozVolumen, Mathf.Clamp01(value));
            Voice.VoiceChatManager.Instance?.RefrescarVolumenes();
        }
    }

    // Sensibilidad del micrófono (0..1): cuánta voz hace falta para que empiece a
    // transmitir. Más alta = se abre con menos ruido. Ver VoiceChatManager.Umbral.
    public static float VozSensibilidad
    {
        get => PlayerPrefs.GetFloat(KeyVozSens, 0.6f);
        set => PlayerPrefs.SetFloat(KeyVozSens, Mathf.Clamp01(value));
    }

    // Nivel de calidad de los subsistemas AR (0=Rendimiento, 1=Equilibrado, 2=Calidad).
    // Ver ARQuality: es la opción que más pesa en batería y temperatura. Default
    // Equilibrado — el máximo (lo que hacía el proyecto antes) drena demasiado.
    public static int CalidadAR
    {
        get => PlayerPrefs.GetInt(KeyCalidadAR, 1);
        set
        {
            PlayerPrefs.SetInt(KeyCalidadAR, Mathf.Clamp(value, 0, 2));
            PlayerPrefs.Save();
        }
    }

    // US-1.5: ¿ya se le mostró al jugador el aviso de que iOS tiene mucho menos
    // drift de tracking AR que Android (ver "Riesgos Identificados" en la
    // documentación)? Se muestra una sola vez, en el primer Android detectado.
    public static bool AvisoIosVisto
    {
        get => PlayerPrefs.GetInt(KeyAvisoIos, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(KeyAvisoIos, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // US-11.1: ¿el filtro VHS / cámara antigua también se dibuja sobre los MENÚS?
    // En partida el filtro es parte de la atmósfera y no se apaga acá; sobre los menús
    // es cuestión de gusto (y de legibilidad), así que el jugador lo decide.
    // Ver VHSOverlayUI. Default: encendido.
    public static bool VhsEnMenus
    {
        get => PlayerPrefs.GetInt(KeyVhsMenus, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(KeyVhsMenus, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // US-1.1: ¿ya se le mostró al jugador el aviso de que no se pudo determinar la
    // versión de su sistema operativo (DeviceCompatibility.CompatResult.Unknown)?
    // A diferencia del bloqueo por versión no soportada (que no se persiste, se
    // re-evalúa siempre), este es un aviso no bloqueante y se muestra una sola vez.
    public static bool AvisoVersionDesconocidaVisto
    {
        get => PlayerPrefs.GetInt(KeyAvisoVerDesc, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(KeyAvisoVerDesc, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // Aplica lo persistido al arrancar la app (cualquier escena).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Aplicar() => AudioListener.volume = Volumen;
}
