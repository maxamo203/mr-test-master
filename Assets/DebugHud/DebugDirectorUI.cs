using UnityEngine;

// Panel de debug del GameDirector (fase, timers, jugadores — solo host). El
// contenido lo arma GameDirector.DebugSnapshot(); acá solo se dibuja.
public class DebugDirectorUI : MonoBehaviour
{
    private void OnGUI()
    {
        var director = Gameplay.GameDirector.Instance;
        if (director == null) return;

        string txt = director.DebugSnapshot();
        if (string.IsNullOrEmpty(txt)) return;

        var sa = Scanner.SafeArea.GuiRect;
        GUI.Label(new Rect(sa.x + 10, sa.y + 300, 560, 110), txt,
                  DebugHudEstilos.Label(Color.white, 18));
    }
}
