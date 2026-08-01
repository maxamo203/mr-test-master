using UnityEngine;

// Panel de debug de los anchor points: cuántos hay, cuál está activa, tracking state
// y distancia de cada una, y de cuánto fue la última corrección. El contenido lo arma
// AnchorPointManager.DebugSnapshot(); acá solo se dibuja.
public class DebugAnclasUI : MonoBehaviour
{
    private void OnGUI()
    {
        var mgr = AnchorPointManager.Instance;
        if (mgr == null) return;

        var sa = Scanner.SafeArea.GuiRect;
        GUI.Label(new Rect(sa.x + 10, sa.y + 570, 560, 220), mgr.DebugSnapshot(),
                  DebugHudEstilos.Label(new Color(0.8f, 0.95f, 0.85f), 17));
    }
}
