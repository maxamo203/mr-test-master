using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using static ImageAnchorPose;

// Cubre la matemática pura del anclaje por imagen (ImageAnchorPose): de qué eje de
// la imagen sale el rumbo según su orientación física, que el anchor SIEMPRE quede
// Y-up, y cuándo la ventana de muestras se da por convergida.
//
// Convención de ARFoundation para un ARTrackedImage: +X ancho, +Z alto, +Y normal.
public class ImageAnchorPoseTests
{
    private const float Eps = 1e-3f;

    // Ejes de una imagen HORIZONTAL (en el piso) girada `yawDeg` sobre Y.
    private static void ImagenHorizontal(float yawDeg, out Vector3 right, out Vector3 up, out Vector3 fwd)
    {
        var q = Quaternion.Euler(0f, yawDeg, 0f);
        right = q * Vector3.right; up = q * Vector3.up; fwd = q * Vector3.forward;
    }

    // Ejes de una imagen VERTICAL (en una pared cuya normal apunta a `normalYawDeg`),
    // capturada con el celular en vertical (ancho horizontal, alto vertical).
    private static void ImagenVertical(float normalYawDeg, out Vector3 right, out Vector3 up, out Vector3 fwd)
    {
        // Partimos de la imagen horizontal y la paramos contra la pared: la normal (+Y)
        // pasa a ser horizontal, el alto (+Z) pasa a apuntar hacia abajo/arriba.
        var q = Quaternion.Euler(0f, normalYawDeg, 0f) * Quaternion.Euler(-90f, 0f, 0f);
        right = q * Vector3.right; up = q * Vector3.up; fwd = q * Vector3.forward;
    }

    [Test]
    public void Horizontal_ElRumboEsElAnchoDeLaImagen()
    {
        ImagenHorizontal(37f, out var r, out var u, out var f);
        var rumbo = EjeRumbo(r, u, f, Orientacion.Horizontal);
        Assert.That(Vector3.Angle(rumbo, Aplanar(r)), Is.LessThan(0.01f));
        Assert.That(rumbo.y, Is.EqualTo(0f).Within(Eps));
    }

    [Test]
    public void Horizontal_DesconocidaSeInfiereComoHorizontalYDaElMismoRumbo()
    {
        ImagenHorizontal(-120f, out var r, out var u, out var f);
        Assert.That(DesdePose(u), Is.EqualTo(Orientacion.Horizontal));
        var a = EjeRumbo(r, u, f, Orientacion.Horizontal);
        var b = EjeRumbo(r, u, f, Orientacion.Desconocida);
        Assert.That(Vector3.Angle(a, b), Is.LessThan(0.01f));
    }

    [Test]
    public void Vertical_ElRumboSaleDelEjeHorizontalDelPlano_NuncaDeLaNormal()
    {
        ImagenVertical(60f, out var r, out var u, out var f);
        Assert.That(DesdePose(u), Is.EqualTo(Orientacion.Vertical));

        var rumbo = EjeRumbo(r, u, f, Orientacion.Vertical);
        Assert.That(rumbo.y, Is.EqualTo(0f).Within(Eps));
        // Es el ancho (horizontal sobre la pared)…
        Assert.That(Vector3.Angle(rumbo, Aplanar(r)), Is.LessThan(0.01f));
        // …y perpendicular a la normal (no la usamos).
        Assert.That(Mathf.Abs(Vector3.Dot(rumbo, Aplanar(u))), Is.LessThan(0.01f));
    }

    [Test]
    public void Vertical_CapturadaEnHorizontal_UsaElAltoQueQuedoHorizontal()
    {
        // Celular apaisado al capturar: en la pared el ANCHO (+X) queda vertical y el
        // ALTO (+Z) horizontal. Tiene que elegir el alto.
        ImagenVertical(0f, out var r, out var u, out var f);
        // Giramos la imagen 90° alrededor de su normal.
        var q = Quaternion.AngleAxis(90f, u);
        var r2 = q * r; var f2 = q * f;
        Assert.That(Mathf.Abs(r2.y), Is.GreaterThan(0.9f));   // ancho vertical
        Assert.That(Mathf.Abs(f2.y), Is.LessThan(0.1f));      // alto horizontal

        var rumbo = EjeRumbo(r2, u, f2, Orientacion.Vertical);
        Assert.That(Vector3.Angle(rumbo, Aplanar(f2)), Is.LessThan(0.01f));
    }

