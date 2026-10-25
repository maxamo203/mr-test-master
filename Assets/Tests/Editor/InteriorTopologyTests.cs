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
        var topology = InteriorTopology.Build(Loop(points), 0.05f);
        Assert.That(topology.Rooms.Count, Is.EqualTo(1), topology.Detalle);
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
        var topology = InteriorTopology.Build(segments, 0.05f);
        Assert.That(topology.Rooms.Count, Is.EqualTo(2), topology.Detalle);
        Assert.That(topology.ContainsDisc(new Vector2(1, 1), 0.1f), Is.True);
        Assert.That(topology.ContainsDisc(new Vector2(5, 1), 0.1f), Is.True);
        Assert.That(topology.ContainsDisc(new Vector2(3, 1), 0.1f), Is.False);
        Assert.That(topology.TryGetRoomIndex(new Vector2(1, 1), out int firstRoom), Is.True);
        Assert.That(topology.TryGetRoomIndex(new Vector2(5, 1), out int secondRoom), Is.True);
        Assert.That(secondRoom, Is.Not.EqualTo(firstRoom));
        Assert.That(topology.ContainsDiscInRoom(firstRoom, new Vector2(5, 1), 0.1f), Is.False);
    }

    [Test]
    public void SameConcaveRoomStillRequiresDirectLineWithoutWalls()
    {
        var points = new[]
        {
            new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 1),
            new Vector2(1, 1), new Vector2(1, 4), new Vector2(0, 4)
        };
        var topology = InteriorTopology.Build(Loop(points), 0.05f);
        Assert.That(topology.TryGetRoomIndex(new Vector2(0.5f, 3f), out int room), Is.True);
        Assert.That(topology.ContainsDiscInRoom(room, new Vector2(3f, 0.5f), 0.1f), Is.True);
        Assert.That(topology.HasClearSegment(new Vector2(0.5f, 3f), new Vector2(3f, 0.5f)), Is.False);
    }

    [Test]
    public void OpenChainIsClosedInternally()
    {
        // U de tres paredes: falta la de y = 2. Se cierra uniendo (0,2) con (2,2).
        var open = new List<InteriorTopology.Segment>
        {
            new(new Vector2(0, 2), new Vector2(0, 0)),
            new(new Vector2(0, 0), new Vector2(2, 0)),
            new(new Vector2(2, 0), new Vector2(2, 2))
        };
        var topology = InteriorTopology.Build(open, 0.05f);
        Assert.That(topology.Rooms.Count, Is.EqualTo(1), topology.Detalle);
        Assert.That(topology.RoomArea(0), Is.EqualTo(4f).Within(0.01f));
        Assert.That(topology.ContainsDisc(new Vector2(1, 1), 0.2f), Is.True);
        // La pared de cierre marca el borde del recinto...
        Assert.That(topology.ContainsDisc(new Vector2(1, 1.95f), 0.1f), Is.False);
        Assert.That(topology.ContainsDisc(new Vector2(1, 2.5f), 0.1f), Is.False);
        // ...pero no existe: no tapa la linea de vision.
        Assert.That(topology.HasClearSegment(new Vector2(1, 1), new Vector2(1, 3)), Is.True);
        Assert.That(topology.Boundaries.Count, Is.EqualTo(3));
    }

    [Test]
    public void SmallScanErrorSnapsWithinTolerance()
    {
        var almostClosed = new List<InteriorTopology.Segment>
        {
            new(new Vector2(0, 0), new Vector2(2, 0)),
            new(new Vector2(2, 0), new Vector2(2, 2)),
            new(new Vector2(2, 2), new Vector2(0, 2)),
            new(new Vector2(0, 2), new Vector2(0.04f, 0.03f))
        };
        Assert.That(InteriorTopology.Build(almostClosed, 0.08f).Rooms.Count, Is.EqualTo(1));
        // Sin snap queda abierta, pero igual se cierra internamente.
        Assert.That(InteriorTopology.Build(almostClosed, 0.01f).Rooms.Count, Is.EqualTo(1));
    }

    [Test]
    public void SingleWallIsIgnoredButStillBlocks()
    {
        var single = new List<InteriorTopology.Segment>
        {
            new(new Vector2(0, 0), new Vector2(2, 0))
        };
        var topology = InteriorTopology.Build(single, 0.05f);
        Assert.That(topology.HasRooms, Is.False);
        Assert.That(topology.ClearOfWalls(new Vector2(1, 0.05f), 0.1f), Is.False);
        Assert.That(topology.ClearOfWalls(new Vector2(1, 1f), 0.1f), Is.True);
        Assert.That(topology.HasClearSegment(new Vector2(1, -1), new Vector2(1, 1)), Is.False);
    }

    [Test]
    public void StraightChainHasNoArea()
    {
        var straight = new List<InteriorTopology.Segment>
        {
            new(new Vector2(0, 0), new Vector2(1, 0)),
            new(new Vector2(1, 0), new Vector2(2, 0))
        };
        Assert.That(InteriorTopology.Build(straight, 0.05f).HasRooms, Is.False);
    }

    [Test]
    public void RoomsTouchingAtACornerAreSplitByPolyline()
    {
        // Dos cuartos dibujados como polilineas distintas que comparten la esquina (2,2).
        var segments = Loop(new[]
        {
            new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 2), new Vector2(0, 2)
        }, "a");
        segments.AddRange(Loop(new[]
        {
            new Vector2(2, 2), new Vector2(4, 2), new Vector2(4, 4), new Vector2(2, 4)
        }, "b"));
        var topology = InteriorTopology.Build(segments, 0.05f);
        Assert.That(topology.Rooms.Count, Is.EqualTo(2), topology.Detalle);
        Assert.That(topology.ContainsDisc(new Vector2(1, 1), 0.2f), Is.True);
        Assert.That(topology.ContainsDisc(new Vector2(3, 3), 0.2f), Is.True);
    }

    [Test]
    public void OneClosedAndOneOpenGroupBothCount()
    {
        var segments = Loop(new[]
        {
            new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 2), new Vector2(0, 2)
        }, "a");
        segments.Add(new InteriorTopology.Segment(new Vector2(5, 0), new Vector2(8, 0), "b"));
        segments.Add(new InteriorTopology.Segment(new Vector2(8, 0), new Vector2(8, 3), "b"));
        segments.Add(new InteriorTopology.Segment(new Vector2(10, 10), new Vector2(11, 10), "c"));
        var topology = InteriorTopology.Build(segments, 0.05f);
        Assert.That(topology.Rooms.Count, Is.EqualTo(2), topology.Detalle);
        // Triangulo (5,0)-(8,0)-(8,3): su interior cuenta.
        Assert.That(topology.ContainsDisc(new Vector2(7.2f, 0.8f), 0.2f), Is.True);
        StringAssert.Contains("suelta", topology.Detalle);
    }

    private static List<InteriorTopology.Segment> Loop(IReadOnlyList<Vector2> points, string group = null)
    {
        var result = new List<InteriorTopology.Segment>();
        for (int i = 0; i < points.Count; i++)
            result.Add(new InteriorTopology.Segment(points[i], points[(i + 1) % points.Count], group));
        return result;
    }
}
