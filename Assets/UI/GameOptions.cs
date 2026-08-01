using UnityEngine;

// Opciones de usuario persistentes (PlayerPrefs) aplicadas globalmente.
// Por ahora solo el volumen maestro; pensado para crecer (brillo, confort, etc.).
public static class GameOptions
{
    private const string KeyVolumen     = "opt_volumen";
    private const string KeyPuntosAncla = "opt_puntos_ancla";

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

    // Aplica lo persistido al arrancar la app (cualquier escena).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Aplicar() => AudioListener.volume = Volumen;
}
