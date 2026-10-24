using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Ubica una puerta/ventana REAL a partir de hits de profundidad (ARCore Depth, LiDAR,
    // planos verticales, feature points) tomados en una fila horizontal a la altura de la
    // mira. La pared que rodea la abertura es la superficie más cercana sobre esa fila: se
    // ajusta una recta a esos puntos (orientación y posición reales del plano de la pared)
    // y, si la mira apunta a un hueco (por la abertura se ve algo más lejos, o nada), el
    // hueco entre el último punto de pared a la izquierda y el primero a la derecha da el
    // ancho y el centro de la abertura.
    //
    // Todo en XZ (Vector2 = x,z) en coordenadas locales de WorldOrigin. Puro: sin Unity
    // runtime, testeable.
    public static class EntradaReal
    {
        public const float TolPlano    = 0.25f;  // m detrás del punto más cercano que siguen siendo "pared"
        public const float MinLateral  = 0.4f;   // m de pared vista, mínimo, para confiar en la recta
        public const float MaxResiduo  = 0.08f;  // m RMS fuera de la recta
        public const float MinCosFrente = 0.5f;  // la pared tiene que mirar al jugador (±60°)
        public const float HuecoMin = 0.45f, HuecoMax = 2.6f;
        public const int   MinPuntos = 3;

        public struct Ajuste
        {
            public bool    Ok;
            public Vector2 Centro;      // sobre el plano de la pared
            public Vector2 Normal;      // hacia el jugador
            public float   Distancia;   // a lo largo del rayo de la mira hasta el plano
            public float   Ancho;       // > 0 sólo si se midió un hueco
            public Vector2 Tangente;    // a lo largo de la pared
            public float   Desde, Hasta; // pared vista, a lo largo de Tangente relativo a Centro (Desde < 0 < Hasta)
        }

        // cam: posición XZ de la cámara. rayo: dirección XZ (normalizada) de la mira.
        // puntos: hits reales de la fila (XZ).
        public static Ajuste Ajustar(Vector2 cam, Vector2 rayo, IReadOnlyList<Vector2> puntos)
        {
            var no = default(Ajuste);
            if (puntos == null || puntos.Count < MinPuntos) return no;

            float dmin = float.PositiveInfinity;
            foreach (var p in puntos)
            {
                float d = Vector2.Dot(p - cam, rayo);
                if (d > 0.2f && d < dmin) dmin = d;
            }
            if (float.IsInfinity(dmin)) return no;

            var cerca = new List<Vector2>(puntos.Count);
            Vector2 media = Vector2.zero;
            foreach (var p in puntos)
            {
                float d = Vector2.Dot(p - cam, rayo);
                if (d > 0.2f && d <= dmin + TolPlano) { cerca.Add(p); media += p; }
            }
            if (cerca.Count < MinPuntos) return no;
            media /= cerca.Count;

            // Recta por PCA 2D: dirección del autovector de mayor autovalor.
            float xx = 0f, xz = 0f, zz = 0f;
            foreach (var p in cerca)
            {
                Vector2 q = p - media;
                xx += q.x * q.x; xz += q.x * q.y; zz += q.y * q.y;
            }
            float ang = 0.5f * Mathf.Atan2(2f * xz, xx - zz);
            var t = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var n = new Vector2(-t.y, t.x);
            if (Vector2.Dot(n, cam - media) < 0f) n = -n;

            float lmin = float.PositiveInfinity, lmax = float.NegativeInfinity, res = 0f;
            foreach (var p in cerca)
            {
                float l = Vector2.Dot(p - media, t);
                lmin = Mathf.Min(lmin, l); lmax = Mathf.Max(lmax, l);
                float e = Vector2.Dot(p - media, n);
                res += e * e;
            }
            if (lmax - lmin < MinLateral) return no;
            if (Mathf.Sqrt(res / cerca.Count) > MaxResiduo) return no;

            float cosFrente = -Vector2.Dot(rayo, n);
            if (cosFrente < MinCosFrente) return no;

            // Donde el rayo de la mira corta el plano de la pared.
            float s = Vector2.Dot(media - cam, n) / Vector2.Dot(rayo, n);
            Vector2 centro = cam + rayo * s;

            // Hueco alrededor de la mira: pared a ambos lados y nada de pared justo en la mira.
            float izq = float.NegativeInfinity, der = float.PositiveInfinity;
            bool paredEnMira = false;
            foreach (var p in cerca)
            {
                float l = Vector2.Dot(p - centro, t);
                if (Mathf.Abs(l) < HuecoMin * 0.5f) { paredEnMira = true; break; }
                if (l < 0f) izq = Mathf.Max(izq, l);
                else        der = Mathf.Min(der, l);
            }

            float ancho = 0f;
            if (!paredEnMira && !float.IsInfinity(izq) && !float.IsInfinity(der))
            {
                float w = der - izq;
                if (w >= HuecoMin && w <= HuecoMax)
                {
                    ancho = w;
                    centro += t * ((izq + der) * 0.5f);
                }
            }

            float desde = 0f, hasta = 0f;
            foreach (var p in cerca)
            {
                float l = Vector2.Dot(p - centro, t);
                desde = Mathf.Min(desde, l); hasta = Mathf.Max(hasta, l);
            }
            return new Ajuste { Ok = true, Centro = centro, Normal = n, Distancia = s, Ancho = ancho,
                                Tangente = t, Desde = desde, Hasta = hasta };
        }
    }
}
