using UnityEngine;

// Aura de humo negro del Arbmos (parte de "figura oscura y semitransparente, aura de humo
// negro"). Procedural (sin assets): un ParticleSystem oscuro con particulas REDONDAS
// (textura radial de ArbmosGfx) que CAE desde la figura y choca / se acumula contra el
// piso (plano de colision horizontal a la altura de la base del Arbmos). Se apaga cuando
// ArbmosEntity.AuraOn == false (la secuencia LETAL de cordura 0 no muestra aura).
//
// Los OJOS son aparte: componente ArbmosEye (lo pones vos en dos GameObjects vacios y los
// ubicas a mano). Este script no maneja ojos.
//
// Solo actua en la copia que se DIBUJA (ArbmosEntity.Rendered): el server no crea el
// efecto para las copias que simula de otros jugadores (esas van ocultas en el host).
[RequireComponent(typeof(ArbmosEntity))]
public class ArbmosSmokeAura : MonoBehaviour
{
    [Header("Humo")]
    [Tooltip("Altura (m) desde la que cae el humo (arranca a la altura de la cabeza).")]
    [SerializeField] private float _bodyHeight = 1.7f;
    [SerializeField] private float _smokeRadius = 0.35f;
    [SerializeField] private Color _smokeColor = new Color(0.02f, 0.02f, 0.03f, 0.55f);
    [Tooltip("Densidad: particulas emitidas por segundo (mas = mas denso).")]
    [SerializeField] private float _smokeRate = 55f;
    [Tooltip("Tope de particulas vivas a la vez (subilo si el humo se 'corta' al ser denso).")]
    [SerializeField] private int _smokeMaxParticles = 320;

    private ArbmosEntity   _arbmos;
    private ParticleSystem _smoke;
    private Transform      _floorPlane;   // plano de colision a la altura del piso
    private bool           _built;

    private void Awake() => _arbmos = GetComponent<ArbmosEntity>();

    private void Start()
    {
        if (!_arbmos.Rendered) return;   // copia oculta (otros jugadores): sin efecto
        BuildSmoke();
        _built = true;
        Apply(_arbmos.AuraOn);
    }

    private void BuildSmoke()
    {
        var smokeGo = new GameObject("SmokeAura");
        smokeGo.transform.SetParent(transform, false);
        smokeGo.transform.localPosition = new Vector3(0f, _bodyHeight, 0f); // arranca arriba y cae

        _smoke = smokeGo.AddComponent<ParticleSystem>();
        _smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _smoke.main;
        main.loop = true;
        main.startLifetime = 2.6f;                      // le alcanza para llegar al piso
        main.startSpeed = 0.12f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startColor = _smokeColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.2831853f); // rota c/puff (menos uniforme)
        main.gravityModifier = 0.22f;                   // CAE (gravedad positiva)
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = _smokeMaxParticles;

        var emission = _smoke.emission;
        emission.rateOverTime = _smokeRate;

        var shape = _smoke.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 14f;
        shape.radius = _smokeRadius;
        shape.rotation = new Vector3(90f, 0f, 0f);      // cono hacia ABAJO

        // Turbulencia suave para que el humo sea mas "vivo" (wispy).
        var noise = _smoke.noise;
        noise.enabled = true;
        noise.strength = 0.16f;
        noise.frequency = 0.4f;
        noise.scrollSpeed = 0.2f;

        var sol = _smoke.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.6f));

        var col = _smoke.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(_smokeColor, 0f), new GradientColorKey(_smokeColor, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        // Plano de colision horizontal a la altura del piso (= base del Arbmos). Independiente
        // del Arbmos para no heredar su rotacion; su Y se sincroniza en Update. Asi el humo
        // "choca" y se acumula en el piso en vez de atravesarlo.
        _floorPlane = new GameObject("SmokeFloorPlane").transform;
        SyncFloorPlane();

        var collision = _smoke.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.dampen = 0.7f;        // frena al tocar (casi sin deslizar)
        collision.bounce = 0f;          // no rebota
        collision.lifetimeLoss = 0f;    // no muere al chocar: queda acumulado en el piso
        collision.SetPlane(0, _floorPlane);

        var renderer = smokeGo.GetComponent<ParticleSystemRenderer>();
        renderer.material = ArbmosGfx.ParticleMaterial(additive: false, tint: Color.white); // humo: alpha suave
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 5;
    }

    private void Update()
    {
        if (!_built) return;
        SyncFloorPlane();
        Apply(_arbmos.AuraOn);
    }

    // Mantiene el plano de colision a la altura de la base del Arbmos (el piso) y
    // horizontal (normal hacia arriba), sin importar hacia donde mire la figura.
    private void SyncFloorPlane()
    {
        if (_floorPlane == null) return;
        _floorPlane.SetPositionAndRotation(transform.position, Quaternion.identity);
    }

    private void Apply(bool auraOn)
    {
        if (_smoke == null) return;
        var em = _smoke.emission;
        em.enabled = auraOn;
        if (auraOn && !_smoke.isPlaying) _smoke.Play(true);
        else if (!auraOn && _smoke.isPlaying) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void OnDestroy()
    {
        if (_smoke != null) Destroy(_smoke.gameObject);
        if (_floorPlane != null) Destroy(_floorPlane.gameObject);
    }
}
