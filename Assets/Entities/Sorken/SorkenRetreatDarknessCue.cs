using UnityEngine;
using UnityEngine.Rendering;

// Efecto local y efimero de la retirada. No reutiliza la niebla de entrada porque esa
// instancia puede haberse destruido mucho antes de que Sorken sea repelido dentro.
[DisallowMultipleComponent]
public sealed class SorkenRetreatDarknessCue : MonoBehaviour
{
    private const float BuildStartRatio = 0.10f;
    private const float SmokeFullSeconds = 0.82f;
    private const float FullyHiddenSeconds = 1.00f;
    private const float HideDurationSeconds = 0.32f;

    private float _duration = 2.5f;
    private float _elapsed;
    private ParticleSystem _mist;
    private Material _material;
    private Renderer[] _bodyRenderers;
    private bool _silhouetteHidden;
    private float _hideElapsed;

    public static SorkenRetreatDarknessCue Ensure(GameObject owner, float durationSeconds)
    {
        if (owner == null) return null;
        SorkenRetreatDarknessCue cue = owner.GetComponent<SorkenRetreatDarknessCue>();
        if (cue == null) cue = owner.AddComponent<SorkenRetreatDarknessCue>();
        // La duracion pertenece a la reaccion localizada; una notificacion de estado
        // posterior con el fallback no debe acortarla mientras crece el humo.
        cue._duration = Mathf.Max(cue._duration, Mathf.Max(2.3f, durationSeconds));
        return cue;
    }

    public static float BuildStartSeconds(float duration) =>
        Mathf.Max(0.45f, Mathf.Max(2.3f, duration) * BuildStartRatio);

    public static float ConcealSeconds(float duration)
    {
        float safeDuration = Mathf.Max(2.3f, duration);
        return Mathf.Max(BuildStartSeconds(safeDuration) + 0.05f,
                         FullyHiddenSeconds - HideDurationSeconds);
    }

    public static float FullyHiddenAtSeconds(float duration) =>
        ConcealSeconds(duration) + HideDurationSeconds;

    public static float Darkness01(float elapsed, float duration = 2.5f)
    {
        float safeDuration = Mathf.Max(2.3f, duration);
        // La nube llega a densidad maxima antes del segundo. El renderer termina de
        // ocultarse dentro de esa nube a los 1,0 s, sin alterar el despawn final.
        float visualFullSeconds = Mathf.Max(BuildStartSeconds(safeDuration) + 0.05f,
                                             SmokeFullSeconds);
        return Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(BuildStartSeconds(safeDuration), visualFullSeconds, elapsed));
    }

    public static bool ShouldHideSilhouette(float elapsed, float duration = 2.5f) =>
        elapsed >= ConcealSeconds(duration);

    private void Awake()
    {
        _bodyRenderers = GetComponentsInChildren<Renderer>(true);
        CreateMist();
    }

    private void Update()
    {
        _elapsed = Mathf.Min(_duration, _elapsed + Mathf.Max(0f, Time.deltaTime));
        float darkness = Darkness01(_elapsed, _duration);
        if (_mist != null)
            _mist.transform.position = MistPosition(darkness);

        ApplyMist(darkness);
        if (!_silhouetteHidden && ShouldHideSilhouette(_elapsed, _duration))
        {
            _hideElapsed += Mathf.Max(0f, Time.deltaTime);
            if (_hideElapsed >= HideDurationSeconds)
            {
                _silhouetteHidden = true;
                SetBodyVisible(false);
            }
        }
    }

    private void CreateMist()
    {
        var go = new GameObject("__SorkenRetreatMist")
        {
            hideFlags = HideFlags.DontSave,
            layer = gameObject.layer,
        };
        go.transform.SetParent(transform, false);
        go.transform.position = MistPosition(0f);
        _mist = go.AddComponent<ParticleSystem>();

        var main = _mist.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.62f);
        main.maxParticles = 900;

        var emission = _mist.emission;
        emission.rateOverTime = 0f;

        var shape = _mist.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.12f;
        shape.radiusThickness = 1f;

        var noise = _mist.noise;
        noise.enabled = true;
        noise.frequency = 0.58f;
        noise.strength = 0.2f;
        noise.scrollSpeed = 0.18f;
        noise.octaveCount = 2;
        noise.damping = true;

        var velocity = _mist.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.03f, 0.32f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

        var color = _mist.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.86f, 0.18f),
                    new GradientAlphaKey(0.72f, 0.78f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        _material = ArbmosGfx.ParticleMaterial(
            false, new Color(0f, 0f, 0f, 0.96f), ArbmosGfx.SmokeTexture(0.78f));
        renderer.material = _material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.sortingOrder = 50;

        _mist.Play(true);
    }

    private void ApplyMist(float amount)
    {
        if (_mist == null) return;
        var emission = _mist.emission;
        emission.rateOverTime = Mathf.Lerp(0f, 480f, amount);

        var shape = _mist.shape;
        shape.radius = Mathf.Lerp(0.12f, 0.9f, amount);

        var main = _mist.main;
        main.startSize = new ParticleSystem.MinMaxCurve(
            Mathf.Lerp(0.2f, 0.42f, amount), Mathf.Lerp(0.38f, 0.9f, amount));

        if (_material == null) return;
        Color tint = new(0f, 0f, 0f, Mathf.Lerp(0f, 0.96f, amount));
        if (_material.HasProperty("_TintColor")) _material.SetColor("_TintColor", tint);
        if (_material.HasProperty("_Color")) _material.SetColor("_Color", tint);
    }

    private Vector3 MistPosition(float darkness) =>
        transform.position + Vector3.up * Mathf.Lerp(0.12f, 0.82f, darkness) -
        transform.forward * Mathf.Lerp(0.02f, 0.18f, darkness);

    private void SetBodyVisible(bool visible)
    {
        if (_bodyRenderers == null) return;
        foreach (Renderer body in _bodyRenderers)
        {
            if (body == null || body is ParticleSystemRenderer) continue;
            body.enabled = visible;
        }
    }

    private void OnDestroy()
    {
        if (_silhouetteHidden) SetBodyVisible(true);
        if (_mist != null) Destroy(_mist.gameObject);
        if (_material != null) Destroy(_material);
    }
}
