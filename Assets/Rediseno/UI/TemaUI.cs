using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using T = MortuoriumTheme;

namespace Rediseno.UI
{
    // Tema de los menús nuevos (UI Toolkit). Misma paleta y fuentes que MortuoriumTheme
    // (el IMGUI de siempre), pero con lo que IMGUI no puede: esquinas redondeadas, espacio
    // entre letras, rellenos con degradé y texto nítido (SDF) a cualquier tamaño.
    //
    // Todo se arma por código, sin UXML/USS: así no hay assets que se rompan en un merge y
    // la misma pantalla se puede dibujar en la pantalla del celular o en una textura (modo
    // visor) sin cambiar nada.
    public static class TemaUI
    {
        public static readonly Color Bg       = T.Bg;
        public static readonly Color Panel    = new(0.086f, 0.071f, 0.063f, 0.78f);
        public static readonly Color Cream    = T.Cream;
        public static readonly Color CreamDim = T.CreamDim;
        public static readonly Color Muted    = T.Muted;
        public static readonly Color Dim      = T.Dim;
        public static readonly Color Red      = new(0.784f, 0.196f, 0.243f, 1f);   // #c8323e
        public static readonly Color RedDeep  = new(0.557f, 0.110f, 0.145f, 1f);   // #8e1c25
        public static readonly Color Tan      = T.Tan;
        public static readonly Color Green    = T.Green;
        public static readonly Color Linea    = new(0.914f, 0.890f, 0.839f, 0.16f);

        // ── Fuentes ──────────────────────────────────────────────────────────
        // Se convierten los TTF a FontAsset dinámico (SDF) una sola vez: texto nítido al
        // escalar y con soporte de letter-spacing. Si la conversión falla, cae a la fuente
        // legacy (se ve igual, un poco menos nítida).
        private static FontDefinition? _bebas, _elite, _mono;
        public static FontDefinition Bebas => Fuente(ref _bebas, T.FBebas);
        public static FontDefinition Elite => Fuente(ref _elite, T.FElite);
        public static FontDefinition Mono  => Fuente(ref _mono,  T.FMono);

        private static FontDefinition Fuente(ref FontDefinition? cache, Font f)
        {
            if (cache.HasValue) return cache.Value;
            FontDefinition def;
            try
            {
                var fa = f != null ? FontAsset.CreateFontAsset(f) : null;
                def = fa != null ? FontDefinition.FromSDFFont(fa) : FontDefinition.FromFont(f);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TemaUI] No se pudo crear el FontAsset de {f?.name}: {e.Message}");
                def = FontDefinition.FromFont(f);
            }
            cache = def;
            return def;
        }

        // ── Texturas generadas ───────────────────────────────────────────────
        private static Texture2D _degradeRojo, _sombraAbajo;

        // Relleno del botón principal: rojo arriba, bordó abajo.
        public static Texture2D DegradeRojo =>
            _degradeRojo != null ? _degradeRojo : (_degradeRojo = Vertical(Red, RedDeep));

        // Oscurece la mitad de abajo del fondo para que los botones se lean sobre el arte.
        public static Texture2D SombraAbajo =>
            _sombraAbajo != null ? _sombraAbajo
                : (_sombraAbajo = Vertical(new Color(Bg.r, Bg.g, Bg.b, 0f), new Color(Bg.r, Bg.g, Bg.b, 0.96f), 0.35f));

        // Degradé vertical arriba→abajo. 'desde' (0..1): hasta dónde se mantiene el color de arriba.
        private static Texture2D Vertical(Color arriba, Color abajo, float desde = 0f)
        {
            const int H = 64;
            var tex = new Texture2D(1, H, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            for (int y = 0; y < H; y++)
            {
                // Fila 0 = abajo de la textura.
                float t = 1f - y / (H - 1f);
                float k = Mathf.Clamp01((t - desde) / Mathf.Max(0.0001f, 1f - desde));
                tex.SetPixel(0, y, Color.Lerp(arriba, abajo, k));
            }
            tex.Apply(false, true);
            return tex;
        }

        // ── Helpers de estilo ────────────────────────────────────────────────
        public static void Radio(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }

        public static void Borde(VisualElement e, float ancho, Color c)
        {
            e.style.borderTopWidth = ancho; e.style.borderBottomWidth = ancho;
            e.style.borderLeftWidth = ancho; e.style.borderRightWidth = ancho;
            BordeColor(e, c);
        }

        public static void BordeColor(VisualElement e, Color c)
        {
            e.style.borderTopColor = c; e.style.borderBottomColor = c;
            e.style.borderLeftColor = c; e.style.borderRightColor = c;
        }

        public static void Relleno(VisualElement e, float v, float h)
        {
            e.style.paddingTop = v; e.style.paddingBottom = v;
            e.style.paddingLeft = h; e.style.paddingRight = h;
        }

        public static Label Texto(string s, FontDefinition f, float size, Color c,
                                  float tracking = 0f, TextAnchor ancla = TextAnchor.MiddleLeft)
        {
            var l = new Label(s) { pickingMode = PickingMode.Ignore };
            l.style.unityFontDefinition = f;
            l.style.fontSize = size;
            l.style.color = c;
            l.style.letterSpacing = tracking;
            l.style.unityTextAlign = ancla;
            l.style.marginTop = 0; l.style.marginBottom = 0; l.style.marginLeft = 0; l.style.marginRight = 0;
            l.style.paddingTop = 0; l.style.paddingBottom = 0; l.style.paddingLeft = 0; l.style.paddingRight = 0;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        // Ícono cuadrado de una textura (tinta con 'c').
        public static VisualElement Icono(Texture2D tex, float size, Color c)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.width = size; e.style.height = size;
            e.style.backgroundImage = tex;
            e.style.unityBackgroundImageTintColor = c;
            e.style.flexShrink = 0;
            return e;
        }
    }
}
