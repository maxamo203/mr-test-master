using NUnit.Framework;
using Scanner;
using UnityEngine;

// El hash identifica el mapa entre dispositivos (multijugador): tiene que ignorar el
// nombre y el propio campo del hash, y cambiar con cualquier elemento o con la imagen.
public class ScanHashTests
{
    private static ScanData Mapa(string nombre)
    {
        var d = new ScanData { name = nombre, refImageWidthMeters = 0.2f, refImageOrientation = 1 };
        d.walls.Add(new WallData
        {
            id = "w1", polylineId = "p1",
            aLocal = new Vec3(Vector3.zero), bLocal = new Vec3(new Vector3(2f, 0f, 0f)),
            height = 2.5f, width = 0.1f,
        });
        return d;
    }

    private static readonly byte[] Png = { 1, 2, 3, 4 };

    [Test]
    public void MismoContenido_OtroNombre_MismoHash()
    {
        var b = Mapa("cuarto (2)");
        b.contentHash = "viejo";
        Assert.AreEqual(ScanHash.Compute(Mapa("cuarto"), Png), ScanHash.Compute(b, Png));
    }

    [Test]
    public void CambioEnUnaPared_CambiaElHash()
    {
        var b = Mapa("cuarto");
        b.walls[0].height = 2.6f;
        Assert.AreNotEqual(ScanHash.Compute(Mapa("cuarto"), Png), ScanHash.Compute(b, Png));
    }

    [Test]
    public void CambioEnLaImagen_CambiaElHash()
    {
        var a = Mapa("cuarto");
        Assert.AreNotEqual(ScanHash.Compute(a, Png), ScanHash.Compute(a, new byte[] { 1, 2, 3, 5 }));
        Assert.AreNotEqual(ScanHash.Compute(a, Png), ScanHash.Compute(a, null));
    }

    [Test]
    public void NoModificaElOriginal()
    {
        var a = Mapa("cuarto");
        ScanHash.Compute(a, Png);
        Assert.AreEqual("cuarto", a.name);
    }
}
