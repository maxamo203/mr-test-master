using UnityEngine;

// Panel de debug del sistema de pilas (estado del spawner — solo host). El
// contenido lo arma BatterySpawnManager.DebugSnapshot(); acá solo se dibuja.
public class DebugBateriasUI : MonoBehaviour
{
    private void OnGUI()
    {
        var mgr = Bateries.BatterySpawnManager.Instance;
        if (mgr == null) return;

        string txt = mgr.DebugSnapshot();
        if (string.IsNullOrEmpty(txt)) return;

        var sa = Scanner.SafeArea.GuiRect;
        GUI.Label(new Rect(sa.x + 10, sa.y + 420, 560, 120), txt,
                  DebugHudEstilos.Label(Color.white, 18));
    }
}
