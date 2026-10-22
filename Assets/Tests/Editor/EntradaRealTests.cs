using System.Collections.Generic;
using Gameplay;
using NUnit.Framework;
using UnityEngine;

public class EntradaRealTests
{
    // Fila de muestras sobre una pared en z = 2 vista desde el origen, con un hueco
    // (puerta abierta) entre huecoIzq y huecoDer: por el hueco se ve el pasillo a z = 5.
    private static List<Vector2> ParedConHueco(float huecoIzq, float huecoDer, float paso = 0.1f)
    {
        var l = new List<Vector2>();
        for (float x = -1.2f; x <= 1.2001f; x += paso)
            l.Add(x > huecoIzq && x < huecoDer ? new Vector2(x * 2.5f, 5f) : new Vector2(x, 2f));
        return l;
    }

    [Test]
    public void PuertaAbiertaMideAnchoYCentroSobreLaPared()
    {
        var a = EntradaReal.Ajustar(Vector2.zero, Vector2.up, ParedConHueco(-0.3f, 0.6f));

        Assert.That(a.Ok, Is.True);
        Assert.That(a.Normal.y, Is.EqualTo(-1f).Within(0.01f));
        Assert.That(a.Centro.y, Is.EqualTo(2f).Within(0.01f));
        Assert.That(a.Ancho, Is.EqualTo(1.0f).Within(0.11f));
        Assert.That(a.Centro.x, Is.EqualTo(0.15f).Within(0.1f));   // ± paso de muestreo
    }

    [Test]
    public void PuertaCerradaQuedaEnLaMiraSinAnchoMedido()
    {
        var a = EntradaReal.Ajustar(Vector2.zero, Vector2.up, ParedConHueco(9f, 9f));

        Assert.That(a.Ok, Is.True);
        Assert.That(a.Ancho, Is.EqualTo(0f));
        Assert.That(a.Centro.x, Is.EqualTo(0f).Within(0.01f));
        Assert.That(a.Distancia, Is.EqualTo(2f).Within(0.01f));
        // La pared vista va de -1.2 a 1.2 m: es el largo del oclusor de la entrada.
        Assert.That(Mathf.Min(a.Desde * a.Tangente.x, a.Hasta * a.Tangente.x), Is.EqualTo(-1.2f).Within(0.01f));
        Assert.That(Mathf.Max(a.Desde * a.Tangente.x, a.Hasta * a.Tangente.x), Is.EqualTo(1.2f).Within(0.01f));
    }

    [Test]
    public void ParedOblicuaDaSuNormalRealYNoLaDeLaMirada()
    {
        // Pared girada 30°: la entrada tiene que quedar alineada con ella.
        var rot = Quaternion.Euler(0f, 0f, 30f);
        var puntos = new List<Vector2>();
        for (float x = -1f; x <= 1.001f; x += 0.1f)
            puntos.Add((Vector2)(rot * new Vector3(x, 0f)) + new Vector2(0f, 2f));

        var a = EntradaReal.Ajustar(Vector2.zero, Vector2.up, puntos);

        Assert.That(a.Ok, Is.True);
        Vector2 esperada = rot * new Vector3(0f, -1f);
        Assert.That(Vector2.Dot(a.Normal, esperada), Is.GreaterThan(0.999f));
    }

    [Test]
    public void SinParedSuficienteNoAjusta()
    {
        var pocos = new List<Vector2> { new(0f, 2f), new(0.1f, 2f), new(0.15f, 2f) };
        Assert.That(EntradaReal.Ajustar(Vector2.zero, Vector2.up, pocos).Ok, Is.False);
    }
}
