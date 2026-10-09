using Gameplay;
using NUnit.Framework;

public class ThreatCoordinatorTests
{
    [TestCase(1, 1, 0, true)]
    [TestCase(1, 1, 1, false)]
    [TestCase(2, 1, 1, true)]
    [TestCase(2, 2, 1, false)]
    [TestCase(4, 2, 1, true)]
    [TestCase(4, 2, 2, false)]
    public void CadaAmenazaNormalRequiereUnDefensorDistinto(
        int capaces, int genericas, int arbmos, bool esperado)
    {
        Assert.That(ThreatCoordinator.CanFit(capaces, genericas, arbmos),
                    Is.EqualTo(esperado));
    }

    [Test]
    public void NuncaHayMasDeTresAmenazasNormalesAunqueHayaMasJugadores()
    {
        Assert.That(ThreatCoordinator.CanFit(8, 1, 2), Is.True);
        Assert.That(ThreatCoordinator.CanFit(8, 1, 3), Is.False);
    }
}
