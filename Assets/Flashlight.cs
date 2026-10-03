using UnityEngine;

public enum FlashlightMode : byte
{
    Off    = 0,
    Dim    = 1,
    Bright = 2,
}

public class Flashlight : MonoBehaviour
{
    [Header("Apariencia")]
    public Color color = Color.white;
    [Tooltip("Alcance en metros")]
    [Range(0.5f, 30f)] public float range = 8f;
    [Tooltip("Ángulo exterior del cono (apagado fuera de aquí)")]
    [Range(2f, 89f)] public float outerAngleDeg = 35f;
    [Tooltip("Ángulo interior (intensidad máxima dentro de aquí)")]
    [Range(0f, 89f)] public float innerAngleDeg = 18f;
    [Tooltip("Intensidad de la linterna (qué tan fuerte revela el entorno)")]
    [Range(0f, 10f)] public float intensity = 2.5f;

    [Header("Oscuridad del entorno")]
    [Tooltip("Qué tan oscuras se ven las superficies fuera del cono. 0 = negro, 1 = sin oscurecer")]
    [Range(0f, 1f)] public float darknessAmount = 0.05f;

    [Header("Iluminación de objetos virtuales")]
    [Tooltip("Crear y manejar un Light component (Spot) para iluminar también el cubo y demás objetos")]
    public bool createRealLight = true;
    [Range(0f, 10f)] public float realLightIntensityMultiplier = 1f;

    [Header("Batería")]
    [Tooltip("Carga máxima de la linterna.")]
    public float maxCharge = 100f;
    [Tooltip("Carga actual. Se drena mientras la linterna está encendida.")]
    public float currentCharge = 100f;
    [Tooltip("Carga consumida por segundo mientras isOn.")]
    public float drainPerSecond = 2f;
    [Tooltip("Carga consumida por segundo mientras se mantiene el modo intenso.")]
    public float brightDrainPerSecond = 6f;
    [Tooltip("Multiplicador visual de intensidad en modo intenso.")]
    [Min(1f)] public float brightIntensityMultiplier = 1.6f;
    [Tooltip("Multiplicador visual del ancho del cono en modo intenso. Un valor menor a 1 " +
             "concentra el haz sin modificar el cono usado por las reglas de gameplay.")]
    [Range(0.5f, 1f)] public float brightConeAngleMultiplier = 0.75f;
    [Tooltip("Ancho angular del degradado exterior en modo concentrado.")]
    [Range(0.25f, 5f)] public float brightEdgeFadeDeg = 1f;

    [Header("Difuminación exterior")]
    [Tooltip("Extensión angular adicional del halo suave fuera del cono normal.")]
    [Range(0f, 20f)] public float edgeHaloAngleDeg = 5f;
    [Tooltip("Porcentaje de iluminación al comenzar el halo exterior normal.")]
    [Range(0f, 1f)] public float edgeHaloStrength = 0.75f;
    [Tooltip("Extensión de la segunda capa de difusión normal.")]
    [Range(0f, 20f)] public float midHaloAngleDeg = 8f;
    [Tooltip("Iluminación inicial de la segunda capa de difusión.")]
    [Range(0f, 1f)] public float midHaloStrength = 0.5f;
    [Tooltip("Extensión del halo exterior cuando el haz está concentrado.")]
    [Range(0f, 6f)] public float brightEdgeHaloAngleDeg = 1.25f;
    [Tooltip("Porcentaje de iluminación al comenzar el halo concentrado.")]
    [Range(0f, 1f)] public float brightEdgeHaloStrength = 0.18f;
    [Tooltip("Extensión de la segunda capa del haz concentrado.")]
    [Range(0f, 6f)] public float brightMidHaloAngleDeg = 1.25f;
    [Tooltip("Iluminación inicial de su segunda capa.")]
    [Range(0f, 1f)] public float brightMidHaloStrength = 0.1f;
    [Tooltip("Extensión adicional de la difusión más tenue fuera del primer halo.")]
    [Range(0f, 30f)] public float farHaloAngleDeg = 15f;
    [Tooltip("Iluminación inicial de la difusión exterior más tenue.")]
    [Range(0f, 1f)] public float farHaloStrength = 0.2f;
    [Tooltip("Extensión exterior tenue del haz concentrado.")]
    [Range(0f, 8f)] public float brightFarHaloAngleDeg = 1.5f;
    [Tooltip("Iluminación inicial exterior del haz concentrado.")]
    [Range(0f, 1f)] public float brightFarHaloStrength = 0.05f;

