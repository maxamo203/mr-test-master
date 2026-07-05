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

    // Fraccion de carga restante (0..1), para HUD.
    public float Charge01 => maxCharge > 0f ? Mathf.Clamp01(currentCharge / maxCharge) : 0f;
    public bool  IsEmpty  => currentCharge <= 0f;

    // Suma carga (al recoger una pila). Vuelve a permitir encender si estaba agotada.
    public void AddCharge(float amount)
    {
        currentCharge = Mathf.Clamp(currentCharge + amount, 0f, maxCharge);
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
        HandleToggle();

        // Drenar la batería mientras está encendida; apagar al agotarse.
        if (isOn)
        {
            if (currentCharge > 0f)
                currentCharge = Mathf.Max(0f, currentCharge - drainPerSecond * Time.deltaTime);
            if (currentCharge <= 0f)
                isOn = false;
        }

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
            // No permitir encender si no hay carga.
            if (!isOn && IsEmpty) return;
            isOn = !isOn;
            _lastToggleTime = Time.time;
        }
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(ID_INTENSITY, 0f);
        EnhancedTouchSupport.Disable();
    }
}
