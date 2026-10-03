using UnityEngine;
using UnityEngine.Rendering;

public class DarknessOverlay : MonoBehaviour
{
    [Tooltip("Cuán oscuro está el ambiente fuera del cono de la linterna. 0 = sin oscurecer, 1 = negro total")]
    [Range(0f, 1f)] public float darkness = 0.92f;

    [Tooltip("Si está apagada la linterna, ¿igual oscurecer la pantalla?")]
    public bool darkWhenFlashlightOff = true;

    [Tooltip("Si darkWhenFlashlightOff = true, qué tan oscuro cuando la linterna está apagada")]
    [Range(0f, 1f)] public float darknessWhenOff = 0.95f;

    // El agujero que abre la linterna en la oscuridad tiene borde con puntas (estilo
    // "spiky vignette") y su tamaño depende de la BATERIA: enorme con la pila llena, se
    // va cerrando hasta un minimo (nunca negro total) a medida que se agota. Es solo la
    // vista: el cono de gameplay (PlayerLights, la Light) sigue usando
    // Flashlight.outerAngleDeg y no se achica.
    [Header("Agujero de la linterna (según batería)")]
    // Ojo con la escala: del centro al borde corto de la pantalla entran ~30° (menos por
    // ojo en Cardboard). Un radio mayor a eso deja toda la pantalla dentro del agujero y
    // el efecto no se ve en absoluto.
    [Tooltip("Radio angular del agujero con la batería al 100%, en grados. ~30° ya roza el borde corto de la pantalla.")]
    [Range(5f, 80f)] public float anguloBateriaLlenaDeg = 17f;
    [Tooltip("Radio angular del agujero con la batería casi en 0%, en grados. Mínimo: no se cierra del todo.")]
    [Range(2f, 60f)] public float anguloBateriaVaciaDeg = 9f;
    [Tooltip("Curva de cierre. 1 = lineal; >1 se mantiene amplio más tiempo y cae al final; <1 se cierra antes.")]
    [Range(0.25f, 4f)] public float curvaCierre = 1.4f;
    [Tooltip("Largo de las puntas del borde, como fracción del radio.")]
    [Range(0f, 0.6f)] public float puntas = 0.22f;
    [Tooltip("Ancho del degradé del borde, como fracción del radio.")]
    [Range(0.01f, 0.6f)] public float suavidadBorde = 0.18f;
    [Tooltip("Cuánto aclaran los rayos finos que se escapan del borde hacia la oscuridad (0 = sin rayos).")]
    [Range(0f, 1f)] public float rayos = 0.35f;
    [Tooltip("Segundos que tarda el agujero en acompañar un cambio brusco de carga (al recoger una pila).")]
    [Range(0f, 3f)] public float suavizadoCarga = 0.6f;

    [Header("Referencias")]
    public Camera arCamera;
    public Flashlight flashlight;

    private GameObject _quad;
    private Material _mat;
    private float _cargaVisible = 1f;
    private float _cargaVel;
    private static readonly int ID_DARK      = Shader.PropertyToID("_OverlayDarkness");
    private static readonly int ID_CONE_TAN  = Shader.PropertyToID("_OverlayConeTan");
    private static readonly int ID_SOFTNESS  = Shader.PropertyToID("_OverlaySoftness");
    private static readonly int ID_SPIKES    = Shader.PropertyToID("_OverlaySpikes");
    private static readonly int ID_RAYS      = Shader.PropertyToID("_OverlayRays");

    void Awake()
    {
        if (arCamera == null) arCamera = GetComponent<Camera>();
        if (arCamera == null) arCamera = Camera.main;
        if (flashlight == null) flashlight = FindFirstObjectByType<Flashlight>();

        var shader = Resources.Load<Shader>("DarknessOverlay");
        if (shader == null) shader = Shader.Find("AR/DarknessOverlay");
        if (shader == null)
        {
            Debug.LogError("DarknessOverlay: shader 'AR/DarknessOverlay' no encontrado en Assets/Resources/.");
            return;
        }
        _mat = new Material(shader) { name = "DarknessOverlayMat" };
        CreateQuad();
    }

