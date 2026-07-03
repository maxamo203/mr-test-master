using UnityEngine;

namespace Scanner
{
    // Coloca puntos de spawn de Sorken (piramide aplanada con la punta segun la
    // normal). Dos flujos:
    //
    //   - Pared (escaneo manual): SpawnPickWall -> tap sobre WallObject ->
    //     Spawn_Place -> COLOCAR. La punta toma la normal de la pared (el lado
    //     que mira hacia el jugador, o sea hacia adentro del cuarto).
    //   - Superficie (mapeo LiDAR o libre): Spawn_Place directo, sin pared.
    //     La punta toma la normal del hit del RaycastResolver (con LiDAR es la
    //     normal fisica real de la superficie apuntada).
    public class SpawnBuilder : MonoBehaviour
    {
        private WallObject _targetWall;
        private ScanStateMachine _fsm;
        private Camera _camera;

        // Pared elegida (null en flujo libre). Lo lee PlacementPreview.
        public WallObject TargetWall => _targetWall;

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            _camera = Camera.main;
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

        // Flujo manual: primero elegir la pared de la que se toma la normal.
        public void StartOnWall()
        {
            _targetWall = null;
            _fsm.SetMode(ScannerMode.SpawnPickWall);
        }

        // Flujo libre / LiDAR: la normal sale del hit de la reticula.
        public void StartFree()
        {
            _targetWall = null;
            _fsm.SetMode(ScannerMode.Spawn_Place);
        }

        // Llamado por SelectionController cuando estamos en SpawnPickWall y se
        // hace tap a una pared (mismo patron que DoorBuilder.OnWallPicked).
        public void OnWallPicked(WallObject wall)
        {
            if (_fsm.Current != ScannerMode.SpawnPickWall) return;
            _targetWall = wall;
            _fsm.SetMode(ScannerMode.Spawn_Place);
        }

        // Pose (anchor-local) que tendria el spawn si se colocara ahora en hit.
        // Compartido con PlacementPreview para el fantasma en vivo.
        public bool TryGetSpawnPose(ResolvedHit hit, out Vector3 posLocal, out Quaternion rotLocal)
        {
            posLocal = Vector3.zero;
            rotLocal = Quaternion.identity;
            var wo = WorldOrigin.Instance;
            if (wo == null || !wo.IsReady || !hit.Hit) return false;

            posLocal = wo.ToRelative(hit.Position);
            if (_camera == null) _camera = Camera.main;
            var camLocal = _camera != null ? wo.ToRelative(_camera.transform.position)
                                           : posLocal + Vector3.forward;

            Vector3 nLocal;
            if (_targetWall != null)
            {
                // Normal de la pared, con el signo que mira hacia el jugador
                // (la punta sale de la pared hacia adentro del cuarto).
                nLocal = _targetWall.Normal;
                if (Vector3.Dot(nLocal, camLocal - posLocal) < 0f) nLocal = -nLocal;
            }
            else
            {
                nLocal = wo.ToRelativeDir(hit.Normal);
                if (nLocal.sqrMagnitude < 1e-6f) nLocal = Vector3.up;
                nLocal.Normalize();
                // Que la punta siempre salga hacia el lado del jugador.
                if (Vector3.Dot(nLocal, camLocal - posLocal) < 0f) nLocal = -nLocal;
            }

            // LookRotation degenera si la normal es (casi) vertical (piso/techo):
            // usamos la direccion horizontal hacia la camara como up-hint.
            Vector3 upHint = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(nLocal, Vector3.up)) > 0.98f)
            {
                var horiz = camLocal - posLocal; horiz.y = 0f;
                upHint = horiz.sqrMagnitude > 1e-6f ? horiz.normalized : Vector3.forward;
            }
            rotLocal = Quaternion.LookRotation(nLocal, upHint);
            return true;
        }

        // Llamado por el boton "Colocar" cuando el FSM esta en Spawn_Place.
        public void PlaceAtCurrentReticle()
        {
            if (_fsm.Current != ScannerMode.Spawn_Place) return;
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady)
            {
                Debug.LogWarning("[SpawnBuilder] WorldOrigin aun no esta listo. Calibrar primero.");
                return;
            }
            var hit = RaycastResolver.Instance?.ResolveFromScreenCenter() ?? ResolvedHit.Miss;
            if (!TryGetSpawnPose(hit, out var posLocal, out var rotLocal)) return;

            SorkenSpawnObject.Create(posLocal, rotLocal);
            _targetWall = null;
            _fsm.SetMode(ScannerMode.Idle);
        }

        // Si salimos del flujo sin completar (ej. Cancelar), limpiamos la pared.
        private void OnModeChanged(ScannerMode prev, ScannerMode next)
        {
            if (next != ScannerMode.SpawnPickWall && next != ScannerMode.Spawn_Place)
                _targetWall = null;
        }
    }
}
