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

    [Test]
    public void ConsecuenciaLetalBloqueaNuevasAmenazasNormales()
    {
        ThreatCoordinator.ResetAll();
        ThreatCoordinator.BeginLethalArbmos(7);

        Assert.That(ThreatCoordinator.LethalConsequenceActive, Is.True);
        Assert.That(ThreatCoordinator.TryBeginSorken(), Is.False);

        ThreatCoordinator.EndLethalArbmos(7);
        Assert.That(ThreatCoordinator.LethalConsequenceActive, Is.False);
    }

    [Test]
    public void SorkenSoloCuentaCuandoYaIngresoAlAmbiente()
    {
        ThreatCoordinator.ResetAll();

        Assert.That(ThreatCoordinator.SorkenActive, Is.False);
        ThreatCoordinator.BeginSorkenInside();
        Assert.That(ThreatCoordinator.SorkenActive, Is.True);

        ThreatCoordinator.EndSorken();
        Assert.That(ThreatCoordinator.SorkenActive, Is.False);
    }

    [Test]
    public void SorkenNoExpulsaUnaAmenazaQueYaHabiaComenzado()
    {
        ThreatCoordinator.ResetAll();
        ThreatCoordinator.BeginSorkenInside();

        Assert.That(ThreatCoordinator.CanKeepExistingThreat(participantCapable: true),
                    Is.True);
        Assert.That(ThreatCoordinator.CanFit(1, genericTasks: 1, ownedTasks: 1),
                    Is.False, "Sorken debe seguir bloqueando eventos nuevos para un jugador");
    }

    [Test]
    public void ConsecuenciaLetalSiInterrumpeUnaAmenazaExistente()
    {
        ThreatCoordinator.ResetAll();
        ThreatCoordinator.BeginLethalArbmos(7);

        Assert.That(ThreatCoordinator.CanKeepExistingThreat(participantCapable: true),
                    Is.False);

        ThreatCoordinator.EndLethalArbmos(7);
    }
}