    [Header("Titileo por batería baja")]
    // Puramente visual (afecta solo la intensidad renderizada, nunca isOn): a nadie que
    // compruebe si esta linterna está "alumbrando" algo (PlayerLights,
    // NetworkManager.LocalFlashlightOn) le importa, porque esos chequeos leen isOn, no
    // la intensidad.
    [Tooltip("Fracción de carga por debajo de la cual puede empezar a titilar (0..1).")]
    [Range(0f, 1f)] public float flickerChargeThreshold = 0.35f;
    [Tooltip("Probabilidad de que arranque un titileo, por segundo, con la batería en 0%. " +
             "Escala linealmente a 0 en flickerChargeThreshold.")]
    [Range(0f, 5f)] public float flickerMaxChancePerSecond = 1.2f;
    [Tooltip("Duración mínima/máxima de cada titileo individual, en segundos.")]
    public float flickerDurationMin = 0.05f;
    public float flickerDurationMax = 0.35f;
    [Tooltip("Rango de multiplicador de intensidad durante un titileo (1 = sin cambio, 0 = apagada).")]
    [Range(0f, 1f)] public float flickerDipMin = 0.05f;
    [Range(0f, 1f)] public float flickerDipMax = 0.6f;

    [Header("Control")]
    public bool isOn = true;
    [Tooltip("La linterna solo funciona con la partida arrancada (NetworkManager.GameStarted). " +
             "Fuera de partida (menu/lobby) queda apagada e ignora el toggle.")]
    public bool requireMatchToOperate = true;

    // True si la linterna puede operar ahora (en partida, o si no se exige partida).
    private bool CanOperate =>
        !requireMatchToOperate ||
        (NetworkManager.Instance != null && NetworkManager.Instance.GameStarted);

    // Publico para la UI (FlashlightHUD): no mostrar nada fuera de partida.
    public bool Operational => CanOperate;
    public FlashlightMode Mode => !CanOperate || !isOn || IsEmpty
        ? FlashlightMode.Off
        : (_brightHeld ? FlashlightMode.Bright : FlashlightMode.Dim);
    public float CurrentDrainPerSecond => Mode switch
    {
        FlashlightMode.Bright => Mathf.Max(0f, brightDrainPerSecond),
        FlashlightMode.Dim => Mathf.Max(0f, drainPerSecond),
        _ => 0f,
    };
    public float VisualOuterAngleDeg => Mode == FlashlightMode.Bright
        ? Mathf.Clamp(outerAngleDeg * brightConeAngleMultiplier, 2f, outerAngleDeg)
        : outerAngleDeg;
    
    public float VisualHaloAngleDeg => Mathf.Clamp(
        VisualOuterAngleDeg + (Mode == FlashlightMode.Bright
            ? brightEdgeHaloAngleDeg
            : edgeHaloAngleDeg),
        VisualOuterAngleDeg, 89f);
    
    public float VisualMidHaloAngleDeg => Mathf.Clamp(
        VisualHaloAngleDeg + (Mode == FlashlightMode.Bright
            ? brightMidHaloAngleDeg
            : midHaloAngleDeg),
        VisualHaloAngleDeg, 89f);
    public float VisualMidHaloStrength => Mode == FlashlightMode.Bright
        ? brightMidHaloStrength
        : midHaloStrength;
    public float VisualFarHaloAngleDeg => Mathf.Clamp(
        VisualMidHaloAngleDeg + (Mode == FlashlightMode.Bright
            ? brightFarHaloAngleDeg
            : farHaloAngleDeg),
        VisualMidHaloAngleDeg, 89f);
    public float VisualFarHaloStrength => Mode == FlashlightMode.Bright
        ? brightFarHaloStrength
        : farHaloStrength;
public float VisualHaloStrength => Mode == FlashlightMode.Bright
        ? brightEdgeHaloStrength
        : edgeHaloStrength;
public float VisualInnerAngleDeg
    {
        get
        {
            float outer = VisualOuterAngleDeg;
            float angle = Mode == FlashlightMode.Bright
                ? outer - brightEdgeFadeDeg
                : innerAngleDeg;
            return Mathf.Clamp(angle, 0f, Mathf.Max(0f, outer - 0.1f));
        }
    }

