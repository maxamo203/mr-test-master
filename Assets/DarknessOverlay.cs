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

    [Header("Referencias")]
    public Camera arCamera;
    public Flashlight flashlight;

    private GameObject _quad;
    private Material _mat;
    private static readonly int ID_DARK = Shader.PropertyToID("_OverlayDarkness");

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

        float dist = arCamera.nearClipPlane * 1.5f + 0.01f;
        float h = 2f * dist * Mathf.Tan(arCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * arCamera.aspect;
        _quad.transform.localPosition = new Vector3(0f, 0f, dist);
        _quad.transform.localRotation = Quaternion.identity;
        _quad.transform.localScale = new Vector3(w, h, 1f);

        float effectiveDark;
        if (flashlight != null && !flashlight.isOn)
            effectiveDark = darkWhenFlashlightOff ? darknessWhenOff : 0f;
        else
            effectiveDark = darkness;

        Shader.SetGlobalFloat(ID_DARK, effectiveDark);
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
