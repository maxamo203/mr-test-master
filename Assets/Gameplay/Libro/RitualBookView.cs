using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gameplay
{
    // Vista del libro anclado a la imagen de referencia. El modelo permanece abierto:
    // la amenaza se representa unicamente oscureciendo sus propias mallas desde el centro.
    // El porcentaje controla AREA cubierta: 50% logico equivale a media superficie negra.
    public class RitualBookView : MonoBehaviour
    {
        public const string ResourceName = "LibroRitual";
        private const string DarknessShaderResource = "RitualBookDarkness";

        public static RitualBookView Active { get; private set; }

        // Se conservan para que el prefab y su herramienta de autoria sigan siendo
        // compatibles, aunque las tapas ya no se animan al ocurrir la amenaza.
        [SerializeField, HideInInspector] private Transform _tapaA;
        [SerializeField, HideInInspector] private Transform _tapaB;
        [SerializeField, HideInInspector] private Transform _lomo;

        [Header("Oscuridad radial")]
        [SerializeField] private Color _colorOscuridad = Color.black;
        [Range(0f, 1f)] [SerializeField] private float _opacidadMaxima = 0.96f;
        [Tooltip("Ancho en metros del borde suave de la oscuridad.")]
        [Min(0.001f)] [SerializeField] private float _suavidadMetros = 0.018f;

        private readonly List<Renderer> _capasOscuridad = new();
        private readonly List<Renderer> _renderersLibro = new();
        private Material _materialOscuridad;
        private MaterialPropertyBlock _props;
        private Vector3 _centroLocal;
        private Vector2 _medioTamanoLocal;
        private float _radioLuz;
        private float _radioOscuridad;
        private float _oscuridad01;

        public Vector3 PuntoDeLuz => transform.TransformPoint(_centroLocal);
        public float RadioAproximado => _radioLuz;
        public bool Disponible { get; private set; } = true;

        private void Awake()
        {
            MedirGeometria();
            CrearCapasDeOscuridad();
        }

        private void OnEnable()
        {
            Active = this;
            AplicarOscuridad(RitualBookDirector.Instance != null
                ? RitualBookDirector.Instance.Oscuridad01
                : 0f);
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
        }

        private void OnDestroy()
        {
            if (_materialOscuridad != null) Destroy(_materialOscuridad);
        }

        private void LateUpdate()
        {
            // El anchor puede corregir su pose en cualquier frame. Los parametros estan
            // en mundo, por eso se refrescan para que el circulo siga pegado al libro.
            ActualizarShader();
        }

        public void AplicarOscuridad(float oscuridad01)
        {
            _oscuridad01 = Mathf.Clamp01(oscuridad01);
            SetDisponible(_oscuridad01 < 1f - 1e-5f);
            ActualizarShader();
        }

        public void SetDisponible(bool disponible)
        {
            Disponible = disponible;
            foreach (var renderer in _renderersLibro)
                if (renderer != null) renderer.enabled = disponible;
            foreach (var renderer in _capasOscuridad)
                if (renderer != null) renderer.enabled = disponible;
        }

        // API anterior mantenida para herramientas o escenas aun no reimportadas.
        public void Aplicar(float apertura01) => AplicarOscuridad(1f - apertura01);

        private void MedirGeometria()
        {
            var rends = GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0)
            {
                _centroLocal = Vector3.zero;
                _radioLuz = 0f;
                _radioOscuridad = 0.2f;
                _medioTamanoLocal = Vector2.one * 0.2f;
                return;
            }

            Bounds localBounds = default;
            bool first = true;
            foreach (var renderer in rends)
            {
                Bounds world = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = world.center + Vector3.Scale(world.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    Vector3 local = transform.InverseTransformPoint(point);
                    if (first) { localBounds = new Bounds(local, Vector3.zero); first = false; }
                    else localBounds.Encapsulate(local);
                }
            }

            _centroLocal = localBounds.center;
            _medioTamanoLocal = new Vector2(
                Mathf.Max(0.001f, localBounds.extents.x),
                Mathf.Max(0.001f, localBounds.extents.z));
            float escalaX = Mathf.Abs(transform.lossyScale.x);
            float escalaZ = Mathf.Abs(transform.lossyScale.z);
            _radioLuz = Mathf.Max(_medioTamanoLocal.x * escalaX,
                                  _medioTamanoLocal.y * escalaZ);
            _radioOscuridad = _medioTamanoLocal.magnitude;
        }

        private void CrearCapasDeOscuridad()
        {
            var shader = Resources.Load<Shader>(DarknessShaderResource);
            if (shader == null) shader = Shader.Find("AR/RitualBookDarkness");
            if (shader == null)
            {
                Debug.LogError("[LibroRitual] No se encontro el shader RitualBookDarkness.");
                return;
            }

            _materialOscuridad = new Material(shader)
            {
                name = "Oscuridad del libro (runtime)",
                hideFlags = HideFlags.DontSave,
            };
            _materialOscuridad.SetColor("_DarknessColor", _colorOscuridad);
            _props = new MaterialPropertyBlock();

            var filtros = GetComponentsInChildren<MeshFilter>(true);
            foreach (var filtro in filtros)
            {
                if (filtro.sharedMesh == null) continue;
                var original = filtro.GetComponent<MeshRenderer>();
                if (original == null) continue;
                if (!_renderersLibro.Contains(original)) _renderersLibro.Add(original);

                var capa = new GameObject("__OscuridadRitual")
                {
                    layer = original.gameObject.layer,
                    hideFlags = HideFlags.DontSave,
                };
                capa.transform.SetParent(filtro.transform, false);

                var copiaFiltro = capa.AddComponent<MeshFilter>();
                copiaFiltro.sharedMesh = filtro.sharedMesh;

                var copiaRenderer = capa.AddComponent<MeshRenderer>();
                int materiales = Mathf.Max(1, filtro.sharedMesh.subMeshCount);
                var mats = new Material[materiales];
                for (int i = 0; i < mats.Length; i++) mats[i] = _materialOscuridad;
                copiaRenderer.sharedMaterials = mats;
                copiaRenderer.shadowCastingMode = ShadowCastingMode.Off;
                copiaRenderer.receiveShadows = false;
                copiaRenderer.lightProbeUsage = LightProbeUsage.Off;
                copiaRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                copiaRenderer.sortingLayerID = original.sortingLayerID;
                copiaRenderer.sortingOrder = original.sortingOrder + 1;
                _capasOscuridad.Add(copiaRenderer);
            }

            ActualizarShader();
        }

        private void ActualizarShader()
        {
            if (_props == null || _capasOscuridad.Count == 0) return;

            float escala = Mathf.Max(Mathf.Abs(transform.lossyScale.x),
                                     Mathf.Max(Mathf.Abs(transform.lossyScale.y),
                                               Mathf.Abs(transform.lossyScale.z)));
            _props.SetFloat("_DarknessAmount", _oscuridad01);
            _props.SetFloat("_DarknessMaxRadius", _radioOscuridad * escala);
            _props.SetFloat("_DarknessSoftness", _suavidadMetros * escala);
            _props.SetFloat("_DarknessOpacity", _opacidadMaxima);
            _props.SetVector("_DarknessCenterWorld", PuntoDeLuz);
            _props.SetVector("_DarknessAxisXWorld", transform.right.normalized);
            _props.SetVector("_DarknessAxisZWorld", transform.forward.normalized);
            _props.SetVector("_DarknessHalfSizeWorld", new Vector4(
                _medioTamanoLocal.x * Mathf.Abs(transform.lossyScale.x),
                _medioTamanoLocal.y * Mathf.Abs(transform.lossyScale.z), 0f, 0f));

            foreach (var renderer in _capasOscuridad)
                if (renderer != null) renderer.SetPropertyBlock(_props);
        }

        public static GameObject TrySpawn(Transform anchor)
        {
            var net = NetworkManager.Instance;
            if (!PuedeAparecer(anchor != null, net != null && net.InSession)) return null;

            var prefab = Resources.Load<GameObject>(ResourceName);
            if (prefab == null)
            {
                Debug.LogWarning($"[LibroRitual] Falta Assets/Resources/{ResourceName}.prefab.");
                return null;
            }

            RitualBookDirector.Ensure();
            var go = Instantiate(prefab, anchor, false);
            go.name = "LibroRitual";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            return go;
        }

        // La vista nunca inventa una ubicacion: necesita simultaneamente una partida
        // activa y el ancla fisica que ARImageAnchor valido/reconocio.
        public static bool PuedeAparecer(bool hayAnclaValida, bool enSesion)
            => hayAnclaValida && enSesion;

        // Una region que crece en X y Z cubre escala^2 de la superficie. La raiz
        // cuadrada convierte el porcentaje de gameplay en porcentaje visual de area.
        public static float EscalaParaCobertura(float cobertura01)
            => Mathf.Sqrt(Mathf.Clamp01(cobertura01));

#if UNITY_EDITOR
        public void EditorConfigurar(Transform tapaA, Transform tapaB, Transform lomo,
                                     Vector3 ejeBisagra, float cierreA, float cierreB)
        {
            _tapaA = tapaA;
            _tapaB = tapaB;
            _lomo = lomo;
        }

        [Header("Solo editor")]
        [Range(0f, 1f)] [SerializeField] private float _oscuridadPreview;

        private void OnValidate()
        {
            if (Application.isPlaying) AplicarOscuridad(_oscuridadPreview);
        }
#endif
    }
}
