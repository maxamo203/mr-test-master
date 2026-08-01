using Scanner;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

// Calidad del punto bajo la mira, para saber si vale la pena anclar ahí.
//
// No hay una API de "calidad de anclaje" disponible en este proyecto: la de ARCore
// (EstimateFeatureMapQualityForHosting) vive en ARCore Extensions, que no está
// instalado. Así que la estimamos con dos señales que sí tenemos:
//
//   1) De dónde vino el hit del raycast (RaycastResolver): un punto sobre malla
//      LiDAR o un plano detectado es mejor base que un feature point suelto, y el
//      fallback (punto en el aire sobre el rayo) no sirve para nada.
//   2) Cuántos feature points hay ALREDEDOR del punto, ponderados por confianza.
//      Ésta es la que captura lo que se ve a ojo: una pared lisa y sin textura casi
//      no genera feature points (el SLAM no tiene de dónde agarrarse ahí), y una
//      estantería o un cuadro genera muchos.
//
// El ARPointCloudManager NO viene en las escenas: se agrega en runtime sobre el
// XROrigin (lo exige [RequireComponent(typeof(XROrigin))]) y se mantiene ENCENDIDO
// SÓLO mientras hay una UI de colocación en pantalla — es un subsistema más
// corriendo por frame y no se paga fuera de ese momento.
public class AnchorQuality : MonoBehaviour
{
    public static AnchorQuality Instance { get; private set; }

    public enum Nivel { Nula, Baja, Media, Alta }

    // Radio alrededor del punto donde contamos feature points.
    private const float RadioM = 0.35f;
    // Puntos (ponderados por confianza) a partir de los cuales el bonus satura.
    private const float PuntosSat = 10f;
    // Cada cuánto recontamos la nube (es O(puntos): unos cientos en ARCore).
    private const float IntervaloMuestreo = 0.2f;

    private ARPointCloudManager _pcm;
    private bool  _activo;
    private float _timer;
    private float _score;          // suavizado 0..1
    private float _scoreObjetivo;
    private float _densidad;       // puntos ponderados cerca del último punto
    private int   _puntosCerca;

    public float  Score01  => _score;
    public int    PuntosCerca => _puntosCerca;

    public Nivel Level =>
        _scoreObjetivo <= 0f ? Nivel.Nula :
        _score < 0.40f       ? Nivel.Baja :
        _score < 0.70f       ? Nivel.Media : Nivel.Alta;

    public string Etiqueta => Level switch
    {
        Nivel.Nula  => "SIN SUPERFICIE",
        Nivel.Baja  => "CALIDAD BAJA",
        Nivel.Media => "CALIDAD MEDIA",
        _           => "CALIDAD ALTA",
    };

    public Color Color => Level switch
    {
        Nivel.Nula  => MortuoriumTheme.Dim,
        Nivel.Baja  => MortuoriumTheme.Red,
        Nivel.Media => MortuoriumTheme.Tan,
        _           => MortuoriumTheme.Green,
    };

    // Sugerencia accionable para el jugador (null si el punto ya está bien).
    public string Consejo => Level switch
    {
        Nivel.Nula => "apuntá a una pared o mueble que AR reconozca",
        Nivel.Baja => "pared muy lisa: buscá un borde, un mueble o algo con textura",
        _          => null,
    };

    // Cartel de calidad JUSTO DEBAJO de la mira, con una barrita de nivel. Lo dibujan
    // igual el escáner (ReticleController) y el lobby (ARLobbyUI), así que vive acá.
    // Se llama desde OnGUI, dentro de UIScale.Begin(); (cx, cy) es el centro virtual.
    public void DibujarBajoLaMira(float cx, float cy)
    {
        var col = Color;
        const float w = 190f, h = 18f;

        GUI.Label(new Rect(cx - w * 0.5f, cy + 46f, w, h), Etiqueta,
                  MortuoriumTheme.Estilo(MortuoriumTheme.FMono, 12, col, TextAnchor.MiddleCenter));

        // Barrita: fondo tenue + relleno proporcional al score.
        var barra = new Rect(cx - 44f, cy + 68f, 88f, 4f);
        MortuoriumTheme.Fill(barra, new Color(1f, 1f, 1f, 0.12f));
        MortuoriumTheme.Fill(new Rect(barra.x, barra.y, barra.width * Mathf.Clamp01(_score), barra.height), col);

        string consejo = Consejo;
        if (!string.IsNullOrEmpty(consejo))
            GUI.Label(new Rect(cx - 150f, cy + 78f, 300f, 32f), consejo,
                      MortuoriumTheme.Estilo(MortuoriumTheme.FMono, 10, MortuoriumTheme.Muted,
                                             TextAnchor.UpperCenter, wrap: true));
    }

