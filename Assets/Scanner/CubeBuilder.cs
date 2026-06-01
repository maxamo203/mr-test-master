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

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            ConfiguredNormalMat   = _cubeMaterial;
            ConfiguredSelectedMat = _cubeSelectedMaterial;
        }

        public void StartCube()
        {
            _fsm.SetMode(ScannerMode.Cube_Place);
        }

        public void PlaceCubeAtCurrentReticle()
        {
            if (_fsm.Current != ScannerMode.Cube_Place) return;
            if (WorldOrigin.Instance == null)
            {
                Debug.LogWarning("[CubeBuilder] WorldOrigin aun no esta listo. Calibrar primero.");
                return;
            }
            var hit = RaycastResolver.Instance?.ResolveFromScreenCenter() ?? ResolvedHit.Miss;
            if (!hit.Hit) return;

            var posLocal = WorldOrigin.Instance.ToRelative(hit.Position);
            CubeObject.Create(posLocal, Quaternion.identity, Vector3.one * CubeObject.DefaultSize);
            _fsm.SetMode(ScannerMode.Idle);
        }
    }
}
