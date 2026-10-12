using UnityEngine;
using static Rediseno.IconosMirada;

namespace Rediseno.UI
{
    // Íconos de línea de los menús nuevos, rasterizados por código (sin assets). Blancos
    // con alfa: se tiñen con unityBackgroundImageTintColor. Coordenadas normalizadas
    // -1..1 con +y arriba; trazo de medio grosor T.
    public static class IconosMenu
    {
        private const float T = 0.09f;

        private static Texture2D _otros, _escenarios, _libro, _engranaje, _visor, _celular;

        public static Texture2D Otros      => _otros      != null ? _otros      : (_otros      = Generar(OtrosA));
        public static Texture2D Escenarios => _escenarios != null ? _escenarios : (_escenarios = Generar(EscenariosA));
        public static Texture2D Libro      => _libro      != null ? _libro      : (_libro      = Generar(LibroA));
        public static Texture2D Engranaje  => _engranaje  != null ? _engranaje  : (_engranaje  = Generar(EngranajeA));
        public static Texture2D Visor      => _visor      != null ? _visor      : (_visor      = Generar(VisorA));
        public static Texture2D Celular    => _celular    != null ? _celular    : (_celular    = Generar(CelularA));

        private static float Circulo(Vector2 p, Vector2 c, float r) => Mathf.Abs(Vector2.Distance(p, c) - r);

        // Medio círculo (la mitad de arriba) de centro c y radio r: hombros.
        private static float Hombros(Vector2 p, Vector2 c, float r)
        {
            Vector2 d = p - c;
            if (d.y >= 0f) return Mathf.Abs(d.magnitude - r);
            return Mathf.Min(Vector2.Distance(p, c + new Vector2(-r, 0f)),
                             Vector2.Distance(p, c + new Vector2(r, 0f)));
        }

        // Rectángulo con esquinas redondeadas (contorno). c centro, h medio tamaño, r radio.
        private static float RectRedondo(Vector2 p, Vector2 c, Vector2 h, float r)
        {
            Vector2 q = new(Mathf.Abs(p.x - c.x) - (h.x - r), Mathf.Abs(p.y - c.y) - (h.y - r));
            float fuera = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
            float dentro = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
            return Mathf.Abs(fuera + dentro - r);
        }

        private static float OtrosA(Vector2 p)
        {
            float d = Circulo(p, new Vector2(-0.36f, 0.30f), 0.2f);
            d = Mathf.Min(d, Circulo(p, new Vector2(0.38f, 0.30f), 0.2f));
            d = Mathf.Min(d, Hombros(p, new Vector2(-0.36f, -0.58f), 0.42f));
            d = Mathf.Min(d, Hombros(p, new Vector2(0.38f, -0.58f), 0.42f));
            return Borde(T - d);
        }

        private static float EscenariosA(Vector2 p)
        {
            // Esquinas de encuadre + una línea de "montañas" (el cuarto escaneado).
            float d = float.MaxValue;
            void S(float ax, float ay, float bx, float by) =>
                d = Mathf.Min(d, Segmento(p, new Vector2(ax, ay), new Vector2(bx, by)));
            S(-0.8f, 0.45f, -0.8f, 0.8f); S(-0.8f, 0.8f, -0.45f, 0.8f);
            S(0.45f, 0.8f, 0.8f, 0.8f);   S(0.8f, 0.8f, 0.8f, 0.45f);
            S(0.8f, -0.45f, 0.8f, -0.8f); S(0.8f, -0.8f, 0.45f, -0.8f);
            S(-0.45f, -0.8f, -0.8f, -0.8f); S(-0.8f, -0.8f, -0.8f, -0.45f);
            S(-0.5f, -0.35f, -0.18f, 0.25f); S(-0.18f, 0.25f, 0.1f, -0.1f);
            S(0.1f, -0.1f, 0.28f, 0.1f);    S(0.28f, 0.1f, 0.5f, -0.35f);
            return Borde(T - d);
        }

        private static float LibroA(Vector2 p)
        {
            float d = float.MaxValue;
            void S(float ax, float ay, float bx, float by) =>
                d = Mathf.Min(d, Segmento(p, new Vector2(ax, ay), new Vector2(bx, by)));
            S(-0.85f, 0.55f, -0.85f, -0.65f); S(0.85f, 0.55f, 0.85f, -0.65f);
            S(-0.85f, 0.55f, 0f, 0.42f);      S(0.85f, 0.55f, 0f, 0.42f);
            S(-0.85f, -0.65f, 0f, -0.78f);    S(0.85f, -0.65f, 0f, -0.78f);
            S(0f, 0.42f, 0f, -0.78f);
            return Borde(T - d);
        }

        private static float EngranajeA(Vector2 p)
        {
            float d = Circulo(p, Vector2.zero, 0.42f);
            d = Mathf.Min(d, Circulo(p, Vector2.zero, 0.14f));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                d = Mathf.Min(d, Segmento(p, dir * 0.5f, dir * 0.78f));
            }
            return Borde(T - d);
        }

        private static float VisorA(Vector2 p)
        {
            float d = RectRedondo(p, new Vector2(0f, 0.02f), new Vector2(0.86f, 0.46f), 0.26f);
            d = Mathf.Min(d, Circulo(p, new Vector2(-0.36f, 0.04f), 0.17f));
            d = Mathf.Min(d, Circulo(p, new Vector2(0.36f, 0.04f), 0.17f));
            return Borde(T - d);
        }

        private static float CelularA(Vector2 p)
        {
            float d = RectRedondo(p, Vector2.zero, new Vector2(0.44f, 0.84f), 0.16f);
            d = Mathf.Min(d, Segmento(p, new Vector2(-0.12f, -0.62f), new Vector2(0.12f, -0.62f)));
            return Borde(T - d);
        }
    }
}
