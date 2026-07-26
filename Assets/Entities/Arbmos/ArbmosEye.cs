using UnityEngine;

// Ojo brillante del Arbmos, AUTOCONTENIDO. Ponelo en un GameObject vacio y ubicalo VOS
// donde quieras desde el editor (tipicamente dos, uno por ojo; emparentalos al hueso de
// la cabeza si queres que sigan la animacion). El script solo MUESTRA el ojo: un orbe que
// brilla del color elegido (billboard additivo que mira a la camara) + una luz puntual
// real que ilumina el entorno (estilo ojos de zombie de CoD).
//
// La POSICION la manejas 100% desde el editor. El orbe se crea como HIJO ("EyeGlow") de
// ESTE GameObject; su tamaño se mantiene en 'size' metros REALES compensando la escala del
// padre (asi no se rompe si cuelgas este objeto de un hueso escalado). La luz no se ve
// afectada por la escala.
[DefaultExecutionOrder(60)]
public class ArbmosEye : MonoBehaviour
{
    [Header("Orbe")]
    public Color color = new Color(1f, 0.95f, 0f);   // amarillo por defecto
    [Tooltip("Tamaño del orbe en metros (real, no depende de la escala del padre).")]
    public float size = 0.04f;
    [Tooltip("Brillo del orbe (intensidad del additivo). Mas alto = mas fuerte.")]
    public float glow = 2.2f;
    [Tooltip("El orbe mira siempre a la camara (para que se vea redondo).")]
    public bool  billboard = true;

    [Header("Luz real")]
    [Tooltip("Ademas del orbe, emitir una luz puntual que ilumina el entorno.")]
    public bool  emitLight = true;
    public float lightRange = 2.2f;
    public float lightIntensity = 1.4f;

    private Transform _glow;   // orbe "EyeGlow" (hijo de este GameObject)

    private void Start()
    {
        // No mostrar en las copias que el server simula para OTROS jugadores (van ocultas
        // en el host): el Arbmos es una alucinacion individual.
        var owner = GetComponentInParent<ArbmosEntity>();
        if (owner != null && !owner.Rendered) { enabled = false; return; }

        // Orbe brillante, HIJO de este GameObject (sigue su posicion automaticamente).
        var glowGo = new GameObject("EyeGlow");
        glowGo.transform.SetParent(transform, false);
        glowGo.transform.localPosition = Vector3.zero;
        glowGo.AddComponent<MeshFilter>().sharedMesh = ArbmosGfx.QuadMesh();
        var mr = glowGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = ArbmosGfx.ParticleMaterial(additive: true, tint: color * glow);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        _glow = glowGo.transform;
        ApplyGlowScale();

        // Luz real en ESTE objeto (la escala no afecta el rango/intensidad de una luz).
        if (emitLight)
        {
            var lt = gameObject.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = color;
            lt.range = lightRange;
            lt.intensity = lightIntensity;
            lt.renderMode = LightRenderMode.ForcePixel;
            lt.shadows = LightShadows.None;
        }

        SyncGlow();
    }

    private void LateUpdate() => SyncGlow();

    // Orienta el orbe a la camara (billboard) y mantiene su tamaño en metros reales.
    private void SyncGlow()
    {
        if (_glow == null) return;
        ApplyGlowScale();
        var cam = Camera.main;
        _glow.rotation = (billboard && cam != null) ? cam.transform.rotation : transform.rotation;
    }

    // localScale del orbe que compensa la escala del padre => mide 'size' metros reales
    // aunque este objeto cuelgue de un hueso con escala rara.
    private void ApplyGlowScale()
    {
        Vector3 s = transform.lossyScale;
        _glow.localScale = new Vector3(size / Safe(s.x), size / Safe(s.y), size / Safe(s.z));
    }

    private static float Safe(float v) { v = Mathf.Abs(v); return v < 1e-4f ? 1e-4f : v; }

    private void OnEnable()  { if (_glow != null) _glow.gameObject.SetActive(true); }
    private void OnDisable() { if (_glow != null) _glow.gameObject.SetActive(false); }
    private void OnDestroy() { if (_glow != null) Destroy(_glow.gameObject); }
}
