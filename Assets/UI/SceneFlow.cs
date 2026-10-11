using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

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
        // Los directores de gameplay son DontDestroyOnLoad y el estado de muerte /
        // resultado es estático: no alcanza con destruir los tres singletons de abajo.
        // Ver Gameplay.NightTransition.TeardownSesion.
        Gameplay.NightTransition.TeardownSesion();

        // Antes de destruir nada: la sesión AR todavía corre y sus managers están vivos.
        ResetearSesionAR();

        if (NetworkManager.Instance != null)
        {
            // Destroy se difiere hasta el final del frame. Liberar ahora el socket evita
            // que la escena destino intente bindear el mismo puerto mientras sigue ocupado.
            NetworkManager.Instance.Shutdown();
            Object.Destroy(NetworkManager.Instance.gameObject);
        }
        if (EntityRegistry.Instance != null) Object.Destroy(EntityRegistry.Instance.gameObject);
        if (WorldOrigin.Instance    != null) Object.Destroy(WorldOrigin.Instance.gameObject);

        SceneManager.LoadScene(escena);
    }

    // La sesión ARKit/ARCore es UNA SOLA para toda la vida de la app: al descargar la
    // escena, ARSession sólo la PAUSA (SubsystemLifecycleManager.OnDisable → Stop), no la
    // resetea ni la destruye. Todo lo que acumuló —anclas, planos, imágenes, malla— sigue
    // vivo del lado nativo y se retoma en la próxima escena AR. Encima, las ARAnchor de la
    // escena que se va intentan quitarse en OnDisable, pero si el ARAnchorManager se
    // deshabilitó antes (el orden de destrucción al descargar no está garantizado), la
    // remoción falla y el ancla nativa queda huérfana en la sesión.
    //
    // Resultado: cada partida sumaba su imagen, sus anchor points y todos los planos del
    // cuarto a la sesión de la anterior, y en sesiones largas (5-6 partidas) la memoria
    // nativa de ARKit crecía hasta que iOS mataba la app. Resetear al SALIR (no al entrar:
    // ahí competiría con la búsqueda de la imagen) deja la sesión limpia sin afectar a
    // nadie — la escena que se va ya no depende de ella.
    //
    // No aplica al reinicio de noche (NightTransition.Reiniciar), que a propósito conserva
    // la sesión para no perder los anchor points: ese camino no pasa por acá.
    private static void ResetearSesionAR()
    {
        var sesion = Object.FindAnyObjectByType<ARSession>();
        if (sesion == null || !sesion.enabled) return;
        try { sesion.Reset(); }
        catch (System.Exception e) { Debug.LogWarning($"[SceneFlow] Reset de sesión AR falló: {e.Message}"); }
    }
}
