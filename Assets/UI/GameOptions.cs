using UnityEngine;

// Opciones de usuario persistentes (PlayerPrefs) aplicadas globalmente.
// Volumen maestro, anchor points y ajustes del chat de voz; pensado para crecer
// (brillo, confort, etc.).
public static class GameOptions
{
    private const string KeyVolumen     = "opt_volumen";
    private const string KeyPuntosAncla = "opt_puntos_ancla";
    private const string KeyVozMic      = "opt_voz_mic";
    private const string KeyVozVolumen  = "opt_voz_volumen";
    private const string KeyVozSens     = "opt_voz_sensibilidad";

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

    // Aplica lo persistido al arrancar la app (cualquier escena).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Aplicar() => AudioListener.volume = Volumen;
}
