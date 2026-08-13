using UnityEngine;

// Panel de debug del GameDirector (fase, timers, jugadores — solo host). El
// contenido lo arma GameDirector.DebugSnapshot(); acá solo se dibuja.
public class DebugDirectorUI : MonoBehaviour
{
    private void OnGUI()
    {
        var director = Gameplay.GameDirector.Instance;
        var arbmos   = ArbmosDirector.Instance;

        string txt  = director != null ? director.DebugSnapshot()   : null;
        string atxt = arbmos   != null ? arbmos.DebugSnapshot()      : null;
        if (string.IsNullOrEmpty(txt) && string.IsNullOrEmpty(atxt)) return;

        var sa = Scanner.SafeArea.GuiRect;
        if (!string.IsNullOrEmpty(txt))
            GUI.Label(new Rect(sa.x + 10, sa.y + 300, 560, 110), txt,
                      DebugHudEstilos.Label(Color.white, 18));
        if (!string.IsNullOrEmpty(atxt))
            GUI.Label(new Rect(sa.x + 10, sa.y + 420, 620, 190), atxt,
                      DebugHudEstilos.Label(new Color(0.85f, 0.8f, 1f), 18));
    }
}
