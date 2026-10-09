using System.Collections.Generic;
using Gameplay.Spawning;
using NUnit.Framework;
using UnityEngine;

public class InteriorTopologyTests
{
    [Test]
    public void ClosedConcaveRoomAcceptsOnlyInteriorWithClearance()
    {
        var points = new[]
        {
            new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 1),
            new Vector2(1, 1), new Vector2(1, 4), new Vector2(0, 4)
        };
        Assert.That(InteriorTopology.TryBuild(Loop(points), 0.05f, out var topology, out var reason),
                    Is.True, reason);
        Assert.That(topology.ContainsDisc(new Vector2(0.5f, 3f), 0.2f), Is.True);
        Assert.That(topology.ContainsDisc(new Vector2(2f, 2f), 0.1f), Is.False);
        Assert.That(topology.ContainsDisc(new Vector2(0.05f, 2f), 0.1f), Is.False);
    }

    [Test]
    public void SupportsTwoIndependentRooms()
    {
        var segments = Loop(new[]
        {
            new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 2), new Vector2(0, 2)
        });
        segments.AddRange(Loop(new[]
        {
            new Vector2(4, 0), new Vector2(6, 0), new Vector2(6, 2), new Vector2(4, 2)
        }));
        Assert.That(InteriorTopology.TryBuild(segments, 0.05f, out var topology, out var reason),
                    Is.True, reason);
        Assert.That(topology.Rooms.Count, Is.EqualTo(2));
        Assert.That(topology.ContainsDisc(new Vector2(1, 1), 0.1f), Is.True);
        Assert.That(topology.ContainsDisc(new Vector2(5, 1), 0.1f), Is.True);
        Assert.That(topology.ContainsDisc(new Vector2(3, 1), 0.1f), Is.False);
    }

    [Test]
    public void SmallScanErrorSnapsButOpenContourIsRejected()
    {
        var almostClosed = new List<InteriorTopology.Segment>
        {
            new(new Vector2(0, 0), new Vector2(2, 0)),
            new(new Vector2(2, 0), new Vector2(2, 2)),
            new(new Vector2(2, 2), new Vector2(0, 2)),
            new(new Vector2(0, 2), new Vector2(0.04f, 0.03f))
        };
        Assert.That(InteriorTopology.TryBuild(almostClosed, 0.08f, out _, out _), Is.True);
        Assert.That(InteriorTopology.TryBuild(almostClosed, 0.01f, out _, out var reason), Is.False);
        StringAssert.Contains("abierto", reason);
    }

    [Test]
    public void CrossingWallsAreRejectedAsAmbiguous()
    {
        var bowTie = Loop(new[]
        {
            new Vector2(0, 0), new Vector2(2, 2), new Vector2(0, 2), new Vector2(2, 0)
        });
        Assert.That(InteriorTopology.TryBuild(bowTie, 0.05f, out _, out var reason), Is.False);
        StringAssert.Contains("cruzadas", reason);
    }

    private static List<InteriorTopology.Segment> Loop(IReadOnlyList<Vector2> points)
    {
        var result = new List<InteriorTopology.Segment>();
        for (int i = 0; i < points.Count; i++)
            result.Add(new InteriorTopology.Segment(points[i], points[(i + 1) % points.Count]));
        return result;
    }
}
