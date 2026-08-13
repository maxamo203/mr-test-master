using System.Collections.Generic;
using UnityEngine;

// Íconos "pseudo-3D" (isométricos) dibujados por código en texturas cacheadas, sin
// depender de assets ni de fuentes con emoji. Cada ícono se rasteriza una vez a una
// Texture2D y se dibuja con GUI.DrawTexture. Se usan en la botonera del escáner para
// mostrar visualmente qué hace cada herramienta.
//
// Las formas (cajas isométricas de 3 caras con sombreado, pin, reticle) se definieron
// y verificaron en un preview aparte; acá se replica ese rasterizado.
public static class MortuoriumIcons
{
    public enum Icon { Pared, Cubo, Puerta, Marca, Piso, RecalFijo, RecalMover, Ancla, Pentagrama }

    private const int N = 128;
    private static readonly Dictionary<Icon, Texture2D> _cache = new();

    // Dibuja el ícono ajustado (ScaleToFit) dentro de r. alpha atenúa (para estados
    // deshabilitados) sin cambiar el tono.
    public static void Draw(Rect r, Icon icon, float alpha = 1f)
    {
        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(r, Get(icon), ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = prev;
    }

    private static Texture2D Get(Icon icon)
    {
        if (_cache.TryGetValue(icon, out var t) && t != null) return t;
        t = Build(icon);
        _cache[icon] = t;
        return t;
    }

    // ── Rasterizado ───────────────────────────────────────────────────────
    private static Color32[] _buf;

    private static void Px(int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= N || y >= N) return;
        _buf[(N - 1 - y) * N + x] = c;   // y hacia abajo -> fila de textura invertida
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 p) =>
        (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

    private static void Tri(Vector2 a, Vector2 b, Vector2 c, Color32 col)
    {
        int minx = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x)), 0, N - 1);
        int maxx = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x)), 0, N - 1);
        int miny = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y)), 0, N - 1);
        int maxy = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y)), 0, N - 1);
        for (int y = miny; y <= maxy; y++)
            for (int x = minx; x <= maxx; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = Edge(b, c, p), w1 = Edge(c, a, p), w2 = Edge(a, b, p);
                if ((w0 >= 0 && w1 >= 0 && w2 >= 0) || (w0 <= 0 && w1 <= 0 && w2 <= 0))
                    Px(x, y, col);
            }
    }

    // Polígono convexo (fan) en coords unitarias [0,1].
    private static void Poly(Color32 col, params Vector2[] u)
    {
        for (int i = 1; i < u.Length - 1; i++)
            Tri(u[0] * N, u[i] * N, u[i + 1] * N, col);
    }

    private static void Disc(float ux, float uy, float ur, Color32 col)
    {
        float cx = ux * N, cy = uy * N, r = ur * N;
        int y0 = Mathf.Clamp(Mathf.FloorToInt(cy - r), 0, N - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(cy + r), 0, N - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(cx - r), 0, N - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(cx + r), 0, N - 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy <= r * r) Px(x, y, col);
            }
    }

    private static void Ring(float ux, float uy, float uro, float uri, Color32 col)
    {
        float cx = ux * N, cy = uy * N, ro = uro * N, ri = uri * N;
        int y0 = Mathf.Clamp(Mathf.FloorToInt(cy - ro), 0, N - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(cy + ro), 0, N - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(cx - ro), 0, N - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(cx + ro), 0, N - 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d2 = dx * dx + dy * dy;
                if (d2 <= ro * ro && d2 >= ri * ri) Px(x, y, col);
            }
    }

    private static void RectU(float ux, float uy, float uw, float uh, Color32 col)
    {
        int x0 = Mathf.RoundToInt(ux * N), y0 = Mathf.RoundToInt(uy * N);
        int w = Mathf.RoundToInt(uw * N), h = Mathf.RoundToInt(uh * N);
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++) Px(x0 + i, y0 + j, col);
    }

    // Segmento de línea de ancho fijo (quad), en coords unitarias [0,1].
    private static void Line(Vector2 a, Vector2 b, float width, Color32 col)
    {
        Vector2 dir = (b - a).normalized;
        Vector2 n = new Vector2(-dir.y, dir.x) * (width * 0.5f);
        Poly(col, a + n, b + n, b - n, a - n);
    }

    // Pentagrama real: polígono estrellado {5/2} — 5 vértices de un pentágono
    // regular, unidos salteando uno (0-2-4-1-3-0), que es lo que genera las
    // líneas cruzadas típicas del símbolo (no una silueta de estrella "inflada").
    // Invertido (una punta hacia abajo) = el sello "satánico"/Baphomet, como ⛧.
    // Coords unitarias [0,1].
    private static void Pentagram(float cx, float cy, float r, float strokeW, Color32 col)
    {
        var v = new Vector2[5];
        for (int i = 0; i < 5; i++)
        {
            float ang = Mathf.PI / 2f + i * 2f * Mathf.PI / 5f;   // punta hacia abajo
            v[i] = new Vector2(cx + Mathf.Cos(ang) * r, cy + Mathf.Sin(ang) * r);
        }
        int[] order = { 0, 2, 4, 1, 3, 0 };
        for (int i = 0; i < order.Length - 1; i++)
            Line(v[order[i]], v[order[i + 1]], strokeW, col);
    }

    private static Color32 Shade(Color32 c, float f) => new Color32(
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * f), 0, 255),
        (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * f), 0, 255), 255);

    // Caja isométrica: 3 caras (techo claro, derecha media, izquierda oscura).
    // cb = centro-abajo; w/d/h en fracciones. Proyección iso simple.
    private static void IsoBox(float cbx, float cby, float w, float d, float h, Color32 baseC)
    {
        const float kx = 0.60f, ky = 0.33f;
        Vector2 P(float x, float z, float y) => new Vector2(cbx + (x - z) * kx, cby + (x + z) * ky - y);

        var A2 = P(-w, -d, h); var B2 = P(w, -d, h); var C2 = P(w, d, h); var D2 = P(-w, d, h);
        var B = P(w, -d, 0); var C = P(w, d, 0); var D = P(-w, d, 0);

        Poly(Shade(baseC, 1.18f), A2, B2, C2, D2);   // techo
        Poly(Shade(baseC, 0.80f), B, C, C2, B2);      // cara derecha
        Poly(Shade(baseC, 0.55f), C, D, D2, C2);      // cara izquierda
    }

    // ── Íconos ────────────────────────────────────────────────────────────
    private static Texture2D Build(Icon icon)
    {
        _buf = new Color32[N * N];   // transparente

        Color32 cream = MortuoriumTheme.Cream, green = MortuoriumTheme.Green;
        Color32 red = MortuoriumTheme.Red, blue = MortuoriumTheme.Blue, tan = MortuoriumTheme.Tan;

        switch (icon)
        {
            case Icon.Pared:
            {
                IsoBox(0.5f, 0.64f, 0.16f, 0.34f, 0.40f, cream);
                // dos surcos (líneas de ladrillo) sobre las caras.
                const float kx = 0.60f, ky = 0.33f, w = 0.16f, d = 0.34f;
                Vector2 P(float x, float z, float y) => new Vector2(0.5f + (x - z) * kx, 0.64f + (x + z) * ky - y);
                Poly(Shade(cream, 0.62f), P(w, -d, 0.20f), P(w, d, 0.20f), P(w, d, 0.235f), P(w, -d, 0.235f));
                Poly(Shade(cream, 0.42f), P(w, d, 0.20f), P(-w, d, 0.20f), P(-w, d, 0.235f), P(w, d, 0.235f));
                break;
            }
            case Icon.Cubo:
                IsoBox(0.5f, 0.70f, 0.24f, 0.24f, 0.24f, green);
                break;
            case Icon.Puerta:
            {
                IsoBox(0.5f, 0.66f, 0.14f, 0.30f, 0.40f, red);
                const float kx = 0.60f, ky = 0.33f, w = 0.14f, d = 0.30f, h = 0.40f;
                Vector2 P(float x, float z, float y) => new Vector2(0.5f + (x - z) * kx, 0.66f + (x + z) * ky - y);
                // hueco de puerta en la cara derecha (x=+w), centrado, desde el piso.
                Poly(Shade(red, 0.30f), P(w, -0.5f * d, 0), P(w, 0.5f * d, 0),
                                        P(w, 0.5f * d, 0.72f * h), P(w, -0.5f * d, 0.72f * h));
                break;
            }
            case Icon.Marca:
                Poly(Shade(blue, 0.7f), new Vector2(0.5f, 0.80f), new Vector2(0.33f, 0.46f), new Vector2(0.67f, 0.46f));
                Disc(0.5f, 0.37f, 0.17f, blue);
                Disc(0.445f, 0.315f, 0.055f, Shade(blue, 1.35f));   // brillo
                Disc(0.5f, 0.37f, 0.055f, Shade(blue, 0.35f));      // hueco
                break;
            case Icon.Piso:
            {
                IsoBox(0.5f, 0.58f, 0.34f, 0.34f, 0.045f, tan);
                const float kx = 0.60f, ky = 0.33f, w = 0.34f, h = 0.045f;
                Vector2 P(float x, float z, float y) => new Vector2(0.5f + (x - z) * kx, 0.58f + (x + z) * ky - y);
                Poly(Shade(tan, 0.6f), P(-0.02f, -w, h), P(0.02f, -w, h), P(0.02f, w, h), P(-0.02f, w, h));
                Poly(Shade(tan, 0.6f), P(-w, -0.02f, h), P(w, -0.02f, h), P(w, 0.02f, h), P(-w, 0.02f, h));
                break;
            }
            case Icon.RecalFijo:
                Ring(0.5f, 0.5f, 0.30f, 0.20f, tan);
                RectU(0.5f - 0.045f, 0.10f, 0.09f, 0.16f, tan);
                RectU(0.5f - 0.045f, 0.74f, 0.09f, 0.16f, tan);
                RectU(0.10f, 0.5f - 0.045f, 0.16f, 0.09f, tan);
                RectU(0.74f, 0.5f - 0.045f, 0.16f, 0.09f, tan);
                Disc(0.5f, 0.5f, 0.07f, tan);
                break;
            // Baliza: plato isométrico en el piso + poste + esfera emisora con onda.
            // Se distingue de Marca (pin azul) por el poste y el anillo de señal.
            case Icon.Ancla:
                Ring(0.5f, 0.28f, 0.30f, 0.255f, Shade(green, 0.45f));   // onda
                IsoBox(0.5f, 0.80f, 0.20f, 0.20f, 0.045f, Shade(green, 0.75f));
                RectU(0.5f - 0.035f, 0.32f, 0.07f, 0.40f, Shade(green, 0.9f));
                Disc(0.5f, 0.27f, 0.125f, green);
                Disc(0.455f, 0.228f, 0.042f, Shade(green, 1.4f));        // brillo
                break;
            case Icon.RecalMover:
                Ring(0.5f, 0.5f, 0.24f, 0.155f, tan);
                Poly(tan, new Vector2(0.5f, 0.04f), new Vector2(0.42f, 0.17f), new Vector2(0.58f, 0.17f));
                Poly(tan, new Vector2(0.5f, 0.96f), new Vector2(0.42f, 0.83f), new Vector2(0.58f, 0.83f));
                Poly(tan, new Vector2(0.04f, 0.5f), new Vector2(0.17f, 0.42f), new Vector2(0.17f, 0.58f));
                Poly(tan, new Vector2(0.96f, 0.5f), new Vector2(0.83f, 0.42f), new Vector2(0.83f, 0.58f));
                Disc(0.5f, 0.5f, 0.05f, tan);
                break;
            // Sello de brujería: pentagrama invertido (una punta hacia abajo) dentro
            // de un círculo, como ⛧.
            case Icon.Pentagrama:
                Ring(0.5f, 0.5f, 0.42f, 0.395f, Color.white);
                Pentagram(0.5f, 0.5f, 0.34f, 0.028f, Color.white);
                break;
        }

        var tex = new Texture2D(N, N, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            hideFlags  = HideFlags.HideAndDontSave,
        };
        tex.SetPixels32(_buf);
        tex.Apply();
        _buf = null;
        return tex;
    }
}
