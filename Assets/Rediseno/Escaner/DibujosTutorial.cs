using UnityEngine;
using UnityEngine.UIElements;

namespace Rediseno.Escaner
{
    // Dibujos de las hojas del tutorial de escaneo, a trazo (Painter2D): nada de texturas
    // ni assets. Cada uno se diseña en un lienzo de 300×90 que se escala centrado en el
    // rectángulo del elemento. Misma paleta que TemaUI; los marcadores usan los colores
    // de sus MarkerType (puerta verde, ventana celeste).
    public static class DibujosTutorial
    {
        private const float W = 300f, H = 90f;

        private static readonly Color Crema   = new(0.914f, 0.890f, 0.839f, 0.75f);
        private static readonly Color Tenue   = new(0.42f, 0.39f, 0.35f, 1f);
        private static readonly Color Dorado  = new(0.788f, 0.541f, 0.239f, 1f);
        private static readonly Color Rojo    = new(0.784f, 0.196f, 0.243f, 1f);
        private static readonly Color Poster  = new(0.431f, 0.165f, 0.141f, 1f);
        private static readonly Color Sol     = new(0.851f, 0.690f, 0.478f, 1f);
        private static readonly Color Puerta  = new(0.3f, 0.9f, 0.4f, 1f);
        private static readonly Color Ventana = new(0.3f, 0.7f, 1f, 1f);
        private static readonly Color Hueco   = new(0.047f, 0.039f, 0.035f, 1f);

        // Escala y desplazamiento del lienzo 300×90 al rect del elemento.
        private static float _s;
        private static Vector2 _o;
        private static Painter2D _p;

        private static Vector2 P(float x, float y) => _o + new Vector2(x, y) * _s;

        // Lienzo 300×90 → rect del elemento (centrado, con margen).
        public static void Mapear(Rect r, out float s, out Vector2 o)
        {
            s = Mathf.Min(r.width / W, r.height / H) * 0.9f;
            o = new Vector2(r.x + (r.width - W * s) * 0.5f, r.y + (r.height - H * s) * 0.5f);
        }

        // Puntos numerados (círculo dorado con el número adentro). Painter2D no dibuja
        // texto: el círculo va acá y el número lo pone TutorialEscaneo como Label encima,
        // en estas mismas coordenadas del lienzo.
        public const float RadioNumero = 9f;
        public struct Numero { public float X, Y; public string Texto; }

        private static Numero N(float x, float y, string t) => new() { X = x, Y = y, Texto = t };

        public static Numero[] Numeros(int paso) => paso switch
        {
            // Primera pared: 1 esquina en el piso, 2 arriba del 1 (altura), 3 la otra
            // esquina del piso. Con eso la pared está.
            3 => new[] { N(80, 80, "1"), N(80, 12, "2"), N(220, 80, "3") },
            6 => new[] { N(124, 80, "1"), N(176, 24, "2") },
            7 => new[] { N(100, 80, "1"), N(222, 20, "2"), N(150, 80, "3") },
            _ => System.Array.Empty<Numero>(),
        };

        public static void Dibujar(int paso, MeshGenerationContext ctx)
        {
            var r = ctx.visualElement.contentRect;
            if (r.width <= 0f || r.height <= 0f) return;
            Mapear(r, out _s, out _o);
            _p = ctx.painter2D;
            _p.lineJoin = LineJoin.Round;
            _p.lineCap = LineCap.Round;

            switch (paso)
            {
                case 1: Imagen(); break;
                case 2: Piso(); break;
                case 3: Paredes(); break;
                case 4: Alinear(); break;
                case 5: Identificar(); break;
                case 6: Huecos(); break;
                case 7: Muebles(); break;
            }

            foreach (var n in Numeros(paso)) CirculoRelleno(n.X, n.Y, RadioNumero, Dorado);
        }

        // Tilde verde del toast "paso hecho".
        public static void Tilde(MeshGenerationContext ctx)
        {
            var r = ctx.visualElement.contentRect;
            var p = ctx.painter2D;
            var c = r.center;
            float rad = Mathf.Min(r.width, r.height) * 0.5f;
            p.fillColor = new Color(0.561f, 0.682f, 0.478f, 1f);
            p.BeginPath();
            Circulo(p, c, rad);
            p.Fill();
            p.strokeColor = new Color(0.024f, 0.02f, 0.016f, 1f);
            p.lineWidth = rad * 0.22f;
            p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(c + new Vector2(-0.42f, 0.04f) * rad);
            p.LineTo(c + new Vector2(-0.12f, 0.34f) * rad);
            p.LineTo(c + new Vector2(0.44f, -0.3f) * rad);
            p.Stroke();
        }

