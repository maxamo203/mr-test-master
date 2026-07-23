using UnityEngine;

// Panel de debug de la malla LiDAR/AR (chunks, vértices, triángulos, managers).
// El contenido lo arma LiDARScanner.DebugSnapshot(); acá solo se dibuja.
public class DebugLidarUI : MonoBehaviour
{
    private LiDARScanner _scanner;
    private float _proximaBusqueda;

    private void OnGUI()
    {
        if (_scanner == null && Time.unscaledTime >= _proximaBusqueda)
        {
            _scanner = FindFirstObjectByType<LiDARScanner>();
            _proximaBusqueda = Time.unscaledTime + 2f;
        }
        if (_scanner == null) return;

        string txt = _scanner.DebugSnapshot();
        if (string.IsNullOrEmpty(txt)) return;

        var sa = Scanner.SafeArea.GuiRect;
        GUI.Label(new Rect(sa.xMax - 600, sa.yMax - 200, 590, 190), txt,
                  DebugHudEstilos.Label(new Color(1f, 0.8f, 0.2f, 1f), 22));
    }
}
