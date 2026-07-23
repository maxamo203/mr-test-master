using UnityEngine;

// Reporte del benchmark de tracking de imagen (ARTrackingBenchmark): jitter,
// drift, % de frames con tracking. El benchmark corre en su componente de la
// escena; acá solo se muestra su reporte.
public class DebugBenchmarkUI : MonoBehaviour
{
    private ARTrackingBenchmark _bench;
    private float _proximaBusqueda;

    private void OnGUI()
    {
        if (_bench == null && Time.unscaledTime >= _proximaBusqueda)
        {
            _bench = FindFirstObjectByType<ARTrackingBenchmark>();
            _proximaBusqueda = Time.unscaledTime + 2f;
        }
        if (_bench == null) return;

        string txt = _bench.Report;
        if (string.IsNullOrEmpty(txt)) return;

        var sa = Scanner.SafeArea.GuiRect;
        float w = sa.width * 0.6f;
        float h = sa.height * 0.45f;
        GUI.Label(new Rect(sa.x + 20, sa.y + 20, w, h), txt,
                  DebugHudEstilos.Label(Color.white, Mathf.RoundToInt(Screen.height * 0.025f)));
    }
}