        // ── Pasos ────────────────────────────────────────────────────────────
        private static void Imagen()
        {
            // Póster con detalle, encuadrado por el recuadro (esquinas doradas) y su ancho.
            RectRelleno(122, 8, 56, 64, Poster);
            CirculoRelleno(138, 26, 9, Sol);
            RectRelleno(128, 46, 42, 3, Crema);
            RectRelleno(128, 53, 26, 3, new Color(Crema.r, Crema.g, Crema.b, 0.5f));
            Trazo(Dorado, 1.8f, 112, 16, 112, 2, 126, 2);
            Trazo(Dorado, 1.8f, 174, 2, 188, 2, 188, 16);
            Trazo(Dorado, 1.8f, 188, 64, 188, 78, 174, 78);
            Trazo(Dorado, 1.8f, 126, 78, 112, 78, 112, 64);
            Trazo(Crema, 1.2f, 122, 86, 178, 86);
            Trazo(Crema, 1.2f, 122, 82, 122, 90);
            Trazo(Crema, 1.2f, 178, 82, 178, 90);

            // Lo que no sirve: una imagen simétrica y una con reflejo, tachadas.
            RectTrazo(20, 18, 44, 44, Tenue, 1.4f);
            CirculoTrazo(42, 40, 12, Tenue, 1.4f);
            Trazo(Rojo, 1.8f, 14, 12, 70, 68);
            Trazo(Rojo, 1.8f, 70, 12, 14, 68);
            RectTrazo(236, 18, 44, 44, Tenue, 1.4f);
            Trazo(new Color(1f, 1f, 1f, 0.25f), 6f, 242, 24, 274, 56);
            Trazo(Rojo, 1.8f, 230, 12, 286, 68);
            Trazo(Rojo, 1.8f, 286, 12, 230, 68);
        }

        private static void Piso()
        {
            Poligono(Crema, 1.4f, 40, 66, 150, 42, 260, 66, 150, 88);
            ElipseRelleno(150, 66, 24, 6.5f, new Color(Dorado.r, Dorado.g, Dorado.b, 0.15f));
            ElipseTrazo(150, 66, 24, 6.5f, Dorado, 1.6f);
            CirculoRelleno(150, 66, 2.5f, Dorado);
            Discontinua(Dorado, 1.2f, 150, 20, 150, 58, 3f, 3f);
            // Mira.
            Trazo(Crema, 1.6f, 140, 12, 146, 12);
            Trazo(Crema, 1.6f, 154, 12, 160, 12);
            Trazo(Crema, 1.6f, 150, 2, 150, 8);
            Trazo(Crema, 1.6f, 150, 16, 150, 22);
        }

        private static void Paredes()
        {
            // De frente: la primera pared sale con tres puntos (los números los pone
            // Numeros): 1 en el piso, 2 arriba del 1 en el techo, 3 la otra esquina del piso.
            Trazo(Tenue, 1.4f, 20, 80, 280, 80);
            Grilla(80, 12, 140, 68);
            RectTrazo(80, 12, 140, 68, Crema, 1.4f);
            // Orden: subir del 1 al 2, después ir al 3.
            Discontinua(Dorado, 1.2f, 64, 72, 64, 22, 4f, 3f);
            Trazo(Dorado, 1.2f, 60, 27, 64, 21, 68, 27);
            Discontinua(Dorado, 1.2f, 92, 88, 206, 88, 4f, 3f);
            Trazo(Dorado, 1.2f, 201, 85, 207, 88, 201, 91);
        }

        private static void Alinear()
        {
            Trazo(Crema, 2f, 20, 82, 280, 82);
            // Dónde tiene que quedar (punteada) y la pared que flota arriba.
            DiscontinuaRect(80, 30, 140, 52, new Color(Dorado.r, Dorado.g, Dorado.b, 0.7f));
            Grilla(80, 8, 140, 56);
            RectTrazo(80, 8, 140, 56, Crema, 1.4f);
            Trazo(Dorado, 1.8f, 150, 66, 150, 78);
            Trazo(Dorado, 1.8f, 145, 73, 150, 79, 155, 73);
        }

        private static void Identificar()
        {
            RectTrazo(30, 6, 240, 80, Crema, 1.4f);
            RectTrazo(66, 30, 40, 56, Tenue, 1.2f);
            RectTrazo(176, 22, 58, 34, Tenue, 1.2f);
            Trazo(Puerta, 2f, 86, 56, 98, 64);
            CirculoRelleno(86, 56, 6, Puerta);
            Trazo(Ventana, 2f, 205, 39, 217, 47);
            CirculoRelleno(205, 39, 6, Ventana);
        }

        private static void Huecos()
        {
            Grilla(40, 6, 220, 74);
            RectTrazo(40, 6, 220, 74, Crema, 1.4f);
            RectRelleno(124, 24, 52, 56, Hueco);
            DiscontinuaRect(124, 24, 52, 56, Dorado);
        }

        private static void Muebles()
        {
            Trazo(Tenue, 1.4f, 20, 82, 280, 82);
            // Caja isométrica: frente, tapa y costado.
            Poligono(Crema, 1.4f, 100, 40, 190, 40, 190, 80, 100, 80);
            Poligono(Crema, 1.4f, 100, 40, 132, 20, 222, 20, 190, 40);
            Poligono(Crema, 1.4f, 190, 40, 222, 20, 222, 60, 190, 80);
            // Los tres puntos: esquina de abajo, opuesta arriba en diagonal, y el del giro.
            Discontinua(Dorado, 1.2f, 100, 80, 222, 20, 4f, 4f);
        }

