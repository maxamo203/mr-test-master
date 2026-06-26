using System;
using UnityEngine;

namespace Gamepad
{
    // Dibuja un gamepad ficticio en IMGUI que refleja en vivo las pulsaciones
    // (GamepadState). El layout es genérico pero las etiquetas/colores de los 4
    // botones de la cara se adaptan a la marca detectada (Xbox / PlayStation /
    // Switch); si no se reconoce, usa etiquetas neutras.
    //
    // Se dibuja en coordenadas VIRTUALES (el caller ya hizo UIScale.Begin()). El
    // área de dibujo es landscape; pasar un Rect con esa proporción (~16:10).
    public static class GamepadVisualizer
    {
        private static Texture2D _white;
        private static Texture2D _circle;
        // Símbolos de los botones de PlayStation dibujados como vectores (texturas
        // generadas en runtime) para no depender de glyphs Unicode de la fuente:
        // en Android la fuente IMGUI no los trae y salían como "tofu" (cuadrito).
        private static Texture2D _cross, _ring, _square;
        private static GUIStyle _label;

        // Colores base.
        private static readonly Color Panel   = new Color(0.12f, 0.12f, 0.14f, 1f);
        private static readonly Color Idle     = new Color(0.30f, 0.30f, 0.34f, 1f);
        private static readonly Color Press     = new Color(0.95f, 0.85f, 0.20f, 1f); // resaltado genérico
        private static readonly Color Outline    = new Color(0.45f, 0.45f, 0.50f, 1f);
        private static readonly Color TextCol      = new Color(0.95f, 0.95f, 0.97f, 1f);

        public static void Draw(Rect area, GamepadBrand brand, in GamepadState s)
        {
            EnsureAssets();

            var prev = GUI.color;

            // Cuerpo.
            DrawRect(area, Panel);

            float W = area.width, H = area.height;
            Vector2 P(float fx, float fy) => new Vector2(area.x + fx * W, area.y + fy * H);
            float U = Mathf.Min(W, H);          // unidad de tamaño relativa

            // --- Bumpers y triggers (arriba) ---
            DrawBar(new Rect(P(0.10f, 0.06f).x, P(0.10f, 0.06f).y, 0.16f * W, 0.05f * H), s.l2, "L2");
            DrawBar(new Rect(P(0.74f, 0.06f).x, P(0.74f, 0.06f).y, 0.16f * W, 0.05f * H), s.r2, "R2");
            DrawPill(P(0.18f, 0.16f), 0.16f * W, 0.05f * H, s.l1, "L1");
            DrawPill(P(0.82f, 0.16f), 0.16f * W, 0.05f * H, s.r1, "R1");

            // --- Stick izquierdo y D-Pad ---
            DrawStick(P(0.20f, 0.42f), 0.13f * U, s.leftStick, s.l3);
            DrawDpad(P(0.34f, 0.72f), 0.12f * U, s.dpad);

            // --- Botones de la cara (derecha) y stick derecho ---
            DrawFaceButtons(P(0.80f, 0.42f), 0.085f * U, brand, s);
            DrawStick(P(0.66f, 0.72f), 0.13f * U, s.rightStick, s.r3);

            // --- Start / Select (centro) ---
            DrawPill(P(0.43f, 0.40f), 0.08f * W, 0.045f * H, s.select, "Select");
            DrawPill(P(0.57f, 0.40f), 0.08f * W, 0.045f * H, s.start, "Start");

            GUI.color = prev;
        }

        // ---- Sub-dibujos --------------------------------------------------------

        private static void DrawStick(Vector2 center, float radius, Vector2 value, bool clicked)
        {
            DrawCircle(center, radius, Outline);
            DrawCircle(center, radius * 0.86f, clicked ? Press : new Color(0.18f, 0.18f, 0.20f));
            // y de input es arriba+, GUI es abajo+ → invertimos.
            var knob = center + new Vector2(value.x, -value.y) * radius * 0.55f;
            DrawCircle(knob, radius * 0.45f, clicked ? Press : Idle);
        }