    public static AnchorQuality Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("AnchorQuality");
        return go.AddComponent<AnchorQuality>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        SetActivo(false);
        if (Instance == this) Instance = null;
    }

    // Encender sólo mientras se está colocando: el point cloud es un subsistema más
    // corriendo por frame.
    public void SetActivo(bool on)
    {
        if (_activo == on) return;
        _activo = on;

        if (on && _pcm == null) _pcm = AsegurarPointCloudManager();
        if (_pcm != null) _pcm.enabled = on;

        if (!on) { _score = 0f; _scoreObjetivo = 0f; _densidad = 0f; _puntosCerca = 0; }
    }

    private static ARPointCloudManager AsegurarPointCloudManager()
    {
        var existente = FindFirstObjectByType<ARPointCloudManager>();
        if (existente != null) return existente;

        // [RequireComponent(typeof(XROrigin))]: tiene que ir sobre el XROrigin, no en
        // un GameObject suelto. Mismo patrón que AdaptiveOcclusion con el ARMeshManager.
        var origin = FindFirstObjectByType<XROrigin>();
        if (origin == null)
        {
            Debug.LogWarning("[AnchorQuality] No hay XROrigin: la calidad usará sólo la fuente del raycast.");
            return null;
        }
        var pcm = origin.gameObject.AddComponent<ARPointCloudManager>();
        pcm.enabled = false;
        return pcm;
    }

    // Alimentado una vez por frame por quien tiene el hit vivo bajo la mira.
    public void Evaluar(in ResolvedHit hit)
    {
        if (!_activo) return;

        _timer += Time.deltaTime;
        if (_timer >= IntervaloMuestreo)
        {
            _timer = 0f;
            _densidad = MedirDensidad(hit);
        }

        // Base según de dónde salió el punto: el fallback es un punto en el aire y no
        // sirve para anclar, así que se lleva score 0 (= SIN SUPERFICIE).
        float baseScore = !hit.Hit ? 0f : hit.Source switch
        {
            RaycastSource.LidarMesh      => 0.60f,
            RaycastSource.ArPlane        => 0.50f,
            RaycastSource.ArDepth        => 0.45f,
            RaycastSource.ArFeaturePoint => 0.30f,
            _                            => 0f,   // Fallback / None
        };

        _scoreObjetivo = baseScore <= 0f
            ? 0f
            : Mathf.Clamp01(baseScore + Mathf.Clamp01(_densidad / PuntosSat) * 0.5f);

        // Suavizado exponencial: sin esto el cartel titila entre niveles.
        _score = Mathf.Lerp(_score, _scoreObjetivo, 1f - Mathf.Exp(-8f * Time.deltaTime));
    }

    // Suma de confianzas de los feature points dentro de RadioM del punto.
    private float MedirDensidad(in ResolvedHit hit)
    {
        _puntosCerca = 0;
        if (!hit.Hit) return 0f;

#if UNITY_EDITOR
        // En editor no hay subsistema de point cloud (ni de nada): devolvemos un valor
        // medio para poder ejercitar la UI en play mode.
        _puntosCerca = 5;
        return 5f;
#else
        if (_pcm == null) return 0f;

        float r2    = RadioM * RadioM;
        float total = 0f;

        foreach (var nube in _pcm.trackables)
        {
            var pos = nube.positions;
            if (pos == null) continue;

            var puntos = pos.Value;
            var conf   = nube.confidenceValues;
            var tf     = nube.transform;

            for (int i = 0; i < puntos.Length; i++)
            {
                // positions viene en espacio de la nube, no del mundo.
                var mundo = tf.TransformPoint(puntos[i]);
                if ((mundo - hit.Position).sqrMagnitude > r2) continue;

                _puntosCerca++;
                total += conf.HasValue && i < conf.Value.Length ? Mathf.Clamp01(conf.Value[i]) : 1f;
            }
        }
        return total;
#endif
    }
}
