using System;
using System.Collections.Generic;
using UnityEngine;
using Gamepad;

// Tema visual "Mortuorium" — paleta, tipografías y widgets IMGUI compartidos por
// todos los menús, portado del prototipo HTML (Assets/Prototipo Navegacion).
//
// Uso: dentro de un OnGUI que ya llamó a UIScale.Begin(), dibujar con las
// primitivas (Fill/Borde/Panel) y los widgets (Boton, Celda, CampoTexto, Slider).
// Los botones se integran con ImguiGamepadMenu para navegarse con el mando: el
// resaltado de foco lo pinta este tema (borde dorado) en vez del tint amarillo.
//
// Tipografías (Assets/Resources/Fonts): Bebas Neue (títulos/botones), Special
// Elite (texto "máquina de escribir"), IBM Plex Mono (datos). Si faltan los
// assets, cae al font por defecto del skin.
public static class MortuoriumTheme
{
    // ── Paleta (hex del prototipo) ────────────────────────────────────────
    public static readonly Color Bg        = Hex("0c0a08");   // fondo general
    public static readonly Color BgPanel   = Hex("141110");   // paneles/modales
    public static readonly Color BgField   = Hex("131010");   // campos de texto
    public static readonly Color Cream     = Hex("e9e3d6");   // texto principal
    public static readonly Color CreamDim  = Hex("c9c2b4");   // texto secundario
    public static readonly Color Muted     = Hex("8a8074");   // texto apagado
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

    // ── Estilos (cacheados por combinación) ───────────────────────────────
    private static readonly Dictionary<string, GUIStyle> _styles = new();

