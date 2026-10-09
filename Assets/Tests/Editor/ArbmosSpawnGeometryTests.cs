using NUnit.Framework;
using UnityEngine;

public class ArbmosSpawnGeometryTests
{
    [Test]
    public void BandaLateralQuedaFueraDelHazYDentroDeCamara()
    {
        Assert.That(ArbmosSpawnGeometry.TryLateralAngleBand(
            horizontalHalfFov: 45f, beamHalfAngle: 12f, distance: 2.5f,
            out float min, out float max), Is.True);
        Assert.That(min, Is.GreaterThan(12f));
        Assert.That(max, Is.LessThan(45f));
        Assert.That(max, Is.GreaterThanOrEqualTo(min));
    }

    [Test]
    public void HazDemasiadoAnchoPostergaLaAparicion()
    {
        Assert.That(ArbmosSpawnGeometry.TryLateralAngleBand(
            horizontalHalfFov: 35f, beamHalfAngle: 34f, distance: 2f,
            out _, out _), Is.False);
    }

    [Test]
    public void PuntoFueraDelEncuadreCompletoEsRechazado()
    {
        Vector3 camera = new Vector3(0f, 1.6f, 0f);
        Assert.That(ArbmosSpawnGeometry.PointInsideView(
            camera, Vector3.forward, new Vector3(0f, 1.6f, 2f), 45f, 30f), Is.True);
        Assert.That(ArbmosSpawnGeometry.PointInsideView(
            camera, Vector3.forward, new Vector3(3f, 1.6f, 2f), 45f, 30f), Is.False);
    }

    [Test]
    public void MuestreoCircularTambienPuedeProponerUnPuntoDetrasDelJugador()
    {
        Vector3 direction = ArbmosSpawnGeometry.LateralDirection(Vector3.forward, 180f);
        Assert.That(Vector3.Dot(direction, Vector3.back), Is.GreaterThan(0.999f));
    }
}
