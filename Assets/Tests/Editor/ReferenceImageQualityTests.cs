using NUnit.Framework;
using Scanner;
using UnityEngine;

// Cubre el chequeo de calidad del recorte de referencia (ReferenceImageQuality)
// con imágenes sintéticas: liso, ruido con detalle parejo, detalle en un rincón,
// y un patrón simétrico (que ARKit/ARCore pueden detectar dado vuelta).
public class ReferenceImageQualityTests
{
    private const int W = 64, H = 48;

    private static float[] Lisa(float valor)
    {
        var g = new float[W * H];
        for (int i = 0; i < g.Length; i++) g[i] = valor;
        return g;
    }

    private static float[] Ruido(int semilla, int x0 = 0, int y0 = 0, int x1 = W, int y1 = H)
    {
        var rnd = new System.Random(semilla);
        var g = Lisa(0.5f);
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
            g[y * W + x] = (float)rnd.NextDouble();
        return g;
    }

    // Anillos concéntricos: mucho detalle y simetría central perfecta (180°).
    private static float[] Anillos()
    {
        var g = new float[W * H];
        float cx = (W - 1) * 0.5f, cy = (H - 1) * 0.5f;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float r = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            g[y * W + x] = 0.5f + 0.5f * Mathf.Sin(0.9f * r);
        }
        return g;
    }

    [Test]
    public void ImagenLisa_TienePocoDetalle()
    {
        var r = ReferenceImageQuality.Analizar(Lisa(0.5f), W, H);
        Assert.That(r.Detalle, Is.EqualTo(0f));
        Assert.That(r.PocoDetalle, Is.True);
        Assert.That(r.Aceptable, Is.False);
        Assert.That(r.Aviso, Does.Contain("poco detalle"));
    }

    [Test]
    public void RuidoParejo_EsAceptable()
    {
        var r = ReferenceImageQuality.Analizar(Ruido(7), W, H);
        Assert.That(r.Detalle, Is.GreaterThan(ReferenceImageQuality.MinDetalle));
        Assert.That(r.Cobertura, Is.EqualTo(1f));
        Assert.That(r.Simetria, Is.LessThan(0.3f));
        Assert.That(r.Aceptable, Is.True);
        Assert.That(r.Aviso, Is.Null);
    }

    [Test]
    public void DetalleEnUnRincon_AvisaMalRepartida()
    {
        var r = ReferenceImageQuality.Analizar(Ruido(3, 0, 0, W / 4, H / 4), W, H);
        Assert.That(r.PocoDetalle, Is.False, "el rincón con ruido aporta detalle suficiente en total");
        Assert.That(r.Cobertura, Is.LessThan(ReferenceImageQuality.MinCobertura));
        Assert.That(r.MalRepartida, Is.True);
        Assert.That(r.Aviso, Does.Contain("rincón"));
    }

    [Test]
    public void PatronSimetrico_AvisaSimetria180()
    {
        var r = ReferenceImageQuality.Analizar(Anillos(), W, H);
        Assert.That(r.Detalle, Is.GreaterThan(ReferenceImageQuality.MinDetalle));
        Assert.That(r.Simetria, Is.GreaterThan(ReferenceImageQuality.MaxSimetria));
        Assert.That(r.Simetrica, Is.True);
        Assert.That(r.CasiCuadrada, Is.False);
        Assert.That(r.Aviso, Does.Contain("180"));
    }

    [Test]
    public void PatronSimetricoCuadrado_DetectaGiroDe90()
    {
        // Anillos en un lienzo cuadrado: también correlaciona con el giro de 90°.
        const int L = 48;
        var g = new float[L * L];
        float c = (L - 1) * 0.5f;
        for (int y = 0; y < L; y++)
        for (int x = 0; x < L; x++)
            g[y * L + x] = 0.5f + 0.5f * Mathf.Sin(0.9f * Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)));

        var r = ReferenceImageQuality.Analizar(g, L, L);
        Assert.That(r.CasiCuadrada, Is.True);
        Assert.That(r.Simetrica, Is.True);
        Assert.That(r.Aviso, Does.Contain("girada"));
    }

    [Test]
    public void Reducir_PromediaBloquesYRespetaElLadoMaximo()
    {
        const int w = 200, h = 100;
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(128, 128, 128, 255);

        ReferenceImageQuality.Reducir(px, w, h, out var gris, out int gw, out int gh);
        Assert.That(gw, Is.LessThanOrEqualTo(ReferenceImageQuality.LadoMax));
        Assert.That(gh, Is.LessThanOrEqualTo(ReferenceImageQuality.LadoMax));
        Assert.That(gris.Length, Is.EqualTo(gw * gh));
        foreach (var v in gris) Assert.That(v, Is.EqualTo(128f / 255f).Within(1e-3f));
    }

    [Test]
    public void Analizar_DesdeColor32_ImagenLisaTienePocoDetalle()
    {
        const int w = 120, h = 90;
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(200, 30, 30, 255);
        var r = ReferenceImageQuality.Analizar(px, w, h);
        Assert.That(r.PocoDetalle, Is.True);
    }

    [Test]
    public void EntradaInvalida_NoRompe()
    {
        var r = ReferenceImageQuality.Analizar((float[])null, 0, 0);
        Assert.That(r.Aceptable, Is.False);
        Assert.That(ReferenceImageQuality.Analizar((Texture2D)null).Aceptable, Is.False);
    }
}
