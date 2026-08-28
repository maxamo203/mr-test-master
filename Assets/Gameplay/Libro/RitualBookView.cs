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

        [Header("Humo de oscuridad (particulas)")]
        [Tooltip("Color de las particulas negras que se acumulan sobre el libro mientras se lo consume.")]
        [SerializeField] private Color _colorHumo = new Color(0.01f, 0.01f, 0.015f, 1f);
        [Tooltip("Particulas por segundo emitidas cuando la oscuridad esta al 100%. Junto con " +
                 "Vida en segundos determina cuantas conviven a la vez (densidad = tasa x vida).")]
        [SerializeField] private float _humoTasaMaxima = 160f;
        [Tooltip("Tope de particulas vivas a la vez. Subilo si la densidad se 'corta' al ser " +
                 "alta (tasa x vida cerca del limite).")]
        [SerializeField] private int _humoParticulasMaximas = 600;
        [SerializeField] private float _humoTamanoMin = 0.07f;
        [SerializeField] private float _humoTamanoMax = 0.2f;
        [Tooltip("Opacidad pico de CADA particula. Se mantiene baja para que se fundan " +
                 "entre si (muchas superpuestas) en vez de leerse como circulos sueltos.")]
        [Range(0f, 1f)] [SerializeField] private float _humoOpacidad = 0.6f;
        [Tooltip("Difuminado del borde de cada particula. Mas alto = mas fusionado con las " +
                 "vecinas, menos notorio que son circulos independientes.")]
        [Range(0f, 1f)] [SerializeField] private float _humoSuavidad = 0.9f;
        [Tooltip("Cuanto vive cada particula (s). MAS = mas densidad acumulada (a igual tasa) " +
                 "y mas recorrido antes de caer/desvanecerse.")]
        [SerializeField] private float _humoVidaSegundos = 1.8f;
        [Tooltip("Velocidad de salida horizontal, hacia afuera desde el borde de la mancha " +
                 "oscura (como chispas/ceniza saliendo por el costado).")]
        [SerializeField] private float _humoVelocidadSalida = 0.08f;
        [Tooltip("Gravedad que las hace caer despues de salir disparadas horizontalmente. " +
                 "Chica a proposito: es una escala de libro, no una explosion.")]
        [SerializeField] private float _humoGravedad = 0.03f;

        private readonly List<Renderer> _capasOscuridad = new();
        private readonly List<Renderer> _renderersLibro = new();
        private Material _materialOscuridad;
        private MaterialPropertyBlock _props;
        private ParticleSystem _humo;
        private Material _materialHumo;
        private float _ultimaSuavidadHumo = -1f;
        private Color _ultimoColorGradHumo;
        private Vector3 _centroLocal;
        private Vector2 _medioTamanoLocal;
        private float _radioLuz;
        private float _radioOscuridad;
        private float _oscuridad01;

        public Vector3 PuntoDeLuz => transform.TransformPoint(_centroLocal);
        public float RadioAproximado => _radioLuz;
        public float Oscuridad01 => _oscuridad01;
        public bool Disponible { get; private set; } = true;

        private void Awake()
        {
            MedirGeometria();
            CrearCapasDeOscuridad();
            CrearHumoOscuridad();
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
            if (_materialHumo != null) Destroy(_materialHumo);
            if (_humo != null) Destroy(_humo.gameObject);
        }

        private void LateUpdate()
        {
            // El anchor puede corregir su pose en cualquier frame. Los parametros estan
            // en mundo, por eso se refrescan para que el circulo siga pegado al libro.
            ActualizarShader();
#if UNITY_EDITOR
            // Tuning en vivo del humo (densidad, vida, textura, color): SOLO en el editor,
            // asi se puede arrastrar los sliders en Play y ver el cambio sin recompilar.
            // En cualquier build (dev o prod) estos valores se fijan una sola vez, sin costo
            // por frame — ver PushParamsHumo/CrearHumoOscuridad.
            PushParamsHumo();
#endif
        }

        // `silencioso` = aplicar el valor sin interpretar la transición como un evento de
        // juego. Lo usan el arranque y el reinicio de noche, que bajan la oscuridad a 0 de
        // golpe: sin esto, empezar una noche después de haber perdido el libro sonaría
        // exactamente igual que salvarlo.
        public void AplicarOscuridad(float oscuridad01, bool silencioso = false)
        {
            float antes = _oscuridad01;
            _oscuridad01 = Mathf.Clamp01(oscuridad01);

            if (silencioso)
            {
                AudioManager.PararBucle(BucleDefensa);
                _defendiendoHasta = 0f;
            }
            else
            {
                SonarSegunOscuridad(antes, _oscuridad01);
            }

            SetDisponible(_oscuridad01 < 1f - 1e-5f);
            ActualizarShader();
        }

        // Los eventos del libro (empieza / salvado / perdido) son flags de
        // RitualBookTickResult que MUEREN en el host: por red solo viaja el float de
        // oscuridad. Por eso el audio se deduce acá, de las transiciones de ese float —
        // este método corre en TODOS los peers, así que host y clientes oyen lo mismo
        // sin tener que tocar el protocolo.
        private void SonarSegunOscuridad(float antes, float ahora)
        {
            const float Cero = 1e-5f;
            const float Todo = 1f - 1e-5f;

            if (antes <= Cero && ahora > Cero)
                AudioManager.Sonar(c => c.libroAtaqueEmpieza, transform.position);
            else if (antes > Cero && ahora <= Cero)
                AudioManager.Sonar(c => c.libroSalvado, transform.position);

            // US-7: el libro se perdió. Es el sonido de alerta que invoca a Veleth.
            if (antes < Todo && ahora >= Todo)
            {
                AudioManager.PararBucle(BucleDefensa);
                AudioManager.Sonar(c => c.libroPerdido);
                return;
            }

            // Mientras la oscuridad retrocede es porque alguien lo está alumbrando. Se
            // sostiene con una ventana de gracia en vez de cortar en cuanto un tick trae el
            // mismo valor: en el cliente esto llega a ~5 Hz, y sin la gracia el loop
            // tartamudearía entre paquete y paquete.
            if (ahora > Cero && ahora < antes) _defendiendoHasta = Time.time + GraciaDefensa;

            if (ahora > Cero && Time.time < _defendiendoHasta)
                AudioManager.Bucle(BucleDefensa, c => c.libroDefendiendo, transform.position);
            else
                AudioManager.PararBucle(BucleDefensa);
        }

        private const string BucleDefensa   = "libro_defensa";
        private const float  GraciaDefensa  = 0.35f;
        private float _defendiendoHasta;

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

            ActualizarHumo(escala);
        }

        // Particulas negras que salen por el borde de la mancha oscura y crecen junto con la
        // oscuridad: mismo centro/radio que el shader radial (sqrt(oscuridad01), ver
        // comentario del shader), asi el borde de emision sigue al borde real de lo ennegrecido.
        // Nacen SOLO en el borde del disco (radiusThickness=0) con una direccion radial-en-el-
        // plano (comportamiento nativo de la forma Circle): salen horizontales hacia afuera, a
        // la altura del libro, y despues caen por gravedad — no directo hacia abajo ni hacia arriba.
        private void CrearHumoOscuridad()
        {
            var go = new GameObject("__HumoOscuridadLibro") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.transform.position = PuntoDeLuz;

            _humo = go.AddComponent<ParticleSystem>();
            _humo.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _humo.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(_colorHumo.r, _colorHumo.g, _colorHumo.b, 1f);

            var shape = _humo.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.01f;
            shape.radiusThickness = 0f; // solo el borde: "por los bordes del libro"
            shape.rotation = new Vector3(-90f, 0f, 0f); // disco horizontal => salida radial horizontal

            var noise = _humo.noise;
            noise.enabled = true;
            noise.frequency = 0.35f;

            var sol = _humo.sizeOverLifetime;
            sol.enabled = true;
            // Crece poco (no un globo): el leve aumento ayuda a que las particulas se
            // superpongan entre si y tapen los huecos, sin notarse como "burbujas" creciendo.
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.15f));

            var col = _humo.colorOverLifetime;
            col.enabled = true;

            var emission = _humo.emission;
            emission.rateOverTime = 0f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            _materialHumo = ArbmosGfx.ParticleMaterial(additive: false, tint: Color.white,
                                                       tex: ArbmosGfx.SmokeTexture(_humoSuavidad));
            _ultimaSuavidadHumo = _humoSuavidad;
            renderer.material = _materialHumo;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = 10;

            PushParamsHumo(); // fija densidad/vida/color una vez (en el editor ademas en vivo)
        }

        // Parametros "de autoria" (densidad, vida, salida, gravedad, color/opacidad, textura):
        // se fijan una vez aca. En builds (dev y prod) quedan asi de por vida — solo el editor
        // los vuelve a llamar cada frame (ver LateUpdate) para poder tunearlos en Play.
        private void PushParamsHumo()
        {
            if (_humo == null) return;

            var main = _humo.main;
            main.startLifetime = Mathf.Max(0.1f, _humoVidaSegundos);
            main.startSpeed = _humoVelocidadSalida;
            main.gravityModifier = _humoGravedad; // cae despues de la salida horizontal
            main.maxParticles = Mathf.Max(1, _humoParticulasMaximas);

            var noise = _humo.noise;
            noise.strength = 0.06f; // roce vivo sutil, sin tapar la trayectoria horizontal+caida

            // Textura de difuminado: solo se regenera si cambio (cachea por "bucket" en
            // ArbmosGfx, pero igual evitamos reasignar el material cada frame sin necesidad).
            if (_materialHumo != null && !Mathf.Approximately(_humoSuavidad, _ultimaSuavidadHumo))
            {
                _materialHumo.mainTexture = ArbmosGfx.SmokeTexture(_humoSuavidad);
                _ultimaSuavidadHumo = _humoSuavidad;
            }

            // El Gradient de color/opacidad asigna clase (GC): solo se reconstruye si cambio.
            var colorActual = new Color(_colorHumo.r, _colorHumo.g, _colorHumo.b, _humoOpacidad);
            if (_ultimoColorGradHumo.r != colorActual.r || _ultimoColorGradHumo.g != colorActual.g ||
                _ultimoColorGradHumo.b != colorActual.b || _ultimoColorGradHumo.a != colorActual.a)
            {
                var col = _humo.colorOverLifetime;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(_colorHumo, 0f), new GradientColorKey(_colorHumo, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(_humoOpacidad, 0.3f),
                            new GradientAlphaKey(0f, 1f) });
                col.color = grad;
                _ultimoColorGradHumo = colorActual;
            }
        }

        // Solo structs (sin GC): se llama cada LateUpdate igual que el shader radial, para
        // que la densidad de humo siga en vivo a _oscuridad01 (server-authoritative).
        private void ActualizarHumo(float escala)
        {
            if (_humo == null) return;

            bool activo = Disponible && _oscuridad01 > 0.001f;

            var emission = _humo.emission;
            emission.rateOverTime = activo ? _humoTasaMaxima * _oscuridad01 : 0f;

            var main = _humo.main;
            float tamano = Mathf.Lerp(_humoTamanoMin, _humoTamanoMax, _oscuridad01);
            main.startSize = new ParticleSystem.MinMaxCurve(tamano * 0.7f, tamano);

            var shape = _humo.shape;
            shape.radius = Mathf.Max(0.01f, _radioLuz * escala * EscalaParaCobertura(_oscuridad01));

            _humo.transform.position = PuntoDeLuz;

            if (activo && !_humo.isPlaying) _humo.Play(true);
            else if (!activo && _humo.isPlaying) _humo.Stop(true, ParticleSystemStopBehavior.StopEmitting);
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
