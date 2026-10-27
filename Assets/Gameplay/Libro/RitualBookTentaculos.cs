using UnityEngine;

namespace Gameplay
{
    // Tentáculos del LIBRO RITUAL: zarcillos negro puro, cortos y gruesos, que salen desde el borde del libro a medida que la oscuridad lo
    // consume (de a uno, cada uno con su umbral), ondulan y se enroscan en la punta. Cuando lo
    // alumbran (la oscuridad BAJA) se retraen rápido. Con el libro consumido del todo se
    // quedan afuera, también mientras sale Veleth y después. Se dibujan con LineRenderers en el espacio local del libro, así siguen a su
    // animación (RitualBookAnimacion). Sólo se lee RitualBookView.Oscuridad01, que llega a
    // todos los peers (el estado del libro no viaja por red, sólo ese float), así que los ven
    // igual el host y los clientes.
    //
    // Costo: Cantidad líneas de Puntos vértices, recalculadas sólo mientras
    // hay algún tentáculo afuera; con el libro sano no hacen nada.
    public class RitualBookTentaculos : MonoBehaviour
    {
        [SerializeField] private int _cantidad = 7;
        [Tooltip("Largo máximo de un tentáculo (m), con la oscuridad al 100%.")]
        [SerializeField] private float _largoMaximo = 0.16f;
        [Tooltip("Grosor en la base (m).")]
        [SerializeField] private float _grosor = 0.055f;
        [Tooltip("Qué tan rápido crecen (fracción por segundo).")]
        [SerializeField] private float _velCrecer = 0.5f;
        [Tooltip("Qué tan rápido se retraen al alumbrar el libro (fracción por segundo).")]
        [SerializeField] private float _velRetraer = 2.5f;
        [SerializeField] private Color _color = new(0f, 0f, 0f, 0.97f);

        private const int Puntos = 16;

        private struct Tentaculo
        {
            public LineRenderer cuerpo;
            public Vector3 ancla, afuera, costado;
            public float semilla, largo, umbral, crecido;
        }

        private Tentaculo[] _t;
        private readonly Vector3[] _pts = new Vector3[Puntos];
        private RitualBookView _vista;
        private Material _mat;
        private Texture2D _tex;
        private float _oscuridadAnterior;

        private void Start()
        {
            _vista = GetComponent<RitualBookView>();
            Crear();
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            if (_tex != null) Destroy(_tex);
        }

        private void Crear()
        {
            // Bordes del libro en su espacio local (las raíces de los tentáculos), medidos con
            // las mallas del libro (no las capas de oscuridad, que se llaman "__…").
            Bounds b = default;
            bool hay = false;
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.gameObject.name.StartsWith("__")) continue;
                var w = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = transform.InverseTransformPoint(w.center + Vector3.Scale(w.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));
                    if (!hay) { b = new Bounds(p, Vector3.zero); hay = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!hay) b = new Bounds(Vector3.zero, new Vector3(0.3f, 0.05f, 0.25f));

