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
        private Vector3?         _secondCornerLocal;
        private GameObject       _firstCornerPreview;
        private GameObject       _secondCornerPreview;

        // Estado en-progreso, leido por PlacementPreview para el fantasma en vivo.
        public Vector3? FirstCorner  => _firstCornerLocal;
        public Vector3? SecondCorner => _secondCornerLocal;

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

        // Flujo: el cubo se define con los dos vertices de su diagonal (esquinas
        // opuestas) + un tercer punto de referencia para la rotacion horizontal.
        public void StartCube()
        {
            _firstCornerLocal  = null;
            _secondCornerLocal = null;
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
                    _firstCornerPreview = SpawnPreview(local, _firstCornerPreview);
                    _fsm.SetMode(ScannerMode.Cube_V2);
                    break;

                case ScannerMode.Cube_V2:
                    _secondCornerLocal = local;
                    _secondCornerPreview = SpawnPreview(local, _secondCornerPreview);
                    _fsm.SetMode(ScannerMode.Cube_V3);
                    break;

                case ScannerMode.Cube_V3:
                    if (_firstCornerLocal.HasValue && _secondCornerLocal.HasValue)
                    {
                        CubeObject.CreateFromThreePoints(
                            _firstCornerLocal.Value, _secondCornerLocal.Value, local);
                        ResetCube();
                        _fsm.SetMode(ScannerMode.Idle);
                    }
                    break;
            }
        }

        // Cierra el cubo sin tercer punto: queda axis-aligned (rotacion identidad),
        // usando solo las dos esquinas de la diagonal. Disponible en Cube_V3.
        public void ConfirmCubeAxisAligned()
        {
            if (_fsm.Current != ScannerMode.Cube_V3) return;
            if (_firstCornerLocal.HasValue && _secondCornerLocal.HasValue)
            {
                CubeObject.CreateFromDiagonal(_firstCornerLocal.Value, _secondCornerLocal.Value);
                ResetCube();
                _fsm.SetMode(ScannerMode.Idle);
            }
        }

        private void ResetCube()
        {
            _firstCornerLocal  = null;
            _secondCornerLocal = null;
            ClearPreview();
        }

        // Si salimos del flujo de cubo sin completarlo (ej. Cancelar), limpiamos.
        private void OnModeChanged(ScannerMode prev, ScannerMode next)
        {
            if (next != ScannerMode.Cube_V1 && next != ScannerMode.Cube_V2 && next != ScannerMode.Cube_V3)
                ResetCube();
        }

        // Marca una esquina confirmada con una esferita. Reutiliza el GO existente
        // si se pasa (reposiciona) y lo devuelve.
        private GameObject SpawnPreview(Vector3 anchorLocal, GameObject existing)
        {
            if (existing != null) Destroy(existing);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "CubeCornerPreview";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            go.transform.localPosition = anchorLocal;
            go.transform.localScale    = Vector3.one * 0.06f;

            var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var mat = new Material(sh) { name = "CubePreviewMat (runtime)" };
            var col = new Color(1f, 0.55f, 0.1f, 1f);
            if (mat.HasProperty("_Color"))     mat.color = col;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private void ClearPreview()
        {
            if (_firstCornerPreview  != null) Destroy(_firstCornerPreview);
            if (_secondCornerPreview != null) Destroy(_secondCornerPreview);
            _firstCornerPreview  = null;
            _secondCornerPreview = null;
        }

        private void OnDestroy() => ClearPreview();
    }
}
