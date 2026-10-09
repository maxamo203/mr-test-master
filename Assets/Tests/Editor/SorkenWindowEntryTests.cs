using System.Linq;
using Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class SorkenWindowEntryTests
{
    [Test]
    public void DefensaConservaProgresoYPosicionHastaDosSegundos()
    {
        var atHalfSecond = GameDirector.StepDefense(
            1.4f, 0f, false, 0.5f, 2f, 0.5f);
        var atExactBoundary = GameDirector.StepDefense(
            atHalfSecond.Progress, 1.9f, false, 0.1f, 2f, 0.5f);

        Assert.That(atHalfSecond.Progress, Is.EqualTo(1.4f).Within(0.0001f));
        Assert.That(atHalfSecond.HoldPosition, Is.True);
        Assert.That(atExactBoundary.Progress, Is.EqualTo(1.4f).Within(0.0001f));
        Assert.That(atExactBoundary.HoldPosition, Is.True);
    }

    [Test]
    public void DefensaDegradaGradualmenteDespuesDeLaTolerancia()
    {
        var afterBoundary = GameDirector.StepDefense(
            1.4f, 2f, false, 0.1f, 2f, 0.5f);
        var recovered = GameDirector.StepDefense(
            afterBoundary.Progress, afterBoundary.LightLostSeconds,
            true, 0.2f, 2f, 0.5f);

        Assert.That(afterBoundary.HoldPosition, Is.False);
        Assert.That(afterBoundary.Progress, Is.EqualTo(1.35f).Within(0.0001f));
        Assert.That(recovered.Progress, Is.EqualTo(1.55f).Within(0.0001f));
        Assert.That(recovered.LightLostSeconds, Is.Zero);
    }

    [Test]
    public void CapturaYFallbackRespetanObstaculos()
    {
        Assert.That(GameDirector.CanCaptureAtDistance(0.8f, 1.1f, true), Is.True);
        Assert.That(GameDirector.CanCaptureAtDistance(0.8f, 1.1f, false), Is.False);
        Assert.That(GameDirector.CanAdvanceWithoutPath(false), Is.True,
            "Sin geometria escaneada se admite linea recta.");
        Assert.That(GameDirector.CanAdvanceWithoutPath(true), Is.False,
            "Con obstaculos y sin ruta debe esperar/recalcular.");
    }

    [Test]
    public void PerfilesSorkenUsanDefensaParaEspaciosPequenos()
    {
        string[] guids = AssetDatabase.FindAssets("t:NightConfig",
            new[] { "Assets/Gameplay/Nights" });
        Assert.That(guids.Length, Is.GreaterThanOrEqualTo(12));

        foreach (string guid in guids)
        {
            NightConfig night = AssetDatabase.LoadAssetAtPath<NightConfig>(
                AssetDatabase.GUIDToAssetPath(guid));
            Assert.That(night.sorkenPostEntryPauseSeconds, Is.GreaterThanOrEqualTo(2.5f), night.name);
            Assert.That(night.sorkenChaseSpeed, Is.InRange(0.4f, 0.6f), night.name);
            Assert.That(night.sorkenIlluminatedSpeed, Is.LessThanOrEqualTo(0.1f), night.name);
            Assert.That(night.sorkenAimToleranceSeconds, Is.EqualTo(2f).Within(0.001f), night.name);
            Assert.That(night.sorkenRepelDecayPerSecond, Is.EqualTo(0.5f).Within(0.001f), night.name);
        }
    }

    [Test]
    public void PrefabUsaLaCaminataCubiertaConAlasReplegadas()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Entities/Prefabs/SorkenGameplay.prefab");
        SorkenAnimator animator = prefab.GetComponent<SorkenAnimator>();
        AnimationClip clip = animator.coverWalkClip;

        Assert.That(clip, Is.Not.Null);
        Assert.That(clip.name, Is.EqualTo("Sorken_CoverWalk_WingsRested_Gameplay"));
        Assert.That(clip.length, Is.EqualTo(3.97f).Within(0.02f));

        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        Assert.That(bindings.Any(binding => binding.path.StartsWith("Armature/Hips")), Is.True);
        Assert.That(bindings.Any(binding => string.IsNullOrEmpty(binding.path) ||
                                            binding.path == "Armature"), Is.False);
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
    public void PrimeraRutaNoHaceVolverAlSorkenHaciaLaVentana()
    {
        Vector3 origin = Vector3.zero;
        var path = new[]
        {
            new Vector3(0f, 0f, -0.8f),
            new Vector3(0.02f, 0f, 0.01f),
            new Vector3(0.3f, 0f, 0.7f),
            new Vector3(1f, 0f, 2f)
        };

        int first = GameDirector.FirstForwardEntryWaypoint(
            path, 0, origin, Vector3.forward);

        Assert.That(first, Is.EqualTo(2));
        Assert.That(Vector3.Dot(path[first] - origin, Vector3.forward), Is.Positive);
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
    public void AterrizajeNoDuplicaLaTraslacionDelHipsDelFbx()
    {
        Vector3 reference = new(0f, 7.3f, 100f);
        Vector3 animatedBelowFloor = new(2f, -54.4f, 114f);

        Vector3 corrected = SorkenAnimator.CorrectLandingHipsPosition(
            animatedBelowFloor, reference, localCompression: 14f,
            impact: 0f, landingWeight: 1f);

        Assert.That(corrected, Is.EqualTo(reference));
    }

    [Test]
    public void CorreccionDelHipsSeLiberaDuranteElFundidoAPersecucion()
    {
        Vector3 reference = new(0f, 7.3f, 100f);
        Vector3 chasePose = new(0f, 8f, 101f);

        Assert.That(SorkenAnimator.CorrectLandingHipsPosition(
            chasePose, reference, 14f, 0f, 0f), Is.EqualTo(chasePose));
    }

    [Test]
    public void EntidadRestauraLaEscalaQueElClipDeVentanaIntentaSobrescribir()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Entities/Prefabs/SorkenGameplay.prefab");
        GameObject instance = Object.Instantiate(prefab);

        try
        {
            SorkenEntity entity = instance.GetComponent<SorkenEntity>();
            Vector3 gameplayScale = instance.transform.localScale;
            typeof(SorkenEntity).GetMethod("Awake",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(entity, null);
            entity.SetPositionDirectly(instance.transform.position);

            instance.transform.localScale = Vector3.one * 0.01f;
            typeof(SorkenEntity).GetMethod("LateUpdate",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(entity, null);

            Assert.That(instance.transform.localScale, Is.EqualTo(gameplayScale));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
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
    public void NocheCortaReservaUnaSenalAntesDeAnimarLaVentana()
    {
        float start = GameDirector.EntryAnimationStart(
            true, graceSeconds: 4f, animationDuration: 5.93f,
            minimumWindowCueSeconds: 1.25f);
        float end = GameDirector.EntryEnd(
            true, graceSeconds: 4f, animationStart: start, animationDuration: 5.93f);

        Assert.That(start, Is.EqualTo(1.25f).Within(0.001f));
        Assert.That(end, Is.EqualTo(7.18f).Within(0.001f));
    }

    [Test]
    public void NocheLargaConservaLaVentanaDeReaccionConfigurada()
    {
        float start = GameDirector.EntryAnimationStart(
            true, graceSeconds: 11f, animationDuration: 5.93f,
            minimumWindowCueSeconds: 1.25f);

        Assert.That(start, Is.EqualTo(5.07f).Within(0.001f));
    }

    [Test]
    public void HumoOcultaElCruceSinDesaparecerAntesDelAterrizaje()
    {
        Assert.That(SorkenDarknessCue.TargetAmount(SorkenState.Idle), Is.EqualTo(1f));
        Assert.That(SorkenDarknessCue.TargetAmount(SorkenState.EmergingDoor),
            Is.GreaterThanOrEqualTo(0.9f));
        Assert.That(SorkenDarknessCue.TargetAmount(SorkenState.EmergingWindow),
            Is.GreaterThanOrEqualTo(0.9f));
        Assert.That(SorkenDarknessCue.TargetAmount(SorkenState.WindowLanding),
            Is.GreaterThanOrEqualTo(0.7f));
        Assert.That(SorkenDarknessCue.TargetAmount(SorkenState.Chasing), Is.EqualTo(0f));
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
        Assert.That(animator.emergeClip.name, Is.EqualTo("Sorken_DoorEntry_Gameplay"));
        Assert.That(animator.emergeWindowClip.name, Is.EqualTo("Sorken_WindowEntry_Gameplay"));
        Assert.That(animator.windowLandingClip.name, Is.EqualTo("Sorken_WindowLanding_Gameplay"));
        Assert.That(animator.emergeClip.length, Is.EqualTo(5.93f).Within(0.05f));
        Assert.That(animator.emergeWindowClip.length, Is.EqualTo(2.97f).Within(0.05f));
        Assert.That(animator.windowLandingClip.length, Is.EqualTo(1.1f).Within(0.05f));

        AssertClipApuntaAlRigDelPrefab(animator.emergeClip);
        AssertClipApuntaAlRigDelPrefab(animator.emergeWindowClip);
        AssertClipApuntaAlRigDelPrefab(animator.windowLandingClip);
    }

    private static void AssertClipApuntaAlRigDelPrefab(AnimationClip clip)
    {
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        bool animaHipsReal = false;

        foreach (EditorCurveBinding binding in bindings)
        {
            Assert.That(binding.path, Is.Not.Empty,
                $"{clip.name} no debe animar el Transform raiz del prefab.");
            if (binding.path == "Armature/Hips") animaHipsReal = true;
        }

        Assert.That(animaHipsReal, Is.True,
            $"{clip.name} debe apuntar a Armature/Hips, no a un Hips inexistente.");
    }
}
