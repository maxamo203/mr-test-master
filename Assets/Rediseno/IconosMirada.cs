using UnityEngine;

namespace Rediseno
{
    // Texturas del HUD de mirada, generadas por código una sola vez (sin assets, igual
    // criterio que MortuoriumIcons). Blancas con alfa: el color lo pone GUI.color.
    public static class IconosMirada
    {
        private const int N = 64;

        private static Texture2D _punto, _recoger, _linterna;

        // Círculo lleno de borde suave: la mira y los puntos del anillo.
        public static Texture2D Punto => _punto != null ? _punto : (_punto = Generar(PuntoAlfa));

        // Flecha hacia abajo que entra en una bandeja: "agarrar / guardar".
        public static Texture2D Recoger => _recoger != null ? _recoger : (_recoger = Generar(RecogerAlfa));

        // Símbolo de encendido (arco abierto arriba + palito): prender o apagar según color.
        public static Texture2D Linterna => _linterna != null ? _linterna : (_linterna = Generar(LinternaAlfa));

        private static float PuntoAlfa(Vector2 p) => Borde(0.92f - p.magnitude);

        private static float RecogerAlfa(Vector2 p)
        {
            const float t = 0.11f;   // medio grosor del trazo
            float d = Segmento(p, new Vector2(0f, 0.78f), new Vector2(0f, -0.12f));        // asta
            d = Mathf.Min(d, Segmento(p, new Vector2(-0.34f, 0.18f), new Vector2(0f, -0.16f)));  // punta izq
            d = Mathf.Min(d, Segmento(p, new Vector2(0.34f, 0.18f), new Vector2(0f, -0.16f)));   // punta der
            d = Mathf.Min(d, Segmento(p, new Vector2(-0.72f, -0.30f), new Vector2(-0.72f, -0.74f))); // bandeja
            d = Mathf.Min(d, Segmento(p, new Vector2(-0.72f, -0.74f), new Vector2(0.72f, -0.74f)));
            d = Mathf.Min(d, Segmento(p, new Vector2(0.72f, -0.74f), new Vector2(0.72f, -0.30f)));
            return Borde(t - d);
        }

        private static float LinternaAlfa(Vector2 p)
        {
            const float t = 0.11f;
            // Arco de radio 0.62 con una abertura de ±40° arriba (y hacia arriba = +y).
            float r = p.magnitude;
            float ang = Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg;   // 0° = arriba
            float dArco;
            if (Mathf.Abs(ang) >= 40f) dArco = Mathf.Abs(r - 0.62f);
            else
            {
                // Fuera de la abertura: distancia al extremo más cercano del arco.
                float a = Mathf.Sign(ang == 0f ? 1f : ang) * 40f * Mathf.Deg2Rad;
                var extremo = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 0.62f;
                dArco = Vector2.Distance(p, extremo);
            }
            float dPalo = Segmento(p, new Vector2(0f, 0.82f), new Vector2(0f, 0.12f));
            return Borde(t - Mathf.Min(dArco, dPalo));
        }

        // Distancia de p al segmento ab.
        private static float Segmento(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
            return Vector2.Distance(p, a + ab * h);
        }

        // Antialias de ~1,5 px: d en unidades normalizadas (1 = medio ancho de la textura).
        private static float Borde(float d) => Mathf.Clamp01(d * N * 0.5f / 1.5f + 0.5f);

        private static Texture2D Generar(System.Func<Vector2, float> alfa)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    // Fila 0 de la textura = abajo de la imagen: +y normalizado = arriba.
                    var p = new Vector2((x + 0.5f) / N * 2f - 1f, (y + 0.5f) / N * 2f - 1f);
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alfa(p)) * 255f);
                    px[y * N + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
