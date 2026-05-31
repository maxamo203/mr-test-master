using UnityEngine;

namespace Scanner
{
    public class CubeBuilder : MonoBehaviour
    {
        private ScanStateMachine _fsm;

        private void Awake() => _fsm = ScanStateMachine.Instance;

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
