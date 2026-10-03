using Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class SorkenWindowEntryTests
{
    [Test]
    public void PrimerIntentoDeLaPrimeraNocheFuerzaPuerta()
    {
        Assert.That(GameDirector.ShouldForceDoorOnFirstAttempt(0, true), Is.True);
        Assert.That(GameDirector.ShouldForceDoorOnFirstAttempt(0, false), Is.False);
        Assert.That(GameDirector.ShouldForceDoorOnFirstAttempt(1, true), Is.False);
        Assert.That(GameDirector.IsDoorKind("door"), Is.True);
        Assert.That(GameDirector.IsDoorKind("Puerta_Principal"), Is.True);
        Assert.That(GameDirector.IsDoorKind("window"), Is.False);
    }

    [Test]
    public void AterrizajeParteExactamenteDesdeLaVentana()
    {
        Vector3 start = new(1f, 2.2f, -1f);
        Vector3 end = new(2f, 0f, 1f);

        Assert.That(GameDirector.WindowLandingPosition(start, end, 0f), Is.EqualTo(start));
    }

    [Test]
    public void BajadaAvanzaHaciaElPisoSinTeletransportarse()
    {
        Vector3 start = new(0f, 2f, 0f);
        Vector3 end = new(1f, 0f, 1f);

        Vector3 early = GameDirector.WindowLandingPosition(start, end, 0.17f);
        Vector3 late = GameDirector.WindowLandingPosition(start, end, 0.51f);

        Assert.That(early.y, Is.GreaterThan(late.y));
        Assert.That(early.y, Is.GreaterThan(end.y));
        Assert.That(late.y, Is.GreaterThan(end.y));
        Assert.That(early.x, Is.GreaterThan(start.x));
        Assert.That(late.x, Is.GreaterThan(early.x));
    }

    [Test]
    public void UltimoTramoPermaneceEnElPisoParaRecuperarLaPostura()
    {
        Vector3 start = new(0f, 2f, 0f);
        Vector3 end = new(1f, 0f, 1f);

        Assert.That(GameDirector.WindowLandingPosition(start, end, 0.68f).y, Is.GreaterThan(end.y));
        Assert.That(GameDirector.WindowLandingPosition(start, end, 0.82f), Is.EqualTo(end));
        Assert.That(GameDirector.WindowLandingPosition(start, end, 0.9f), Is.EqualTo(end));
        Assert.That(GameDirector.WindowLandingPosition(start, end, 1f), Is.EqualTo(end));
    }

    [Test]
    public void PuertaCruzaSuavementeSinCambiarDeAltura()
    {
        Vector3 start = new(0f, 0.2f, -1f);
        const float crossingDistance = 1.08f;

        Vector3 early = GameDirector.DoorEntryPosition(start, Vector3.forward, 0.2f, crossingDistance);
        Vector3 middle = GameDirector.DoorEntryPosition(start, Vector3.forward, 0.5f, crossingDistance);
        Vector3 late = GameDirector.DoorEntryPosition(start, Vector3.forward, 0.8f, crossingDistance);

        Assert.That(early.y, Is.EqualTo(start.y));
        Assert.That(middle.y, Is.EqualTo(start.y));
        Assert.That(late.y, Is.EqualTo(start.y));
        Assert.That(early.z, Is.GreaterThan(start.z));
        Assert.That(middle.z, Is.GreaterThan(early.z));
        Assert.That(late.z, Is.GreaterThan(middle.z));
        Assert.That(GameDirector.DoorEntryPosition(start, Vector3.forward, 0f, crossingDistance),
            Is.EqualTo(start));
        Vector3 finished = GameDirector.DoorEntryPosition(
            start, Vector3.forward, 1f, crossingDistance);
        Assert.That(finished.x, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(finished.y, Is.EqualTo(0.2f).Within(0.0001f));
        Assert.That(finished.z, Is.EqualTo(0.08f).Within(0.0001f));
    }

    [Test]
    public void PuertaApareceDesdeElInicioALaAlturaDelPiso()
    {
        Vector3 result = GameDirector.DoorEmergePosition(
            new Vector3(2f, 1.7f, 3f), Vector3.forward, 0.5f, 0.15f);

        Assert.That(result, Is.EqualTo(new Vector3(2f, 0.15f, 2.5f)));
    }

    [Test]
    public void NieblaPermaneceDuranteTodaLaEntrada()
    {
        Assert.That(SorkenDarknessCue.KeepsMistBuilt(SorkenState.Idle), Is.True);
        Assert.That(SorkenDarknessCue.KeepsMistBuilt(SorkenState.EmergingDoor), Is.True);
        Assert.That(SorkenDarknessCue.KeepsMistBuilt(SorkenState.EmergingWindow), Is.True);
        Assert.That(SorkenDarknessCue.KeepsMistBuilt(SorkenState.WindowLanding), Is.True);
        Assert.That(SorkenDarknessCue.KeepsMistBuilt(SorkenState.Chasing), Is.False);
    }

    [Test]
    public void NuevoEstadoNoCambiaLosValoresDeRedExistentes()
    {
        Assert.That((byte)SorkenState.EmergingWindow, Is.EqualTo(7));
        Assert.That((byte)SorkenState.WindowLanding, Is.EqualTo(8));
    }

    [Test]
    public void EntradaConservaElApoyoYNoTeletransportaElRoot()
    {
        Assert.That(SorkenAnimator.EvaluateWindowEntryRootOffset(0f), Is.EqualTo(0f));
        Assert.That(SorkenAnimator.EvaluateWindowEntryRootOffset(0.39f),
            Is.InRange(0.05f, 0.07f));
        Assert.That(SorkenAnimator.EvaluateWindowEntryRootOffset(0.56f),
            Is.InRange(-0.21f, -0.12f));
        Assert.That(SorkenAnimator.EvaluateWindowEntryRootOffset(1f),
            Is.EqualTo(-0.1871f).Within(0.0001f));
    }

    [Test]
    public void OffsetDeEntradaRespetaLaNormalHorizontalDeLaVentana()
    {
        Vector3 result = GameDirector.WindowEntryPosition(
            new Vector3(2f, 1.4f, 3f), new Vector3(0f, 2f, 4f),
            1f, 1.25f, -0.2f);

        Assert.That(result, Is.EqualTo(new Vector3(2f, 1.4f, 4.05f)));
    }

    [Test]
    public void EntradaCruzaElPlanoDeLaParedAntesDeDescender()
    {
        Vector3 outside = new(0f, 1.4f, -1f);
        Vector3 forward = Vector3.forward;

        Vector3 start = GameDirector.WindowEntryPosition(outside, forward, 0f, 1.25f, 0f);
        Vector3 middle = GameDirector.WindowEntryPosition(outside, forward, 0.5f, 1.25f, -0.1f);
        Vector3 end = GameDirector.WindowEntryPosition(outside, forward, 1f, 1.25f, -0.1871f);

        Assert.That(start, Is.EqualTo(outside));
        Assert.That(middle.z, Is.GreaterThan(outside.z));
        Assert.That(end.z, Is.GreaterThan(0f),
            "Al terminar, el root debe quedar del lado interior de la pared.");
    }

    [Test]
    public void VentanaAltaDaMasTiempoParaDescender()
    {
        float low = SorkenAnimator.EvaluateWindowLandingDuration(2.4f, 0.75f, 0.4f);
        float medium = SorkenAnimator.EvaluateWindowLandingDuration(2.4f, 0.75f, 1f);
        float high = SorkenAnimator.EvaluateWindowLandingDuration(2.4f, 0.75f, 2f);

        Assert.That(low, Is.LessThan(medium));
        Assert.That(high, Is.GreaterThan(medium));
        Assert.That(high, Is.LessThanOrEqualTo(4f));
    }

    [Test]
    public void PrefabTieneLosDosClipsDeVentanaIntegrados()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Entities/Prefabs/SorkenGameplay.prefab");
        SorkenAnimator animator = prefab.GetComponent<SorkenAnimator>();

        Assert.That(animator.emergeWindowClip, Is.Not.Null);
        Assert.That(animator.windowLandingClip, Is.Not.Null);
        Assert.That(animator.emergeWindowClip.name, Is.EqualTo("Sorken_WindowEntry_v02"));
        Assert.That(animator.windowLandingClip.name, Is.EqualTo("Sorken_WindowLanding_v02"));
        Assert.That(animator.emergeWindowClip.length, Is.EqualTo(5.93f).Within(0.05f));
        Assert.That(animator.windowLandingClip.length, Is.EqualTo(1.1f).Within(0.05f));
    }
}
