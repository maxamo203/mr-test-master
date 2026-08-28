using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

public class Flashlight : MonoBehaviour
{
    [Header("Apariencia")]
    public Color color = Color.white;
    [Tooltip("Alcance en metros")]
    [Range(0.5f, 30f)] public float range = 8f;
    [Tooltip("Ángulo exterior del cono (apagado fuera de aquí)")]
    [Range(2f, 89f)] public float outerAngleDeg = 35f;
    [Tooltip("Ángulo interior (intensidad máxima dentro de aquí)")]
    [Range(0f, 89f)] public float innerAngleDeg = 22f;
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

    [Header("Control")]
    public bool isOn = true;
    [Tooltip("Toggle con touch (toca con 2 dedos para alternar)")]
    public bool toggleWithTwoFingers = true;
    [Tooltip("La linterna solo funciona con la partida arrancada (NetworkManager.GameStarted). " +
             "Fuera de partida (menu/lobby) queda apagada e ignora el toggle.")]
    public bool requireMatchToOperate = true;

    // True si la linterna puede operar ahora (en partida, o si no se exige partida).
    private bool CanOperate =>
        !requireMatchToOperate ||
        (NetworkManager.Instance != null && NetworkManager.Instance.GameStarted);

    // Publico para la UI (FlashlightHUD): no mostrar nada fuera de partida.
    public bool Operational => CanOperate;

    // Fraccion de carga restante (0..1), para HUD.
    public float Charge01 => maxCharge > 0f ? Mathf.Clamp01(currentCharge / maxCharge) : 0f;
    public bool  IsEmpty  => currentCharge <= 0f;

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
    // (lo usan el gesto de 2 dedos y la acción del botón A / ContextActionController).
    public void Toggle()
    {
        if (!CanOperate) return;      // fuera de partida no se prende
        // Click en falso: intentas prenderla sin bateria. Merece su propio sonido, es
        // informacion (te quedaste sin pilas) en el peor momento posible.
        if (!isOn && IsEmpty) { AudioManager.Sonar(c => c.linternaVacia); return; }

        isOn = !isOn;
        if (isOn) AudioManager.Sonar(c => c.linternaOn);
        else      AudioManager.Sonar(c => c.linternaOff);
    }

    static readonly int ID_POS       = Shader.PropertyToID("_FlashlightPos");
    static readonly int ID_DIR       = Shader.PropertyToID("_FlashlightDir");
    static readonly int ID_RANGE     = Shader.PropertyToID("_FlashlightRange");
    static readonly int ID_COS_OUTER = Shader.PropertyToID("_FlashlightCosOuter");
    static readonly int ID_COS_INNER = Shader.PropertyToID("_FlashlightCosInner");
    static readonly int ID_INTENSITY = Shader.PropertyToID("_FlashlightIntensity");
    static readonly int ID_COLOR     = Shader.PropertyToID("_FlashlightColor");
    static readonly int ID_DARKNESS  = Shader.PropertyToID("_DarknessAmount");

    private Light _light;
    private float _lastToggleTime = -999f;

    // Umbrales del aviso sonoro de batería baja (fracción de carga). El de rearme es más
    // alto que el de disparo a propósito: sin esa histéresis el aviso se repetiría en loop
    // mientras la carga oscila alrededor del umbral.
    private const float AvisoBateriaBaja  = 0.20f;
    private const float RearmeBateriaBaja = 0.30f;
    private bool _avisoBateriaBaja;

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

    void OnEnable()  { EnhancedTouchSupport.Enable(); }


    void Update()
    {
        // Fuera de partida la linterna no funciona: suprimir su salida (sin tocar isOn,
        // asi al arrancar la partida queda encendida por defecto). El toggle esta
        // bloqueado por CanOperate, y no se drena bateria aca.
        if (!CanOperate)
        {
            Shader.SetGlobalFloat(ID_INTENSITY, 0f);
            Shader.SetGlobalFloat(ID_DARKNESS, 1f);
            if (_light != null) _light.enabled = false;
            return;
        }

        HandleToggle();

        // Drenar la batería mientras está encendida; apagar al agotarse.
        if (isOn)
        {
            if (currentCharge > 0f)
                currentCharge = Mathf.Max(0f, currentCharge - drainPerSecond * Time.deltaTime);

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
                AudioManager.Sonar(c => c.linternaSeAgota);
            }
        }
        if (_avisoBateriaBaja && Charge01 > RearmeBateriaBaja) _avisoBateriaBaja = false;

        if (innerAngleDeg > outerAngleDeg - 1f)
            innerAngleDeg = Mathf.Max(0f, outerAngleDeg - 1f);

        float effectiveIntensity = isOn ? intensity : 0f;

        Shader.SetGlobalVector(ID_POS, transform.position);
        Shader.SetGlobalVector(ID_DIR, transform.forward);
        Shader.SetGlobalFloat(ID_RANGE, range);
        Shader.SetGlobalFloat(ID_COS_OUTER, Mathf.Cos(outerAngleDeg * Mathf.Deg2Rad));
        Shader.SetGlobalFloat(ID_COS_INNER, Mathf.Cos(innerAngleDeg * Mathf.Deg2Rad));
        Shader.SetGlobalFloat(ID_INTENSITY, effectiveIntensity);
        Shader.SetGlobalColor(ID_COLOR, color);
        Shader.SetGlobalFloat(ID_DARKNESS, isOn ? darknessAmount : 1f);

        if (_light != null)
        {
            _light.enabled = isOn;
            _light.color = color;
            _light.range = range;
            _light.spotAngle = outerAngleDeg * 2f;
            _light.innerSpotAngle = innerAngleDeg * 2f;
            _light.intensity = intensity * realLightIntensityMultiplier;
        }
    }

    void HandleToggle()
    {
        if (!toggleWithTwoFingers) return;
        if (Time.time - _lastToggleTime < 0.5f) return;

        int fingers = 0;
        var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
        for (int i = 0; i < touches.Count; i++)
        {
            if (touches[i].phase == UnityEngine.InputSystem.TouchPhase.Began) fingers++;
        }
        if (fingers >= 2)
        {
            Toggle();
            _lastToggleTime = Time.time;
        }
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(ID_INTENSITY, 0f);
        EnhancedTouchSupport.Disable();
    }
}