    // Fraccion de carga restante (0..1), para HUD.
    public float Charge01 => maxCharge > 0f ? Mathf.Clamp01(currentCharge / maxCharge) : 0f;
    public bool  IsEmpty  => currentCharge <= 0f;

    // Estado de arranque de noche: pila llena y encendida.
    //
    // Se engancha a NetworkManager.OnGameStarted (ver Update/OnDisable), que es el unico
    // evento por el que pasan host y cliente tanto en el arranque en frio (menu ->
    // SampleScene) como en el REINTENTO, que no recarga la escena (ver
    // Gameplay.NightTransition) y por lo tanto no restaura los valores del prefab. Sin
    // esto, al reintentar se empezaba la noche con la carga drenada de la anterior — y si
    // se habia agotado, ademas apagada, lo que arrancaba drenando cordura de una.
    public void ResetNoche()
    {
        var night = Gameplay.GameDirector.Instance != null
            ? Gameplay.GameDirector.Instance.NocheActual
            : (Gameplay.GameSession.Instance != null ? Gameplay.GameSession.Instance.SelectedNight : null);
        if (night != null)
        {
            maxCharge = night.flashlightMaxCharge;
            drainPerSecond = night.flashlightDrainPerSecond;
            brightDrainPerSecond = night.flashlightBrightDrainPerSecond;
        }
        currentCharge     = maxCharge;
        isOn              = true;
        _brightHeld       = false;
        _avisoBateriaBaja = false;
        _flickerTimer     = 0f;
    }

    // Suma carga (al recoger una pila). Vuelve a permitir encender si estaba agotada.
    //
    // Es tambien el UNICO punto por el que pasan host y cliente al recoger una pila (el
    // host resuelve en BatterySpawnManager y el cliente en ContextActionController), asi
    // que el sonido de "pila recogida" va aca y no se duplica.
    public void AddCharge(float amount)
    {
        float antes = currentCharge;
        currentCharge = Mathf.Clamp(currentCharge + amount, 0f, maxCharge);
        if (currentCharge > antes) AudioManager.Sonar(c => c.pilaRecogida);
    }

    // Prende/apaga la linterna. No enciende si no hay carga. Punto único de toggle
    // (lo usa la pulsación corta del botón único en ContextActionController).
    public void Toggle()
    {
        if (!CanOperate) return;      // fuera de partida no se prende
        // Click en falso: intentas prenderla sin bateria. Merece su propio sonido, es
        // informacion (te quedaste sin pilas) en el peor momento posible.
        if (!isOn && IsEmpty) { AudioManager.Sonar(c => c.linternaVacia); return; }

        isOn = !isOn;
        if (!isOn) _brightHeld = false;
        if (isOn) AudioManager.Sonar(c => c.linternaOn);
        else      AudioManager.Sonar(c => c.linternaOff);
    }

    // El modo intenso es momentaneo: solo existe mientras se mantiene el botón único.
    // Soltar siempre vuelve a Dim.
    public void SetBrightHeld(bool held)
    {
        _brightHeld = held && CanOperate && isOn && !IsEmpty;
    }

    // Al superar el umbral de hold, el mismo botón enciende si era necesario y pasa
    // directamente a intensa. Al soltar queda encendida en tenue.
    public bool BeginBrightHold()
    {
        if (!CanOperate || IsEmpty) { _brightHeld = false; return false; }
        if (!isOn)
        {
            isOn = true;
            AudioManager.Sonar(c => c.linternaOn);
        }
        _brightHeld = true;
        return true;
    }

    static readonly int ID_POS       = Shader.PropertyToID("_FlashlightPos");
    static readonly int ID_DIR       = Shader.PropertyToID("_FlashlightDir");
    static readonly int ID_RANGE     = Shader.PropertyToID("_FlashlightRange");
    static readonly int ID_COS_OUTER = Shader.PropertyToID("_FlashlightCosOuter");
    
