using UnityEngine;

// Panel de debug de la búsqueda de la imagen de referencia: estado (no la ve / a
// medias / siguiéndola), segundos buscando, trackables con su tracking state y datos
// de la referencia cargada. El contenido lo arma ARImageAnchor.DebugSnapshot(); acá
// solo se dibuja.
public class DebugImagenUI : MonoBehaviour
{
    private ARImageAnchor _anchor;

    private void OnGUI()
    {
        if (_anchor == null) _anchor = FindFirstObjectByType<ARImageAnchor>();
        if (_anchor == null) return;

        var sa = Scanner.SafeArea.GuiRect;
        GUI.Label(new Rect(sa.x + 10, sa.y + 800, 560, 220), _anchor.DebugSnapshot(),
                  DebugHudEstilos.Label(new Color(0.95f, 0.9f, 0.7f), 17));
    }
}
