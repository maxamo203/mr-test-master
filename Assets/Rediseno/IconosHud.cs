using UnityEngine;

namespace Rediseno
{
    // Íconos de las barras de estado de la noche (MortuoriumTheme.HudFila), generados por
    // código una sola vez, sin assets (mismo criterio que IconosMirada, cuyos helpers usa).
    // Blancos con alfa: el color lo pone quien dibuja.
    //   - Pila: el contorno; el nivel de carga y el rayo de la luz intensa se dibujan encima.
    //   - Cerebro: 4 etapas que se deterioran con la cordura (sano → fisura → agrietado →
    //     partido); las grietas van en una capa aparte para pintarlas de rojo.
    //   - Luna: lo que queda de noche.
    public static class IconosHud
    {
        public const int EtapasCerebro = 4;

        private static Texture2D _pila, _rayo, _luna, _casilla, _casillaHecha;
        private static readonly Texture2D[] _cerebro = new Texture2D[EtapasCerebro];
        private static readonly Texture2D[] _grietas = new Texture2D[EtapasCerebro];

        public static Texture2D Pila => _pila != null ? _pila : (_pila = IconosMirada.Generar(PilaAlfa));
        public static Texture2D Rayo => _rayo != null ? _rayo : (_rayo = IconosMirada.Generar(RayoAlfa));
        public static Texture2D Luna => _luna != null ? _luna : (_luna = IconosMirada.Generar(LunaAlfa));

        // Objetivos de las prácticas: un casillero por objetivo; vacío = aro, logrado = disco
        // con un check calado.
        public static Texture2D Casilla => _casilla != null ? _casilla : (_casilla = IconosMirada.Generar(CasillaAlfa));
        public static Texture2D CasillaHecha =>
            _casillaHecha != null ? _casillaHecha : (_casillaHecha = IconosMirada.Generar(CasillaHechaAlfa));

        private static float CasillaAlfa(Vector2 p) => IconosMirada.Borde(0.08f - Mathf.Abs(p.magnitude - 0.72f));

        private static float CasillaHechaAlfa(Vector2 p)
        {
            float disco = IconosMirada.Borde(0.82f - p.magnitude);
            float check = Mathf.Min(IconosMirada.Segmento(p, new Vector2(-0.38f, 0.02f), new Vector2(-0.10f, -0.28f)),
                                    IconosMirada.Segmento(p, new Vector2(-0.10f, -0.28f), new Vector2(0.40f, 0.30f)));
            return disco * (1f - IconosMirada.Borde(0.12f - check));
        }

        // Interior de la pila (donde va el nivel), en coordenadas normalizadas del ícono
        // (-1..1, +y arriba): x mínimo/máximo e y medio alto.
        public const float PilaXMin = -0.62f, PilaXMax = 0.46f, PilaMedioAlto = 0.22f;

        public static Texture2D Cerebro(int etapa)
        {
            etapa = Mathf.Clamp(etapa, 0, EtapasCerebro - 1);
            if (_cerebro[etapa] == null) _cerebro[etapa] = IconosMirada.Generar(p => CerebroAlfa(p, etapa));
            return _cerebro[etapa];
        }

        // Null en la etapa sana (no hay grietas).
        public static Texture2D Grietas(int etapa)
        {
            etapa = Mathf.Clamp(etapa, 0, EtapasCerebro - 1);
            if (etapa == 0) return null;
            if (_grietas[etapa] == null) _grietas[etapa] = IconosMirada.Generar(p => GrietasAlfa(p, etapa));
            return _grietas[etapa];
        }

        // Etapa según la cordura (0..1): ≥70% sano, ≥40% fisura, ≥15% agrietado, menos partido.
        public static int EtapaCordura(float cordura01) =>
            cordura01 >= 0.7f ? 0 : cordura01 >= 0.4f ? 1 : cordura01 >= 0.15f ? 2 : 3;

        // ── Pila ─────────────────────────────────────────────────────────────
        private static float PilaAlfa(Vector2 p)
        {
            const float t = 0.08f;
            float caja = Mathf.Abs(CajaRedondeada(p, new Vector2(-0.08f, 0f), new Vector2(0.70f, 0.38f), 0.12f));
            float borne = IconosMirada.Segmento(p, new Vector2(0.80f, 0.13f), new Vector2(0.80f, -0.13f)) - 0.02f;
            return IconosMirada.Borde(t - Mathf.Min(caja, borne));
        }

        private static readonly Vector2[] _rayoPts =
        {
            new(0.14f, 0.34f), new(-0.20f, -0.04f), new(-0.02f, -0.04f), new(-0.12f, -0.34f),
            new(0.22f, 0.06f), new(0.04f, 0.06f),
        };

        private static float RayoAlfa(Vector2 p) => IconosMirada.Borde(-DistanciaPoligono(p, _rayoPts));

        // ── Luna: medialuna llena ─────────────────────────────────────────────
        private static float LunaAlfa(Vector2 p)
        {
            float disco = p.magnitude - 0.66f;
            float mordida = (p - new Vector2(0.32f, 0.24f)).magnitude - 0.54f;
            return IconosMirada.Borde(-Mathf.Max(disco, -mordida));
        }