    static readonly int ID_COS_HALO  = Shader.PropertyToID("_FlashlightCosHalo");
    
    static readonly int ID_COS_FAR_HALO = Shader.PropertyToID("_FlashlightCosFarHalo");
    
    static readonly int ID_COS_MID_HALO = Shader.PropertyToID("_FlashlightCosMidHalo");
    static readonly int ID_MID_HALO     = Shader.PropertyToID("_FlashlightMidHaloStrength");
static readonly int ID_FAR_HALO     = Shader.PropertyToID("_FlashlightFarHaloStrength");
static readonly int ID_HALO      = Shader.PropertyToID("_FlashlightHaloStrength");
static readonly int ID_COS_INNER = Shader.PropertyToID("_FlashlightCosInner");
    static readonly int ID_INTENSITY = Shader.PropertyToID("_FlashlightIntensity");
    static readonly int ID_FLICKER   = Shader.PropertyToID("_FlashlightFlicker");
    static readonly int ID_COLOR     = Shader.PropertyToID("_FlashlightColor");
    static readonly int ID_DARKNESS  = Shader.PropertyToID("_DarknessAmount");

    private Light _light;
    private bool _brightHeld;

    // Umbrales del aviso sonoro de batería baja (fracción de carga). El de rearme es más
    // alto que el de disparo a propósito: sin esa histéresis el aviso se repetiría en loop
    // mientras la carga oscila alrededor del umbral.
    private const float AvisoBateriaBaja  = 0.20f;
    private const float RearmeBateriaBaja = 0.30f;
    private bool _avisoBateriaBaja;

    // Estado del titileo actual (si _flickerTimer > 0, hay uno en curso).
    private float _flickerTimer;
    private float _flickerDepth = 1f;

    // Multiplicador de intensidad (0..1) a aplicar este frame. Sólo toca el render:
    // isOn y las comprobaciones de "estoy alumbrando algo" (PlayerLights, NetworkManager)
    // ni se enteran de que existe.
    private float UpdateFlicker(float dt)
    {
        if (!isOn) { _flickerTimer = 0f; return 1f; }

        float lowFactor = Mathf.InverseLerp(flickerChargeThreshold, 0f, Charge01);
        if (lowFactor <= 0f) { _flickerTimer = 0f; return 1f; }

        if (_flickerTimer > 0f)
        {
            _flickerTimer -= dt;
            // Jitter rápido dentro del dip para que no se vea como un escalón plano.
            float jitter = Mathf.PerlinNoise(Time.time * 45f, 0.37f);
            return Mathf.Lerp(_flickerDepth, 1f, jitter * 0.3f);
        }

        float chance = flickerMaxChancePerSecond * lowFactor * dt;
        if (Random.value < chance)
        {
            _flickerTimer = Random.Range(flickerDurationMin, flickerDurationMax);
            _flickerDepth = Random.Range(flickerDipMin, flickerDipMax);
        }
        return 1f;
    }

    void Awake()
    {
        if (createRealLight)
        {
            _light = GetComponent<Light>();
            if (_light == null) _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Spot;
            _light.shadows = LightShadows.None;
        }
    }

    // NetworkManager puede no existir todavia cuando arranca la escena (mismo patron
    // que LocalDeath / LocalSanity): suscribir en cuanto aparezca.
    private bool _suscritoAPartida;

