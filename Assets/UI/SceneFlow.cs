using UnityEngine;
using UnityEngine.SceneManagement;

// Navegación entre las escenas del juego con limpieza de singletons cross-escena,
// el mismo criterio que usaba la vieja SceneNavUI: al cambiar de escena se
// destruyen NetworkManager, EntityRegistry y WorldOrigin para que la escena
// destino arranque limpia (sin conexión a medias, anchor viejo ni entidades).
// MscnReceiver se deja vivo (maneja el "abrir con" de archivos .mscn).
public static class SceneFlow
{
    public const string EscenaMenu    = "NightMenuScene";
    public const string EscenaJuego   = "SampleScene";
    public const string EscenaEscaner = "ScannerScene";

    public static void GoTo(string escena)
    {
        if (NetworkManager.Instance != null) Object.Destroy(NetworkManager.Instance.gameObject);
        if (EntityRegistry.Instance != null) Object.Destroy(EntityRegistry.Instance.gameObject);
        if (WorldOrigin.Instance    != null) Object.Destroy(WorldOrigin.Instance.gameObject);

        SceneManager.LoadScene(escena);
    }
}