        // ── Cerebro ──────────────────────────────────────────────────────────
        // Hemisferio izquierdo como unión de lóbulos (círculos), cortado en la línea media
        // (x = 0): ese corte dibuja la cisura central. El derecho es su espejo. En la etapa
        // partida los dos hemisferios se separan.
        private static readonly Vector3[] _lobulos =
        {
            new(-0.30f, 0.40f, 0.27f), new(-0.56f, 0.17f, 0.26f), new(-0.55f, -0.17f, 0.25f),
            new(-0.30f, -0.38f, 0.25f), new(-0.20f, 0.04f, 0.32f),
        };

        private static float Hemisferio(Vector2 p)
        {
            float d = float.MaxValue;
            foreach (var l in _lobulos) d = Mathf.Min(d, (p - new Vector2(l.x, l.y)).magnitude - l.z);
            return Mathf.Max(d, p.x);
        }

        private static float Separacion(int etapa) => etapa >= 3 ? 0.09f : 0f;

        // p del espacio del ícono al de cada hemisferio (el derecho, espejado).
        private static Vector2 AIzquierdo(Vector2 p, int etapa) => p + new Vector2(Separacion(etapa), 0f);
        private static Vector2 ADerecho(Vector2 p, int etapa)
        {
            var q = p - new Vector2(Separacion(etapa), 0f);
            return new Vector2(-q.x, q.y);
        }

        private static readonly Vector2[][] _pliegues =
        {
            new[] { new Vector2(-0.46f, 0.10f), new Vector2(-0.26f, -0.02f) },
            new[] { new Vector2(-0.40f, -0.26f), new Vector2(-0.20f, -0.20f) },
            new[] { new Vector2(-0.32f, 0.40f), new Vector2(-0.16f, 0.26f) },
        };

        private static float CerebroAlfa(Vector2 p, int etapa)
        {
            const float t = 0.07f;
            var izq = AIzquierdo(p, etapa);
            var der = ADerecho(p, etapa);
            float d = Mathf.Min(Mathf.Abs(Hemisferio(izq)), Mathf.Abs(Hemisferio(der)));

            // Pliegues: todos sanos; con la fisura sólo quedan los del lado sano; después, ninguno.
            if (etapa <= 1)
                foreach (var s in _pliegues)
                {
                    d = Mathf.Min(d, IconosMirada.Segmento(der, s[0], s[1]) + 0.01f);
                    if (etapa == 0) d = Mathf.Min(d, IconosMirada.Segmento(izq, s[0], s[1]) + 0.01f);
                }

            // Partido: gotea por la cisura.
            if (etapa >= 3)
            {
                d = Mathf.Min(d, IconosMirada.Segmento(p, new Vector2(0f, -0.62f), new Vector2(0f, -0.80f)));
                d = Mathf.Min(d, (p - new Vector2(0f, -0.92f)).magnitude - 0.03f);
            }
            return IconosMirada.Borde(t - d);
        }

        private static readonly Vector2[] _grietaIzq =
            { new(-0.56f, 0.42f), new(-0.42f, 0.22f), new(-0.56f, 0.06f), new(-0.40f, -0.14f) };
        private static readonly Vector2[] _grietaIzq2 = { new(-0.24f, -0.44f), new(-0.10f, -0.30f) };

        private static float GrietasAlfa(Vector2 p, int etapa)
        {
            const float t = 0.05f;
            var izq = AIzquierdo(p, etapa);
            var der = ADerecho(p, etapa);
            float d = Polilinea(izq, _grietaIzq);
            if (etapa >= 2)
            {
                d = Mathf.Min(d, Polilinea(der, _grietaIzq));
                d = Mathf.Min(d, Polilinea(izq, _grietaIzq2));
            }
            return IconosMirada.Borde(t - d);
        }

        // ── Geometría ────────────────────────────────────────────────────────
        private static float Polilinea(Vector2 p, Vector2[] pts)
        {
            float d = float.MaxValue;
            for (int i = 0; i + 1 < pts.Length; i++) d = Mathf.Min(d, IconosMirada.Segmento(p, pts[i], pts[i + 1]));
            return d;
        }

        // Distancia con signo a una caja de esquinas redondeadas (negativa adentro).
        private static float CajaRedondeada(Vector2 p, Vector2 centro, Vector2 medio, float radio)
        {
            Vector2 q = new(Mathf.Abs(p.x - centro.x) - (medio.x - radio), Mathf.Abs(p.y - centro.y) - (medio.y - radio));
            Vector2 qMax = new(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            return qMax.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radio;
        }

        // Distancia con signo a un polígono cerrado (negativa adentro; par/impar).
        private static float DistanciaPoligono(Vector2 p, Vector2[] v)
        {
            float d = float.MaxValue;
            bool adentro = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                d = Mathf.Min(d, IconosMirada.Segmento(p, v[j], v[i]));
                if ((v[i].y > p.y) != (v[j].y > p.y) &&
                    p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x)
                    adentro = !adentro;
            }
            return adentro ? -d : d;
        }
    }
}
