using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Fuente de datos de CONSUMO para el panel DebugEnergiaUI. Vive como hijo del
// DebugHud, así que solo existe en development build (ver DebugHud.Bootstrap).
//
// Para qué: en un juego AR estéreo el drenaje de batería sale de tres lados que
// desde afuera se ven iguales (baja el fps, se calienta) pero se arreglan distinto:
//
//   1. GPU — rendes de más (resolución, estéreo, fill rate).
//   2. CPU — el juego, o los subsistemas AR corriendo en el hilo principal.
//   3. Subsistemas de ARKit (LiDAR meshing, depth neuronal) — no aparecen ni en
//      cpuFrameTime ni en gpuFrameTime porque corren fuera del frame de Unity:
//      se delatan por el consumo y el estado térmico con fps normales.
//
// Por eso el panel muestra CPU y GPU por separado, el estado térmico, y qué
// subsistemas AR están activos con qué configuración.
//
// El texto se rearma a 4 Hz a propósito: un panel que asigna strings todos los
// frames perturba justamente la medición que está tomando.
public class PowerProbe : MonoBehaviour
{
    public const int Muestras = 120;        // columnas del gráfico

    // ── Historia (ring buffer) para el gráfico ────────────────────────────
    private readonly float[] _cpuMs = new float[Muestras];
    private readonly float[] _gpuMs = new float[Muestras];
    private int _idx;

    public float[] HistCpu => _cpuMs;
    public float[] HistGpu => _gpuMs;
    public int     HistIdx => _idx;
    public float   EscalaMs { get; private set; } = 20f;   // techo del gráfico

    // ── Estado actual ─────────────────────────────────────────────────────
    public float UltimoCpuMs { get; private set; }
    public float UltimoGpuMs { get; private set; }
    public float Fps         { get; private set; }
    public bool  HayGpu      { get; private set; }

    private readonly FrameTiming[] _timings = new FrameTiming[1];

    // Batería: el nivel de iOS viene en escalones (~5%), así que la tasa
    // instantánea no sirve. Lo que vale es el delta contra el arranque.
    private float _bateriaInicio = -1f;
    private float _tInicio;

    // Media móvil del frame time (para el número, no para el gráfico).
    private float _acumCpu, _acumGpu;
    private int   _nAcum;

    private string _texto = "Midiendo...";
    private float  _proximoTexto;
    private readonly StringBuilder _sb = new(512);

    // Managers AR (cacheados; se rebuscan cada 2 s como el resto de los paneles).
    private AROcclusionManager    _occ;
    private ARMeshManager         _mesh;
    private ARPlaneManager        _planes;
    private ARTrackedImageManager _img;
    // AdaptiveOcclusion ya no dibuja su propio HUD (corría en release): su diagnóstico
    // se muestra acá.
    private AdaptiveOcclusion _occlusionStrategy;
    private float _proximaBusqueda;

    public string Texto => _texto;

    private void Start()
    {
        _bateriaInicio = SystemInfo.batteryLevel;
        _tInicio       = Time.unscaledTime;
    }

    private void Update()
    {
        MuestrearFrame();

        if (Time.unscaledTime >= _proximaBusqueda)
        {
            BuscarManagers();
            _proximaBusqueda = Time.unscaledTime + 2f;
        }

        if (Time.unscaledTime >= _proximoTexto)
        {
            ArmarTexto();
            _proximoTexto = Time.unscaledTime + 0.25f;
        }
    }

    // ── Muestreo ──────────────────────────────────────────────────────────

    private void MuestrearFrame()
    {
        float dt = Time.unscaledDeltaTime;
        Fps = dt > 0f ? 1f / dt : 0f;

        // FrameTimingManager separa CPU de GPU de verdad (no estimado). Requiere
        // "Frame Timing Stats" activo en Player Settings; si está apagado devuelve
        // 0 muestras y caemos a medir solo el frame time total.
        float cpu, gpu = 0f;
        FrameTimingManager.CaptureFrameTimings();
        uint n = FrameTimingManager.GetLatestTimings(1, _timings);
        if (n > 0)
        {
            cpu    = (float)_timings[0].cpuFrameTime;
            gpu    = (float)_timings[0].gpuFrameTime;
            HayGpu = gpu > 0f;
        }
        else
        {
            cpu    = dt * 1000f;   // fallback: frame time de pared
            HayGpu = false;
        }

        UltimoCpuMs = cpu;
        UltimoGpuMs = gpu;

        _acumCpu += cpu; _acumGpu += gpu; _nAcum++;

        _cpuMs[_idx] = cpu;
        _gpuMs[_idx] = gpu;
        _idx = (_idx + 1) % Muestras;

        // Techo del gráfico: el máximo de la ventana redondeado hacia arriba, con
        // piso en 20 ms para que 60 fps no ocupe toda la altura.
        float max = 20f;
        for (int i = 0; i < Muestras; i++)
        {
            if (_cpuMs[i] > max) max = _cpuMs[i];
            if (_gpuMs[i] > max) max = _gpuMs[i];
        }
        EscalaMs = Mathf.Ceil(max / 10f) * 10f;
    }

    private void BuscarManagers()
    {
        if (_occ    == null) _occ    = FindFirstObjectByType<AROcclusionManager>();
        if (_mesh   == null) _mesh   = FindFirstObjectByType<ARMeshManager>();
        if (_planes == null) _planes = FindFirstObjectByType<ARPlaneManager>();
        if (_img    == null) _img    = FindFirstObjectByType<ARTrackedImageManager>();
        if (_occlusionStrategy == null) _occlusionStrategy = FindFirstObjectByType<AdaptiveOcclusion>();
    }

