using UnityEngine;

namespace Gameplay
{
    // Visual del LIBRO RITUAL: el objeto que descansa sobre la imagen de referencia (el
    // mismo punto físico que ancla toda la escena escaneada). Reemplaza al marcador de
    // esferas del anchor mientras hay partida.
    //
    // La apertura NO es una animación ni un clip: son dos rotaciones sobre el eje de la
    // bisagra (el lomo), así que puede quedar en cualquier valor intermedio y responder en
    // vivo a las linternas. El modelo se importa ABIERTO — esa es la pose de reposo
    // (apertura 1) y se captura tal cual viene; lo único que se configura es cuánto rota
    // cada tapa AL CERRARSE.
    //
    // Sin Update propio: la apertura sólo se recalcula cuando el director la cambia.
    //
    // Va en el prefab Assets/Resources/LibroRitual.prefab, que arma el menú
    // "Mortuorium > Crear prefab del Libro Ritual" (Assets/Editor/RitualBookPrefabSetup.cs).
    public class RitualBookView : MonoBehaviour
    {
        public const string ResourceName = "LibroRitual";

        // El libro vivo de este dispositivo (uno solo: cuelga del anchor de la imagen).
        public static RitualBookView Active { get; private set; }

        [Header("Partes del modelo (origen en la bisagra)")]
        [Tooltip("Tapa que gira hacia un lado. Su transform tiene que tener el origen en el lomo.")]
        [SerializeField] private Transform _tapaA;
        [Tooltip("La otra tapa. Idem: origen en el lomo.")]
        [SerializeField] private Transform _tapaB;
        [Tooltip("El lomo (no rota; es la referencia de la bisagra).")]
        [SerializeField] private Transform _lomo;

        [Header("Cierre")]
        [Tooltip("Eje de la bisagra, en el espacio del PADRE de las tapas (no en el de cada " +
                 "tapa: así las dos giran sobre el mismo eje físico aunque el modelo traiga " +
                 "una de ellas espejada). Lo calcula el menú que arma el prefab.")]
        [SerializeField] private Vector3 _ejeBisagra = Vector3.forward;
        [Tooltip("Grados que rota la tapa A desde la pose ABIERTA del modelo hasta quedar " +
                 "cerrada. 90 = las dos tapas suben y se juntan en el medio; 180 en una y 0 " +
                 "en la otra = una tapa se da vuelta sobre la otra.")]
        [SerializeField] private float _cierreTapaA = 90f;
        [Tooltip("Idem para la tapa B.")]
        [SerializeField] private float _cierreTapaB = 90f;

        // Pose ABIERTA del modelo. Serializada (y no leída en Awake a secas) para que el
        // preview del inspector pueda mover las tapas sin perder la referencia.
        [SerializeField, HideInInspector] private bool       _reposoCapturado;
        [SerializeField, HideInInspector] private Quaternion _reposoA = Quaternion.identity;
        [SerializeField, HideInInspector] private Quaternion _reposoB = Quaternion.identity;

        // Centro visual del libro (no su pivote, que puede caer en un borde): es el punto
        // que hay que alumbrar. Se calcula una sola vez.
        private Vector3 _centroLocal;
        private float   _radio;

        private void Awake()
        {
            if (!_reposoCapturado) CapturarReposo();   // prefab armado a mano
            MedirGeometria();
        }

        private void OnEnable()
        {
            Active = this;
            // El visual se destruye y se recrea en cada recalibración del anchor; la
            // apertura vive en el director, así que la recuperamos de ahí.
            Aplicar(RitualBookDirector.Instance != null ? RitualBookDirector.Instance.Apertura01 : 1f);
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
        }

        // Punto al que hay que apuntar con la linterna para que cuente.
        public Vector3 PuntoDeLuz => transform.TransformPoint(_centroLocal);

        // Radio horizontal aproximado (m): alumbrarle el BORDE al libro también tiene que
        // contar, no sólo pegarle justo al centro.
        public float RadioAproximado => _radio;

