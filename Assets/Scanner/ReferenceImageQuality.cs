using UnityEngine;

namespace Scanner
{
    // Chequeo de calidad del recorte que se va a usar como imagen de referencia.
    // ARKit/ARCore trackean por puntos característicos (esquinas, bordes con
    // contraste): un recorte liso, o repetitivo, o simétrico se detecta mal — y una
    // imagen SIMÉTRICA se puede detectar dada vuelta (180°), que es una de las
    // causas de "el mapa aparece rotado respecto a como lo escaneé".
    //
    // Se corre UNA vez al capturar (y nunca por frame): reduce el recorte a
    // gris de <= 96 px por lado, así que cuesta un par de ms. Es puro C# sobre un
    // arreglo de floats para poder testearlo sin texturas (ReferenceImageQualityTests).
    public static class ReferenceImageQuality
    {
        public const int LadoMax = 96;

        // Umbrales calibrados a ojo sobre recortes típicos (posters, cajas, libros):
        //   - Detalle: fracción de píxeles con gradiente fuerte. < 4% = liso.
        //   - Cobertura: fracción de celdas (grilla 4x4) con detalle. < 50% = el
        //     detalle está concentrado en un rincón y el resto es fondo liso.
        //   - Simetría: correlación con la misma imagen rotada 180° (o 90° si es
        //     casi cuadrada). > 0.75 = ambigua.
        public const float MinDetalle   = 0.04f;
        public const float MinCobertura = 0.50f;
        public const float MaxSimetria  = 0.75f;
        private const float UmbralGradiente = 0.10f;   // en escala 0..1 de luminancia

        public readonly struct Resultado
        {
            public readonly float Detalle;     // 0..1, fracción de píxeles con borde
            public readonly float Cobertura;   // 0..1, fracción de celdas con detalle
            public readonly float Simetria;    // -1..1, correlación con la rotación
            public readonly bool  CasiCuadrada;

            public Resultado(float detalle, float cobertura, float simetria, bool casiCuadrada)
            {
                Detalle = detalle; Cobertura = cobertura; Simetria = simetria; CasiCuadrada = casiCuadrada;
            }

            public bool PocoDetalle  => Detalle < MinDetalle;
            public bool MalRepartida => !PocoDetalle && Cobertura < MinCobertura;
            public bool Simetrica    => Simetria > MaxSimetria;
            public bool Aceptable    => !PocoDetalle && !MalRepartida && !Simetrica;

            // Aviso corto para la UI (null si la imagen está bien). Un solo motivo, el
            // más grave primero: sin detalle no hay nada que trackear.
            public string Aviso
            {
                get
                {
                    if (PocoDetalle)  return "Zona con poco detalle: el tracking va a fallar. Buscá algo con bordes y contraste.";
                    if (MalRepartida) return "El detalle está en un solo rincón: encuadrá para que ocupe todo el recuadro.";
                    if (Simetrica)    return CasiCuadrada
                        ? "Imagen casi simétrica: puede detectarse girada. Elegí una zona asimétrica."
                        : "Imagen simétrica al darla vuelta: puede detectarse rotada 180°. Elegí una zona asimétrica.";
                    return null;
                }
            }
        }

        // Entrada cómoda desde una Texture2D legible (RGBA32 o cualquier formato que
        // soporte GetPixels32).
        public static Resultado Analizar(Texture2D tex)
        {
            if (tex == null) return new Resultado(0f, 0f, 0f, false);
            var px = tex.GetPixels32();
            return Analizar(px, tex.width, tex.height);
        }

        public static Resultado Analizar(Color32[] px, int w, int h)
        {
            Reducir(px, w, h, out var gris, out int gw, out int gh);
            return Analizar(gris, gw, gh);
        }

