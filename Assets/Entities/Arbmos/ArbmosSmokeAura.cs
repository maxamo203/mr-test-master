using UnityEngine;

// Aura de humo negro del Arbmos (parte de "figura oscura y semitransparente, aura de humo
// negro"). Procedural (sin assets): un ParticleSystem oscuro con particulas REDONDAS
// (textura radial de ArbmosGfx) que cae LENTO desde la figura y se acumula contra el piso
// (plano de colision horizontal a la altura de la base del Arbmos). Se apaga cuando
// ArbmosEntity.AuraOn == false (la secuencia LETAL de cordura 0 no muestra aura).
//
// Los parametros se configuran desde el prefab. SOLO en el editor de Unity se aplican en
// vivo (cambias un valor en Play y el humo se actualiza sin reiniciar) — en cualquier
// build (dev o prod) se fijan una sola vez al crear el humo, sin costo por frame. Los
// OJOS son aparte (componente ArbmosEye).
//
// Solo actua en la copia que se DIBUJA (ArbmosEntity.Rendered): el server no crea el
// efecto para las copias que simula de otros jugadores (esas van ocultas en el host).
[RequireComponent(typeof(ArbmosEntity))]
public class ArbmosSmokeAura : MonoBehaviour
{
    [Header("Humo — forma / emision")]
    [Tooltip("Altura (m) desde la que cae el humo (arranca a la altura de la cabeza).")]
    [SerializeField] private float _bodyHeight = 1.7f;
    [Tooltip("Radio (m) del cono de emision alrededor de la figura.")]
    [SerializeField] private float _smokeRadius = 0.4f;
    [Tooltip("Apertura del cono (grados).")]
    [SerializeField] private float _coneAngle = 18f;
    [Tooltip("Color del humo (el alfa lo maneja Opacidad + el fade por vida).")]
    [SerializeField] private Color _smokeColor = new Color(0.02f, 0.02f, 0.03f, 1f);

    [Header("Humo — densidad")]
    [Tooltip("Particulas emitidas por segundo (MAS = mas denso).")]
    [SerializeField] private float _smokeRate = 90f;
    [Tooltip("Tope de particulas vivas a la vez (subilo si el humo se 'corta' al ser denso).")]
    [SerializeField] private int _smokeMaxParticles = 600;
    [Tooltip("Tamaño de cada puff al nacer (min / max, m).")]
    [SerializeField] private float _sizeMin = 0.4f;
    [SerializeField] private float _sizeMax = 0.9f;
    [Tooltip("Cuanto crece cada puff a lo largo de su vida (1 = no crece).")]
    [SerializeField] private float _growth = 2f;
    [Tooltip("Opacidad pico del humo (0..1).")]
    [Range(0f, 1f)] [SerializeField] private float _opacity = 0.85f;
    [Tooltip("Suavidad/difuminado de cada particula. MAS alto = mas difuso (no se notan los " +
             "circulos individuales, se funde en niebla). Mas bajo = borde mas marcado.")]
    [Range(0f, 1f)] [SerializeField] private float _softness = 0.7f;

    [Header("Humo — caida (que tan rapido baja)")]
    [Tooltip("Gravedad: MENOS = cae mas lento / flota mas. 0 = no cae (solo se dispersa).")]
    [Range(0f, 0.5f)] [SerializeField] private float _fallSpeed = 0.06f;
    [Tooltip("Empuje inicial hacia abajo al nacer (m/s).")]
    [SerializeField] private float _startSpeed = 0.04f;
    [Tooltip("Cuanto vive cada puff (s). MAS = llega mas abajo y se acumula mas.")]
    [SerializeField] private float _lifetime = 4.5f;
    [Tooltip("Turbulencia: da forma 'wispy' / viva al humo.")]
    [SerializeField] private float _turbulence = 0.2f;
    [Tooltip("Escala de la turbulencia (frecuencia del ruido).")]
    [SerializeField] private float _turbulenceScale = 0.4f;

    [Header("Humo — piso propio")]
    [Tooltip("El humo choca con su PROPIO plano de piso (no usa el piso del escaneo), ubicado " +
             "a esta distancia (m) por DEBAJO del Arbmos. Subilo si el humo queda flotando " +
             "(p.ej. el pivote del modelo no esta en los pies): un valor ~la altura del pivote " +
             "lleva el piso del humo al suelo real.")]
    [SerializeField] private float _floorDepth = 0f;
    [Tooltip("Cuanto frena el humo al tocar su piso (1 = frena del todo, 0 = se desliza).")]
    [Range(0f, 1f)] [SerializeField] private float _floorDampen = 0.85f;

    [Header("Humo — al aparecer")]
    [Tooltip("Al spawnear, pre-simula estos segundos de humo (prewarm) para que YA aparezca " +
             "con humo caido/acumulado en vez de arrancar vacio. ~4s = humo ya asentado. 0 = sin prewarm.")]
    [SerializeField] private float _prewarmSeconds = 4f;

    private ArbmosEntity   _arbmos;
    private ParticleSystem _smoke;
    private Transform      _smokeTr;
    private Material       _smokeMat;     // para poder cambiar la textura (suavidad) en vivo
    private float          _lastSoftness = -1f;
    private Transform      _floorPlane;   // plano de colision a la altura del piso
    private bool           _built;

    private void Awake() => _arbmos = GetComponent<ArbmosEntity>();

    private void Start()
    {
        if (!_arbmos.Rendered) return;   // copia oculta (otros jugadores): sin efecto
        BuildSmoke();
        _built = true;
        if (_arbmos.AuraOn) Prewarm();   // aparece con humo ya caido
        Apply(_arbmos.AuraOn);
    }