    private void SuscribirAPartida()
    {
        if (_suscritoAPartida || NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnGameStarted += ResetNoche;
        _suscritoAPartida = true;
    }

    void Update()
    {
        SuscribirAPartida();

        // Fuera de partida la linterna no funciona: suprimir su salida (sin tocar isOn,
        // asi al arrancar la partida queda encendida por defecto). El toggle esta
        // bloqueado por CanOperate, y no se drena bateria aca.
        if (!CanOperate)
        {
            Shader.SetGlobalFloat(ID_INTENSITY, 0f);
            Shader.SetGlobalFloat(ID_FLICKER, 0f);
            Shader.SetGlobalFloat(ID_DARKNESS, 1f);
            if (_light != null) _light.enabled = false;
            return;
        }

        // Drenar la batería mientras está encendida; apagar al agotarse.
        if (Mode != FlashlightMode.Off)
        {
            if (currentCharge > 0f)
            {
                currentCharge = Mathf.Max(0f,
                    currentCharge - CurrentDrainPerSecond * Time.deltaTime);
            }

            // Aviso de batería baja, una sola vez por bajada (se rearma al recargar por
            // encima del umbral alto, para no repetirlo titilando en el borde).
            if (!_avisoBateriaBaja && !IsEmpty && Charge01 <= AvisoBateriaBaja)
            {
                AudioManager.Sonar(c => c.bateriaBaja);
                _avisoBateriaBaja = true;
            }

            // Apagón por agotamiento: NO pasa por Toggle(), así que el sonido va acá.
            if (currentCharge <= 0f)
            {
                isOn = false;
                _brightHeld = false;
                AudioManager.Sonar(c => c.linternaSeAgota);
            }
        }
        if (_avisoBateriaBaja && Charge01 > RearmeBateriaBaja) _avisoBateriaBaja = false;

        if (innerAngleDeg > outerAngleDeg - 0.1f)
            innerAngleDeg = Mathf.Max(0f, outerAngleDeg - 0.1f);

        // El titileo solo afecta la salida visual. El modo reportado a gameplay/red no
        // cambia, por lo que Dim y Bright conservan sus reglas aunque la luz titile.
        float flicker = UpdateFlicker(Time.deltaTime);
        float modeIntensity = Mode switch
        {
            FlashlightMode.Bright => intensity * brightIntensityMultiplier,
            FlashlightMode.Dim    => intensity,
            _                     => 0f,
        };
        float effectiveIntensity = modeIntensity * flicker;

        Shader.SetGlobalVector(ID_POS, transform.position);
        Shader.SetGlobalVector(ID_DIR, transform.forward);
        Shader.SetGlobalFloat(ID_RANGE, range);
        float visualOuterAngle = VisualOuterAngleDeg;
        float visualInnerAngle = VisualInnerAngleDeg;
        Shader.SetGlobalFloat(ID_COS_OUTER, Mathf.Cos(visualOuterAngle * Mathf.Deg2Rad));
        
        Shader.SetGlobalFloat(ID_COS_HALO,
            Mathf.Cos(VisualHaloAngleDeg * Mathf.Deg2Rad));
        
        Shader.SetGlobalFloat(ID_COS_FAR_HALO,
            Mathf.Cos(VisualFarHaloAngleDeg * Mathf.Deg2Rad));
        Shader.SetGlobalFloat(ID_FAR_HALO, VisualFarHaloStrength);

        Shader.SetGlobalFloat(ID_COS_MID_HALO,
            Mathf.Cos(VisualMidHaloAngleDeg * Mathf.Deg2Rad));
        Shader.SetGlobalFloat(ID_MID_HALO, VisualMidHaloStrength);
Shader.SetGlobalFloat(ID_HALO, VisualHaloStrength);
Shader.SetGlobalFloat(ID_COS_INNER, Mathf.Cos(visualInnerAngle * Mathf.Deg2Rad));
        Shader.SetGlobalFloat(ID_INTENSITY, effectiveIntensity);
        Shader.SetGlobalFloat(ID_FLICKER, Mode != FlashlightMode.Off ? flicker : 0f);
        Shader.SetGlobalColor(ID_COLOR, color);
        Shader.SetGlobalFloat(ID_DARKNESS, Mode != FlashlightMode.Off
            ? Mathf.Lerp(1f, darknessAmount, flicker)
            : 1f);

        if (_light != null)
        {
            _light.enabled = Mode != FlashlightMode.Off;
            _light.color = color;
            _light.range = range;
            _light.spotAngle = VisualFarHaloAngleDeg * 2f;
            _light.innerSpotAngle = visualInnerAngle * 2f;
            _light.intensity = effectiveIntensity * realLightIntensityMultiplier;
        }
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(ID_INTENSITY, 0f);
        Shader.SetGlobalFloat(ID_FLICKER, 0f);
        if (_suscritoAPartida && NetworkManager.Instance != null)
            NetworkManager.Instance.OnGameStarted -= ResetNoche;
        _suscritoAPartida = false;
    }
}
