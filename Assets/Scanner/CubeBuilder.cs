using UnityEngine;

namespace Scanner
{
    public class CubeBuilder : MonoBehaviour
    {
        [Header("Materiales (opcional, asignar desde el Inspector)")]
        [Tooltip("Material para cubos en estado normal. Si esta vacio se crea uno azul en runtime.")]
        [SerializeField] private Material _cubeMaterial;
        [Tooltip("Material para cubo seleccionado. Si esta vacio se genera tintado en runtime.")]
        [SerializeField] private Material _cubeSelectedMaterial;

        public static Material ConfiguredNormalMat   { get; private set; }
        public static Material ConfiguredSelectedMat { get; private set; }

        private ScanStateMachine _fsm;
        private Vector3?         _firstCornerLocal;
        private GameObject       _firstCornerPreview;

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            ConfiguredNormalMat   = _cubeMaterial;
            ConfiguredSelectedMat = _cubeSelectedMaterial;
        }

        private void OnEnable()
        {
            if (_fsm == null) _fsm = ScanStateMachine.Instance;
            if (_fsm != null) _fsm.OnModeChanged += OnModeChanged;
        }

        private void OnDisable()
        {
            if (_fsm != null) _fsm.OnModeChanged -= OnModeChanged;
        }

        // Nuevo flujo: el cubo se define con los dos vertices de su diagonal.
        public void StartCube()
        {
            _firstCornerLocal = null;
            ClearPreview();
            _fsm.SetMode(ScannerMode.Cube_V1);
        }

        public void PlaceCubeVertexAtCurrentReticle()
        {
            if (WorldOrigin.Instance == null)
            {
                Debug.LogWarning("[CubeBuilder] WorldOrigin aun no esta listo. Calibrar primero.");
                return;
            }
            var hit = RaycastResolver.Instance?.ResolveFromScreenCenter() ?? ResolvedHit.Miss;
            if (!hit.Hit) return;

            var local = WorldOrigin.Instance.ToRelative(hit.Position);

            switch (_fsm.Current)
            {
                case ScannerMode.Cube_V1:
                    _firstCornerLocal = local;
                    SpawnPreview(local);
                    _fsm.SetMode(ScannerMode.Cube_V2);
                    break;

                case ScannerMode.Cube_V2:
                    if (_firstCornerLocal.HasValue)
                    {
                        CubeObject.CreateFromDiagonal(_firstCornerLocal.Value, local);
                        _firstCornerLocal = null;
                        ClearPreview();
                        _fsm.SetMode(ScannerMode.Idle);
                    }
                    break;
            }
        }

        // Si salimos del flujo de cubo sin completarlo (ej. Cancelar), limpiamos.
        private void OnModeChanged(ScannerMode prev, ScannerMode next)
        {
            if (next != ScannerMode.Cube_V1 && next != ScannerMode.Cube_V2)
            {
                _firstCornerLocal = null;
                ClearPreview();
            }
        }

        private void SpawnPreview(Vector3 anchorLocal)
        {
            ClearPreview();
            _firstCornerPreview = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _firstCornerPreview.name = "CubeFirstCornerPreview";
            Destroy(_firstCornerPreview.GetComponent<Collider>());
            _firstCornerPreview.transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            _firstCornerPreview.transform.localPosition = anchorLocal;
            _firstCornerPreview.transform.localScale    = Vector3.one * 0.06f;

            var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var mat = new Material(sh) { name = "CubePreviewMat (runtime)" };
            var col = new Color(1f, 0.55f, 0.1f, 1f);
            if (mat.HasProperty("_Color"))     mat.color = col;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            _firstCornerPreview.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private void ClearPreview()
        {
            if (_firstCornerPreview != null) Destroy(_firstCornerPreview);
            _firstCornerPreview = null;
        }

        private void OnDestroy() => ClearPreview();
    }
}
