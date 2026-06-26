using UnityEngine;
using UnityEngine.SceneManagement;

// Botón para saltar entre la escena de multijugador (SampleScene) y la de escaneo
// (ScannerScene) MIENTRAS NO se está en una partida. Detecta solo la escena actual y
// ofrece ir a la otra. Pegalo en un GameObject en AMBAS escenas.
//
// Al navegar limpia los singletons DontDestroyOnLoad (NetworkManager, EntityRegistry,
// WorldOrigin) para que la escena destino arranque en limpio y no arrastre estado
// (conexión a medias, anchor viejo, entidades). MscnReceiver se deja (es inofensivo y
// maneja el "abrir con" de archivos .mscn).
//
// IMPORTANTE: ambas escenas deben estar en Build Settings > Scenes In Build para que
// SceneManager.LoadScene por nombre funcione.
public class SceneNavUI : MonoBehaviour
{
    private const string SceneScanner = "ScannerScene";
    private const string SceneMultiplayer = "SampleScene";

    private void OnGUI()
    {
        // En partida no se navega.
        var net = NetworkManager.Instance;
        if (net != null && net.InSession) return;

        string current = SceneManager.GetActiveScene().name;
        string target, label;
        if (current == SceneScanner)
        {
            target = SceneMultiplayer;
            label  = "Ir a Multijugador";
        }
        else
        {
            target = SceneScanner;
            label  = "Ir a Escanear (ScannerScene)";
        }

        var area = new Rect(10, Screen.height - 70, 340, 60);
        GUILayout.BeginArea(area);
        if (GUILayout.Button(label, GUILayout.Height(50)))
            GoTo(target);
        GUILayout.EndArea();
    }

    private void GoTo(string scene)
    {
        // Limpiar singletons cross-scene para arrancar la otra escena sin estado viejo.
        if (NetworkManager.Instance != null) Destroy(NetworkManager.Instance.gameObject);
        if (EntityRegistry.Instance != null) Destroy(EntityRegistry.Instance.gameObject);
        if (WorldOrigin.Instance    != null) Destroy(WorldOrigin.Instance.gameObject);

        SceneManager.LoadScene(scene);
    }
}
