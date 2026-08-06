using UnityEngine;

// Panel de consumo: batería, estado térmico, CPU vs GPU y qué subsistemas AR están
// activos. Los datos los junta PowerProbe (mismo GameObject); acá solo se dibuja,
// siguiendo el patrón del resto del DebugHud.
//
// El gráfico son dos series superpuestas de los últimos ~120 frames: CPU (cian) y
// GPU (magenta). Sirve para distinguir de un vistazo quién manda:
//   - barra GPU alta y CPU baja  → estás rindiendo de más (resolución / fill rate)
//   - barra CPU alta y GPU baja  → el cuello está en el hilo principal
//   - las dos bajas pero la batería se va y el térmico sube → son los subsistemas
//     de ARKit (LiDAR meshing / depth), que corren fuera del frame de Unity
public class DebugEnergiaUI : MonoBehaviour
{
    private PowerProbe _probe;
    private static Texture2D _blanco;
    // Reutilizado para medir/dibujar el texto (CalcHeight pide un GUIContent).
    private readonly GUIContent _contenido = new();

    private void OnGUI()
    {
        if (_probe == null)
        {
            _probe = GetComponent<PowerProbe>();
            if (_probe == null) return;
        }

        // Anclado ARRIBA y con el alto calculado del texto real: con una caja de alto
        // fijo las últimas líneas (los subsistemas AR, que son las importantes) se
        // cortaban fuera de pantalla.
        var sa = Scanner.SafeArea.GuiRect;
        float w = Mathf.Min(sa.width * 0.42f, 620f);

        var estilo = DebugHudEstilos.Label(PowerProbe.ColorTermico(_probe.Termico),
                                           Mathf.RoundToInt(Screen.height * 0.021f));

        _contenido.text = _probe.Texto;
        float hTexto = estilo.CalcHeight(_contenido, w);
        const float HGrafico = 96f;

        var caja = new Rect(sa.x + sa.width * 0.30f, sa.y + 20f, w, hTexto + HGrafico);

        GUI.Label(new Rect(caja.x, caja.y, w, hTexto), _contenido, estilo);
        DibujarGrafico(new Rect(caja.x + 10f, caja.y + hTexto + 4f, w - 20f, HGrafico - 12f));
    }

    // Gráfico de barras del ring buffer, dibujado de más viejo (izq) a más nuevo (der).
    private void DibujarGrafico(Rect r)
    {
        var p = _probe;
        float escala = Mathf.Max(1f, p.EscalaMs);
        int   n      = PowerProbe.Muestras;
        float colW   = r.width / n;

        Rect(r, new Color(0f, 0f, 0f, 0.55f));

        // Línea de 16.7 ms (60 fps): el presupuesto por frame.
        float y60 = r.yMax - (16.7f / escala) * r.height;
        if (y60 > r.y) Rect(new Rect(r.x, y60, r.width, 1f), new Color(1f, 1f, 1f, 0.35f));

        for (int i = 0; i < n; i++)
        {
            // El índice de escritura es el más viejo del ring.
            int   src = (p.HistIdx + i) % n;
            float x   = r.x + i * colW;

            float cpu = Mathf.Clamp01(p.HistCpu[src] / escala) * r.height;
            Rect(new Rect(x, r.yMax - cpu, Mathf.Max(1f, colW - 0.5f), cpu),
                 new Color(0.3f, 0.85f, 1f, 0.75f));

            if (p.HayGpu)
            {
                float gpu = Mathf.Clamp01(p.HistGpu[src] / escala) * r.height;
                Rect(new Rect(x, r.yMax - gpu, Mathf.Max(1f, colW * 0.5f), gpu),
                     new Color(1f, 0.35f, 0.9f, 0.8f));
            }
        }

        var chico = DebugHudEstilos.Label(Color.white, Mathf.RoundToInt(Screen.height * 0.016f));
        GUI.Label(new Rect(r.x + 2f, r.y - 2f, r.width, 20f),
                  $"CPU (cian) / GPU (magenta) — techo {escala:0} ms, línea = 16.7 ms", chico);
    }

    private static void Rect(Rect r, Color c)
    {
        if (_blanco == null)
        {
            _blanco = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _blanco.SetPixel(0, 0, Color.white);
            _blanco.Apply();
            _blanco.hideFlags = HideFlags.HideAndDontSave;
        }
        var prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, _blanco);
        GUI.color = prev;
    }
}