            _mat = MaterialLinea();
            // Negro puro de punta a punta; sólo la punta se desvanece un poco.
            var cuerpo = new Gradient();
            cuerpo.SetKeys(
                new[] { new GradientColorKey(_color, 0f), new GradientColorKey(_color, 1f) },
                new[] { new GradientAlphaKey(_color.a, 0f), new GradientAlphaKey(_color.a, 0.75f),
                        new GradientAlphaKey(0.3f, 1f) });
            var afinar = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.55f), new Keyframe(1f, 0.05f));

            int n = Mathf.Max(1, _cantidad);
            _t = new Tentaculo[n];
            for (int i = 0; i < n; i++)
            {
                float ang = (i + Random.Range(-0.3f, 0.3f)) / n * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var ancla = new Vector3(b.center.x + b.extents.x * 0.85f * dir.x, b.max.y - 0.005f,
                                        b.center.z + b.extents.z * 0.85f * dir.z);
                _t[i] = new Tentaculo
                {
                    cuerpo = Linea($"__Tentaculo{i}", cuerpo, afinar, _grosor),
                    ancla = ancla,
                    afuera = dir,
                    costado = Vector3.Cross(Vector3.up, dir),
                    semilla = Random.Range(0f, 100f),
                    largo = Random.Range(0.7f, 1.2f),
                    // Umbrales repartidos: con poca oscuridad salen pocos, con mucha, todos.
                    umbral = (i + Random.Range(0f, 0.8f)) / n * 0.55f,
                };
            }
        }

        private LineRenderer Linea(string nombre, Gradient color, AnimationCurve ancho, float grosor)
        {
            var go = new GameObject(nombre) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = Puntos;
            lr.colorGradient = color;
            lr.widthCurve = ancho;
            lr.widthMultiplier = grosor;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.sharedMaterial = _mat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        private void LateUpdate()
        {
            if (_t == null) return;
            float oscuridad = _vista != null ? _vista.Oscuridad01 : 0f;
            // Consumido del todo: el libro se esconde cuando sale Veleth, pero los tentáculos
            // se quedan (pedido de diseño). Sólo se van cuando la oscuridad baja.
            // La oscuridad baja sólo cuando lo están alumbrando (o al reiniciar): se retraen.
            bool retrayendo = oscuridad < _oscuridadAnterior - 1e-4f;
            _oscuridadAnterior = oscuridad;
            float t = Time.time, dt = Time.deltaTime;

            for (int i = 0; i < _t.Length; i++)
            {
                ref var tt = ref _t[i];
                float objetivo = Mathf.Clamp01((oscuridad - tt.umbral) / Mathf.Max(0.05f, 1f - tt.umbral));
                if (retrayendo) objetivo *= 0.3f;
                float vel = objetivo < tt.crecido ? _velRetraer : _velCrecer;
                tt.crecido = Mathf.MoveTowards(tt.crecido, objetivo, vel * dt);

                bool afuera = tt.crecido > 0.01f;
                tt.cuerpo.enabled = afuera;
                if (!afuera) continue;

                float largo = _largoMaximo * tt.largo * Mathf.SmoothStep(0f, 1f, tt.crecido);
                for (int k = 0; k < Puntos; k++)
                {
                    float s = k / (float)(Puntos - 1);
                    float a = s * largo;
                    // Sube y se abre hacia afuera; la punta se enrosca hacia afuera y abajo.
                    float rulo = s * s * s * largo * 0.45f;
                    // Ondas proporcionales al largo: cortos y gruesos, se mecen sin culebrear.
                    float onda = Mathf.Sin(t * 1.7f + tt.semilla + s * 5f) * 0.2f * largo * s;
                    float onda2 = Mathf.Sin(t * 1.1f + tt.semilla * 2f + s * 3.3f) * 0.13f * largo * s;
                    _pts[k] = tt.ancla
                            + tt.afuera * (a * 0.5f + rulo + onda2)
                            + Vector3.up * (a * 0.85f - rulo * 0.6f)
                            + tt.costado * onda;
                }
                tt.cuerpo.SetPositions(_pts);
                tt.cuerpo.widthMultiplier = _grosor * Mathf.Lerp(0.4f, 1f, tt.crecido);
            }
        }

        // Material de partículas (el mismo que usa el humo) con una textura blanda a lo ancho:
        // la línea se ve como un zarcillo y no como una cinta de bordes duros. El color sale del
        // gradiente de cada línea (color de vértice).
        private Material MaterialLinea()
        {
            const int alto = 32;
            _tex = new Texture2D(2, alto, TextureFormat.RGBA32, false)
            {
                name = "TentaculoLibro", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[2 * alto];
            for (int y = 0; y < alto; y++)
            {
                float v = (y + 0.5f) / alto * 2f - 1f;            // -1..1 a lo ancho
                byte a = (byte)(Mathf.Clamp01(1f - v * v * v * v) * 255f);
                px[y * 2] = px[y * 2 + 1] = new Color32(255, 255, 255, a);
            }
            _tex.SetPixels32(px);
            _tex.Apply(false, true);

            var m = ArbmosGfx.ParticleMaterial(additive: false, tint: Color.white, tex: _tex);
            m.name = "TentaculosLibro (runtime)";
            // Los shaders de partículas legacy multiplican por 2 × _TintColor: gris medio = neutro.
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return m;
        }
    }
}