    [Test]
    public void Vertical_EsDeterministaEntreSesionesConLaMismaImagen()
    {
        // La misma imagen física vista en dos sesiones (con orígenes de AR distintos):
        // el rumbo relativo a la pared tiene que ser el mismo.
        ImagenVertical(20f,  out var r1, out var u1, out var f1);
        ImagenVertical(20f + 90f, out var r2, out var u2, out var f2);   // el mundo AR giró 90°
        var a = EjeRumbo(r1, u1, f1, Orientacion.Vertical);
        var b = EjeRumbo(r2, u2, f2, Orientacion.Vertical);
        Assert.That(Vector3.Angle(a, Aplanar(u1)), Is.EqualTo(Vector3.Angle(b, Aplanar(u2))).Within(0.01f));
    }

    [Test]
    public void Upright_SiempreYUp()
    {
        var q = UprightDesdeRumbo(new Vector3(0.3f, 0.8f, -0.5f));
        Assert.That(Vector3.Angle(q * Vector3.up, Vector3.up), Is.LessThan(0.01f));
        Assert.That((q * Vector3.forward).y, Is.EqualTo(0f).Within(Eps));
    }

    [Test]
    public void Resolver_ElFlagGuardadoMandaSalvoContradiccionClara()
    {
        Assert.That(Resolver(Orientacion.Horizontal, new Vector3(0, 0.6f, 0.8f)), Is.EqualTo(Orientacion.Horizontal));
        Assert.That(Resolver(Orientacion.Horizontal, new Vector3(0, 0.1f, 0.99f)), Is.EqualTo(Orientacion.Vertical));
        Assert.That(Resolver(Orientacion.Vertical,   new Vector3(0, 0.4f, 0.9f)),  Is.EqualTo(Orientacion.Vertical));
        Assert.That(Resolver(Orientacion.Vertical,   new Vector3(0, 0.95f, 0.3f)), Is.EqualTo(Orientacion.Horizontal));
        Assert.That(Resolver(Orientacion.Desconocida, Vector3.up),      Is.EqualTo(Orientacion.Horizontal));
        Assert.That(Resolver(Orientacion.Desconocida, Vector3.forward), Is.EqualTo(Orientacion.Vertical));
    }

    [Test]
    public void DesdeCamara_MirandoAlPisoEsHorizontal_DeFrenteEsVertical()
    {
        Assert.That(DesdeCamara(new Vector3(0f, -0.9f, 0.4f)), Is.EqualTo(Orientacion.Horizontal));
        Assert.That(DesdeCamara(new Vector3(0.3f, -0.2f, 0.9f)), Is.EqualTo(Orientacion.Vertical));
        Assert.That(DesdeCamara(new Vector3(0f, 0.9f, 0.4f)), Is.EqualTo(Orientacion.Horizontal)); // techo
    }

    [Test]
    public void AlinearSigno_InvierteUnFlipDe180()
    {
        var eje = AlinearSigno(-Vector3.forward, Vector3.forward);
        Assert.That(Vector3.Angle(eje, Vector3.forward), Is.LessThan(0.01f));
    }

    private static List<Muestra> Ventana(int n, float jitterGrados, float jitterMetros)
    {
        var lista = new List<Muestra>();
        for (int i = 0; i < n; i++)
        {
            float ang = (i % 2 == 0 ? 1f : -1f) * jitterGrados;
            var rumbo = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            var pos   = new Vector3(1f, 0f, 2f) + Vector3.right * ((i % 2 == 0 ? 1f : -1f) * jitterMetros);
            lista.Add(new Muestra(rumbo, pos));
        }
        return lista;
    }

    [Test]
    public void Convergio_RequiereVentanaLlenaYEstable()
    {
        Assert.That(Convergio(Ventana(4, 0.5f, 0.005f), 8, 2f, 0.02f), Is.False, "ventana incompleta");
        Assert.That(Convergio(Ventana(8, 0.5f, 0.005f), 8, 2f, 0.02f), Is.True,  "estable");
        Assert.That(Convergio(Ventana(8, 5f,   0.005f), 8, 2f, 0.02f), Is.False, "rumbo inestable");
        Assert.That(Convergio(Ventana(8, 0.5f, 0.05f),  8, 2f, 0.02f), Is.False, "posición inestable");
    }

    [Test]
    public void Promediar_DevuelveMediaAplanadaYNormalizada()
    {
        var v = Ventana(8, 3f, 0.01f);
        Assert.That(Promediar(v, out var rumbo, out var pos), Is.True);
        Assert.That(rumbo.magnitude, Is.EqualTo(1f).Within(Eps));
        Assert.That(rumbo.y, Is.EqualTo(0f).Within(Eps));
        Assert.That(Vector3.Angle(rumbo, Vector3.forward), Is.LessThan(0.01f));
        Assert.That(pos.x, Is.EqualTo(1f).Within(Eps));
        Assert.That(Promediar(new List<Muestra>(), out _, out _), Is.False);
    }
}