    // ── Texto ─────────────────────────────────────────────────────────────

    private void ArmarTexto()
    {
        _sb.Clear();

        float promCpu = _nAcum > 0 ? _acumCpu / _nAcum : 0f;
        float promGpu = _nAcum > 0 ? _acumGpu / _nAcum : 0f;
        _acumCpu = _acumGpu = 0f; _nAcum = 0;

        // ── Batería ──
        float bat = SystemInfo.batteryLevel;
        _sb.Append("== ENERGÍA ==\n");
        if (bat < 0f)
        {
            _sb.Append("Batería: no disponible en este device\n");
        }
        else
        {
            float mins = (Time.unscaledTime - _tInicio) / 60f;
            _sb.Append("Batería: ").Append(Mathf.RoundToInt(bat * 100f)).Append("%  (")
               .Append(SystemInfo.batteryStatus).Append(")\n");

            if (_bateriaInicio >= 0f && mins > 0.5f)
            {
                float gastado = (_bateriaInicio - bat) * 100f;
                _sb.Append("Consumido: ").Append(gastado.ToString("0.0")).Append("% en ")
                   .Append(mins.ToString("0.0")).Append(" min");
                if (gastado > 0f)
                    _sb.Append("  →  ").Append((gastado / mins * 60f).ToString("0")).Append(" %/h");
                _sb.Append('\n');
            }
            else
            {
                _sb.Append("Consumido: (esperando 30 s de muestra)\n");
            }
        }

        // ── Térmico ──
        _sb.Append("Térmico: ").Append(NombreTermico(EstadoTermico()));
        int lpm = ModoBajoConsumo();
        if (lpm == 1) _sb.Append("   [MODO BAJO CONSUMO ON — medición sesgada]");
        _sb.Append('\n');

        // ── Frame ──
        _sb.Append("\n== FRAME ==\n");
        _sb.Append("FPS: ").Append(Fps.ToString("0")).Append("   target: ")
           .Append(Application.targetFrameRate < 0 ? "sin cap" : Application.targetFrameRate.ToString())
           .Append("   vSync: ").Append(QualitySettings.vSyncCount).Append('\n');
        _sb.Append("CPU: ").Append(promCpu.ToString("0.0")).Append(" ms\n");
        _sb.Append("GPU: ");
        if (HayGpu) _sb.Append(promGpu.ToString("0.0")).Append(" ms\n");
        else        _sb.Append("n/d (activá Frame Timing Stats en Player Settings)\n");

        _sb.Append("Pantalla: ").Append(Screen.width).Append('x').Append(Screen.height)
           .Append(" @").Append(Screen.currentResolution.refreshRateRatio.value.ToString("0")).Append("Hz\n");

        // ── Subsistemas AR: lo que no aparece en CPU/GPU pero sí en la batería ──
        _sb.Append("\n== SUBSISTEMAS AR ==\n");
        _sb.Append("Calidad: ").Append(ARQuality.Nombre(ARQuality.Actual))
           .Append("  (render x").Append(ARQuality.RenderScale.ToString("0.00")).Append(")\n");
        if (_occlusionStrategy != null)
            _sb.Append("Estrategia: ").Append(_occlusionStrategy.ChosenStrategy).Append('\n');

        if (_occ != null && _occ.enabled)
            _sb.Append("Occlusion: ON  envDepth=").Append(_occ.currentEnvironmentDepthMode).Append('\n');
        else
            _sb.Append("Occlusion: off\n");

        if (_mesh != null && _mesh.enabled && _mesh.subsystem != null)
            _sb.Append("Mesh LiDAR: ON  density=").Append(_mesh.density.ToString("0.00")).Append('\n');
        else
            _sb.Append("Mesh LiDAR: off\n");

        _sb.Append("Planos: ").Append(_planes != null && _planes.enabled ? "ON" : "off");
        if (_planes != null && _planes.enabled) _sb.Append(" (").Append(_planes.trackables.count).Append(')');
        _sb.Append('\n');

        _sb.Append("Img tracking: ").Append(_img != null && _img.enabled ? "ON" : "off").Append('\n');

        _texto = _sb.ToString();
    }

    // ── Nativo iOS ────────────────────────────────────────────────────────

#if UNITY_IOS && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int _MortuoriumThermalState();
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int _MortuoriumLowPowerMode();
#endif

    private static int EstadoTermico()
    {
#if UNITY_IOS && !UNITY_EDITOR
        return _MortuoriumThermalState();
#else
        return -1;
#endif
    }

    private static int ModoBajoConsumo()
    {
#if UNITY_IOS && !UNITY_EDITOR
        return _MortuoriumLowPowerMode();
#else
        return -1;
#endif
    }

    private static string NombreTermico(int e) => e switch
    {
        0 => "nominal",
        1 => "fair (tibio)",
        2 => "SERIOUS (throttling)",
        3 => "CRITICAL (throttling fuerte)",
        _ => "n/d (solo iOS)",
    };

    // Color del estado térmico, para que el panel resalte cuando empieza a throttlear.
    public static Color ColorTermico(int e) => e switch
    {
        2 => new Color(1f, 0.6f, 0.1f),
        3 => new Color(1f, 0.25f, 0.25f),
        _ => new Color(0.6f, 1f, 0.7f),
    };

    public int Termico => EstadoTermico();
}