    void CreateQuad()
    {
        if (arCamera == null) return;
        _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _quad.name = "DarknessOverlayQuad";
        var col = _quad.GetComponent<Collider>();
        if (col != null) Destroy(col);
        _quad.transform.SetParent(arCamera.transform, false);
        var r = _quad.GetComponent<Renderer>();
        r.sharedMaterial = _mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    void LateUpdate()
    {
        if (_quad == null || arCamera == null) return;

        // Sobredimensionamos el quad para que SIEMPRE tape toda la cámara visible. Dos motivos:
        //  1) AR Foundation reemplaza la proyección por las intrínsecas de la cámara, así que la
        //     FOV real no coincide con arCamera.fieldOfView → el quad "exacto" queda chico y deja
        //     márgenes (parece que respeta la zona segura).
        //  2) En Cardboard hay dos ojos (viewports izq/der); un quad ajustado a uno deja el otro
        //     costado sin tapar.
        // El negro sobrante se recorta contra el borde de pantalla y el cono de la linterna se
        // calcula en world-space (por-ojo), así que pasarnos de tamaño no tiene costo visual.
        const float kOversize = 2.5f;
        float dist = arCamera.nearClipPlane * 1.5f + 0.01f;
        float h = 2f * dist * Mathf.Tan(arCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * kOversize;
        float w = h * Mathf.Max(arCamera.aspect, 2.2f);   // ancho suficiente para landscape y ambos ojos
        _quad.transform.localPosition = new Vector3(0f, 0f, dist);
        _quad.transform.localRotation = Quaternion.identity;
        _quad.transform.localScale = new Vector3(w, h, 1f);

        float effectiveDark;
        if (flashlight != null && !flashlight.Operational)
            effectiveDark = 0f;   // fuera de partida (menú/lobby) no oscurecer: el overlay
                                  // de la linterna solo aparece cuando arranca la partida
        else if (flashlight != null && !flashlight.isOn)
            effectiveDark = darkWhenFlashlightOff ? darknessWhenOff : 0f;
        else
            effectiveDark = darkness;

        Shader.SetGlobalFloat(ID_DARK, effectiveDark);
        PublicarAgujero();
    }

    void PublicarAgujero()
    {
        float carga = flashlight != null ? flashlight.Charge01 : 1f;
        // Fuera de partida no hay transición que mostrar: arrancar la noche ya con el
        // tamaño correcto en vez de verlo abrirse.
        if (flashlight == null || !flashlight.Operational || suavizadoCarga <= 0f)
        {
            _cargaVisible = carga;
            _cargaVel = 0f;
        }
        else
        {
            _cargaVisible = Mathf.SmoothDamp(_cargaVisible, carga, ref _cargaVel, suavizadoCarga);
        }

        float t = Mathf.Pow(Mathf.Clamp01(_cargaVisible), curvaCierre);
        float minDeg = Mathf.Min(anguloBateriaVaciaDeg, anguloBateriaLlenaDeg);
        float deg = Mathf.Lerp(minDeg, anguloBateriaLlenaDeg, t);

        Shader.SetGlobalFloat(ID_CONE_TAN, Mathf.Tan(deg * Mathf.Deg2Rad));
        Shader.SetGlobalFloat(ID_SOFTNESS, suavidadBorde);
        Shader.SetGlobalFloat(ID_SPIKES, puntas);
        Shader.SetGlobalFloat(ID_RAYS, rayos);
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(ID_DARK, 0f);
        if (_quad != null) _quad.SetActive(false);
    }

    void OnEnable()
    {
        if (_quad != null) _quad.SetActive(true);
    }

    void OnDestroy()
    {
        if (_quad != null) Destroy(_quad);
        if (_mat != null) Destroy(_mat);
    }
}
