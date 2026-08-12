using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Scanner.ScanV3;
using UnityEngine;

public class ScanV3CoreTests
{
    [Test]
    public void DescriptorIdenticoTieneSimilitudUno()
    {
        byte[] image = Checkerboard(32, 32);
        var a = ScanV3Vision.BuildDescriptor(image, 32, 32);
        var b = ScanV3Vision.BuildDescriptor(image, 32, 32);
        Assert.That(ScanV3Vision.Similarity(a, b), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void CalidadRechazaOscuridadYSuperficieSinTextura()
    {
        Assert.That(ScanV3Vision.Evaluate(new byte[32 * 32], 32, 32).Acceptable, Is.False);
        var flat = new byte[32 * 32];
        Array.Fill(flat, (byte)128);
        var quality = ScanV3Vision.Evaluate(flat, 32, 32);
        Assert.That(quality.Acceptable, Is.False);
        Assert.That(quality.Rejection, Does.Contain("borrosa"));
    }

    [Test]
    public void CalidadAceptaImagenExpuestaYConDetalle()
    {
        var quality = ScanV3Vision.Evaluate(Checkerboard(32, 32), 32, 32);
        Assert.That(quality.Acceptable, Is.True);
        Assert.That(quality.MeanLuminance, Is.InRange(0.4f, 0.6f));
    }

    [Test]
    public void PoseGraphReduceResiduoDeCierre()
    {
        var nodes = new List<ScanV3Keyframe>
        {
            Frame(0, new Vector3(0f, 0f, 0f)),
            Frame(1, new Vector3(1f, 0f, 0f)),
            Frame(2, new Vector3(1f, 0f, 1f)),
            Frame(3, new Vector3(0.35f, 0f, 0.10f)),
        };
        var edges = Odometry(nodes);
        edges.Add(new ScanV3PoseEdge
        {
            from = 0, to = 3, expectedWorldDelta = Vector3.zero,
            expectedYawDelta = 0f, weight = 0.7f, kind = ScanV3EdgeKind.LoopClosure,
        });
        var result = ScanV3PoseGraph.Optimize(nodes, edges);
        Assert.That(result.accepted, Is.True);
        Assert.That(result.finalResidual, Is.LessThan(result.initialResidual));
        Assert.That(result.positions[3].magnitude, Is.LessThan(nodes[3].initialPositionLocal.magnitude));
        Assert.That(result.positions[0], Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void LoopClosureExigeSeparacionAparienciaYPoseCompatible()
    {
        var frames = new List<ScanV3Keyframe>();
        var descriptor = Descriptor();
        for (int i = 0; i < 10; i++)
        {
            var frame = new ScanV3Keyframe
            {
                id = i,
                initialPositionLocal = i == 9 ? new Vector3(0.15f, 0f, 0.1f) : new Vector3(i, 0f, 0f),
                initialRotationLocal = Quaternion.identity,
                descriptor = (float[])descriptor.Clone(),
            };
            for (int p = 0; p < 20; p++)
            {
                Vector3 worldPoint = new Vector3((p % 5) * 0.15f, (p / 5) * 0.12f, 1f);
                frame.observations.Add(new ScanV3CameraObservation
                {
                    positionCamera = worldPoint - frame.initialPositionLocal,
                    normalCamera = Vector3.back,
                    confidence = 1f,
                });
            }
            frames.Add(frame);
        }
        Assert.That(ScanV3PoseGraph.TryCreateLoopEdge(frames, 9, out var edge), Is.True);
        Assert.That(edge.from, Is.EqualTo(0));
        Assert.That(edge.to, Is.EqualTo(9));

        frames[9].initialRotationLocal = Quaternion.Euler(0f, 90f, 0f);
        Assert.That(ScanV3PoseGraph.TryCreateLoopEdge(frames, 9, out _), Is.False);
    }

    [Test]
    public void BundleEsIncrementalYSeEliminaAlCancelar()
    {
        string basePath = Path.Combine(Path.GetTempPath(), "mortuorium-v3-tests-" + Guid.NewGuid().ToString("N"));
        var store = new ScanV3BundleStore("capture", basePath);
        try
        {
            var frame = Frame(0, Vector3.zero);
            frame.observations.Add(new ScanV3CameraObservation
            {
                positionCamera = Vector3.forward,
                normalCamera = Vector3.back,
                confidence = 1f,
            });
            store.AddKeyframe(frame, new byte[] { 1, 2, 3 });
            Assert.That(File.Exists(Path.Combine(store.RootPath, "manifest.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(store.RootPath, "frame-00000.jpg")), Is.True);
            string json = File.ReadAllText(Path.Combine(store.RootPath, "manifest.json"));
            var manifest = JsonUtility.FromJson<ScanV3BundleManifest>(json);
            Assert.That(manifest.keyframes, Has.Count.EqualTo(1));
            Assert.That(manifest.keyframes[0].observationFile, Is.Not.Empty);
            Assert.That(ScanV3BundleStore.TryOpenLatestIncomplete(out var recovered, basePath), Is.True);
            Assert.That(recovered.Manifest.keyframes[0].observations, Has.Count.EqualTo(1));
            store.Delete();
            Assert.That(Directory.Exists(store.RootPath), Is.False);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, true);
        }
    }

    private static ScanV3Keyframe Frame(int id, Vector3 position) => new()
    {
        id = id,
        initialPositionLocal = position,
        initialRotationLocal = Quaternion.identity,
        descriptor = Descriptor(),
    };

    private static List<ScanV3PoseEdge> Odometry(IReadOnlyList<ScanV3Keyframe> nodes)
    {
        var edges = new List<ScanV3PoseEdge>();
        for (int i = 1; i < nodes.Count; i++)
            edges.Add(new ScanV3PoseEdge
            {
                from = i - 1,
                to = i,
                expectedWorldDelta = nodes[i].initialPositionLocal - nodes[i - 1].initialPositionLocal,
                weight = 1f,
                kind = ScanV3EdgeKind.Odometry,
            });
        return edges;
    }

    private static byte[] Checkerboard(int width, int height)
    {
        var image = new byte[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            image[y * width + x] = (byte)((((x / 4) + (y / 4)) & 1) == 0 ? 32 : 224);
        return image;
    }

    private static float[] Descriptor()
    {
        var values = new float[64];
        for (int i = 0; i < values.Length; i++) values[i] = Mathf.Sin(i * 0.41f);
        return values;
    }
}