    public static GUIStyle Estilo(Font font, int size, Color color,
                                  TextAnchor anchor = TextAnchor.MiddleLeft,
                                  bool wrap = false)
    {
        string key = $"{(font != null ? font.name : "def")}|{size}|{ColorUtility.ToHtmlStringRGBA(color)}|{(int)anchor}|{wrap}";
        if (_styles.TryGetValue(key, out var st)) return st;
        st = new GUIStyle
        {
            font      = font,           // null => font por defecto del skin
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
    private static Texture2D _white, _gradDown, _gradUp;

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
        GUI.matrix = m;
    }

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

    // Ícono de candado dibujado con rects (no depende de fuentes con emoji).
    // Se ajusta al rect r: cuerpo abajo + arco (U invertida) arriba + cerradura.
    public static void Candado(Rect r, Color c)
    {
        float bodyH = r.height * 0.58f;
        var body = new Rect(r.x, r.yMax - bodyH, r.width, bodyH);

        // Arco: dos verticales + travesaño superior, centrado y más angosto que el cuerpo.
        float t     = Mathf.Max(2f, r.width * 0.14f);
        float archW = r.width * 0.62f;
        float archX = r.x + (r.width - archW) * 0.5f;
        float archH = (r.height - bodyH) + t;   // baja un poco tras el cuerpo
        Fill(new Rect(archX, r.y, t, archH), c);
        Fill(new Rect(archX + archW - t, r.y, t, archH), c);
        Fill(new Rect(archX, r.y, archW, t), c);

        // Cuerpo.
        Fill(body, c);

        // Cerradura (agujero) en el centro del cuerpo, con el color del fondo.
        float kh = bodyH * 0.42f, kw = Mathf.Max(2f, r.width * 0.12f);
        Fill(new Rect(body.center.x - kw * 0.5f, body.y + bodyH * 0.28f, kw, kh), Bg);
    }

    // ── Barras de HUD (batería / cordura) ─────────────────────────────────
    // Geometría compartida para apilar las barras del HUD abajo-izquierda, en
    // coords VIRTUALES (tras UIScale.Begin). fila 0 = la de más abajo (batería),
    // fila 1 = encima (cordura). Así ambos componentes quedan alineados sin
    // conocerse entre sí.
    public const float HudBarW = 240f, HudBarH = 26f, HudBarGap = 8f, HudMargin = 20f;

    public static Rect HudBarRect(float vw, float vh, int fila)
    {
        float y = vh - HudMargin - HudBarH - fila * (HudBarH + HudBarGap);
        return new Rect(HudMargin, y, HudBarW, HudBarH);
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

    // Título con "glitch" cromático (sombra roja/teal) tipo MORTUORIUM.
    public static void TituloGlitch(Rect r, string texto, int size)
    {
        var rojo = new Color(1f, 0.12f, 0.20f, 0.55f);
        var teal = new Color(0f, 0.86f, 0.78f, 0.35f);
        GUI.Label(new Rect(r.x + 2f, r.y, r.width, r.height), texto, Estilo(FBebas, size, rojo, TextAnchor.MiddleCenter));
        GUI.Label(new Rect(r.x - 2f, r.y, r.width, r.height), texto, Estilo(FBebas, size, teal, TextAnchor.MiddleCenter));
        GUI.Label(r, texto, Estilo(FBebas, size, Cream, TextAnchor.MiddleCenter));
    }

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
        bool clicked;
        if (nav != null)
            clicked = nav.Button(r, label, st, () => { if (enabled) onClick?.Invoke(); });
        else
        {
            clicked = GUI.Button(r, label, st);
            if (clicked && enabled) onClick?.Invoke();
        }
        GUI.enabled = prevEnabled;
        return clicked;
    }

    // Botón "volver" (flecha en la esquina sup-izq de cada pantalla).
    public static void BotonVolver(ImguiGamepadMenu nav, Action onClick, float x = 16f, float y = 14f)
    {
        var r = new Rect(x, y, 56f, 44f);
        bool foco = nav != null && ImguiGamepadMenu.NextHasFocus;
        if (foco) { Fill(r, new Color(Tan.r, Tan.g, Tan.b, 0.14f)); Borde(r, Tan); }
        var st = Estilo(FBebas, 26, foco ? Cream : Muted, TextAnchor.MiddleCenter);
        if (nav != null) nav.Button(r, "<", st, onClick);
        else if (GUI.Button(r, "<", st)) onClick?.Invoke();
    }

    // Celda/fila navegable con dibujo custom: pinta fondo+borde (con foco) y un
    // botón invisible; el contenido se dibuja después por el caller sobre el rect.
    // Devuelve el color de borde usado (por si el caller quiere combinar).
    public static void Celda(ImguiGamepadMenu nav, Rect r, bool primario, Action onClick,
                             bool enabled = true, Color? bordeOverride = null,
                             Color? fillOverride = null)
    {
        bool foco = nav != null && enabled && ImguiGamepadMenu.NextHasFocus;
        Color borde = !enabled ? BorderDim : foco ? Tan : bordeOverride ?? (primario ? Red : Border);
        if (foco) Fill(r, new Color(Tan.r, Tan.g, Tan.b, 0.12f));
        else Fill(r, fillOverride ?? new Color(0f, 0f, 0f, 0.35f));
        Borde(r, borde);

        bool prevEnabled = GUI.enabled;
        GUI.enabled = enabled;
        var st = Estilo(FMono, 1, Color.clear, TextAnchor.MiddleCenter);
        if (nav != null) nav.Button(r, "", st, () => { if (enabled) onClick?.Invoke(); });
        else if (GUI.Button(r, "", st) && enabled) onClick?.Invoke();
        GUI.enabled = prevEnabled;
    }

    // Campo de texto estilizado. En device, tocar el campo abre el teclado nativo
    // (comportamiento estándar de IMGUI en iOS/Android).
    public static string CampoTexto(Rect r, string valor, string placeholder)
    {
        Fill(r, BgField);
        Borde(r, Border);
        var st = _fieldStyle ??= CrearFieldStyle();
        string nuevo = GUI.TextField(new Rect(r.x + 12f, r.y, r.width - 24f, r.height), valor ?? "", st);
        if (string.IsNullOrEmpty(nuevo) && !string.IsNullOrEmpty(placeholder))
            GUI.Label(new Rect(r.x + 12f, r.y, r.width - 24f, r.height), placeholder,
                      Estilo(FMono, 15, Dim));
        return nuevo;
    }

    private static GUIStyle _fieldStyle;
    private static GUIStyle CrearFieldStyle()
    {
        var st = new GUIStyle(GUI.skin.textField)
        {
            font = FMono, fontSize = 15, alignment = TextAnchor.MiddleLeft,
        };
        st.normal.textColor = st.focused.textColor = st.hover.textColor = st.active.textColor = Cream;
        st.normal.background = st.focused.background = st.hover.background = st.active.background = null;
        return st;
    }

    // Slider horizontal con la estética del prototipo (track fino, acento rojo).
    public static float Slider(Rect r, float valor, float min, float max)
    {
        float t = Mathf.InverseLerp(min, max, valor);
        var track = new Rect(r.x, r.y + r.height * 0.5f - 2f, r.width, 4f);
        Fill(track, BorderDim);
        Fill(new Rect(track.x, track.y, track.width * t, track.height), Red);

        // Slider invisible encima para el drag (estilos transparentes + thumb rojo).
        _sliderBg    ??= new GUIStyle();
        _sliderThumb ??= CrearThumbStyle();
        float nuevo = GUI.HorizontalSlider(r, valor, min, max, _sliderBg, _sliderThumb);
        return nuevo;
    }

    private static GUIStyle _sliderBg, _sliderThumb;
    private static Texture2D _thumbTex;
    private static GUIStyle CrearThumbStyle()
    {
        _thumbTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _thumbTex.SetPixel(0, 0, Red);
        _thumbTex.Apply();
        _thumbTex.hideFlags = HideFlags.HideAndDontSave;
        var st = new GUIStyle { fixedWidth = 14f, fixedHeight = 26f };
        st.normal.background = _thumbTex;
        return st;
    }

    // Fila de opción con toggle ON/OFF a la derecha (estilo prototipo).
    public static void FilaToggle(ImguiGamepadMenu nav, Rect r, string titulo, string subtitulo,
                                  bool valor, Action onToggle)
    {
        Celda(nav, r, primario: false, onClick: onToggle);
        GUI.Label(new Rect(r.x + 14f, r.y + 8f, r.width - 120f, 22f), titulo, Estilo(FMono, 14, Cream));
        if (!string.IsNullOrEmpty(subtitulo))
            GUI.Label(new Rect(r.x + 14f, r.y + 30f, r.width - 120f, 18f), subtitulo, Estilo(FMono, 11, Dim));
        var pill = new Rect(r.xMax - 74f, r.y + (r.height - 30f) * 0.5f, 58f, 30f);
        Borde(pill, valor ? Tan : Border);
        GUI.Label(pill, valor ? "ON" : "OFF", Estilo(FMono, 12, valor ? Tan : Dim, TextAnchor.MiddleCenter));
    }
}