        // Núcleo: luminancia 0..1, fila-mayor, origen abajo-izquierda (como Unity).
        public static Resultado Analizar(float[] gris, int w, int h)
        {
            if (gris == null || w < 3 || h < 3) return new Resultado(0f, 0f, 0f, false);

            // ── Detalle + cobertura (gradiente por diferencias centrales) ──
            const int Celdas = 4;
            int   bordes = 0;
            var   porCelda = new int[Celdas * Celdas];
            for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                float gx = gris[y * w + x + 1] - gris[y * w + x - 1];
                float gy = gris[(y + 1) * w + x] - gris[(y - 1) * w + x];
                if (gx * gx + gy * gy < UmbralGradiente * UmbralGradiente) continue;
                bordes++;
                porCelda[(y * Celdas / h) * Celdas + (x * Celdas / w)]++;
            }
            int   interior = (w - 2) * (h - 2);
            float detalle  = interior > 0 ? bordes / (float)interior : 0f;

            // Una celda "tiene detalle" si supera la mitad del umbral global de detalle
            // sobre su propia área: así una foto con detalle parejo pero tenue no se
            // castiga dos veces.
            int   conDetalle = 0;
            float areaCelda  = interior / (float)(Celdas * Celdas);
            for (int i = 0; i < porCelda.Length; i++)
                if (porCelda[i] / areaCelda >= MinDetalle * 0.5f) conDetalle++;
            float cobertura = conDetalle / (float)(Celdas * Celdas);

            // ── Simetría: correlación con la imagen rotada 180° ──
            float sim = Correlacion180(gris, w, h);

            // Si es casi cuadrada, también puede confundirse con un giro de 90°.
            bool casiCuadrada = Mathf.Abs(w - h) <= Mathf.Max(w, h) * 0.15f;
            if (casiCuadrada)
            {
                int lado = Mathf.Min(w, h);
                sim = Mathf.Max(sim, Correlacion90(gris, w, h, lado));
            }

            return new Resultado(detalle, cobertura, sim, casiCuadrada);
        }

        // Correlación de Pearson entre la imagen y ella misma girada 180°.
        private static float Correlacion180(float[] g, int w, int h)
        {
            int n = w * h;
            float media = 0f;
            for (int i = 0; i < n; i++) media += g[i];
            media /= n;

            float num = 0f, den = 0f;
            for (int i = 0; i < n; i++)
            {
                float a = g[i] - media;
                float b = g[n - 1 - i] - media;   // (x,y) -> (w-1-x, h-1-y)
                num += a * b;
                den += a * a;
            }
            return den < 1e-6f ? 1f : Mathf.Clamp(num / den, -1f, 1f);
        }

        // Correlación con el giro de 90° sobre el recorte cuadrado central de `lado`.
        private static float Correlacion90(float[] g, int w, int h, int lado)
        {
            int ox = (w - lado) / 2, oy = (h - lado) / 2;
            int n = lado * lado;
            float media = 0f;
            for (int y = 0; y < lado; y++)
            for (int x = 0; x < lado; x++)
                media += g[(oy + y) * w + ox + x];
            media /= n;

            float num = 0f, den = 0f;
            for (int y = 0; y < lado; y++)
            for (int x = 0; x < lado; x++)
            {
                float a = g[(oy + y) * w + ox + x] - media;
                // giro de 90°: (x, y) -> (lado-1-y, x)
                float b = g[(oy + x) * w + ox + (lado - 1 - y)] - media;
                num += a * b;
                den += a * a;
            }
            return den < 1e-6f ? 1f : Mathf.Clamp(num / den, -1f, 1f);
        }

        // Reduce a gris de <= LadoMax por lado promediando bloques (box filter): así el
        // ruido del sensor no cuenta como "detalle".
        public static void Reducir(Color32[] px, int w, int h, out float[] gris, out int gw, out int gh)
        {
            int paso = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(w, h) / (float)LadoMax));
            gw = Mathf.Max(1, w / paso);
            gh = Mathf.Max(1, h / paso);
            gris = new float[gw * gh];

            float inv = 1f / (paso * paso * 255f);
            for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                float acc = 0f;
                int y0 = gy * paso, x0 = gx * paso;
                for (int y = y0; y < y0 + paso; y++)
                for (int x = x0; x < x0 + paso; x++)
                {
                    var c = px[y * w + x];
                    acc += 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                }
                gris[gy * gw + gx] = acc * inv;
            }
        }
    }
}
