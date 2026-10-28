using System;
using System.Collections.Generic;
using UnityEngine;
using Gamepad;

// Tema visual "Mortuorium" — paleta, tipografías y widgets IMGUI compartidos por
// todos los menús, portado del prototipo HTML (Assets/Prototipo Navegacion).
//
// Uso: dentro de un OnGUI que ya llamó a UIScale.Begin(), dibujar con las
// primitivas (Fill/Borde/Panel) y los widgets (Boton, BotonVolver, Barra) o, en las
// pantallas con el estilo 2.0, sus variantes redondeadas (Boton2, Slider2, CampoTexto2…).
// Los botones se integran con ImguiGamepadMenu para navegarse con el mando: el
// resaltado de foco lo pinta este tema (borde dorado) en vez del tint amarillo.
//
// Tipografías (Assets/Resources/Fonts): Bebas Neue (títulos/botones), Special
// Elite (texto "máquina de escribir"), IBM Plex Mono (datos). Si faltan los
// assets, cae al font por defecto del skin.
public static class MortuoriumTheme
{
    // ── Paleta (hex del prototipo) ────────────────────────────────────────
    public static readonly Color Bg        = Hex("060504");   // fondo general
    public static readonly Color BgPanel   = Hex("141110");   // paneles/modales
    public static readonly Color BgField   = Hex("131010");   // campos de texto
    public static readonly Color Cream     = Hex("e9e3d6");   // texto principal
    public static readonly Color CreamDim  = Hex("c9c2b4");   // texto secundario
    public static readonly Color Muted     = Hex("a89e90");   // texto apagado
    public static readonly Color Dim       = Hex("6b6459");   // texto muy apagado
    public static readonly Color Disabled  = Hex("4a443c");   // deshabilitado
    public static readonly Color Border    = Hex("3a322a");   // borde estándar
    public static readonly Color BorderDim = Hex("2a231c");   // borde apagado
    public static readonly Color Red       = Hex("a3222c");   // acento principal
    public static readonly Color Tan       = Hex("c98a3d");   // acento dorado (foco)
    public static readonly Color Green     = Hex("8fae7a");   // ok/conectado
    public static readonly Color Blue      = Hex("7aa6c2");   // identificadores

    private static Color Hex(string h)
    {
        return ColorUtility.TryParseHtmlString("#" + h, out var c) ? c : Color.magenta;
    }

    // ── Fuentes ───────────────────────────────────────────────────────────
    private static Font _bebas, _elite, _mono;
    private static bool _fontsLoaded;

    private static void EnsureFonts()
    {
        if (_fontsLoaded) return;
        _fontsLoaded = true;
        _bebas = Resources.Load<Font>("Fonts/BebasNeue-Regular");
        _elite = Resources.Load<Font>("Fonts/SpecialElite-Regular");
        _mono  = Resources.Load<Font>("Fonts/IBMPlexMono-Regular");
    }

    public static Font FBebas { get { EnsureFonts(); return _bebas; } }
    public static Font FElite { get { EnsureFonts(); return _elite; } }
    public static Font FMono  { get { EnsureFonts(); return _mono; } }

    // ── Logo (wordmark MORTUORIUM) ──────────────────────────────────────────
    // Assets/Resources/Logo/mortuorium.png: letras blancas sobre fondo transparente.
    // Mismo archivo que usa MortuoriumSplashSetup para el Splash Screen.
    private static Texture2D _logo;
    private static bool _logoLoaded;

    private static void EnsureLogo()
    {
        if (_logoLoaded) return;
        _logoLoaded = true;
        _logo = Resources.Load<Texture2D>("Logo/mortuorium");
        // Si el import quedó como Sprite (lo hace MortuoriumSplashSetup para el
        // Splash Screen), Resources.Load<Texture2D> igual debería resolverlo; esto
        // es sólo una red de seguridad por si alguna vez no lo hace.
        if (_logo == null)
        {
            var sprite = Resources.Load<Sprite>("Logo/mortuorium");
            if (sprite != null) _logo = sprite.texture;
        }
    }

    public static Texture2D LogoMortuorium { get { EnsureLogo(); return _logo; } }

