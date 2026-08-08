using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Nivel de calidad de los SUBSISTEMAS AR durante el gameplay: es la perilla que más
// pesa en batería y temperatura del dispositivo.
//
// Por qué existe: en un iPhone con LiDAR, AdaptiveOcclusion prendía la malla de
// reconstrucción a density=1 Y la profundidad neuronal en Best al mismo tiempo. Las
// dos features más caras de ARKit, las dos al máximo. El coste no se ve como GPU:
// la malla llega por chunks que Unity convierte en GameObjects y Mesh en el HILO
// PRINCIPAL, así que aparece como CPU y como drenaje de batería.
//
// Qué NO se toca acá: los fps. El doc de diseño marca que por debajo de 60 aparece
// el motion sickness en estéreo, así que 60 es piso en los tres niveles — se baja
// calidad de AR y resolución de render, nunca la fluidez.
//
// El escáner (LiDARScanner) NO usa esto a propósito: escanear el cuarto es una
// operación puntual donde la precisión de la malla sí importa y dura poco.
public static class ARQuality
{
    public enum Nivel
    {
        Rendimiento,   // menos batería y calor; oclusión más gruesa
        Equilibrado,   // por defecto
        Calidad,       // lo que hacía antes: todo al máximo
    }

    // Cambia el nivel y lo aplica en caliente. Persiste en GameOptions.
    public static Nivel Actual
    {
        get => (Nivel)Mathf.Clamp(GameOptions.CalidadAR, 0, 2);
        set
        {
            if (GameOptions.CalidadAR == (int)value) return;
            GameOptions.CalidadAR = (int)value;
            Aplicar();
            OnCambio?.Invoke();
        }
    }

    // Lo escuchan los sistemas que tienen que reconfigurarse (AdaptiveOcclusion).
    public static event Action OnCambio;

    // ── Parámetros derivados ──────────────────────────────────────────────

    // Densidad de la malla de LiDAR. Es el parámetro más caro de todos: la cantidad
    // de vértices (y de chunks a procesar por frame) escala con esto.
    public static float MeshDensity => Actual switch
    {
        Nivel.Rendimiento => 0.25f,
        Nivel.Equilibrado => 0.50f,
        _                 => 1.00f,
    };

    // Profundidad por píxel. Con malla real de LiDAR es en buena medida redundante:
    // la oclusión ya la da la geometría. Por eso en Rendimiento se apaga entera.
    public static EnvironmentDepthMode DepthMode => Actual switch
    {
        Nivel.Rendimiento => EnvironmentDepthMode.Disabled,
        Nivel.Equilibrado => EnvironmentDepthMode.Fastest,
        _                 => EnvironmentDepthMode.Best,
    };

    // Escala de la RenderTexture del Cardboard. Cada ojo muestra la MITAD del ancho,
    // así que rendear a resolución nativa completa es fill rate que nunca se ve.
    public static float RenderScale => Actual switch
    {
        Nivel.Rendimiento => 0.65f,
        Nivel.Equilibrado => 0.80f,
        _                 => 1.00f,
    };

    // Factor EXTRA sobre RenderScale cuando el Cardboard está en estéreo real: ahí la escena
    // se rendea DOS veces por frame (una por ojo), así que sin bajar la resolución el fill
    // rate se duplicaría. 0.75 por eje deja el total en ~1.1x del mono en vez de 2x.
    public const float StereoScale = 0.75f;

    // Piso innegociable: ver la nota de motion sickness arriba.
    public const int TargetFps = 60;

    public static string Descripcion(Nivel n) => n switch
    {
        Nivel.Rendimiento => "Malla gruesa, sin profundidad por píxel, render al 65%. Menos calor y batería.",
        Nivel.Equilibrado => "Malla media, profundidad rápida, render al 80%.",
        _                 => "Malla máxima y profundidad Best. La oclusión más precisa, el mayor consumo.",
    };

    public static string Nombre(Nivel n) => n switch
    {
        Nivel.Rendimiento => "RENDIMIENTO",
        Nivel.Equilibrado => "EQUILIBRADO",
        _                 => "CALIDAD",
    };

    // ── Aplicación ────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init() => AplicarFrameRate();

    private static void AplicarFrameRate()
    {
        // vSyncCount tiene que ser 0 para que targetFrameRate mande; con vSync activo
        // Unity ignora el target. Sin esto el render queda suelto y en un equipo
        // ProMotion se va por encima de 60 gastando batería de gusto.
        QualitySettings.vSyncCount   = 0;
        Application.targetFrameRate  = TargetFps;
    }

    // Reconfigura los managers AR vivos. La RenderTexture del Cardboard no hace falta
    // tocarla: MRCardboardController.EnsureRT ya recalcula su tamaño cada LateUpdate y
    // se rehace sola cuando cambia RenderScale.
    public static void Aplicar()
    {
        AplicarFrameRate();

        var occ = UnityEngine.Object.FindFirstObjectByType<AROcclusionManager>();
        if (occ != null)
        {
            var modo = DepthMode;
            occ.requestedEnvironmentDepthMode = modo;
            occ.enabled = modo != EnvironmentDepthMode.Disabled;
        }

        var mesh = UnityEngine.Object.FindFirstObjectByType<ARMeshManager>();
        if (mesh != null && mesh.subsystem != null)
            mesh.density = MeshDensity;

        Debug.Log($"[ARQuality] {Nombre(Actual)}: density={MeshDensity:0.00} " +
                  $"depth={DepthMode} renderScale={RenderScale:0.00} fps={TargetFps}");
    }
}
