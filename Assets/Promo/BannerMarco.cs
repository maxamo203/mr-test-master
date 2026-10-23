#if UNITY_EDITOR
using UnityEngine;

// Marco dorado con rombos en las esquinas + el wordmark MORTUORIUM arriba, encima del render
// del banner. Es el mismo marco que los banners del stand (BannerMarco del visor de banners:
// línea #d6b275 de 0,25 cm a 1,8 cm del borde y rombos de 1,4 cm centrados en las esquinas),
// así la foto de Sorken + Veleth combina con las otras piezas.
//
// Va DESPUÉS de BannerCamaraFX en la cámara (OnRenderImage corre en el orden de los
// componentes): el marco y el logo quedan nítidos, sin bloom, viñeta, aberración ni grano.
// Se ve en el Game view y sale igual en Mortuorium > Banner > Renderizar PNG. Las medidas
// están en cm del banner impreso y se pasan a píxeles con el ancho de la imagen, así la vista
// previa y el render de imprenta quedan iguales.
//
// TIER: editor-only (archivo entero bajo #if UNITY_EDITOR, como BannerCamaraFX).
[ExecuteAlways, RequireComponent(typeof(Camera))]
public class BannerMarco : MonoBehaviour
{
    [Tooltip("Ancho del banner impreso: es la escala de todas las medidas en cm.")]
    public float anchoBannerCm = 90f;

    [Header("Marco dorado")]
    public bool marco = true;
    [Range(0f, 8f)] public float margenCm = 1.8f;
    [Range(0.05f, 1.5f)] public float grosorCm = 0.25f;
    [Range(0f, 5f)] public float romboCm = 1.4f;
    public Color colorMarco = new Color32(0xd6, 0xb2, 0x75, 0xff);

    [Header("Logo")]
    public Texture2D logo;
    [Range(0.2f, 0.95f)] public float anchoLogo = 0.62f;
    [Tooltip("Distancia del borde superior de la imagen al borde superior del logo.")]
    [Range(0f, 60f)] public float arribaCm = 7f;
    [Tooltip("Corrimiento horizontal desde el centro (+ = derecha).")]
    [Range(-30f, 30f)] public float corrimientoCm = 0f;

    [Header("Sombra del logo (lo despega del fondo)")]
    [Range(0f, 1f)] public float sombra = 0.55f;
    [Range(0f, 4f)] public float radioSombraCm = 1.2f;

    private Material _mat;

    private void OnDisable()
    {
        if (_mat != null) DestroyImmediate(_mat);
        _mat = null;
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        // Se dibuja en una RT propia (no en dst, que en el Game view puede ser la pantalla
        // con otro viewport) y con MSAA, para que las diagonales de los rombos no serruchen.
        // En los renders de alta resolución el escalón ya no se ve y una RT HDR con 8× MSAA
        // pasaría de los 3 GB: ahí va con 2×.
        var d = src.descriptor;
        d.msaaSamples = src.width > 3000 ? 2 : 8;
        d.depthBufferBits = 0;
        var tmp = RenderTexture.GetTemporary(d);
        Graphics.Blit(src, tmp);
        Dibujar(tmp);
        Graphics.Blit(tmp, dst);
        RenderTexture.ReleaseTemporary(tmp);
    }

    private void Dibujar(RenderTexture rt)
    {
        float w = rt.width, h = rt.height;
        float cm = w / Mathf.Max(anchoBannerCm, 1f);

        RenderTexture previa = RenderTexture.active;
        RenderTexture.active = rt;
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, w, h, 0f); // y hacia abajo, como en el HTML

        if (logo != null) DibujarLogo(w, cm);
        if (marco) DibujarMarco(w, h, cm);

        GL.PopMatrix();
        RenderTexture.active = previa;
    }

    private void DibujarLogo(float w, float cm)
    {
        float lw = w * anchoLogo;
        float lh = lw * logo.height / logo.width;
        var r = new Rect((w - lw) * 0.5f + corrimientoCm * cm, arribaCm * cm, lw, lh);
        var todo = new Rect(0f, 0f, 1f, 1f);

        // Sombra suave: el logo en negro repetido en anillos alrededor. El color de
        // Graphics.DrawTexture tiene neutro 0,5, de ahí la mitad en el alfa.
        if (sombra > 0f && radioSombraCm > 0f)
        {
            var negro = new Color(0f, 0f, 0f, sombra * 0.5f * 0.12f);
            for (int anillo = 1; anillo <= 3; anillo++)
            {
                float rad = radioSombraCm * cm * anillo / 3f;
                for (int i = 0; i < 12; i++)
                {
                    float a = (i + anillo * 0.5f) / 12f * Mathf.PI * 2f;
                    var o = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                    Graphics.DrawTexture(new Rect(r.position + o, r.size), logo, todo, 0, 0, 0, 0, negro);
                }
            }
        }
        Graphics.DrawTexture(r, logo);
    }

    private void DibujarMarco(float w, float h, float cm)
    {
        if (_mat == null)
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return;
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _mat.SetInt("_ZWrite", 0);
            _mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
        // Por _Color (no GL.Color) para que Unity lo pase a lineal como cualquier color de material.
        _mat.SetColor("_Color", colorMarco);
        _mat.SetPass(0);

        float m = margenCm * cm, g = grosorCm * cm;
        float x0 = m, y0 = m, x1 = w - m, y1 = h - m;

        GL.Begin(GL.QUADS);
        GL.Color(Color.white);
        Rect(x0, y0, x1, y0 + g);     // arriba
        Rect(x0, y1 - g, x1, y1);     // abajo
        Rect(x0, y0 + g, x0 + g, y1 - g); // izquierda
        Rect(x1 - g, y0 + g, x1, y1 - g); // derecha

        // Rombos centrados sobre la línea en cada esquina (cuadrado girado 45°).
        float c = g * 0.5f, s = romboCm * cm * Mathf.Sqrt(2f) * 0.5f;
        if (s > 0f)
        {
            Rombo(x0 + c, y0 + c, s);
            Rombo(x1 - c, y0 + c, s);
            Rombo(x0 + c, y1 - c, s);
            Rombo(x1 - c, y1 - c, s);
        }
        GL.End();
    }

    private static void Rect(float xa, float ya, float xb, float yb)
    {
        GL.Vertex3(xa, ya, 0f);
        GL.Vertex3(xb, ya, 0f);
        GL.Vertex3(xb, yb, 0f);
        GL.Vertex3(xa, yb, 0f);
    }

    private static void Rombo(float x, float y, float s)
    {
        GL.Vertex3(x, y - s, 0f);
        GL.Vertex3(x + s, y, 0f);
        GL.Vertex3(x, y + s, 0f);
        GL.Vertex3(x - s, y, 0f);
    }
}
#endif
