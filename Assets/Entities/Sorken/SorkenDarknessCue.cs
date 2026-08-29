using UnityEngine;
using UnityEngine.Rendering;

// Local visual cue for each replicated Sorken: black mist grows at the entry point.
[RequireComponent(typeof(SorkenEntity))]
public sealed class SorkenDarknessCue : MonoBehaviour
{
    [Header("Timing")]
    [Min(0.1f)] [SerializeField] private float _buildSeconds = 5f;
    [Min(0.1f)] [SerializeField] private float _fadeSeconds = 0.45f;

    [Header("Black mist")]
    [Min(0.1f)] [SerializeField] private float _maxRadius = 1.35f;
    [Min(1)] [SerializeField] private int _maxParticles = 260;

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
    }

    private void Update()
    {
        bool entering = _sorken != null &&
            (_sorken.State == SorkenState.Idle ||
             _sorken.State == SorkenState.EmergingDoor ||
             _sorken.State == SorkenState.EmergingWindow);

        float speed = entering ? 1f / Mathf.Max(0.1f, _buildSeconds)
                               : 1f / Mathf.Max(0.1f, _fadeSeconds);
        _amount = Mathf.MoveTowards(_amount, entering ? 1f : 0f, speed * Time.deltaTime);
        ApplyMist();

        if (!entering && _amount <= 0.001f && Time.time > _createdAt + 0.2f)
            Destroy(this);
    }

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
        main.startLifetime = 1.8f;
        main.startSpeed = 0.08f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
        main.maxParticles = _maxParticles;
        main.gravityModifier = 0.01f;

        var emission = _mist.emission;
        emission.rateOverTime = 0f;

        var shape = _mist.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;
        shape.radiusThickness = 1f;

        var noise = _mist.noise;
        noise.enabled = true;
        noise.frequency = 0.3f;
        noise.strength = 0.12f;

        var color = _mist.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.68f, 0.25f),
                    new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        _material = ArbmosGfx.ParticleMaterial(false, new Color(0f, 0f, 0f, 0.75f),
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
        emission.rateOverTime = Mathf.Lerp(0f, 180f, _amount);

        var shape = _mist.shape;
        shape.radius = Mathf.Lerp(0.1f, _maxRadius, _amount);

        var main = _mist.main;
        main.startSize = new ParticleSystem.MinMaxCurve(
            Mathf.Lerp(0.18f, 0.55f, _amount), Mathf.Lerp(0.35f, 1.1f, _amount));

        if (_material != null)
        {
            var c = new Color(0f, 0f, 0f, Mathf.Lerp(0f, 0.78f, _amount));
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
