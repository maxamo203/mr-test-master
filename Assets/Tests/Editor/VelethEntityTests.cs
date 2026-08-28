using NUnit.Framework;
using UnityEngine;

public class VelethEntityTests
{
    private GameObject _go;
    private VelethEntity _veleth;

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("VelethTest");
        _veleth = _go.AddComponent<VelethEntity>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_go);
    }

    [Test]
    public void PersigueAlObjetivoSinAlterarLaAltura()
    {
        _veleth.SetPositionDirectly(new Vector3(0f, 1.2f, 0f));

        _veleth.MoveTo(new Vector3(10f, 4f, 0f), speed: 3f, deltaTime: 1f);

        Assert.That(_veleth.Position.x, Is.EqualTo(3f).Within(0.0001f));
        Assert.That(_veleth.Position.y, Is.EqualTo(1.2f).Within(0.0001f));
    }

    [Test]
    public void AdaptaLaDireccionCuandoElJugadorCambiaDeRuta()
    {
        _veleth.MoveTo(new Vector3(10f, 0f, 0f), speed: 1f, deltaTime: 1f);
        Vector3 primera = _veleth.Position;

        _veleth.MoveTo(new Vector3(1f, 0f, 10f), speed: 1f, deltaTime: 1f);

        Assert.That(_veleth.Position.z, Is.GreaterThan(primera.z));
        Assert.That(Vector3.Dot(_veleth.transform.forward, Vector3.forward), Is.GreaterThan(0f));
    }

    [Test]
    public void EstadoDeAgarreQuedaDisponibleParaReplicacion()
    {
        _veleth.SetState(VelethState.Grabbing);
        Assert.That(_veleth.State, Is.EqualTo(VelethState.Grabbing));
    }
}
