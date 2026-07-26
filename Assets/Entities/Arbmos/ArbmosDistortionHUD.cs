using UnityEngine;

// Distorsion de lente LOCALIZADA en el segmento de pantalla donde aparece el Arbmos
// (diseño: "distorsion de lente localizada — pixelado, rayado o desenfoque — en el
// segmento de pantalla donde aparece"). Ademas, en la secuencia LETAL de cordura 0,
// una distorsion brusca de camara que escala con ArbmosEntity.Distort01.
//
// Sin post-process/URP en el proyecto (pipeline built-in con overlays IMGUI + shaders
// globales, ver SanityHUD/DarknessOverlay): se dibuja con IMGUI sobre el rect que ocupa
// el Arbmos en pantalla. Publica ademas el global _ArbmosDistort (0..1) por si luego se
// suma un post-effect real.
//
// Singleton auto-creado (ArbmosNetwork.OnNetworkSpawn lo asegura en la copia que se
// dibuja). Lee ArbmosEntity.Active — la unica copia visible en este dispositivo.
public class ArbmosDistortionHUD : MonoBehaviour
{
    private static readonly int ID_DISTORT = Shader.PropertyToID("_ArbmosDistort");
    private static ArbmosDistortionHUD _instance;

    private Texture2D _tex;

    public static ArbmosDistortionHUD Ensure()
    {
        if (_instance == null)
        {
            var go = new GameObject("ArbmosDistortionHUD");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ArbmosDistortionHUD>();
        }
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        _tex = Texture2D.whiteTexture;
    }

    private void OnGUI()
    {
        var a = ArbmosEntity.Active;
        var cam = Camera.main;
        var s = a != null ? a.distortion : null;
        if (a == null || cam == null || s == null || !s.enabled) { Shader.SetGlobalFloat(ID_DISTORT, 0f); return; }

        // Proyeccion del cuerpo del Arbmos a pantalla (base + cabeza) para un rect vertical.
        Vector3 baseW = a.Position;
        Vector3 headW = a.Position + Vector3.up * 1.7f;
        Vector3 sp0 = cam.WorldToScreenPoint(baseW);
        Vector3 sp1 = cam.WorldToScreenPoint(headW);
        if (sp0.z <= 0f && sp1.z <= 0f) { Shader.SetGlobalFloat(ID_DISTORT, 0f); return; } // detras de camara

        // GUI usa Y hacia abajo; WorldToScreenPoint usa Y hacia arriba.
        float yTop    = Screen.height - Mathf.Max(sp0.y, sp1.y);
        float yBottom = Screen.height - Mathf.Min(sp0.y, sp1.y);
        float cx = (sp0.x + sp1.x) * 0.5f;
        float h  = Mathf.Max(60f, yBottom - yTop);
        float w  = h * 0.85f;

        // Intensidad: base por presencia + la que fija el server (escala en la fase letal).
        float intensity = Mathf.Clamp01(s.baseIntensity + s.intensityScale * a.Distort01);
        Shader.SetGlobalFloat(ID_DISTORT, intensity);

        // Zona afectada, escalada alrededor del centro por regionScale.
        float rw = w * 1.0f * s.regionScale;
        float rh = h * 1.3f * s.regionScale;
        float cy = (yTop + yBottom) * 0.5f;
        var rect = new Rect(cx - rw * 0.5f, cy - rh * 0.5f, rw, rh);
        rect.x = Mathf.Clamp(rect.x, 0f, Screen.width - rect.width);
        rect.y = Mathf.Clamp(rect.y, 0f, Screen.height - rect.height);

        DrawLocalizedGlitch(rect, intensity, s);

        // Distorsion brusca de camara en la secuencia letal (full-screen, escala con distort).
        if (a.Lethal && s.lethalBurst > 0f) DrawLethalJolt(a.Distort01, s);
    }

    // Pixelado + rayado + tinte con aberracion cromatica dentro del rect del Arbmos.
    private void DrawLocalizedGlitch(Rect rect, float intensity, ArbmosDistortionSettings s)
    {
        var prev = GUI.color;
        float t = Time.unscaledTime;
        var rng = new System.Random((int)(t * 20f));

        // Tinte de base (color configurable, opacidad escala con la intensidad).
        var tint = s.tintColor;
        tint.a = s.tintStrength * Mathf.Lerp(0.33f, 1f, intensity);
        GUI.color = tint;
        GUI.DrawTexture(rect, _tex);

        // Bandas horizontales (rayado / scanlines desplazadas).
        int bands = Mathf.RoundToInt(s.scanlines * Mathf.Lerp(0.4f, 1f, intensity));
        for (int i = 0; i < bands; i++)
        {
            float y  = rect.y + (float)rng.NextDouble() * rect.height;
            float bh = 2f + (float)rng.NextDouble() * (6f + 10f * intensity);
            float dx = ((float)rng.NextDouble() - 0.5f) * 12f * intensity;   // corrimiento
            GUI.color = new Color(1f, 1f, 1f, 0.04f + (float)rng.NextDouble() * 0.14f * intensity);
            GUI.DrawTexture(new Rect(rect.x + dx, y, rect.width, bh), _tex);
        }

        // Aberracion cromatica: dos rects tenues rojo/cian corridos.
        float ca = s.chromatic * Mathf.Lerp(0.3f, 1f, intensity);
        GUI.color = new Color(1f, 0f, 0f, 0.06f + 0.10f * intensity);
        GUI.DrawTexture(new Rect(rect.x - ca, rect.y, rect.width, rect.height), _tex);
        GUI.color = new Color(0f, 1f, 1f, 0.06f + 0.10f * intensity);
        GUI.DrawTexture(new Rect(rect.x + ca, rect.y, rect.width, rect.height), _tex);

        GUI.color = prev;
    }

    // Sacudida/estatica full-screen para el ataque letal.
    private void DrawLethalJolt(float d01, ArbmosDistortionSettings s)
    {
        var prev = GUI.color;
        float t = Time.unscaledTime;
        var rng = new System.Random((int)(t * 30f));
        float k = s.lethalBurst;

        var jolt = s.lethalColor;
        jolt.a = Mathf.Clamp01((0.12f + 0.30f * d01) * k);
        GUI.color = jolt;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _tex);

        int lines = Mathf.RoundToInt((4f + 16f * d01) * k);
        for (int i = 0; i < lines; i++)
        {
            float y  = (float)rng.NextDouble() * Screen.height;
            float bh = 2f + (float)rng.NextDouble() * 22f * d01;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((0.05f + (float)rng.NextDouble() * 0.20f * d01) * k));
            GUI.DrawTexture(new Rect(0, y, Screen.width, bh), _tex);
        }
        GUI.color = prev;
    }

    private void OnDisable()  => Shader.SetGlobalFloat(ID_DISTORT, 0f);
    private void OnDestroy()
    {
        Shader.SetGlobalFloat(ID_DISTORT, 0f);
        if (_instance == this) _instance = null;
    }
}