        // 0 = cerrado (game over), 1 = abierto del todo.
        public void Aplicar(float apertura01)
        {
            float cierre = 1f - Mathf.Clamp01(apertura01);
            var   eje    = _ejeBisagra.sqrMagnitude > 1e-6f ? _ejeBisagra.normalized : Vector3.forward;

            // Pre-multiplicamos: el eje se interpreta en el espacio del PADRE, así que las
            // dos tapas giran sobre la misma bisagra física aunque sus ejes locales no
            // coincidan (el modelo trae una tapa rotada 180°). Como localRotation gira
            // sobre el origen del propio transform —que está en la bisagra— la tapa abre y
            // cierra por donde corresponde sin tocar la posición.
            if (_tapaA != null) _tapaA.localRotation = Quaternion.AngleAxis(_cierreTapaA * cierre, eje) * _reposoA;
            if (_tapaB != null) _tapaB.localRotation = Quaternion.AngleAxis(_cierreTapaB * cierre, eje) * _reposoB;
        }

        // Guarda la pose actual de las tapas como "libro abierto". La llama el menú que
        // arma el prefab, con el modelo recién importado (que viene abierto).
        [ContextMenu("Capturar pose abierta")]
        public void CapturarReposo()
        {
            if (_tapaA != null) _reposoA = _tapaA.localRotation;
            if (_tapaB != null) _reposoB = _tapaB.localRotation;
            _reposoCapturado = true;
        }

        private void MedirGeometria()
        {
            var rends = GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { _centroLocal = Vector3.zero; _radio = 0f; return; }

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            _centroLocal = transform.InverseTransformPoint(b.center);
            _radio       = Mathf.Max(b.extents.x, b.extents.z);
        }

        // ── Spawn ─────────────────────────────────────────────────────────────

        // Crea el libro colgando del anchor de la imagen. Devuelve null si no corresponde
        // (fuera de partida) o si falta el prefab; ahí ARImageAnchor cae al marcador de
        // esferas de siempre.
        public static GameObject TrySpawn(Transform anchor)
        {
            // Fuera de partida (escáner, calibración) el visual del anchor sigue siendo el
            // marcador: el libro es una mecánica de gameplay, no una ayuda de escaneo.
            var net = NetworkManager.Instance;
            if (anchor == null || net == null || !net.InSession) return null;

            var prefab = Resources.Load<GameObject>(ResourceName);
            if (prefab == null)
            {
                Debug.LogWarning($"[LibroRitual] Falta Assets/Resources/{ResourceName}.prefab — corré " +
                                 "el menú 'Mortuorium > Crear prefab del Libro Ritual'. Mientras tanto " +
                                 "el anchor muestra las esferas.");
                return null;
            }

            // Antes de instanciar: la vista lee la apertura del director en su OnEnable.
            RitualBookDirector.Ensure();

            // false = conserva la pose LOCAL del prefab: queda pegado al anchor.
            var go = Instantiate<GameObject>(prefab, anchor, false);
            go.name = "LibroRitual";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            return go;
        }

#if UNITY_EDITOR
        // Deja el componente listo de una. Lo llama el menú que arma el prefab, con el
        // modelo recién importado (o sea, con el libro abierto).
        public void EditorConfigurar(Transform tapaA, Transform tapaB, Transform lomo,
                                     Vector3 ejeBisagra, float cierreA, float cierreB)
        {
            _tapaA = tapaA; _tapaB = tapaB; _lomo = lomo;
            _ejeBisagra  = ejeBisagra;
            _cierreTapaA = cierreA;
            _cierreTapaB = cierreB;
            CapturarReposo();
        }

        // Ayuda de autoría: mover este slider en el inspector abre/cierra el libro en la
        // escena para ajustar el eje y los ángulos. Compilado fuera de TODO build.
        [Header("Sólo editor")]
        [Tooltip("Preview de la apertura para ajustar eje y ángulos. No existe en el build.")]
        [Range(0f, 1f)] [SerializeField] private float _aperturaPreview = 1f;

        private void OnValidate()
        {
            if (!_reposoCapturado) return;
            Aplicar(_aperturaPreview);
        }
#endif
    }
}
