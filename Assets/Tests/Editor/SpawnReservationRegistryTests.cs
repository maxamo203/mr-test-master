using Gameplay.Spawning;
using NUnit.Framework;
using UnityEngine;

public class SpawnReservationRegistryTests
{
    [SetUp]
    public void SetUp() => SpawnReservationRegistry.ResetForTests();

    [Test]
    public void DifferentSystemsCannotReserveOverlappingFootprints()
    {
        SpawnReservationRegistry.BeginGeometry(10);
        Assert.That(SpawnReservationRegistry.TryReserve(
            "battery", "0", Vector3.zero, 0.1f, 0.05f, out _), Is.True);
        Assert.That(SpawnReservationRegistry.TryReserve(
            "collectible", "0", new Vector3(0.3f, 0f, 0f), 0.1f, 0.15f, out var reason), Is.False);
        StringAssert.Contains("battery", reason);
    }

    [Test]
    public void NewGeometryInvalidatesAllReservations()
    {
        SpawnReservationRegistry.BeginGeometry(10);
        Assert.That(SpawnReservationRegistry.TryReserve(
            "battery", "0", Vector3.zero, 0.1f, 0f, out _), Is.True);
        SpawnReservationRegistry.BeginGeometry(11);
        Assert.That(SpawnReservationRegistry.Count, Is.Zero);
    }

    [Test]
    public void PrefabFootprintIncludesRealMeshScale()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            cube.transform.localScale = new Vector3(2f, 1f, 4f);
            float radius = InteriorSpawnValidator.EstimatePrefabFootprintRadius(cube, 0.1f);
            Assert.That(radius, Is.EqualTo(Mathf.Sqrt(5f)).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(cube);
        }
    }
}