    // Pre-simula _prewarmSeconds de humo de una para que al spawnear ya haya humo caido /
    // acumulado en el piso (incluye gravedad, ruido y la colision con el piso). Requiere que
    // el plano de piso ya este ubicado (BuildSmoke lo hace) y la pose del Arbmos ya fijada
    // (el ArbmosDirector la setea antes del Start).
    private void Prewarm()
    {
        if (_smoke == null || _prewarmSeconds <= 0f) return;
        _smoke.Simulate(_prewarmSeconds, true, true);   // restart + avanzar N segundos
        _smoke.Play(true);                              // reanudar el update normal
    }

    private void BuildSmoke()
    {
        var smokeGo = new GameObject("SmokeAura");
        smokeGo.transform.SetParent(transform, false);
        smokeGo.transform.localPosition = new Vector3(0f, _bodyHeight, 0f); // arranca arriba y cae
        _smokeTr = smokeGo.transform;

        _smoke = smokeGo.AddComponent<ParticleSystem>();
        _smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _smoke.main;
        main.loop = true;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.2831853f); // rota c/puff (menos uniforme)
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var shape = _smoke.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.rotation = new Vector3(90f, 0f, 0f);      // cono hacia ABAJO

        var noise = _smoke.noise;
        noise.enabled = true;
        noise.scrollSpeed = 0.2f;

        // Crecimiento del puff a lo largo de su vida (curva, se fija una vez).
        var sol = _smoke.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, Mathf.Max(0.4f, _growth)));

        RebuildColorOverLifetime();

        // Plano de colision horizontal a la altura del piso (= base del Arbmos). Independiente
        // del Arbmos para no heredar su rotacion; su Y se sincroniza en Update. Asi el humo
        // se acumula en el piso en vez de atravesarlo.
        _floorPlane = new GameObject("SmokeFloorPlane").transform;
        SyncFloorPlane();

        var collision = _smoke.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.bounce = 0f;          // no rebota
        collision.lifetimeLoss = 0f;    // no muere al chocar: queda acumulado en el piso
        collision.SetPlane(0, _floorPlane);

        var renderer = smokeGo.GetComponent<ParticleSystemRenderer>();
        _smokeMat = ArbmosGfx.ParticleMaterial(additive: false, tint: Color.white,
                                               tex: ArbmosGfx.SmokeTexture(_softness)); // blob difuso
        _lastSoftness = _softness;
        renderer.material = _smokeMat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 5;

        PushParams();   // fija los parametros una vez (en el editor ademas se refresca por frame)
    }

    // Fade de opacidad por vida (0 -> pico -> 0). Se reconstruye solo cuando cambia el
    // color/opacidad (no cada frame, para no generar GC con el Gradient).
    private Color _lastGradColor;
    private void RebuildColorOverLifetime()
    {
        var col = _smoke.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        var c = _smokeColor;
        grad.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(_opacity, 0.25f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        _lastGradColor = new Color(c.r, c.g, c.b, _opacity);
    }

    // Vuelca los parametros "baratos" (structs, sin GC) al sistema vivo => tuning en Play.
    private void PushParams()
    {
        if (_smoke == null) return;
        if (_smokeTr != null) _smokeTr.localPosition = new Vector3(0f, _bodyHeight, 0f);

        var main = _smoke.main;
        main.gravityModifier = _fallSpeed;
        main.startSpeed      = _startSpeed;
        main.startLifetime   = Mathf.Max(0.1f, _lifetime);
        main.startSize       = new ParticleSystem.MinMaxCurve(Mathf.Min(_sizeMin, _sizeMax), Mathf.Max(_sizeMin, _sizeMax));
        // Base opaca: la opacidad real la maneja SOLO _opacity (via colorOverLifetime), no
        // el alfa del color (asi no se mezcla con un alfa viejo serializado en el prefab).
        main.startColor      = new Color(_smokeColor.r, _smokeColor.g, _smokeColor.b, 1f);
        main.maxParticles    = Mathf.Max(1, _smokeMaxParticles);

        var em = _smoke.emission;
        em.rateOverTime = _smokeRate;

        var shape = _smoke.shape;
        shape.angle  = _coneAngle;
        shape.radius = _smokeRadius;

        var noise = _smoke.noise;
        noise.strength  = _turbulence;
        noise.frequency = _turbulenceScale;

        var col = _smoke.collision;
        col.dampen = _floorDampen;

        // Suavidad: cambia la textura del blob (solo cuando cambia; en el editor permite
        // tunear el difuminado en vivo).
        if (_smokeMat != null && !Mathf.Approximately(_softness, _lastSoftness))
        {
            _smokeMat.mainTexture = ArbmosGfx.SmokeTexture(_softness);
            _lastSoftness = _softness;
        }

        // El fade de color/opacidad usa un Gradient (asigna clase): solo si cambio.
        if (_lastGradColor.r != _smokeColor.r || _lastGradColor.g != _smokeColor.g ||
            _lastGradColor.b != _smokeColor.b || _lastGradColor.a != _opacity)
            RebuildColorOverLifetime();
    }

    private void Update()
    {
        if (!_built) return;
        SyncFloorPlane();
#if UNITY_EDITOR
        PushParams();          // tuning en vivo: SOLO en el editor (no en builds dev ni prod)
#endif
        Apply(_arbmos.AuraOn);
    }

    // Mantiene el PROPIO plano de piso del humo: horizontal (normal hacia arriba) y a
    // _floorDepth metros por debajo del Arbmos. No depende del piso del escaneo, asi el
    // humo siempre se acumula a una altura fija respecto de la figura.
    private void SyncFloorPlane()
    {
        if (_floorPlane == null) return;
        var p = transform.position;
        _floorPlane.SetPositionAndRotation(new Vector3(p.x, p.y - _floorDepth, p.z), Quaternion.identity);
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