        private static void DrawDpad(Vector2 center, float size, Vector2 d)
        {
            float arm = size, thick = size * 0.6f;
            bool up = d.y > 0.3f, down = d.y < -0.3f, left = d.x < -0.3f, right = d.x > 0.3f;
            // vertical
            DrawRect(new Rect(center.x - thick / 2, center.y - arm, thick, arm * 2), Idle);
            // horizontal
            DrawRect(new Rect(center.x - arm, center.y - thick / 2, arm * 2, thick), Idle);
            if (up)    DrawRect(new Rect(center.x - thick / 2, center.y - arm, thick, arm), Press);
            if (down)  DrawRect(new Rect(center.x - thick / 2, center.y, thick, arm), Press);
            if (left)  DrawRect(new Rect(center.x - arm, center.y - thick / 2, arm, thick), Press);
            if (right) DrawRect(new Rect(center.x, center.y - thick / 2, arm, thick), Press);
        }

        // Figura del botón: texto (letras/ASCII) o una de las 4 formas de PlayStation.
        private enum Glyph { Text, Cross, Circle, Square, Triangle }

        private static readonly Color BodyDark = new Color(0.18f, 0.18f, 0.20f, 1f);

        // Define cómo se dibuja un botón de la cara según marca y posición física.
        // Ningún caso usa glyphs Unicode: Xbox/Switch usan letras ASCII, Generic usa
        // ^/v/</> (ASCII) y PlayStation dibuja las formas como vectores.
        private static void FaceStyle(GamepadBrand brand, string pos,
                                      out string text, out Glyph glyph,
                                      out Color body, out Color symbol)
        {
            text = ""; glyph = Glyph.Text; symbol = TextCol;
            // pos: "down","right","up","left" (posición física)
            switch (brand)
            {
                case GamepadBrand.Xbox:
                    switch (pos) {
                        case "down":  text = "A"; body = new Color(0.30f, 0.75f, 0.30f); return;
                        case "right": text = "B"; body = new Color(0.85f, 0.25f, 0.25f); return;
                        case "up":    text = "Y"; body = new Color(0.90f, 0.80f, 0.20f); return;
                        default:      text = "X"; body = new Color(0.25f, 0.50f, 0.90f); return;
                    }
                case GamepadBrand.PlayStation:
                    // Botón oscuro con la forma de color (como los mandos reales).
                    body = BodyDark;
                    switch (pos) {
                        case "down":  glyph = Glyph.Cross;    symbol = new Color(0.55f, 0.75f, 1.00f); return;
                        case "right": glyph = Glyph.Circle;   symbol = new Color(0.95f, 0.40f, 0.40f); return;
                        case "up":    glyph = Glyph.Triangle; symbol = new Color(0.35f, 0.90f, 0.75f); return;
                        default:      glyph = Glyph.Square;   symbol = new Color(0.95f, 0.50f, 0.85f); return;
                    }
                case GamepadBrand.Switch:
                    // Posiciones Nintendo: A der, B abajo, X arriba, Y izq.
                    body = Idle;
                    switch (pos) {
                        case "down":  text = "B"; return;
                        case "right": text = "A"; return;
                        case "up":    text = "X"; return;
                        default:      text = "Y"; return;
                    }
                default: // Generic (ASCII, sin Unicode)
                    body = Idle;
                    switch (pos) {
                        case "down":  text = "v"; return;
                        case "right": text = ">"; return;
                        case "up":    text = "^"; return;
                        default:      text = "<"; return;
                    }
            }
        }

        private static void DrawFaceButtons(Vector2 center, float r, GamepadBrand brand, in GamepadState s)
        {
            float off = r * 1.9f;
            DrawFaceButton(center + new Vector2(0,  off), r, brand, "down",  s.south);
            DrawFaceButton(center + new Vector2(off, 0), r, brand, "right", s.east);
            DrawFaceButton(center + new Vector2(0, -off), r, brand, "up",    s.north);
            DrawFaceButton(center + new Vector2(-off, 0), r, brand, "left",  s.west);
        }

        private static void DrawFaceButton(Vector2 c, float r, GamepadBrand brand, string pos, bool pressed)
        {
            FaceStyle(brand, pos, out string text, out Glyph glyph, out Color body, out Color symbol);
            DrawCircle(c, r, Outline);
            DrawCircle(c, r * 0.85f, pressed ? Press : body);

            Color fg = pressed ? Color.black : symbol;
            float sz = r * 0.95f;   // tamaño de la forma
            switch (glyph)
            {
                case Glyph.Cross:    DrawSymbol(c, sz, _cross,  fg); break;
                case Glyph.Circle:   DrawSymbol(c, sz, _ring,   fg); break;
                case Glyph.Square:   DrawSymbol(c, sz, _square, fg); break;
                case Glyph.Triangle: DrawTriangleUp(c, sz, fg);      break;
                default:
                    DrawLabel(new Rect(c.x - r, c.y - r, r * 2, r * 2), text,
                              Mathf.RoundToInt(r * 1.1f), pressed ? Color.black : TextCol);
                    break;
            }
        }

