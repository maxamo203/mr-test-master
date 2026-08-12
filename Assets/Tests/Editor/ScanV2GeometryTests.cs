using System.Collections.Generic;
using NUnit.Framework;
using Scanner.ScanV2;
using UnityEngine;

public class ScanV2GeometryTests
{
    [Test]
    public void FusionPromediaRuidoYExigeObservacionesRepetidas()
    {
        var volume = new SparseSurfelVolume(0.1f);
        volume.Integrate(new[]
        {
            new ScanV2Observation(new Vector3(1.01f, 0f, 2f), Vector3.forward),
            new ScanV2Observation(new Vector3(0.99f, 0f, 2f), Vector3.forward),
        });
        Assert.That(volume.Extract(2), Is.Empty, "un solo keyframe no debe simular dos vistas");
        volume.Integrate(new[]
        {
            new ScanV2Observation(new Vector3(1.005f, 0f, 2f), Vector3.forward),
        });

        Assert.That(volume.Extract(3), Is.Empty);
        var surfels = volume.Extract(2);
        Assert.That(surfels, Has.Count.EqualTo(1));
        Assert.That(surfels[0].positionLocal.x, Is.EqualTo(1.0025f).Within(0.001f));
        Assert.That(surfels[0].observations, Is.EqualTo(2));
    }

    [Test]
    public void FusionUnificaNormalesConOrientacionInvertida()
    {
        var volume = new SparseSurfelVolume(0.1f);
        volume.Integrate(new[] { new ScanV2Observation(Vector3.zero, Vector3.forward) });
        volume.Integrate(new[] { new ScanV2Observation(Vector3.zero, Vector3.back) });
        var surfel = volume.Extract(2)[0];
        Assert.That(Mathf.Abs(Vector3.Dot(surfel.normalLocal, Vector3.forward)),
                    Is.GreaterThan(0.99f));
    }

    [Test]
    public void FusionDescartaDatosNoFinitos()
    {
        var volume = new SparseSurfelVolume(0.1f);
        volume.Integrate(new[]
        {
            new ScanV2Observation(new Vector3(float.NaN, 0f, 0f), Vector3.up),
            new ScanV2Observation(Vector3.zero, new Vector3(float.PositiveInfinity, 0f, 0f)),
        });
        Assert.That(volume.Count, Is.Zero);
    }

    [Test]
    public void ExtraePisoYParedesDeHabitacionMultivista()
    {
        var surfels = SyntheticRoom(4f, 3f, 2.5f, 0.1f);
        var structure = ScanV2Geometry.ExtractStructure(surfels, 12, 12);

        Assert.That(structure.hasFloor, Is.True);
        Assert.That(structure.floorY, Is.EqualTo(0f).Within(0.01f));
        Assert.That(structure.walls, Has.Count.EqualTo(4));
        foreach (var wall in structure.walls)
        {
            Assert.That(Vector3.Distance(wall.aLocal, wall.bLocal), Is.GreaterThan(2.8f));
            Assert.That(wall.height, Is.EqualTo(2.5f).Within(0.11f));
        }
    }

    [Test]
    public void NoConvierteUnaMesaEnPisoCuandoExisteSuperficieMasBaja()
    {
        var surfels = new List<ScanV2Surfel>();
        AddHorizontal(surfels, 0f, 4f, 3f, Vector3.up);
        AddHorizontal(surfels, 0.8f, 1.2f, 0.8f, Vector3.up);
        var structure = ScanV2Geometry.ExtractStructure(surfels, 8, 12);
        Assert.That(structure.hasFloor, Is.True);
        Assert.That(structure.floorY, Is.EqualTo(0f).Within(0.01f));
    }

    [Test]
    public void FragmentosCoplanaresSeConsolidanEnUnaPared()
    {
        var surfels = new List<ScanV2Surfel>();
        AddWall(surfels, new Vector3(-2f, 0f, 0f), Vector3.forward, 2f, 2.4f, 0.1f);
        AddWall(surfels, new Vector3(0f, 0f, 0.03f), Vector3.forward, 2f, 2.4f, 0.1f);
        var structure = ScanV2Geometry.ExtractStructure(surfels, 12, 12);
        Assert.That(structure.walls, Has.Count.EqualTo(1));
        Assert.That(Vector3.Distance(structure.walls[0].aLocal, structure.walls[0].bLocal),
                    Is.EqualTo(4f).Within(0.15f));
    }

