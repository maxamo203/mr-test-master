#if UNITY_EDITOR
using UnityEngine;

// Efectos de cámara para los renders del banner (bloom, tonemapping, gradación de color,
// viñeta, aberración cromática y grano). El proyecto es Built-in sin el paquete de
// post-processing, así que se hace con OnRenderImage + Assets/Promo/BannerFX.shader.
// Se ve en vivo en el Game view (y en la Scene view) en edit mode, y lo aplica también
// Mortuorium > Banner > Renderizar PNG, porque cam.Render() pasa por OnRenderImage.
//
// TIER: editor-only (archivo entero bajo #if UNITY_EDITOR, como EditorPanorama360).
[ExecuteAlways, ImageEffectAllowedInSceneView, RequireComponent(typeof(Camera))]
public class BannerCamaraFX : MonoBehaviour
{
    [Header("Bloom (brillo de velas, luna, puerta)")]
    [Range(0f, 4f)] public float umbral = 0.8f;
    [Range(0f, 1f)] public float rodilla = 0.5f;
    [Range(0f, 3f)] public float intensidadBloom = 1.1f;
    [Range(1, 8)] public int iteraciones = 7;

    [Header("Color")]
    [Range(0.2f, 4f)] public float exposicion = 1.4f;
    [Range(0.5f, 1.8f)] public float contraste = 1.12f;
    [Range(0f, 1.5f)] public float saturacion = 0.85f;
    public Color tintaSombras = new Color(0.55f, 0.72f, 1f);
    public Color tintaLuces = new Color(1f, 0.78f, 0.58f);
    [Range(0f, 1f)] public float virado = 0.35f;

    [Header("Lente")]
    [Range(0f, 1.5f)] public float vineta = 0.75f;
    [Range(0.5f, 5f)] public float curvaVineta = 2.2f;
    public Color colorVineta = new Color(0.02f, 0f, 0.008f);
    [Range(0f, 3f)] public float aberracion = 0.8f;
    [Range(0f, 0.2f)] public float grano = 0.045f;

    private Material _mat;

    private static readonly int IdBloom = Shader.PropertyToID("_BloomTex");

    // El editor está en el target Android, cuya calidad por defecto (Medium) tiene sombras
    // duras de baja resolución y antialiasing apagado: el Game view se vería pobre. Sólo
    // mientras dibuja ESTA cámara se sube la calidad, y se restaura al terminar, así no
    // queda guardado en QualitySettings ni afecta al resto del proyecto.
    private ShadowQuality _sombras;
    private ShadowResolution _resolucion;
    private float _distancia;
    private int _cascadas;
    private bool _subida;

    // El humo y los haces (BannerNiebla.shader) leen la profundidad para desvanecerse contra
    // el piso y las criaturas. No es una propiedad serializada: se pide en cada OnEnable.
    private void OnEnable() => GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth;

    private void OnPreCull()
    {
        _sombras = QualitySettings.shadows;
        _resolucion = QualitySettings.shadowResolution;
        _distancia = QualitySettings.shadowDistance;
        _cascadas = QualitySettings.shadowCascades;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowDistance = 25f;
        QualitySettings.shadowCascades = 4;
        _subida = true;
    }

    private void OnPostRender() => Restaurar();

    private void Restaurar()
    {
        if (!_subida) return;
        QualitySettings.shadows = _sombras;
        QualitySettings.shadowResolution = _resolucion;
        QualitySettings.shadowDistance = _distancia;
        QualitySettings.shadowCascades = _cascadas;
        _subida = false;
    }

    private void OnDisable()
    {
        Restaurar();
        if (_mat != null) DestroyImmediate(_mat);
        _mat = null;
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (_mat == null)
        {
            var shader = Shader.Find("Hidden/Mortuorium/BannerFX");
            if (shader == null) { Graphics.Blit(src, dst); return; }
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        _mat.SetVector("_Umbral", new Vector4(umbral, Mathf.Max(rodilla * umbral, 1e-4f), 30f, 0f));

        // Cadena de mips: prefiltro → bajadas → subidas aditivas. Se mide relativo a la
        // imagen, así la vista previa y el render de imprenta tienen el mismo halo. Por
        // encima del render de imprenta (5700 de alto) cada duplicación de tamaño suma una
        // bajada: si no, el halo de los renders de alta resolución sale la mitad de ancho.
        int extra = Mathf.Max(0, Mathf.RoundToInt(Mathf.Log(src.height / 5700f, 2f)));
        int iteraciones = this.iteraciones + extra;
        var nivel = new RenderTexture[iteraciones];
        int w = src.width / 2, h = src.height / 2;
        RenderTextureFormat fmt = src.format;
        RenderTexture actual = nivel[0] = RenderTexture.GetTemporary(w, h, 0, fmt);
        Graphics.Blit(src, actual, _mat, 0);
        int n = 1;
        for (; n < iteraciones; n++)
        {
            w /= 2; h /= 2;
            if (w < 2 || h < 2) break;
            nivel[n] = RenderTexture.GetTemporary(w, h, 0, fmt);
            Graphics.Blit(actual, nivel[n], _mat, 1);
            actual = nivel[n];
        }
        for (int i = n - 2; i >= 0; i--)
        {
            Graphics.Blit(actual, nivel[i], _mat, 2);
            actual = nivel[i];
        }

        _mat.SetTexture(IdBloom, nivel[0]);
        _mat.SetFloat("_Bloom", intensidadBloom);
        _mat.SetFloat("_Exposicion", exposicion);
        _mat.SetFloat("_Contraste", contraste);
        _mat.SetFloat("_Saturacion", saturacion);
        _mat.SetColor("_TintaSombras", tintaSombras);
        _mat.SetColor("_TintaLuces", tintaLuces);
        _mat.SetFloat("_Virado", virado);
        _mat.SetFloat("_Vineta", vineta);
        _mat.SetFloat("_VinetaCurva", curvaVineta);
        _mat.SetColor("_ColorVineta", colorVineta);
        _mat.SetFloat("_Aberracion", aberracion);
        _mat.SetFloat("_Grano", grano);
        _mat.SetFloat("_TamGrano", Mathf.Max(1f, src.height / 1400f));
        Graphics.Blit(src, dst, _mat, 3);

        for (int i = 0; i < n; i++) RenderTexture.ReleaseTemporary(nivel[i]);
    }
}
#endif
