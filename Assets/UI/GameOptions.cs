using UnityEngine;

// Opciones de usuario persistentes (PlayerPrefs) aplicadas globalmente.
// Por ahora solo el volumen maestro; pensado para crecer (brillo, confort, etc.).
public static class GameOptions
{
    private const string KeyVolumen = "opt_volumen";

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

    // Aplica lo persistido al arrancar la app (cualquier escena).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Aplicar() => AudioListener.volume = Volumen;
}