    [Test]
    public void CierraHuecoEntreParedesPerpendicularesCercanas()
    {
        var walls = new List<ScanV2WallCandidate>
        {
            new() { aLocal = Vector3.zero, bLocal = new Vector3(1.88f, 0f, 0f), height = 2.4f },
            new() { aLocal = new Vector3(2f, 0f, 0.11f), bLocal = new Vector3(2f, 0f, 2f), height = 2.4f },
        };
        ScanV2Geometry.CloseNearbyCorners(walls, 0.25f);
        Assert.That(walls[0].bLocal.x, Is.EqualTo(2f).Within(0.001f));
        Assert.That(walls[0].bLocal.z, Is.EqualTo(0f).Within(0.001f));
        Assert.That(walls[1].aLocal.x, Is.EqualTo(2f).Within(0.001f));
        Assert.That(walls[1].aLocal.z, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void NoUneTramosCoplanaresSeparadosPorHuecoGrande()
    {
        var surfels = new List<ScanV2Surfel>();
        AddWall(surfels, new Vector3(-3f, 0f, 0f), Vector3.forward, 1f, 2.4f, 0.1f);
        AddWall(surfels, new Vector3(1f, 0f, 0f), Vector3.forward, 1f, 2.4f, 0.1f);
        var structure = ScanV2Geometry.ExtractStructure(surfels, 12, 12);
        Assert.That(structure.walls, Has.Count.EqualTo(2));
    }

    [Test]
    public void SuperficieHorizontalAngostaNoSeAceptaComoPiso()
    {
        var surfels = new List<ScanV2Surfel>();
        AddHorizontal(surfels, 0f, 4f, 0.1f, Vector3.up);
        var structure = ScanV2Geometry.ExtractStructure(surfels, 8, 12);
        Assert.That(structure.hasFloor, Is.False);
    }

    [Test]
    public void VolumenRespetaLimiteDeMemoria()
    {
        var volume = new SparseSurfelVolume(0.02f, 100);
        var points = new List<ScanV2Observation>();
        for (int i = 0; i < 500; i++)
            points.Add(new ScanV2Observation(new Vector3(i * 0.03f, 0f, 0f), Vector3.up));
        volume.Integrate(points);
        Assert.That(volume.Count, Is.EqualTo(100));
        Assert.That(volume.IsFull, Is.True);
    }

    private static List<ScanV2Surfel> SyntheticRoom(float width, float depth,
                                                     float height, float step)
    {
        var result = new List<ScanV2Surfel>();
        AddHorizontal(result, 0f, width, depth, Vector3.up);
        AddWall(result, new Vector3(-width / 2f, 0f, 0f), Vector3.forward, width, height, step);
        AddWall(result, new Vector3(width / 2f, 0f, depth), Vector3.back, width, height, step);
        AddWall(result, new Vector3(-width / 2f, 0f, depth), Vector3.right, depth, height, step);
        AddWall(result, new Vector3(width / 2f, 0f, 0f), Vector3.left, depth, height, step);
        return result;
    }

    private static void AddHorizontal(List<ScanV2Surfel> output, float y,
                                      float width, float depth, Vector3 normal)
    {
        for (float x = -width / 2f; x <= width / 2f; x += 0.1f)
        for (float z = 0f; z <= depth; z += 0.1f)
            output.Add(Surfel(new Vector3(x, y, z), normal));
    }

    private static void AddWall(List<ScanV2Surfel> output, Vector3 origin,
                                Vector3 normal, float length, float height, float step)
    {
        var tangent = Vector3.Cross(Vector3.up, normal).normalized;
        for (float along = 0f; along <= length; along += step)
        for (float y = 0f; y <= height; y += step)
            output.Add(Surfel(origin + tangent * along + Vector3.up * y, normal));
    }

    private static ScanV2Surfel Surfel(Vector3 position, Vector3 normal) => new()
    {
        positionLocal = position,
        normalLocal = normal,
        confidence = 1f,
        observations = 3,
    };
}