    // ── Estilos (cacheados por combinación) ───────────────────────────────
    // La clave es un struct, no un string: Estilo() se llama por cada label en cada
    // evento de OnGUI (varios por frame, en HUDs que viven toda la partida), y armar
    // un string interpolado + ToHtmlStringRGBA por llamada era basura de GC constante.
    private readonly struct EstiloKey : IEquatable<EstiloKey>
    {
        private readonly int  _font;     // instanceID (0 = default del skin)
        private readonly int  _size;
        private readonly Color _color;
        private readonly TextAnchor _anchor;
        private readonly bool _wrap;

        public EstiloKey(Font font, int size, Color color, TextAnchor anchor, bool wrap)
        {
            _font = font != null ? font.GetInstanceID() : 0;
            _size = size; _color = color; _anchor = anchor; _wrap = wrap;
        }

        public bool Equals(EstiloKey o) =>
            _font == o._font && _size == o._size && _wrap == o._wrap &&
            _anchor == o._anchor && _color.Equals(o._color);

        public override bool Equals(object obj) => obj is EstiloKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = _font;
                h = h * 397 ^ _size;
                h = h * 397 ^ _color.GetHashCode();
                h = h * 397 ^ (int)_anchor;
                h = h * 397 ^ (_wrap ? 1 : 0);
                return h;
            }
        }
    }

    private static readonly Dictionary<EstiloKey, GUIStyle> _styles = new();

    public static GUIStyle Estilo(Font font, int size, Color color,
                                  TextAnchor anchor = TextAnchor.MiddleLeft,
                                  bool wrap = false)
    {
        var key = new EstiloKey(font, size, color, anchor, wrap);
        if (_styles.TryGetValue(key, out var st)) return st;
        st = new GUIStyle(GUI.skin.label)
        {
            // PRUEBA: antes forzaba null en Android ("no renderizan" — commit 6892163).
            // Se sospecha que ese diagnóstico se hizo sin el contenido real de Git LFS
            // descargado (los .ttf eran punteros de ~130 bytes, no la fuente real — ver
            // conversación). Ahora que el LFS está bien, probamos la fuente real en
            // Android también. Si vuelve a verse mal, revertir a la condición de antes.
            font      = font,
            fontSize  = size,
            alignment = anchor,
            wordWrap  = wrap,
            richText  = false,
        };
        st.normal.textColor = color;
        _styles[key] = st;
        return st;
    }

    // ── Texturas base ─────────────────────────────────────────────────────
    private static Texture2D _white, _gradDown, _gradUp, _scanlines;

    private static Texture2D White()
    {
        if (_white == null)
        {
            _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            _white.hideFlags = HideFlags.HideAndDontSave;
        }
        return _white;
    }

    // Gradiente vertical de alpha (1 arriba -> 0 abajo o al revés), para las
    // franjas superior/inferior sobre la cámara AR.
    private static Texture2D Grad(bool haciaAbajo)
    {
        ref Texture2D tex = ref (haciaAbajo ? ref _gradDown : ref _gradUp);
        if (tex == null)
        {
            const int H = 64;
            tex = new Texture2D(1, H, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            for (int y = 0; y < H; y++)
            {
                // y=0 es la fila de ABAJO de la textura.
                float t = (float)y / (H - 1);
                float a = haciaAbajo ? t : 1f - t;
                tex.SetPixel(0, y, new Color(1f, 1f, 1f, a * a)); // curva suave
            }
            tex.Apply();
        }
        return tex;
    }

    // ── Primitivas ────────────────────────────────────────────────────────
    public static void Fill(Rect r, Color c)
    {
        var prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, White());
        GUI.color = prev;
    }

    public static void Borde(Rect r, Color c, float t = 2f)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y + t, t, r.height - 2f * t), c);
        Fill(new Rect(r.xMax - t, r.y + t, t, r.height - 2f * t), c);
    }

    public static void Panel(Rect r, Color fill, Color borde, float t = 2f)
    {
        Fill(r, fill);
        Borde(r, borde, t);
    }

    // Rellena TODA la pantalla (incluye lo que queda FUERA del área segura: notch,
    // Dynamic Island, home indicator) con un color sólido. Se dibuja en px crudos
    // (resetea la GUI.matrix de UIScale y la restaura), así que sirve para tapar el
    // fondo 3D detrás de un menú opaco. Llamalo al principio del OnGUI.
    public static void FillScreen(Color c)
    {
        var m = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        Fill(new Rect(0, 0, Screen.width, Screen.height), c);
        // Overlay de scanlines (efecto CRT del prototipo HTML: cada 3px oscurece una fila).
        // Forma parte del mismo VHS opcional de los menus. Antes se dibujaba siempre,
        // aunque la preferencia persistida estuviera apagada, y parecia que la opcion se
        // habia reactivado al volver a cargar una escena.
        if (GameOptions.VhsActivo && _scanlines == null)
        {
            _scanlines = new Texture2D(1, 3, TextureFormat.RGBA32, false);
            _scanlines.filterMode = FilterMode.Point;
            _scanlines.wrapMode   = TextureWrapMode.Repeat;
            _scanlines.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
            _scanlines.SetPixel(0, 1, new Color(0f, 0f, 0f, 0f));
            _scanlines.SetPixel(0, 2, new Color(0f, 0f, 0f, 0.22f));
            _scanlines.Apply();
            _scanlines.hideFlags = HideFlags.HideAndDontSave;
        }
        if (GameOptions.VhsActivo)
        {
            var prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, Screen.width, Screen.height),
                                         _scanlines,
                                         new Rect(0, 0, 1f, Screen.height / 3f));
            GUI.color = prev;
        }
        GUI.matrix = m;
        ScanlinesYaPintadas = GameOptions.VhsActivo;
    }

    // ¿Alguna pantalla ya pintó las scanlines del tema en este frame? Lo consulta el
    // filtro VHS de los menús (US-11.1, VHSOverlayUI) para no dibujar un segundo juego
    // de líneas encima. Lo limpia ese mismo overlay, que corre último en el frame.
    public static bool ScanlinesYaPintadas { get; private set; }
    public static void LimpiarFlagScanlines() => ScanlinesYaPintadas = false;

    // Rellena SÓLO las franjas fuera del área segura (arriba/abajo/laterales) con un
    // color sólido, dejando el área segura intacta — para pantallas sobre la cámara
    // AR (escáner / sincronización): la cámara sigue visible dentro del área segura
    // y las franjas del notch / home indicator quedan en negro en vez de mostrar
    // cámara "cortada" junto al degradado de la UI.
    public static void FillOutsideSafeArea(Color c)
    {
        var sg = Scanner.SafeArea.GuiRect;   // px crudos, origen arriba-izq
        float sw = Screen.width, sh = Screen.height;
        var m = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        if (sg.y > 0f)     Fill(new Rect(0f, 0f, sw, sg.y), c);                          // arriba
        if (sg.yMax < sh)  Fill(new Rect(0f, sg.yMax, sw, sh - sg.yMax), c);             // abajo
        if (sg.x > 0f)     Fill(new Rect(0f, sg.y, sg.x, sg.height), c);                 // izquierda
        if (sg.xMax < sw)  Fill(new Rect(sg.xMax, sg.y, sw - sg.xMax, sg.height), c);    // derecha
        GUI.matrix = m;
    }

    // Franja con gradiente (negro -> transparente). haciaAbajo=true para la
    // franja superior (opaca arriba), false para la inferior (opaca abajo).
    public static void Gradiente(Rect r, float alpha, bool haciaAbajo)
    {
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.DrawTexture(r, Grad(haciaAbajo), ScaleMode.StretchToFill);
        GUI.color = prev;
    }

    // ── Barras de HUD (batería / cordura) ─────────────────────────────────
    // Geometría compartida para apilar las barras del HUD abajo-izquierda, en
    // coords VIRTUALES (tras UIScale.Begin). fila 0 = la de más abajo (batería),
    // fila 1 = encima (cordura). Así ambos componentes quedan alineados sin
    // conocerse entre sí.
    // Cada fila: ícono redondo a la izquierda, nombre y valor arriba, barra fina abajo
    // (HudFila). El alto de la fila es el del ícono.
    public const float HudBarW = 260f, HudBarH = 34f, HudBarGap = 10f, HudMargin = 44f;

    public static Rect HudBarRect(float vw, float vh, int fila)
    {
        float y = vh - HudMargin - HudBarH - fila * (HudBarH + HudBarGap);
        return new Rect(HudMargin, y, HudBarW, HudBarH);
    }

    // En Cardboard la esquina inf-izq de la pantalla cae fuera de las lentes (y debajo
    // del recorte de la nariz): las barras no se veían. Ahí se dibuja una copia por ojo,
    // centrada bajo el centro de cada uno — que MRCardboardController ubica en
    // W*0.25 / W*0.75 — y un poco por debajo de la mitad, dentro del campo cómodo.
    // Las dos copias quedan en la misma posición relativa a su ojo (disparidad 0), así
    // que se fusionan en una sola barra.
    public const float HudBarWCardboard = 200f;
    // Borde inferior de la fila 0 en Cardboard: grados por debajo del centro de cada ojo,
    // dentro de Rediseno.UI.ZonaNitida.Radio. En píxeles (0.66 del alto) caía a ~13° en un
    // 20:9 pero a ~16° en un 16:9, cerca del borde de lo nítido.
    public const float HudCardboardBaseGrados = 13f;

    // Devuelve cuántos rects hay que dibujar para la fila: 1 fuera de Cardboard (abajo-izq,
    // como siempre) o 2 en Cardboard (uno por ojo). Coords virtuales, tras UIScale.Begin.
    public static int HudBarRects(float vw, float vh, int fila, out Rect a, out Rect b)
    {
        if (!MRCardboardController.Activo)
        {
            a = HudBarRect(vw, vh, fila);
            b = default;
            return 1;
        }

        // Pixeles de pantalla → espacio virtual de UIScale (área segura escalada).
        var   sg = Scanner.UIScale.SafeGui;
        float s  = Scanner.UIScale.Factor;
        float w  = HudBarWCardboard;
        // El centro de cada ojo según la calibración del visor (zoom + distancia de cada
        // ojo). Con el centro de cada mitad, las dos copias de la barra no caían en el
        // mismo lugar y no se fusionaban.
        Rediseno.UI.OjosVisor.Centros(out var izq, out var der);
        float y = (izq.y + HudCardboardBaseGrados * Rediseno.UI.ZonaNitida.PixelesPorGrado - sg.y) / s
                - HudBarH - fila * (HudBarH + HudBarGap);
        float cxL = (izq.x - sg.x) / s;
        float cxR = (der.x - sg.x) / s;
        a = new Rect(cxL - w * 0.5f, y, w, HudBarH);
        b = new Rect(cxR - w * 0.5f, y, w, HudBarH);
        return 2;
    }

    // Atajo sin ícono (los objetivos de las prácticas del tutorial): mismo estilo de fila.
    public static void HudBarra(float vw, float vh, int fila, float pct01, Color fill,
                                string etiqueta, string valor) =>
        HudFila(vw, vh, fila, pct01, fill, default, etiqueta, valor);

    // Fila de OBJETIVOS de las prácticas: el nombre a la izquierda y un casillero por
    // objetivo a la derecha, que se completa (check verde) al lograrlo. Mismo lugar y placa
    // que HudFila.
    public static void HudObjetivos(float vw, float vh, int fila, int logrados, int total, string etiqueta)
    {
        int n = HudBarRects(vw, vh, fila, out var a, out var b);
        HudObjetivos(a, logrados, total, etiqueta);
        if (n > 1) HudObjetivos(b, logrados, total, etiqueta);
    }

    public static void HudObjetivos(Rect r, int logrados, int total, string etiqueta)
    {
        float h = r.height;
        if (Event.current.type == EventType.Repaint)
        {
            FillR(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), HudPlaca, (h + 8f) * 0.5f);
            float lado = h - 8f, sep = 6f;
            float x = r.xMax - 6f - total * lado - (total - 1) * sep;
            var prev = GUI.color;
            for (int i = 0; i < total; i++)
            {
                bool hecho = i < logrados;
                GUI.color = prev * (hecho ? Green : new Color(CreamDim.r, CreamDim.g, CreamDim.b, 0.55f));
                GUI.DrawTexture(new Rect(x + i * (lado + sep), r.y + 4f, lado, lado),
                                hecho ? Rediseno.IconosHud.CasillaHecha : Rediseno.IconosHud.Casilla);
            }
            GUI.color = prev;
        }
        if (!string.IsNullOrEmpty(etiqueta))
            GUI.Label(new Rect(r.x + 12f, r.y, r.width - 24f, h), etiqueta,
                      Estilo(FBebas, 18, logrados >= total ? Green : CreamDim, TextAnchor.MiddleLeft));
    }

    // Ícono de una fila del HUD. tex = el ícono (blanco con alfa, se tiñe con color);
    // capa = una segunda textura con su propio color (las grietas del cerebro); nivel >= 0
    // = la pila: se rellena por dentro hasta ese nivel; rayo = la luz intensa (se dibuja
    // sobre el nivel). sacudir = píxeles de temblor (el cerebro al empeorar de etapa).
    public struct IconoHud
    {
        public Texture2D tex, capa;
        public Color color, colorCapa;
        public float nivel, sacudir;
        public bool rayo;
        public Texture2D texRayo;
    }

    // Fila del HUD de la noche (Rediseño de las barras, opción A): ícono redondo, nombre y
    // valor ARRIBA de la barra (nunca sobre el relleno, que con poca carga dejaba el texto
    // blanco ilegible), barra fina abajo y, si hay, una etiqueta chica al lado del nombre
    // ("INTENSA ×3"). fantasma01 > pct01 dibuja en rojo lo recién perdido (un golpe).
    // Una vez por ojo con el visor (HudBarRects).
    public static void HudFila(float vw, float vh, int fila, float pct01, Color color, IconoHud icono,
                               string etiqueta, string valor, string etiqueta2 = null,
                               Color? colorEtiqueta2 = null, float fantasma01 = -1f, bool resaltar = false)
    {
        int n = HudBarRects(vw, vh, fila, out var a, out var b);
        HudFila(a, pct01, color, icono, etiqueta, valor, etiqueta2, colorEtiqueta2, fantasma01, resaltar);
        if (n > 1) HudFila(b, pct01, color, icono, etiqueta, valor, etiqueta2, colorEtiqueta2, fantasma01, resaltar);
    }

    private static readonly Color HudPlaca = new(0.024f, 0.02f, 0.016f, 0.62f);
    private static readonly Color HudChip  = new(0.08f, 0.063f, 0.055f, 0.94f);
    private static readonly Color HudPista = new(0.914f, 0.89f, 0.84f, 0.12f);
    private static readonly Color HudGolpe = new(0.784f, 0.196f, 0.243f, 0.75f);

    public static void HudFila(Rect r, float pct01, Color color, IconoHud icono, string etiqueta, string valor,
                               string etiqueta2 = null, Color? colorEtiqueta2 = null, float fantasma01 = -1f,
                               bool resaltar = false)
    {
        bool conIcono = icono.tex != null;
        float h = r.height;
        float x0 = conIcono ? r.x + h + 8f : r.x + 10f;
        float wTexto = r.xMax - x0 - (conIcono ? 4f : 10f);

        if (Event.current.type == EventType.Repaint)
        {
            // Placa oscura detrás de toda la fila: el texto se lee sobre cualquier fondo.
            var placa = new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f);
            FillR(placa, HudPlaca, (h + 8f) * 0.5f);
            // Resaltada (la presentación de la cordura en el tutorial): borde dorado que late.
            if (resaltar)
                BordeR(placa, new Color(Tan.r, Tan.g, Tan.b, 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f))),
                       (h + 8f) * 0.5f, 2.5f);

            if (conIcono)
            {
                var chip = new Rect(r.x, r.y, h, h);
                FillR(chip, HudChip, h * 0.5f);
                BordeR(chip, new Color(Linea2.r, Linea2.g, Linea2.b, 0.18f), h * 0.5f, 1f);
                Vector2 temblor = icono.sacudir > 0f
                    ? new Vector2(Mathf.PerlinNoise(Time.unscaledTime * 40f, 0.1f) - 0.5f,
                                  Mathf.PerlinNoise(0.7f, Time.unscaledTime * 40f) - 0.5f) * 2f * icono.sacudir
                    : Vector2.zero;
                var ir = new Rect(chip.x + 5f + temblor.x, chip.y + 5f + temblor.y, h - 10f, h - 10f);
                var prev = GUI.color;
                GUI.color = prev * icono.color;
                GUI.DrawTexture(ir, icono.tex);
                if (icono.nivel >= 0f)
                {
                    // Interior de la pila (coords normalizadas del ícono → píxeles).
                    float nx0 = Rediseno.IconosHud.PilaXMin, nx1 = Rediseno.IconosHud.PilaXMax;
                    float nh = Rediseno.IconosHud.PilaMedioAlto;
                    float px0 = ir.x + (nx0 + 1f) * 0.5f * ir.width;
                    float pw = (nx1 - nx0) * 0.5f * ir.width * Mathf.Clamp01(icono.nivel);
                    float py = ir.y + (1f - nh) * 0.5f * ir.height;
                    float ph = nh * ir.height;
                    if (pw > 0.5f) GUI.DrawTexture(new Rect(px0, py, pw, ph), White());
                }
                if (icono.rayo && icono.texRayo != null)
                {
                    GUI.color = prev * new Color(Bg.r, Bg.g, Bg.b, 0.9f);
                    GUI.DrawTexture(ir, icono.texRayo);
                }
                if (icono.capa != null)
                {
                    GUI.color = prev * icono.colorCapa;
                    GUI.DrawTexture(ir, icono.capa);
                }
                GUI.color = prev;
            }

            // Barra fina abajo, con lo recién perdido en rojo.
            var pista = new Rect(x0, r.yMax - 8f, wTexto, 6f);
            FillR(pista, HudPista, 3f);
            float p = Mathf.Clamp01(pct01);
            if (fantasma01 > p + 0.002f)
            {
                float f = Mathf.Clamp01(fantasma01);
                FillR(new Rect(pista.x, pista.y, pista.width * f, pista.height), HudGolpe, 3f);
            }
            if (p > 0.002f)
                FillR(new Rect(pista.x, pista.y, Mathf.Max(pista.width * p, pista.height), pista.height), color, 3f);
        }

        // Nombre y valor arriba de la barra.
        var linea = new Rect(x0, r.y - 1f, wTexto, 20f);
        var stNombre = Estilo(FBebas, 16, CreamDim, TextAnchor.MiddleLeft);
        if (!string.IsNullOrEmpty(etiqueta)) GUI.Label(linea, etiqueta, stNombre);
        if (!string.IsNullOrEmpty(valor))
            GUI.Label(linea, valor, Estilo(FMono, 12, Cream, TextAnchor.MiddleRight));
        if (!string.IsNullOrEmpty(etiqueta2) && Event.current.type == EventType.Repaint)
        {
            float wNombre = string.IsNullOrEmpty(etiqueta) ? 0f : stNombre.CalcSize(new GUIContent(etiqueta)).x;
            var stTag = Estilo(FMono, 9, Bg, TextAnchor.MiddleCenter);
            float wTag = stTag.CalcSize(new GUIContent(etiqueta2)).x + 10f;
            var tag = new Rect(x0 + wNombre + 6f, linea.y + 3f, wTag, 14f);
            FillR(tag, colorEtiqueta2 ?? color, 7f);
            GUI.Label(tag, etiqueta2, stTag);
        }
    }

    // Barra 2.0: píldora oscura con borde fino y el relleno redondeado adentro. Etiqueta
    // en Bebas a la izquierda, valor en mono a la derecha.
    public static void Barra2(Rect r, float pct01, Color fill, string etiqueta, string valor)
    {
        if (Event.current.type == EventType.Repaint)
        {
            float radio = r.height * 0.5f;
            Tarjeta2(r, Hoja2, null, radio);
            const float inner = 3f;
            var track = new Rect(r.x + inner, r.y + inner, r.width - 2f * inner, r.height - 2f * inner);
            float w = track.width * Mathf.Clamp01(pct01);
            // Nunca más angosto que alto: con poca carga queda un punto redondo, no una astilla.
            if (w > 0.5f)
                FillR(new Rect(track.x, track.y, Mathf.Max(w, track.height), track.height),
                      new Color(fill.r, fill.g, fill.b, 0.8f), track.height * 0.5f);
        }

        if (!string.IsNullOrEmpty(etiqueta))
            GUI.Label(new Rect(r.x + 12f, r.y, r.width - 24f, r.height), etiqueta,
                      Estilo(FBebas, 15, Cream, TextAnchor.MiddleLeft));
        if (!string.IsNullOrEmpty(valor))
            GUI.Label(new Rect(r.x + 12f, r.y, r.width - 24f, r.height), valor,
                      Estilo(FMono, 12, Cream, TextAnchor.MiddleRight));
    }

    // Barra estilo Mortuorium: fondo + borde + relleno proporcional, con etiqueta a
    // la izquierda y valor a la derecha (mismo lenguaje que los sliders del pausa).
    public static void Barra(Rect r, float pct01, Color fill, string etiqueta, string valor)
    {
        Fill(r, new Color(0f, 0f, 0f, 0.55f));
        Borde(r, Border);

        const float inner = 3f;
        var track = new Rect(r.x + inner, r.y + inner, r.width - 2f * inner, r.height - 2f * inner);
        Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(pct01), track.height), fill);

        if (!string.IsNullOrEmpty(etiqueta))
            GUI.Label(new Rect(r.x + 10f, r.y, r.width - 20f, r.height), etiqueta,
                      Estilo(FMono, 12, Cream, TextAnchor.MiddleLeft));
        if (!string.IsNullOrEmpty(valor))
            GUI.Label(new Rect(r.x + 10f, r.y, r.width - 20f, r.height), valor,
                      Estilo(FMono, 12, Cream, TextAnchor.MiddleRight));
    }

    // ── Widgets ───────────────────────────────────────────────────────────

    // Botón estándar del tema: borde 2px + texto Bebas centrado.
    //   primario=true  -> borde rojo (acción principal)
    //   con foco del gamepad -> borde/tint dorado
    // nav puede ser null (botón fuera de la navegación con mando).
    public static bool Boton(ImguiGamepadMenu nav, Rect r, string label, bool primario,
                             Action onClick, bool enabled = true, int fontSize = 20,
                             Color? textColor = null)
    {
        bool foco = nav != null && enabled && ImguiGamepadMenu.NextHasFocus;
        Color borde = !enabled ? BorderDim : foco ? Tan : primario ? Red : Border;
        if (foco) Fill(r, new Color(Tan.r, Tan.g, Tan.b, 0.14f));
        else Fill(r, new Color(0f, 0f, 0f, 0.35f));
        Borde(r, borde);

        var st = Estilo(FBebas, fontSize,
                        !enabled ? Disabled : (textColor ?? (primario ? Cream : CreamDim)),
                        TextAnchor.MiddleCenter);

        bool prevEnabled = GUI.enabled;
        GUI.enabled = enabled;
        // GUI.Button no renderiza texto en este entorno — separamos click-detection
        // (botón invisible) del texto (GUI.Label encima).
        var invisible = Estilo(FMono, 1, Color.clear, TextAnchor.MiddleCenter);
        bool clicked;
        // El sonido va DENTRO de la acción, no al lado del `clicked`: por acá pasan tanto
        // el tap como la activación con el mando (ImguiGamepadMenu invoca esta misma
        // Action desde su Tick), así suena una sola vez en los dos caminos.
        if (nav != null)
            clicked = nav.Button(r, "", invisible,
                                 () => { if (enabled) { AudioManager.Sonar(c => c.uiConfirmar); onClick?.Invoke(); } });
        else
        {
            clicked = GUI.Button(r, "", invisible);
            if (clicked && enabled) { AudioManager.Sonar(c => c.uiConfirmar); onClick?.Invoke(); }
        }
        GUI.enabled = prevEnabled;
        GUI.Label(r, label, st);
        return clicked;
    }

    // Botón "volver" (flecha en la esquina sup-izq de cada pantalla).
    public static void BotonVolver(ImguiGamepadMenu nav, Action onClick, float x = 16f, float y = 14f)
    {
        var r = new Rect(x, y, 56f, 44f);
        bool foco = nav != null && ImguiGamepadMenu.NextHasFocus;
        if (foco) { Fill(r, new Color(Tan.r, Tan.g, Tan.b, 0.14f)); Borde(r, Tan); }
        var st = Estilo(FBebas, 26, foco ? Cream : Muted, TextAnchor.MiddleCenter);
        var invisible = Estilo(FMono, 1, Color.clear, TextAnchor.MiddleCenter);
        Action volver = () => { AudioManager.Sonar(c => c.uiVolver); onClick?.Invoke(); };
        // Rediseño 2.0: la ← del mando hace lo mismo que este botón (ver ImguiGamepadMenu).
        if (Event.current.type == EventType.Repaint) ImguiGamepadMenu.RegistrarVolver(volver);
        if (nav != null) nav.Button(r, "", invisible, volver);
        else if (GUI.Button(r, "", invisible)) volver();
        GUI.Label(r, "<", st);
    }

    // ══ Estilo 2.0 en IMGUI ════════════════════════════════════════════════
    // El lenguaje del Rediseño 2.0 (Rediseno.UI.TemaUI: tarjetas con esquinas redondeadas,
    // borde fino claro, botón principal con degradé rojo, foco dorado) para las pantallas
    // que siguen siendo IMGUI, como el escáner. Funciones aparte a propósito: Boton/Fill/
    // Borde siguen dibujando el estilo viejo para los menús que todavía lo usan.
    //
    // Las esquinas las dibuja el propio IMGUI (GUI.DrawTexture con radio: un solo draw por
    // rectángulo, con antialias). Si el build no trajera su shader interno, cae a esquinas
    // rectas en vez de dibujar mal.
    public const float Radio2 = 12f;                                      // tarjetas / botones
    public static readonly Color Panel2 = new(0.086f, 0.071f, 0.063f, 0.78f);   // TemaUI.Panel
    public static readonly Color Hoja2  = new(0.047f, 0.039f, 0.035f, 0.92f);   // paneles sobre la cámara
    public static readonly Color Linea2 = new(0.914f, 0.890f, 0.839f, 0.16f);   // TemaUI.Linea
    public static readonly Color Rojo2  = new(0.784f, 0.196f, 0.243f, 1f);      // TemaUI.Red

    private static bool? _redondeoOk;
    private static bool RedondeoOk =>
        _redondeoOk ??= Application.isEditor || Shader.Find("Hidden/Internal-GUIRoundedRect") != null;

    public static void FillR(Rect r, Color c, float radio = Radio2)
    {
        if (!RedondeoOk) { Fill(r, c); return; }
        GUI.DrawTexture(r, White(), ScaleMode.StretchToFill, true, 0f, c * GUI.color, 0f, radio);
    }

    public static void BordeR(Rect r, Color c, float radio = Radio2, float ancho = 1.5f)
    {
        if (!RedondeoOk) { Borde(r, c, ancho); return; }
        GUI.DrawTexture(r, White(), ScaleMode.StretchToFill, true, 0f, c * GUI.color, ancho, radio);
    }

    // Tarjeta: relleno oscuro translúcido + borde fino claro.
    public static void Tarjeta2(Rect r, Color? relleno = null, Color? borde = null, float radio = Radio2,
                                float ancho = 1.5f)
    {
        FillR(r, relleno ?? Panel2, radio);
        BordeR(r, borde ?? Linea2, radio, ancho);
    }

    // Relleno con el degradé rojo del botón principal (rojo arriba → bordó abajo).
    public static void FillRojo(Rect r, float radio = Radio2)
    {
        var tex = Rediseno.UI.TemaUI.DegradeRojo;
        if (!RedondeoOk) { GUI.DrawTexture(r, tex, ScaleMode.StretchToFill); return; }
        GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true, 0f, GUI.color, 0f, radio);
    }

    // Botón 2.0. primario = degradé rojo; secundario = tarjeta. Foco del mando (o
    // 'resaltado') = borde dorado. Deshabilitado = todo apagado.
    public static bool Boton2(ImguiGamepadMenu nav, Rect r, string label, bool primario,
                              Action onClick, bool enabled = true, int fontSize = 17,
                              Color? textColor = null, float radio = Radio2)
    {
        bool foco = nav != null && enabled && ImguiGamepadMenu.NextHasFocus;
        if (Event.current.type == EventType.Repaint)
        {
            if (!enabled) Tarjeta2(r, new Color(0f, 0f, 0f, 0.45f), new Color(Linea2.r, Linea2.g, Linea2.b, 0.08f), radio);
            else if (primario) { FillRojo(r, radio); BordeR(r, new Color(1f, 1f, 1f, 0.10f), radio); }
            else Tarjeta2(r, null, null, radio);
            if (foco) BordeR(r, Tan, radio, 2.5f);
        }

        var st = Estilo(FBebas, fontSize,
                        !enabled ? Disabled : (textColor ?? Cream), TextAnchor.MiddleCenter);
        var invisible = Estilo(FMono, 1, Color.clear, TextAnchor.MiddleCenter);
        bool prevEnabled = GUI.enabled;
        GUI.enabled = enabled;
        bool clicked;
        if (nav != null)
            clicked = nav.Button(r, "", invisible,
                                 () => { if (enabled) { AudioManager.Sonar(c => c.uiConfirmar); onClick?.Invoke(); } });
        else
        {
            clicked = GUI.Button(r, "", invisible);
            if (clicked && enabled) { AudioManager.Sonar(c => c.uiConfirmar); onClick?.Invoke(); }
        }
        GUI.enabled = prevEnabled;
        GUI.Label(r, label, st);
        return clicked;
    }

    // Botón "volver" redondo (como el encabezado de las pantallas 2.0).
    public static void BotonVolver2(ImguiGamepadMenu nav, Action onClick, float x = 16f, float y = 14f)
    {
        var r = new Rect(x, y, 44f, 44f);
        bool foco = nav != null && ImguiGamepadMenu.NextHasFocus;
        if (Event.current.type == EventType.Repaint)
        {
            FillR(r, new Color(0.024f, 0.02f, 0.016f, 0.45f), 22f);
            BordeR(r, foco ? Tan : new Color(1f, 1f, 1f, 0.25f), 22f, foco ? 2.5f : 1.5f);
        }
        var st = Estilo(FBebas, 24, Cream, TextAnchor.MiddleCenter);
        var invisible = Estilo(FMono, 1, Color.clear, TextAnchor.MiddleCenter);
        Action volver = () => { AudioManager.Sonar(c => c.uiVolver); onClick?.Invoke(); };
        if (Event.current.type == EventType.Repaint) ImguiGamepadMenu.RegistrarVolver(volver);
        if (nav != null) nav.Button(r, "", invisible, volver);
        else if (GUI.Button(r, "", invisible)) volver();
        GUI.Label(r, "<", st);
    }

    // Slider 2.0: pista redondeada, relleno dorado, perilla redonda crema.
    public static float Slider2(Rect r, float valor, float min, float max)
    {
        float t = Mathf.InverseLerp(min, max, valor);
        if (Event.current.type == EventType.Repaint)
        {
            var track = new Rect(r.x, r.y + r.height * 0.5f - 3f, r.width, 6f);
            FillR(track, new Color(1f, 1f, 1f, 0.10f), 3f);
            if (t > 0f) FillR(new Rect(track.x, track.y, track.width * t, track.height), Tan, 3f);
            const float d = 18f;
            var thumb = new Rect(track.x + track.width * t - d * 0.5f, r.y + r.height * 0.5f - d * 0.5f, d, d);
            FillR(thumb, Cream, d * 0.5f);
        }
        _sliderBg  ??= new GUIStyle();
        _sliderThumb2 ??= new GUIStyle { fixedWidth = 18f, fixedHeight = 26f };
        return GUI.HorizontalSlider(r, valor, min, max, _sliderBg, _sliderThumb2);
    }

    private static GUIStyle _sliderBg, _sliderThumb2;

    // Campo de texto 2.0 (tarjeta redondeada). En device, tocar el campo abre el teclado
    // nativo (comportamiento estándar de IMGUI en iOS/Android).
    public static string CampoTexto2(Rect r, string valor, string placeholder)
    {
        if (Event.current.type == EventType.Repaint) Tarjeta2(r, BgField);
        var st = _fieldStyle ??= CrearFieldStyle();
        string nuevo = GUI.TextField(new Rect(r.x + 14f, r.y, r.width - 28f, r.height), valor ?? "", st);
        if (string.IsNullOrEmpty(nuevo) && !string.IsNullOrEmpty(placeholder))
            GUI.Label(new Rect(r.x + 14f, r.y, r.width - 28f, r.height), placeholder,
                      Estilo(FMono, 15, Dim));
        return nuevo;
    }

    private static GUIStyle _fieldStyle;
    private static GUIStyle CrearFieldStyle()
    {
        var st = new GUIStyle(GUI.skin.textField)
        {
            // En Android las fuentes custom no renderizan bien (ver Estilo()) — con
            // FMono directo el texto se actualiza pero el glyph no se pinta, así que
            // parece que "no se ve lo que escribís" aunque el valor sí cambie.
            font = Application.platform == RuntimePlatform.Android ? null : FMono,
            fontSize = 15, alignment = TextAnchor.MiddleLeft,
        };
        st.normal.textColor = st.focused.textColor = st.hover.textColor = st.active.textColor = Cream;
        st.normal.background = st.focused.background = st.hover.background = st.active.background = null;
        return st;
    }
}
