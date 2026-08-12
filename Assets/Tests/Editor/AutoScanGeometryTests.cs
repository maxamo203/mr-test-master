using System.Collections.Generic;
using NUnit.Framework;
using Scanner.AutoScan;
using UnityEngine;

public class AutoScanGeometryTests
{
    [Test]
    public void IgnoraPlanosQueTodaviaNoSonEstables()
    {
        var samples = new List<AutoScanPlaneSample>
        {
            Wall("a", Vector3.zero, Vector3.forward, 3f, 0f, 2.5f, observations: 2),
        };

        var walls = Build(samples, minObservations: 3);

        Assert.That(walls, Is.Empty);
    }

    [Test]
    public void FusionaFragmentosCoplanaresSolapados()
    {
        var samples = new List<AutoScanPlaneSample>
        {
            Wall("a", new Vector3(-0.9f, 1.25f, 2f), Vector3.forward, 2.2f, 0f, 2.5f),
            Wall("b", new Vector3( 0.9f, 1.25f, 2.03f), Vector3.forward, 2.2f, 0f, 2.5f),
        };

        var walls = Build(samples);

        Assert.That(walls, Has.Count.EqualTo(1));
        Assert.That(Vector3.Distance(walls[0].aLocal, walls[0].bLocal),
                    Is.EqualTo(4f).Within(0.25f));
        Assert.That(walls[0].height, Is.EqualTo(2.5f).Within(0.01f));
    }

    [Test]
    public void NoFusionaParedesPerpendiculares()
    {
        var samples = new List<AutoScanPlaneSample>
        {
            Wall("n", new Vector3(0f, 1.25f, 2f), Vector3.forward, 3f, 0f, 2.5f),
            Wall("e", new Vector3(1.5f, 1.25f, 0.5f), Vector3.right, 3f, 0f, 2.5f),
        };

        Assert.That(Build(samples), Has.Count.EqualTo(2));
    }

    [Test]
    public void UsaElPlanoHorizontalMasBajoComoPiso()
    {
        var samples = new List<AutoScanPlaneSample>
        {
            Horizontal("mesa", 0.75f, 1.5f),
            Horizontal("piso", 0f, 8f),
        };

        bool found = AutoScanModel.TryFindFloor(samples, 3, 0.8f, out var floor);

        Assert.That(found, Is.True);
        Assert.That(floor.y, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void AlineaLaBaseDeLaParedAlPisoCercano()
    {
        var samples = new List<AutoScanPlaneSample>
        {
            Wall("a", new Vector3(0f, 1.3f, 2f), Vector3.forward, 3f, 0.18f, 2.55f),
        };

        var walls = AutoScanModel.BuildWalls(samples, 3, 0.4f, 0.6f,
            10f, 0.16f, 0.3f, floorY: 0f);

        Assert.That(walls, Has.Count.EqualTo(1));
        Assert.That(walls[0].aLocal.y, Is.EqualTo(0f).Within(0.001f));
        Assert.That(walls[0].height, Is.EqualTo(2.55f).Within(0.001f));
    }

    [Test]
    public void ObservacionesConPequenoRuidoAcumulanEstabilidad()
    {
        var previous = Wall("a", new Vector3(0f, 1.25f, 2f), Vector3.forward,
                            3f, 0f, 2.5f);
        var current = Wall("a", new Vector3(0.03f, 1.27f, 2.02f),
                           Quaternion.Euler(0f, 3f, 0f) * Vector3.forward,
                           3.12f, 0.02f, 2.52f);

        Assert.That(AutoScanModel.IsConsistentObservation(
            previous, current, 0.08f, 6f, 0.20f), Is.True);
    }

    [Test]
    public void SaltoDePlanoReiniciaLaEstabilidad()
    {
        var previous = Wall("a", new Vector3(0f, 1.25f, 2f), Vector3.forward,
                            3f, 0f, 2.5f);
        var jumped = Wall("a", new Vector3(0.35f, 1.25f, 2f), Vector3.forward,
                          3f, 0f, 2.5f);

        Assert.That(AutoScanModel.IsConsistentObservation(
            previous, jumped, 0.08f, 6f, 0.20f), Is.False);
    }

    [Test]
    public void CambioDeTamanoGrandeReiniciaLaEstabilidad()
    {
        var previous = Wall("a", Vector3.zero, Vector3.forward, 1f, 0f, 1f);
        var expanded = Wall("a", Vector3.zero, Vector3.forward, 2f, 0f, 1f);

        Assert.That(AutoScanModel.IsConsistentObservation(
            previous, expanded, 0.08f, 6f, 0.20f), Is.False);
    }

    [Test]
    public void NormalInvertidaRepresentaElMismoPlanoFisico()
    {
        var previous = Wall("a", Vector3.zero, Vector3.forward, 2f, 0f, 2f);
        var flipped = Wall("a", Vector3.zero, Vector3.back, 2f, 0f, 2f);

        Assert.That(AutoScanModel.IsConsistentObservation(
            previous, flipped, 0.08f, 6f, 0.20f), Is.True);
    }

    [Test]
    public void AreaDePlanoIrregularUsaPoligonoYNoCajaEnvolvente()
    {
        var triangle = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(2f, 0f, 0f),
            new Vector3(0f, 0f, 2f),
        };

        Assert.That(AutoScanModel.PolygonAreaXZ(triangle), Is.EqualTo(2f).Within(0.0001f));
    }

    [Test]
    public void DescartaMuestrasConValoresNoFinitos()
    {
        var invalid = Wall("bad", Vector3.zero, Vector3.forward, 2f, 0f, 2f);
        invalid.centerLocal.x = float.NaN;

        Assert.That(Build(new[] { invalid }), Is.Empty);
    }

    private static List<AutoScanWallCandidate> Build(
        IReadOnlyList<AutoScanPlaneSample> samples, int minObservations = 3) =>
        AutoScanModel.BuildWalls(samples, minObservations, 0.4f, 0.6f,
                                    10f, 0.16f, 0.3f);

    private static AutoScanPlaneSample Wall(string id, Vector3 center, Vector3 normal,
                                            float width, float minY, float maxY,
                                            int observations = 5) =>
        new AutoScanPlaneSample
        {
            sourceId = id,
            kind = AutoScanPlaneKind.Vertical,
            centerLocal = center,
            normalLocal = normal,
            width = width,
            height = maxY - minY,
            minY = minY,
            maxY = maxY,
            observations = observations,
            area = width * (maxY - minY),
        };

    private static AutoScanPlaneSample Horizontal(string id, float y, float area) =>
        new AutoScanPlaneSample
        {
            sourceId = id,
            kind = AutoScanPlaneKind.HorizontalUp,
            centerLocal = new Vector3(0f, y, 0f),
            normalLocal = Vector3.up,
            width = Mathf.Sqrt(area),
            height = Mathf.Sqrt(area),
            minY = y,
            maxY = y,
            observations = 5,
            area = area,
        };
}
