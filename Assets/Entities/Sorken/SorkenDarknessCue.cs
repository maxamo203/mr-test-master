using UnityEngine;
using UnityEngine.Rendering;

// Local visual cue for each replicated Sorken: black mist grows at the entry point.
[RequireComponent(typeof(SorkenEntity))]
public sealed class SorkenDarknessCue : MonoBehaviour
{
    [Header("Timing")]
    [Min(0.1f)] [SerializeField] private float _buildSeconds = 0.75f;
    [Min(0.1f)] [SerializeField] private float _fadeSeconds = 0.7f;
    [Min(0.1f)] [SerializeField] private float _entryRevealSeconds = 1.8f;

    [Header("Black mist")]
    [Min(0.1f)] [SerializeField] private float _maxRadius = 1.9f;
    [Min(1)] [SerializeField] private int _maxParticles = 560;

    private SorkenEntity _sorken;
    private ParticleSystem _mist;
    private Material _material;
    private float _amount;
    private float _createdAt;

    private void Awake()
    {
        _sorken = GetComponent<SorkenEntity>();
        _createdAt = Time.time;
        CreateMist();

        // La masa negra ya es evidente en el primer fotograma: avisa enseguida por donde
        // entrara el Sorken y oculta la parte mas mecanica del cruce.
        _amount = 0.82f;
        ApplyMist();
        _mist.Simulate(0.9f, true, true, true);
        _mist.Play(true);
    }

    private void Update()
    {
        // La niebla conserva casi toda su densidad durante el cruce y solo comienza a
        // abrirse durante el aterrizaje. Asi se percibe la silueta, no el traspaso.
        float target = _sorken != null ? TargetAmount(_sorken.State) : 0f;
        float seconds = target > _amount
            ? Mathf.Max(0.1f, _buildSeconds)
            : target > 0f
                ? Mathf.Max(0.1f, _entryRevealSeconds)
                : Mathf.Max(0.1f, _fadeSeconds);
        _amount = Mathf.MoveTowards(_amount, target, Time.deltaTime / seconds);
        ApplyMist();

        if (target <= 0f && _amount <= 0.001f && Time.time > _createdAt + 0.2f)
            Destroy(this);
    }

    public static float TargetAmount(SorkenState state)
    {
        return state switch
        {
            SorkenState.Idle => 1f,
            SorkenState.EmergingDoor => 0.96f,
            SorkenState.EmergingWindow => 1f,
            SorkenState.WindowLanding => 0.78f,
            _ => 0f,
        };
    }

    public static bool KeepsMistBuilt(SorkenState state) =>
        TargetAmount(state) > 0f;

    private void CreateMist()
    {
        var go = new GameObject("__SorkenEntryMist")
        {
            hideFlags = HideFlags.DontSave,
            layer = gameObject.layer,
        };
        // Keep this at the original entrance point when Sorken moves indoors.
        go.transform.position = transform.position + Vector3.up * 0.9f;
        _mist = go.AddComponent<ParticleSystem>();

        var main = _mist.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.7f, 2.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.55f, 1.3f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = _maxParticles;
        main.gravityModifier = -0.015f;

        var emission = _mist.emission;
        emission.rateOverTime = 0f;

        var shape = _mist.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.18f;
        shape.radiusThickness = 1f;

        var noise = _mist.noise;
        noise.enabled = true;
        noise.frequency = 0.48f;
        noise.strength = 0.34f;
        noise.scrollSpeed = 0.22f;
        noise.octaveCount = 3;
        noise.damping = true;

        var velocity = _mist.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.03f, 0.19f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);

        var rotation = _mist.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.65f, 0.65f);

        var color = _mist.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f),
                    new GradientAlphaKey(0.76f, 0.68f),
                    new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        _material = ArbmosGfx.ParticleMaterial(false, new Color(0f, 0f, 0f, 0.9f),
                                               ArbmosGfx.SmokeTexture(0.92f));
        renderer.material = _material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.sortingOrder = 15;
    }

    private void ApplyMist()
    {
        if (_mist == null) return;
        var emission = _mist.emission;
        emission.rateOverTime = Mathf.Lerp(0f, 430f, _amount);

        var shape = _mist.shape;
        shape.radius = Mathf.Lerp(0.18f, _maxRadius, _amount);

        var main = _mist.main;
        main.startSize = new ParticleSystem.MinMaxCurve(
            Mathf.Lerp(0.28f, 0.78f, _amount), Mathf.Lerp(0.55f, 1.65f, _amount));

        if (_material != null)
        {
            var c = new Color(0f, 0f, 0f, Mathf.Lerp(0f, 0.94f, _amount));
            if (_material.HasProperty("_TintColor")) _material.SetColor("_TintColor", c);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", c);
        }

        if (_amount > 0.001f && !_mist.isPlaying) _mist.Play(true);
        else if (_amount <= 0.001f && _mist.isPlaying)
            _mist.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void OnDestroy()
    {
        if (_mist != null) Destroy(_mist.gameObject);
        if (_material != null) Destroy(_material);
    }
}
