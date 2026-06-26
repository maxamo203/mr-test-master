using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Adjuntar al mismo GameObject que ARTrackedImageManager.
// Mostrá la imagen de referencia, apuntá el teléfono, y observá las métricas en pantalla.
// Comparar los números entre dispositivos para elegir el mejor hardware.
public class ARTrackingBenchmark : MonoBehaviour
{
    [Header("Duración de la sesión de medición (segundos)")]
    [SerializeField] private float _sessionDuration = 30f;

    private ARTrackedImageManager _imageManager;

    // ── Métricas acumuladas ───────────────────────────────────────────────
    private Vector3 _firstPosition;
    private Vector3 _lastPosition;
    private bool    _hasFirst;

    private float _maxDrift;           // distancia máxima desde la posición inicial
    private float _jitterAccum;        // suma de deltas frame-a-frame
    private int   _trackingFrames;     // frames con TrackingState.Tracking
    private int   _totalFrames;
    private int   _jitterSamples;

    private float _elapsed;
    private bool  _done;

    // Ventana deslizante para desvío estándar en tiempo real
    private readonly Queue<Vector3> _window = new();
    private Vector3 _windowSum;
    private const int WindowSize = 60; // últimos 60 frames (~1s)

    private GUIStyle _style;

    // ── Resultados finales ────────────────────────────────────────────────
    private string _report = "Apuntá a la imagen para iniciar...";

    private void Awake()
    {
        _imageManager = GetComponent<ARTrackedImageManager>();
        if (_imageManager == null)
            _imageManager = FindFirstObjectByType<ARTrackedImageManager>();
    }

    private void Update()
    {
        if (_done) return;

        _totalFrames++;

        ARTrackedImage img = null;
        foreach (var t in _imageManager.trackables)
        {
            if (t.trackingState == TrackingState.Tracking) { img = t; break; }
        }

        if (img == null)
        {
            _report = $"Sin tracking ({_trackingFrames}/{_totalFrames} frames OK hasta ahora)\nApuntá a la imagen...";
            return;
        }

        _trackingFrames++;
        _elapsed += Time.deltaTime;

        var pos = img.transform.position;

        // Primera posición de referencia
        if (!_hasFirst)
        {
            _firstPosition = pos;
            _lastPosition  = pos;
            _hasFirst      = true;
        }

        // Jitter (delta frame-a-frame)
        float delta = Vector3.Distance(pos, _lastPosition);
        _jitterAccum += delta;
        _jitterSamples++;
        _lastPosition = pos;

        // Drift máximo desde el primer punto
        float drift = Vector3.Distance(pos, _firstPosition);
        if (drift > _maxDrift) _maxDrift = drift;

        // Desvío estándar en ventana deslizante
        _windowSum += pos;
        _window.Enqueue(pos);
        if (_window.Count > WindowSize)
            _windowSum -= _window.Dequeue();

        float stability = (_trackingFrames / (float)_totalFrames) * 100f;
        float avgJitter = _jitterSamples > 0 ? (_jitterAccum / _jitterSamples) * 1000f : 0f;

        _report = $"Tiempo: {_elapsed:F1}s / {_sessionDuration}s\n" +
                  $"Tracking: {stability:F1}%\n" +
                  $"Drift max: {_maxDrift * 100f:F1} cm\n" +
                  $"Jitter avg: {avgJitter:F2} mm/frame\n" +
                  $"Jitter std: {WindowStdDev() * 1000f:F2} mm\n" +
                  $"(drift actual: {drift * 100f:F1} cm)";

        if (_elapsed >= _sessionDuration)
            Finish();
    }

    private void Finish()
    {
        _done = true;
        float stability = (_trackingFrames / (float)Mathf.Max(_totalFrames, 1)) * 100f;
        float avgJitter = _jitterSamples > 0 ? (_jitterAccum / _jitterSamples) * 1000f : 0f;

        _report =
            $"=== RESULTADO FINAL ===\n" +
            $"Tracking estable: {stability:F1}%\n" +
            $"Drift máximo: {_maxDrift * 100f:F1} cm\n" +
            $"Jitter promedio: {avgJitter:F2} mm/frame\n\n" +
            $"Calidad estimada: {Rate(stability, _maxDrift, avgJitter)}";

        Debug.Log($"[ARBenchmark] {_report}");
    }

    // ── UI ────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.box)
            {
                fontSize  = Mathf.RoundToInt(Screen.height * 0.03f),
                alignment = TextAnchor.UpperLeft,
            };
            _style.normal.textColor = Color.white;
        }

        float w = Screen.width  * 0.6f;
        float h = Screen.height * 0.45f;
        GUI.Box(new Rect(20, 20, w, h), _report, _style);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private float WindowStdDev()
    {
        int n = _window.Count;
        if (n < 2) return 0f;

        Vector3 mean = _windowSum / n;
        float variance = 0f;
        foreach (var p in _window)
            variance += (p - mean).sqrMagnitude;
        return Mathf.Sqrt(variance / n);
    }

    private static string Rate(float stability, float maxDrift, float avgJitter)
    {
        // Criterios empíricos orientativos
        bool goodStability = stability >= 90f;
        bool goodDrift     = maxDrift  <= 0.02f;  // ≤ 2 cm
        bool goodJitter    = avgJitter <= 0.5f;    // ≤ 0.5 mm/frame

        int score = (goodStability ? 1 : 0) + (goodDrift ? 1 : 0) + (goodJitter ? 1 : 0);
        return score switch
        {
            3 => "EXCELENTE - apto para AR sin corrección",
            2 => "BUENO - drift tolerable con imagen visible",
            1 => "REGULAR - se va a notar el desfasaje",
            _ => "MALO - no apto para este uso",
        };
    }
}
