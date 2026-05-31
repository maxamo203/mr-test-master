using UnityEngine;

namespace Scanner
{
    // Maneja el flujo de colocacion de paredes con polilinea + height-anchor.
    //
    // Flujo nuevo:
    //   1) Wall_V1     -> Place: marca primer punto de piso (V1) y guarda como ultimo.
    //   2) Wall_Height -> Place: marca un punto "arriba" para definir H = max(0.1, Vy - V1y).
    //   3) Wall_Vn     -> Place: marca proximo punto de piso (Vn). Crea pared
    //                    de Vlast -> Vn con altura H. Vlast = Vn.
    //
    // "Terminar polilinea" corta la cadena (vuelve a Idle).
    public class WallBuilder : MonoBehaviour
    {
        [Header("Defaults")]
        [Tooltip("Altura inicial sugerida (m). Se sobreescribe con el segundo vertice del polilinea.")]
        [SerializeField] private float _defaultHeight = 2.5f;

        [Header("Materiales (opcional, asignar desde el Inspector)")]
        [Tooltip("Material aplicado a las paredes en estado normal. Si esta vacio se crea uno semitransparente en runtime.")]
        [SerializeField] private Material _wallMaterial;
        [Tooltip("Material aplicado cuando la pared esta seleccionada. Opcional.")]
        [SerializeField] private Material _wallSelectedMaterial;

        // Statics que WallObject lee al crear/cargar paredes.
        public static Material ConfiguredNormalMat   { get; private set; }
        public static Material ConfiguredSelectedMat { get; private set; }

        [Header("Preview de polilinea")]
        [Tooltip("Radio (m) de las esferas que se muestran en V1 (piso) y V2 (techo) durante la polilinea.")]
        [SerializeField] private float _previewSphereRadius = 0.05f;
        [Tooltip("Color de la esfera de V1 (piso).")]
        [SerializeField] private Color _previewFloorColor   = new Color(0.2f, 1f, 0.4f, 1f);
        [Tooltip("Color de la esfera de V2 (techo).")]
        [SerializeField] private Color _previewCeilingColor = new Color(1f, 0.6f, 0.2f, 1f);

        private Vector3? _lastFloorLocal;
        private Vector3? _firstFloorLocal;
        private float    _polylineHeight;
        private ScanStateMachine _fsm;
        private readonly System.Collections.Generic.List<GameObject> _previewSpheres = new();

        public float CurrentPolylineHeight => _polylineHeight;

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            ConfiguredNormalMat   = _wallMaterial;
            ConfiguredSelectedMat = _wallSelectedMaterial;
        }

        public void StartPolyline()
        {
            _firstFloorLocal = null;
            _lastFloorLocal  = null;
            _polylineHeight  = _defaultHeight;
            ClearPreviewSpheres();
            _fsm.SetMode(ScannerMode.Wall_V1);
        }

        public void EndPolyline()
        {
            _firstFloorLocal = null;
            _lastFloorLocal  = null;
            ClearPreviewSpheres();
            if (_fsm.Current == ScannerMode.Wall_V1
             || _fsm.Current == ScannerMode.Wall_Height
             || _fsm.Current == ScannerMode.Wall_Vn)
                _fsm.SetMode(ScannerMode.Idle);
        }

        private void SpawnPreviewSphere(Vector3 anchorLocal, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "PolylinePreviewSphere";
            Destroy(go.GetComponent<Collider>()); // sin collider: no interfiere con seleccion
            go.transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            go.transform.localPosition = anchorLocal;
            go.transform.localScale    = Vector3.one * (_previewSphereRadius * 2f);

            var sh  = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var mat = new Material(sh) { name = "PreviewSphereMat (runtime)" };
            if (mat.HasProperty("_Color"))     mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            _previewSpheres.Add(go);
        }

        private void ClearPreviewSpheres()
        {
            foreach (var s in _previewSpheres)
            {
                if (s == null) continue;
                var mr = s.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null) Destroy(mr.sharedMaterial);
                Destroy(s);
            }
            _previewSpheres.Clear();
        }

        public void PlaceVertexAtCurrentReticle()
        {
            if (WorldOrigin.Instance == null)
            {
                Debug.LogWarning("[WallBuilder] WorldOrigin aun no esta listo. Calibrar primero.");
                return;
            }
            var hit = RaycastResolver.Instance?.ResolveFromScreenCenter() ?? ResolvedHit.Miss;
            if (!hit.Hit) return;
            PlaceVertex(WorldOrigin.Instance.ToRelative(hit.Position));
        }

        public void PlaceVertex(Vector3 anchorLocal)
        {
            switch (_fsm.Current)
            {
                case ScannerMode.Wall_V1:
                {
                    _firstFloorLocal = anchorLocal;
                    _lastFloorLocal  = anchorLocal;
                    SpawnPreviewSphere(anchorLocal, _previewFloorColor);
                    _fsm.SetMode(ScannerMode.Wall_Height);
                    return;
                }
                case ScannerMode.Wall_Height:
                {
                    // El segundo vertice marca la altura. Usamos el delta vertical
                    // (eje Y del anchor) — robusto a que el usuario no apunte
                    // perfectamente arriba.
                    float h = Mathf.Abs(anchorLocal.y - (_firstFloorLocal?.y ?? anchorLocal.y));
                    _polylineHeight = Mathf.Max(0.1f, h);
                    SpawnPreviewSphere(anchorLocal, _previewCeilingColor);
                    _fsm.SetMode(ScannerMode.Wall_Vn);
                    return;
                }
                case ScannerMode.Wall_Vn:
                {
                    if (_lastFloorLocal.HasValue)
                    {
                        WallObject.Create(_lastFloorLocal.Value, anchorLocal, _polylineHeight);
                        SpawnPreviewSphere(anchorLocal, _previewFloorColor);
                        _lastFloorLocal = anchorLocal;
                    }
                    return;
                }
            }
        }

        private void OnDestroy() => ClearPreviewSpheres();
    }
}
