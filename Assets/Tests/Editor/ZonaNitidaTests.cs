// Regla de la zona nítida del visor: cuánto subir una tarjeta sobre la cámara para que su
// borde inferior no salga de lo que la lente enfoca.
using NUnit.Framework;
using Rediseno.UI;

public class ZonaNitidaTests
{
    private const float Eps = 1e-4f;

    [Test]
    public void SubirTarjeta_BordeFueraDeLaZona_SubeLoJusto()
    {
        Assert.AreEqual(3f, ZonaNitida.SubirTarjeta(-21f), Eps);
    }

    [Test]
    public void SubirTarjeta_BordeAdentro_NoBaja()
    {
        Assert.AreEqual(0f, ZonaNitida.SubirTarjeta(-10f), Eps);
        Assert.AreEqual(0f, ZonaNitida.SubirTarjeta(5f), Eps);
    }
}
