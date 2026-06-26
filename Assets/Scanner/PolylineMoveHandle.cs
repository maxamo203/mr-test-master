using UnityEngine;

namespace Scanner
{
    // Gizmo para mover TODA la polilinea de una pared a la vez (conservando su
    // rotacion). Aparece en el MEDIO del piso de la pared seleccionada — siempre
    // en el piso (no depende de la altura, que puede ser muy alta y quedar fuera de
    // cuadro) y en el medio del segmento para diferenciarse de las esferas-vertice
    // (que estan en los extremos).
    //
    // Es el "target" del TransformGizmoController: cuando se lo arrastra, traslada
    // todas las paredes con el mismo PolylineId por el mismo delta. Util para
    // reubicar de una sola vez una polilinea entera que quedo descalibrada.
    //
    // Lo crea/destruye WallObject en OnSelect/OnDeselect. No tiene visual propio ni
    // collider: el visual y el input son los del gizmo.
    [DefaultExecutionOrder(60)] // despues del gizmo (-100) y de las esferas (50)
    public class PolylineMoveHandle : MonoBehaviour
    {
        private WallObject _owner;
        private Vector3 _lastWorld;

        public static PolylineMoveHandle Create(WallObject owner)
        {
            var go = new GameObject("PolylineMover");
            var h = go.AddComponent<PolylineMoveHandle>();
            h.Init(owner);
            return h;
        }

        private void Init(WallObject owner)
        {
            _owner = owner;
            transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            SnapToMid();
            // Gizmo de mover orientado al frame de la pared (igual que las esferas-
            // vertice): Z a lo largo de la pared, X perpendicular (ancho), Y vertical.
            // Solo traslada, nunca rota => la polilinea conserva su rotacion.
            TransformGizmoController.Instance?.Attach(transform, moveOnly: true, WallOrientation());
            _lastWorld = transform.position;
        }

        // Orientacion del frame de la pared en world: Z = a lo largo del segmento,
        // X = perpendicular (ancho), Y = vertical mundo.
        private Quaternion? WallOrientation()
        {
            if (_owner == null) return null;
            Vector3 dir = _owner.GetCornerWorld(1) - _owner.GetCornerWorld(0);
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return null;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private Vector3 MidWorld()
        {
            var mid = (_owner.ALocal + _owner.BLocal) * 0.5f; // medio del piso (base)
            var wo = WorldOrigin.Instance;
            return wo != null ? wo.ToWorld(mid) : mid;
        }

        private void SnapToMid()
        {
            transform.position = MidWorld();
            _lastWorld = transform.position;
        }

        private void LateUpdate()
        {
            if (_owner == null) { Destroy(gameObject); return; }

            var gizmo = TransformGizmoController.Instance;
            bool dragging = gizmo != null && gizmo.ActiveHandle != null && gizmo.Target == transform;

            if (dragging)
            {
                var wo = WorldOrigin.Instance;
                if (wo == null) return;
                Vector3 cur = transform.position;
                Vector3 deltaLocal = wo.ToRelative(cur) - wo.ToRelative(_lastWorld);
                if (deltaLocal.sqrMagnitude > 1e-10f) MovePolyline(deltaLocal);
                _lastWorld = cur; // ya quedamos en el nuevo medio (movimos todo por el mismo delta)
            }
            else
            {
                SnapToMid();
                // Mantener el gizmo alineado al frame de la pared (por si se editó).
                var o = WallOrientation();
                if (o.HasValue) gizmo?.SetOrientation(o);
            }
        }

        // Traslada todas las paredes de la polilinea por deltaLocal (anchor-space).
        private void MovePolyline(Vector3 deltaLocal)
        {
            var reg = SceneRegistry.Instance;
            if (reg == null) return;
            string pid = _owner.PolylineId;
            foreach (var w in reg.Walls)
            {
                if (w == null) continue;
                bool inPoly = !string.IsNullOrEmpty(pid) ? w.PolylineId == pid : w == _owner;
                if (inPoly) w.SetEndpoints(w.ALocal + deltaLocal, w.BLocal + deltaLocal);
            }
        }

        public void Dispose()
        {
            TransformGizmoController.Instance?.Detach();
            Destroy(gameObject);
        }
    }
}
