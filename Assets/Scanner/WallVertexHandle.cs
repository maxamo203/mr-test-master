using UnityEngine;

namespace Scanner
{
    // Handle visible en cada esquina de piso de una pared (2 por wall: A y B).
    // Es ISelectable: al tocarlo se selecciona, se muestra el gizmo en modo
    // MoveOnly, y arrastrandolo se actualiza la pared.
    //
    // Si dos paredes consecutivas (polilinea) comparten el vertice, mover este
    // handle actualiza tambien la pared adyacente para mantener la conexion.
    [DefaultExecutionOrder(50)]
    public class WallVertexHandle : MonoBehaviour, ISelectable
    {
        public WallObject Owner;
        public int CornerIndex; // 0 = A (BL), 1 = B (BR)

        public SelectableKind Kind => SelectableKind.WallVertex;
        public Transform Transform => transform;

        private MeshRenderer _mr;
        private Material     _matNormal;
        private Material     _matSelected;
        private bool         _selected;
        // Posicion anchor-local de este corner ANTES del frame actual de drag.
        // Se usa para identificar a otras paredes que compartian el mismo vertice.
        private Vector3 _prevAnchorLocal;
        private const float ShareTolerance = 0.005f; // 5mm
        // Largo horizontal de referencia (m) entre este corner y el otro. Se usa
        // para la rotacion pura: arrastrar el eje normal (X) conserva este largo.
        private float _refLen = 1f;

        public void Init(WallObject owner, int cornerIdx, float radius)
        {
            Owner       = owner;
            CornerIndex = cornerIdx;

            transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            transform.localScale = Vector3.one * (radius * 2f);

            int placedLayer = LayerMask.NameToLayer("Placed");
            if (placedLayer >= 0) gameObject.layer = placedLayer;

            _mr = GetComponent<MeshRenderer>();
            EnsureMaterials();
            _mr.sharedMaterial = _matNormal;

            SnapToCorner();
        }

        private void EnsureMaterials()
        {
            var sh = Shader.Find("Custom/LitMarker") ?? Shader.Find("Unlit/Color");
            _matNormal = new Material(sh) { name = "VertexHandleMat (runtime)" };
            var col = new Color(0.2f, 1f, 0.4f, 1f); // piso = verde
            if (_matNormal.HasProperty("_Color"))     _matNormal.color = col;
            if (_matNormal.HasProperty("_BaseColor")) _matNormal.SetColor("_BaseColor", col);

            _matSelected = new Material(_matNormal) { name = "VertexHandleMatSelected (runtime)" };
            var sel = new Color(1f, 1f, 0.2f, 1f);
            if (_matSelected.HasProperty("_Color"))     _matSelected.color = sel;
            if (_matSelected.HasProperty("_BaseColor")) _matSelected.SetColor("_BaseColor", sel);
        }

        public void OnSelect()
        {
            _selected = true;
            if (_mr != null && _matSelected != null) _mr.sharedMaterial = _matSelected;
            // Snapshot del estado actual para identificar paredes compartidas.
            _prevAnchorLocal = Owner != null ? Owner.GetCornerLocal(CornerIndex) : Vector3.zero;

            // Orientamos el gizmo al frame de la pared: Z = a lo largo (alarga),
            // X = normal/perpendicular (rotacion pura), Y = vertical mundo.
            Quaternion? orient = null;
            if (Owner != null)
            {
                Vector3 other = Owner.GetCornerWorld(1 - CornerIndex);
                Vector3 dir   = transform.position - other; dir.y = 0f;
                _refLen = Mathf.Max(0.05f, dir.magnitude);
                if (dir.sqrMagnitude > 1e-6f)
                    orient = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            TransformGizmoController.Instance?.Attach(transform, moveOnly: true, orient);
        }

        public void OnDeselect()
        {
            _selected = false;
            if (_mr != null && _matNormal != null) _mr.sharedMaterial = _matNormal;
            TransformGizmoController.Instance?.Detach();
        }

        public void SnapToCorner()
        {
            if (Owner == null) return;
            transform.position = Owner.GetCornerWorld(CornerIndex);
        }

        private void LateUpdate()
        {
            if (Owner == null) return;
            if (!_selected) { SnapToCorner(); return; }

            var wo = WorldOrigin.Instance;
            if (wo == null) return;

            // 1) Restringir el arrastre segun el eje del gizmo (alineado al plano):
            //    X (normal) => rotacion pura (conserva el largo); Z (a lo largo) y
            //    Y (vertical) => traslacion (cambian largo / altura).
            ConstrainToActiveAxis();

            // 2) Re-orientar el gizmo al frame actual de la pared, para que siga
            //    alineado al plano mientras rota.
            UpdateGizmoOrientation();

            var newLocal = wo.ToRelative(transform.position);
            // Si el handle no cambio nada respecto al snapshot, nada que hacer.
            if ((newLocal - _prevAnchorLocal).sqrMagnitude < (ShareTolerance * ShareTolerance) * 0.01f)
                return;

            // Propagar a TODAS las paredes que tenian un corner de piso coincidiendo
            // con _prevAnchorLocal — esto incluye al propio Owner.
            var registry = SceneRegistry.Instance;
            if (registry != null)
            {
                float tol2 = ShareTolerance * ShareTolerance;
                foreach (var w in registry.Walls)
                {
                    if (w == null) continue;
                    if ((w.ALocal - _prevAnchorLocal).sqrMagnitude < tol2)
                        w.SetCornerLocal(0, newLocal);
                    if ((w.BLocal - _prevAnchorLocal).sqrMagnitude < tol2)
                        w.SetCornerLocal(1, newLocal);
                }
            }
            _prevAnchorLocal = newLocal;
        }

        // Aplica la restriccion del eje que se esta arrastrando. El otro extremo de
        // la pared (Owner) queda fijo y es el centro de la rotacion.
        private void ConstrainToActiveAxis()
        {
            var ctrl   = TransformGizmoController.Instance;
            var active = ctrl != null ? ctrl.ActiveHandle : null;

            Vector3 other = Owner.GetCornerWorld(1 - CornerIndex);
            Vector3 pos   = transform.position;
            Vector3 flat  = pos - other; flat.y = 0f;

            bool rotating = active != null
                         && active.Operation == GizmoOperation.Move
                         && active.Axis == GizmoAxis.X;

            if (rotating)
            {
                // Rotacion pura: proyectar al circulo de radio _refLen alrededor del
                // otro extremo (en horizontal), conservando la altura del handle.
                if (flat.sqrMagnitude > 1e-6f)
                {
                    flat = flat.normalized * _refLen;
                    transform.position = new Vector3(other.x + flat.x, pos.y, other.z + flat.z);
                }
            }
            else
            {
                // Z (largo) / Y (vertical) / sin arrastre: el largo horizontal vigente
                // pasa a ser la referencia para la proxima rotacion.
                _refLen = Mathf.Max(0.05f, flat.magnitude);
            }
        }

        private void UpdateGizmoOrientation()
        {
            Vector3 other = Owner.GetCornerWorld(1 - CornerIndex);
            Vector3 dir   = transform.position - other; dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f)
                TransformGizmoController.Instance?.SetOrientation(
                    Quaternion.LookRotation(dir.normalized, Vector3.up));
        }

        private void OnDestroy()
        {
            if (_matNormal   != null) Destroy(_matNormal);
            if (_matSelected != null) Destroy(_matSelected);
        }
    }
}