        // ── Primitivas ───────────────────────────────────────────────────────
        private static void Trazo(Color c, float ancho, params float[] xy)
        {
            _p.strokeColor = c;
            _p.lineWidth = ancho * _s;
            _p.BeginPath();
            _p.MoveTo(P(xy[0], xy[1]));
            for (int i = 2; i + 1 < xy.Length; i += 2) _p.LineTo(P(xy[i], xy[i + 1]));
            _p.Stroke();
        }

        private static void Poligono(Color c, float ancho, params float[] xy)
        {
            _p.strokeColor = c;
            _p.lineWidth = ancho * _s;
            _p.BeginPath();
            _p.MoveTo(P(xy[0], xy[1]));
            for (int i = 2; i + 1 < xy.Length; i += 2) _p.LineTo(P(xy[i], xy[i + 1]));
            _p.ClosePath();
            _p.Stroke();
        }

        private static void RectTrazo(float x, float y, float w, float h, Color c, float ancho) =>
            Poligono(c, ancho, x, y, x + w, y, x + w, y + h, x, y + h);

        private static void RectRelleno(float x, float y, float w, float h, Color c)
        {
            _p.fillColor = c;
            _p.BeginPath();
            _p.MoveTo(P(x, y)); _p.LineTo(P(x + w, y)); _p.LineTo(P(x + w, y + h)); _p.LineTo(P(x, y + h));
            _p.ClosePath();
            _p.Fill();
        }

        private static void Discontinua(Color c, float ancho, float x0, float y0, float x1, float y1,
                                        float trazo, float hueco)
        {
            var a = new Vector2(x0, y0); var b = new Vector2(x1, y1);
            float largo = Vector2.Distance(a, b);
            if (largo <= 0f) return;
            var dir = (b - a) / largo;
            _p.strokeColor = c;
            _p.lineWidth = ancho * _s;
            _p.BeginPath();
            for (float d = 0f; d < largo; d += trazo + hueco)
            {
                var p0 = a + dir * d;
                var p1 = a + dir * Mathf.Min(largo, d + trazo);
                _p.MoveTo(P(p0.x, p0.y));
                _p.LineTo(P(p1.x, p1.y));
            }
            _p.Stroke();
        }

        private static void DiscontinuaRect(float x, float y, float w, float h, Color c)
        {
            Discontinua(c, 1.4f, x, y, x + w, y, 4f, 3f);
            Discontinua(c, 1.4f, x + w, y, x + w, y + h, 4f, 3f);
            Discontinua(c, 1.4f, x + w, y + h, x, y + h, 4f, 3f);
            Discontinua(c, 1.4f, x, y + h, x, y, 4f, 3f);
        }

        // Cuadrícula tenue, como el shader de las paredes escaneadas.
        private static void Grilla(float x, float y, float w, float h)
        {
            var c = new Color(Crema.r, Crema.g, Crema.b, 0.16f);
            _p.strokeColor = c;
            _p.lineWidth = 0.8f * _s;
            _p.BeginPath();
            for (float gx = x + 12f; gx < x + w; gx += 12f) { _p.MoveTo(P(gx, y)); _p.LineTo(P(gx, y + h)); }
            for (float gy = y + 12f; gy < y + h; gy += 12f) { _p.MoveTo(P(x, gy)); _p.LineTo(P(x + w, gy)); }
            _p.Stroke();
        }

        private static void CirculoRelleno(float x, float y, float r, Color c) => ElipseRelleno(x, y, r, r, c);
        private static void CirculoTrazo(float x, float y, float r, Color c, float ancho) => ElipseTrazo(x, y, r, r, c, ancho);

        private static void ElipseRelleno(float x, float y, float rx, float ry, Color c)
        {
            _p.fillColor = c;
            _p.BeginPath();
            Elipse(x, y, rx, ry);
            _p.Fill();
        }

        private static void ElipseTrazo(float x, float y, float rx, float ry, Color c, float ancho)
        {
            _p.strokeColor = c;
            _p.lineWidth = ancho * _s;
            _p.BeginPath();
            Elipse(x, y, rx, ry);
            _p.Stroke();
        }

        // Polígono de 32 lados (evita depender de la firma de Painter2D.Arc entre versiones).
        private static void Elipse(float x, float y, float rx, float ry)
        {
            const int N = 32;
            for (int i = 0; i <= N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                var q = P(x + Mathf.Cos(a) * rx, y + Mathf.Sin(a) * ry);
                if (i == 0) _p.MoveTo(q); else _p.LineTo(q);
            }
            _p.ClosePath();
        }

        private static void Circulo(Painter2D p, Vector2 c, float r)
        {
            const int N = 32;
            for (int i = 0; i <= N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                var q = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (i == 0) p.MoveTo(q); else p.LineTo(q);
            }
            p.ClosePath();
        }
    }
}
