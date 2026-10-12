using NUnit.Framework;
using UnityEngine;
using Bateries;

public class ArbmosV2Tests
{
    [Test]
    public void DesplazamientoDelArbmosFrenaEnApoyoYAceleraEntrePasos()
    {
        const float clipLength = 18f;

        float apoyo = ArbmosAnimator.EvaluateStepSpeed(
            playbackSeconds: 1.5, clipLength, stepsPerLoop: 6, plantedSpeedRatio: 0.15f);
        float transferencia = ArbmosAnimator.EvaluateStepSpeed(
            playbackSeconds: 3.0, clipLength, stepsPerLoop: 6, plantedSpeedRatio: 0.15f);

        Assert.That(apoyo, Is.EqualTo(0.15f).Within(0.001f));
        Assert.That(transferencia, Is.EqualTo(1.85f).Within(0.001f));
    }

    [Test]
    public void DesplazamientoDelArbmosConservaLaVelocidadMediaConfigurada()
    {
        const float clipLength = 18f;
        const int samples = 600;
        float sum = 0f;

        for (int i = 0; i < samples; i++)
            sum += ArbmosAnimator.EvaluateStepSpeed(
                i * clipLength / samples, clipLength, 6, 0.15f);

        Assert.That(sum / samples, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void VelocidadMediaDelArbmosSeDerivaDeSusPasosReales()
    {
        const float clipLength = 18f;
        const int steps = 6;
        const float metersPerStep = 1.5f;

        const float playbackSpeed = 1f;
        float speed = ArbmosAnimator.EvaluateAverageChaseSpeed(
            clipLength, steps, metersPerStep, playbackSpeed);

        Assert.That(speed, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(clipLength / playbackSpeed, Is.EqualTo(18f).Within(0.001f));
    }

    [TestCase(12, 0.2f)]
    [TestCase(5, 0.2f)]
    [TestCase(4, 0.2f)]
    public void SincronizacionDeTodasLasEntidadesConservaLaVelocidadMedia(
        int impulsesPerLoop, float plantedSpeedRatio)
    {
        const int samples = 1200;
        float sum = 0f;

        for (int i = 0; i < samples; i++)
            sum += AnimationMotionSync.EvaluateNormalized(
                i / (float)samples, impulsesPerLoop, plantedSpeedRatio);

        Assert.That(sum / samples, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void SincronizacionFrenaEnCadaApoyoSinDetenerLaEntidad()
    {
        float planted = AnimationMotionSync.EvaluateNormalized(
            normalizedTime: 0.5f / 12f, impulsesPerLoop: 12, plantedSpeedRatio: 0.2f);
        float transfer = AnimationMotionSync.EvaluateNormalized(
            normalizedTime: 1f / 12f, impulsesPerLoop: 12, plantedSpeedRatio: 0.2f);

        Assert.That(planted, Is.EqualTo(0.2f).Within(0.001f));
        Assert.That(transfer, Is.EqualTo(1.8f).Within(0.001f));
    }

    [Test]
    public void PlayerPoseConservaElModoDeLinternaEnRed()
    {
        var original = new PlayerPoseMsg
        {
            RelPos = new Vector3(1f, 2f, 3f),
            Forward = new Vector3(0f, 0f, 1f),
            FlashlightMode = FlashlightMode.Bright,
            TrackingValid = false,
            FlashlightCharge01 = 0.42f,
        };

        PlayerPoseMsg copy = PlayerPoseMsg.Deserialize(original.Serialize());

        Assert.That(copy.RelPos, Is.EqualTo(original.RelPos));
        Assert.That(copy.Forward, Is.EqualTo(original.Forward));
        Assert.That(copy.FlashlightMode, Is.EqualTo(FlashlightMode.Bright));
        Assert.That(copy.TrackingValid, Is.False);
        Assert.That(copy.FlashlightCharge01, Is.EqualTo(0.42f).Within(0.001f));
    }

    [Test]
    public void ModoIntensoSoloExisteMientrasSeMantieneLaAccion()
    {
        var go = new GameObject("Flashlight test");
        var flashlight = go.AddComponent<Flashlight>();
        flashlight.requireMatchToOperate = false;
        flashlight.maxCharge = 100f;
        flashlight.currentCharge = 100f;
        flashlight.isOn = true;

        Assert.That(flashlight.Mode, Is.EqualTo(FlashlightMode.Dim));
        flashlight.SetBrightHeld(true);
        Assert.That(flashlight.Mode, Is.EqualTo(FlashlightMode.Bright));
        flashlight.SetBrightHeld(false);
        Assert.That(flashlight.Mode, Is.EqualTo(FlashlightMode.Dim));

        Object.DestroyImmediate(go);
    }

[Test]
    public void ModoIntensoConcentraElConoYReduceSuDegradado()
    {
        var go = new GameObject("Flashlight concentrated cone feedback test");
        var flashlight = go.AddComponent<Flashlight>();
        flashlight.requireMatchToOperate = false;
        flashlight.currentCharge = 100f;
        flashlight.isOn = true;
        flashlight.outerAngleDeg = 10f;
        flashlight.innerAngleDeg = 4f;
        flashlight.brightConeAngleMultiplier = 0.75f;
        
        flashlight.edgeHaloAngleDeg = 5f;
        flashlight.edgeHaloStrength = 0.75f;
        flashlight.midHaloAngleDeg = 8f;
        flashlight.midHaloStrength = 0.5f;
        flashlight.brightEdgeHaloAngleDeg = 1.25f;
        
        flashlight.brightMidHaloAngleDeg = 1.25f;
        flashlight.brightMidHaloStrength = 0.1f;
flashlight.brightEdgeHaloStrength = 0.18f;
        flashlight.farHaloAngleDeg = 15f;
        flashlight.farHaloStrength = 0.2f;
        flashlight.brightFarHaloAngleDeg = 1.5f;
        flashlight.brightFarHaloStrength = 0.05f;
flashlight.brightEdgeFadeDeg = 1f;

        Assert.That(flashlight.VisualOuterAngleDeg, Is.EqualTo(10f));
        
        Assert.That(flashlight.VisualHaloAngleDeg, Is.EqualTo(15f));
        Assert.That(flashlight.VisualHaloStrength, Is.EqualTo(0.75f));
        Assert.That(flashlight.VisualMidHaloAngleDeg, Is.EqualTo(23f));
        Assert.That(flashlight.VisualMidHaloStrength, Is.EqualTo(0.5f));
        Assert.That(flashlight.VisualFarHaloAngleDeg, Is.EqualTo(38f));
        Assert.That(flashlight.VisualFarHaloStrength, Is.EqualTo(0.2f));
Assert.That(flashlight.VisualOuterAngleDeg - flashlight.VisualInnerAngleDeg,
                    Is.EqualTo(6f));
        flashlight.SetBrightHeld(true);
        Assert.That(flashlight.VisualOuterAngleDeg, Is.EqualTo(7.5f));
        Assert.That(flashlight.VisualInnerAngleDeg, Is.EqualTo(6.5f).Within(0.001f));
        
        Assert.That(flashlight.VisualHaloAngleDeg, Is.EqualTo(8.75f).Within(0.001f));
        Assert.That(flashlight.VisualHaloStrength, Is.EqualTo(0.18f));
        Assert.That(flashlight.VisualMidHaloAngleDeg, Is.EqualTo(10f).Within(0.001f));
        Assert.That(flashlight.VisualMidHaloStrength, Is.EqualTo(0.1f));
        Assert.That(flashlight.VisualFarHaloAngleDeg, Is.EqualTo(11.5f).Within(0.001f));
        Assert.That(flashlight.VisualFarHaloStrength, Is.EqualTo(0.05f));
Assert.That(flashlight.VisualOuterAngleDeg - flashlight.VisualInnerAngleDeg,
                    Is.EqualTo(1f).Within(0.001f));
        Assert.That(flashlight.outerAngleDeg, Is.EqualTo(10f));

        Object.DestroyImmediate(go);
    }

    [Test]
    public void PulsacionCortaEjecutaTapSoloAlSoltar()
    {
        var gesture = new PrimaryButtonGesture();

        Assert.That(gesture.Tick(true, 0.2f, 1f), Is.EqualTo(PrimaryGestureEvent.None));
        Assert.That(gesture.Tick(false, 0f, 1f), Is.EqualTo(PrimaryGestureEvent.Tap));
    }

    [Test]
    public void MantenerMedioSegundoNoGeneraTapYTerminaAlSoltar()
    {
        var gesture = new PrimaryButtonGesture();

        Assert.That(gesture.Tick(true, 0.25f, 0.5f), Is.EqualTo(PrimaryGestureEvent.None));
        Assert.That(gesture.Tick(true, 0.25f, 0.5f), Is.EqualTo(PrimaryGestureEvent.HoldStarted));
        Assert.That(gesture.Tick(true, 0.2f, 0.5f), Is.EqualTo(PrimaryGestureEvent.None));
        Assert.That(gesture.Tick(false, 0f, 0.5f), Is.EqualTo(PrimaryGestureEvent.HoldReleased));
    }

    [Test]
    public void HoldDesdeApagadaEnciendeIntensaYAlSoltarQuedaTenue()
    {
        var go = new GameObject("Flashlight unified button test");
        var flashlight = go.AddComponent<Flashlight>();
        flashlight.requireMatchToOperate = false;
        flashlight.currentCharge = 100f;
        flashlight.isOn = false;

        Assert.That(flashlight.BeginBrightHold(), Is.True);
        Assert.That(flashlight.Mode, Is.EqualTo(FlashlightMode.Bright));
        flashlight.SetBrightHeld(false);
        Assert.That(flashlight.Mode, Is.EqualTo(FlashlightMode.Dim));

        Object.DestroyImmediate(go);
    }

    [Test]
    public void LinternaApagadaNoPuedeQuedarEnModoIntenso()
    {
        var go = new GameObject("Flashlight off test");
        var flashlight = go.AddComponent<Flashlight>();
        flashlight.requireMatchToOperate = false;
        flashlight.currentCharge = 100f;
        flashlight.isOn = false;

        flashlight.SetBrightHeld(true);

        Assert.That(flashlight.Mode, Is.EqualTo(FlashlightMode.Off));
        Object.DestroyImmediate(go);
    }

    [Test]
    public void CadaModoUsaSuConsumoYApagadaNoConsume()
    {
        var go = new GameObject("Flashlight drain test");
        var flashlight = go.AddComponent<Flashlight>();
        flashlight.requireMatchToOperate = false;
        flashlight.currentCharge = 100f;
        flashlight.drainPerSecond = 2f;
        flashlight.brightDrainPerSecond = 7f;
        flashlight.isOn = true;

        Assert.That(flashlight.CurrentDrainPerSecond, Is.EqualTo(2f));
        flashlight.SetBrightHeld(true);
        Assert.That(flashlight.CurrentDrainPerSecond, Is.EqualTo(7f));
        flashlight.Toggle();
        Assert.That(flashlight.CurrentDrainPerSecond, Is.Zero);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void MensajeDeMuerteConservaAtacanteYResultadoGlobal()
    {
        var original = new PlayerDeathMsg
        {
            KillerFacePosition = new Vector3(3f, 2f, 1f),
            KillerNetworkId = 42,
            AllPlayersDead = true,
        };

        PlayerDeathMsg copy = PlayerDeathMsg.Deserialize(original.Serialize());

        Assert.That(copy.KillerFacePosition, Is.EqualTo(original.KillerFacePosition));
        Assert.That(copy.KillerNetworkId, Is.EqualTo(42));
        Assert.That(copy.AllPlayersDead, Is.True);
    }

    [TestCase(0f, 0f, 3f, true)]
    [TestCase(1f, 0f, 3f, false)]
    [TestCase(0f, 0f, 9f, false)]
    public void ConoDeLuzRespetaAnguloYAlcance(float x, float y, float z, bool expected)
    {
        bool reaches = Gameplay.PlayerLights.Alcanza(
            Vector3.zero, Vector3.forward, new Vector3(x, y, z),
            angleDeg: 10f, range: 5f, radioObjetivo: 0f);

        Assert.That(reaches, Is.EqualTo(expected));
    }

    [Test]
    public void ClipDePersecucionUsaLaMismaEscalaQueElPrefab()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Arbmos.prefab");
        var animator = prefab.GetComponent<ArbmosAnimator>();

        Assert.That(animator, Is.Not.Null);
        Assert.That(animator.chaseClip, Is.Not.Null);

        bool foundRootScale = false;
        bool foundHipHeight = false;
        foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(animator.chaseClip))
        {
            var curve = UnityEditor.AnimationUtility.GetEditorCurve(animator.chaseClip, binding);
            if (curve == null) continue;

            if (binding.path == "target_character" &&
                binding.propertyName.StartsWith("m_LocalScale."))
            {
                foundRootScale = true;
                foreach (var key in curve.keys)
                    Assert.That(key.value, Is.EqualTo(1f).Within(0.001f));
            }

            if (binding.path == "target_character/Hips" &&
                binding.propertyName == "m_LocalPosition.y")
            {
                foundHipHeight = true;
                foreach (var key in curve.keys)
                    Assert.That(key.value, Is.GreaterThan(0.5f));
            }
        }

        Assert.That(foundRootScale, Is.True);
        Assert.That(foundHipHeight, Is.True);
    }
}
