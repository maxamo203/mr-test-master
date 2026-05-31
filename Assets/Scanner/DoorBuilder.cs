using UnityEngine;

namespace Scanner
{
    // Coloca puertas (huecos rectangulares) sobre una pared seleccionada.
    // Flujo: DoorPickWall -> tap sobre WallObject -> Door_V1 -> Place -> Door_V2 -> Place -> Idle.
    public class DoorBuilder : MonoBehaviour
    {
        private WallObject _target;
        private Vector2? _uv1;
        private ScanStateMachine _fsm;

        private void Awake() => _fsm = ScanStateMachine.Instance;

        public void StartDoor()
        {
            _target = null;
            _uv1    = null;
            _fsm.SetMode(ScannerMode.DoorPickWall);
        }

        // Llamado por SelectionController cuando estamos en DoorPickWall y se hace tap a una pared.
        public void OnWallPicked(WallObject wall)
        {
            if (_fsm.Current != ScannerMode.DoorPickWall) return;
            _target = wall;
            _uv1    = null;
            _fsm.SetMode(ScannerMode.Door_V1);
        }

        // Llamado por el boton "Colocar" cuando el FSM esta en Door_V1/Door_V2.
        public void PlaceCornerAtCurrentReticle()
        {
            if (_target == null) return;
            var hit = RaycastResolver.Instance?.ResolveFromScreenCenter() ?? ResolvedHit.Miss;
            if (!hit.Hit) return;
            if (!_target.WorldPointToWallUV(hit.Position, out var u, out var v)) return;

            if (_fsm.Current == ScannerMode.Door_V1)
            {
                _uv1 = new Vector2(u, v);
                _fsm.SetMode(ScannerMode.Door_V2);
                return;
            }

            if (_fsm.Current == ScannerMode.Door_V2 && _uv1.HasValue)
            {
                float uMin = Mathf.Min(_uv1.Value.x, u);
                float uMax = Mathf.Max(_uv1.Value.x, u);
                float vMin = Mathf.Min(_uv1.Value.y, v);
                float vMax = Mathf.Max(_uv1.Value.y, v);

                // Clamp a los limites de la pared.
                uMin = Mathf.Clamp(uMin, 0f, _target.Length);
                uMax = Mathf.Clamp(uMax, 0f, _target.Length);
                vMin = Mathf.Clamp(vMin, 0f, _target.Height);
                vMax = Mathf.Clamp(vMax, 0f, _target.Height);

                // El usuario pidio que la puerta inicie siempre desde el piso (base de la pared).
                vMin = 0f;

                if (uMax - uMin > 0.05f && vMax - vMin > 0.05f)
                {
                    _target.AddDoor(new DoorData
                    {
                        id   = System.Guid.NewGuid().ToString("N").Substring(0, 8),
                        uMin = uMin, uMax = uMax,
                        vMin = vMin, vMax = vMax,
                    });
                }

                _target = null;
                _uv1    = null;
                _fsm.SetMode(ScannerMode.Idle);
            }
        }
    }
}
