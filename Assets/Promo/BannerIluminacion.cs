#if UNITY_EDITOR
using System;
using UnityEngine;

// Panel único de iluminación de la escena del banner: cada luz con su color e intensidad,
// más niebla y luz ambiente, todo desde un solo Inspector (objeto "Iluminación"). Los
// cambios se aplican al instante en edit mode. Las luces siguen siendo objetos normales:
// para moverlas o reapuntarlas se selecciona la luz en la jerarquía.
//
// TIER: editor-only (archivo entero bajo #if UNITY_EDITOR).
[ExecuteAlways]
public class BannerIluminacion : MonoBehaviour
{
    [Serializable]
    public class Canal
    {
        public string nombre;
        public Light luz;
        public bool encendida = true;
        public Color color = Color.white;
        [Range(0f, 12f)] public float intensidad = 1f;
        [Range(0f, 1f)] public float sombra = 1f;
    }

    [Tooltip("Multiplica todas las luces a la vez (la exposición final está en BannerCamaraFX).")]
    [Range(0f, 3f)] public float general = 1f;
    public Canal[] luces = Array.Empty<Canal>();

    [Header("Ambiente")]
    public Color luzAmbiente = new Color(0.035f, 0.02f, 0.03f);

    [Header("Niebla")]
    public bool niebla = true;
    public Color colorNiebla = new Color(0.05f, 0.015f, 0.02f);
    [Range(0f, 0.4f)] public float densidadNiebla = 0.08f;

    private void OnEnable() => Aplicar();
    private void OnValidate() => Aplicar();

    [ContextMenu("Aplicar")]
    public void Aplicar()
    {
        foreach (var c in luces)
        {
            if (c?.luz == null) continue;
            c.luz.enabled = c.encendida;
            c.luz.color = c.color;
            c.luz.intensity = c.intensidad * general;
            c.luz.shadowStrength = c.sombra;
        }
        RenderSettings.ambientLight = luzAmbiente;
        RenderSettings.fog = niebla;
        RenderSettings.fogColor = colorNiebla;
        RenderSettings.fogDensity = densidadNiebla;
    }
}
#endif