        private static void DrawPill(Vector2 center, float w, float h, bool pressed, string label)
        {
            var r = new Rect(center.x - w / 2, center.y - h / 2, w, h);
            DrawRect(r, Outline);
            DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), pressed ? Press : Idle);
            DrawLabel(r, label, Mathf.RoundToInt(h * 0.6f), pressed ? Color.black : TextCol);
        }

        private static void DrawBar(Rect r, float fill, string label)
        {
            DrawRect(r, Outline);
            DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(0.18f, 0.18f, 0.20f));
            fill = Mathf.Clamp01(fill);
            if (fill > 0.01f)
                DrawRect(new Rect(r.x + 1, r.y + 1, (r.width - 2) * fill, r.height - 2), Press);
            DrawLabel(r, label, Mathf.RoundToInt(r.height * 0.6f), TextCol);
        }

        // ---- Primitivas ---------------------------------------------------------

        private static void DrawRect(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, _white);
        }

        private static void DrawCircle(Vector2 center, float radius, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), _circle);
        }

        // Dibuja una textura de símbolo (cruz/anillo/cuadrado) centrada y tintada.
        private static void DrawSymbol(Vector2 center, float size, Texture2D tex, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - size, center.y - size, size * 2, size * 2), tex);
        }

        // Triángulo lleno apuntando hacia arriba, dibujado con rects horizontales
        // (sin rotación ni textura, así no hay ambigüedad de orientación).
        private static void DrawTriangleUp(Vector2 center, float size, Color c)
        {
            const int steps = 12;
            float h = size * 1.7f, w = size * 1.9f;
            float top = center.y - h / 2f;
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 1f) / steps;             // 0 (arriba, angosto) → 1 (abajo, ancho)
                float ww = w * t;
                float y = top + (h / steps) * i;
                DrawRect(new Rect(center.x - ww / 2f, y, ww, h / steps + 1f), c);
            }
        }

        private static void DrawLabel(Rect r, string text, int fontSize, Color c)
        {
            _label.fontSize = Mathf.Max(8, fontSize);
            _label.normal.textColor = c;
            GUI.color = Color.white;
            GUI.Label(r, text, _label);
        }

        private static void EnsureAssets()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                _white.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_circle == null)
            {
                const int N = 64;
                _circle = new Texture2D(N, N, TextureFormat.RGBA32, false);
                _circle.hideFlags = HideFlags.HideAndDontSave;
                float rad = N / 2f - 0.5f;
                var ctr = new Vector2(rad, rad);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), ctr);
                        float a = Mathf.Clamp01(rad - d);   // borde suave de 1 px
                        _circle.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                _circle.Apply();
            }
            // Símbolos de PlayStation (texturas alfa, simétricas → sin problema de flip).
            if (_cross == null)
                _cross = GenAlpha(64, (nx, ny) =>
                {
                    if (Mathf.Abs(nx) > 0.80f || Mathf.Abs(ny) > 0.80f) return 0f;
                    float d = Mathf.Min(Mathf.Abs(nx - ny), Mathf.Abs(nx + ny));
                    return Mathf.Clamp01((0.26f - d) / 0.07f);
                });
            if (_ring == null)
                _ring = GenAlpha(64, (nx, ny) =>
                {
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    return Mathf.Clamp01((0.86f - r) / 0.07f) * Mathf.Clamp01((r - 0.52f) / 0.07f);
                });
            if (_square == null)
                _square = GenAlpha(64, (nx, ny) =>
                {
                    float m = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));
                    return Mathf.Clamp01((0.80f - m) / 0.06f) * Mathf.Clamp01((m - 0.50f) / 0.06f);
                });
            if (_label == null)
                _label = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }

        // Genera una textura cuadrada NxN cuyo alfa lo define f(nx,ny) con nx,ny ∈ [-1,1].
        private static Texture2D GenAlpha(int n, Func<float, float, float> f)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float nx = ((x + 0.5f) / n) * 2f - 1f;
                    float ny = ((y + 0.5f) / n) * 2f - 1f;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(f(nx, ny))));
                }
            t.Apply();
            return t;
        }
    }
}
