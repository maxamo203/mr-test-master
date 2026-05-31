using UnityEngine;

namespace Scanner
{
    // Handle visible en cada esquina de una pared (4 por wall: BL, BR, TL, TR).
    // Es ISelectable: al tocarlo se selecciona, se muestra el gizmo en modo
    // MoveOnly, y arrastrandolo se actualiza la pared.
    //
    // Mientras esta seleccionado, su transform.position es la "fuente de verdad"
    // del corner: el LateUpdate (despues del gizmo, gracias a DefaultExecutionOrder)
    // empuja esa posicion hacia los datos del WallObject.
    //
    // Cuando NO esta seleccionado, su transform.position se snapea al corner
    // computado a partir de los datos del wall.
    [DefaultExecutionOrder(50)]
    public class WallVertexHandle : MonoBehaviour, ISelectable
    {
        public WallObject Owner;
        public int CornerIndex; // 0=BL (a), 1=BR (b), 2=TL (a+up*H), 3=TR (b+up*H)

        public SelectableKind Kind => SelectableKind.WallVertex;
        public Transform Transform => transform;

        private MeshRenderer _mr;
        private Material     _matNormal;
        private Material     _matSelected;
        private bool         _selected;

        public void Init(WallObject owner, int cornerIdx, float radius)
        {
            Owner       = owner;
            CornerIndex = cornerIdx;

            transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            transform.localScale = Vector3.one * (radius * 2f);

            int placedLayer = LayerMask.NameToLayer("Placed");
            if (placedLayer >= 0) gameObject.layer = placedLayer;

            _mr = GetComponent<MeshRenderer>();
            EnsureMaterials(cornerIdx);
            _mr.sharedMaterial = _matNormal;

            SnapToCorner();
        }

        private void EnsureMaterials(int cornerIdx)
        {
            var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            _matNormal = new Material(sh) { name = "VertexHandleMat (runtime)" };
            // Bottom corners verdes, top corners naranjas — coinciden con el preview de polilinea.
            var col = cornerIdx < 2
                ? new Color(0.2f, 1f, 0.4f, 1f)
                : new Color(1f, 0.6f, 0.2f, 1f);
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
            TransformGizmoController.Instance?.Attach(transform, moveOnly: true);
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
            if (_selected)
            {
                // El usuario esta arrastrando con el gizmo: empujar la posicion
                // del transform a los datos del wall.
                Owner.SetCornerWorld(CornerIndex, transform.position);
            }
            else
            {
                SnapToCorner();
            }
        }

        private void OnDestroy()
        {
            if (_matNormal   != null) Destroy(_matNormal);
            if (_matSelected != null) Destroy(_matSelected);
        }
    }
}
