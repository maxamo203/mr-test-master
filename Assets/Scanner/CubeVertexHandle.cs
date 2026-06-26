using UnityEngine;

namespace Scanner
{
    // Handle visible en cada uno de los dos vertices de la diagonal de un cubo.
    // Es ISelectable: al tocarlo se selecciona, se muestra el gizmo MoveOnly, y
    // arrastrandolo se reforma el cubo (el corner opuesto queda fijo).
    //
    // Es hijo de WorldOrigin (no del cubo) para que arrastrarlo no se vea afectado
    // por la escala del cubo; cuando no esta seleccionado se re-snapea a la esquina.
    [DefaultExecutionOrder(50)]
    public class CubeVertexHandle : MonoBehaviour, ISelectable
    {
        public CubeObject Owner;
        public Vector3 CornerSign; // signo (±1 por eje) de la esquina que representa

        public SelectableKind Kind => SelectableKind.CubeVertex;
        public Transform Transform => transform;

        private MeshRenderer _mr;
        private Material     _matNormal;
        private Material     _matSelected;
        private bool         _selected;

        public void Init(CubeObject owner, Vector3 cornerSign, float radius)
        {
            Owner      = owner;
            CornerSign = cornerSign;

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
            _matNormal = new Material(sh) { name = "CubeVertexMat (runtime)" };
            var col = new Color(1f, 0.55f, 0.1f, 1f); // naranja, para distinguir de los de pared (verdes)
            if (_matNormal.HasProperty("_Color"))     _matNormal.color = col;
            if (_matNormal.HasProperty("_BaseColor")) _matNormal.SetColor("_BaseColor", col);

            _matSelected = new Material(_matNormal) { name = "CubeVertexMatSelected (runtime)" };
            var sel = new Color(1f, 1f, 0.2f, 1f);
            if (_matSelected.HasProperty("_Color"))     _matSelected.color = sel;
            if (_matSelected.HasProperty("_BaseColor")) _matSelected.SetColor("_BaseColor", sel);
        }

        public void OnSelect()
        {
            _selected = true;
            if (_mr != null && _matSelected != null) _mr.sharedMaterial = _matSelected;
            // Orientamos el gizmo al frame del cubo: las flechas apuntan a las
            // aristas del cubo y arrastrarlas mueve a lo largo de sus ejes locales
            // (importa cuando el cubo esta rotado).
            var orient = Owner != null ? Owner.transform.rotation : (Quaternion?)null;
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
            transform.position = Owner.GetDiagonalCornerWorld(CornerSign);
        }

        private void LateUpdate()
        {
            if (Owner == null) return;
            if (_selected)
                Owner.SetDiagonalCornerFromHandle(CornerSign, transform.position);
            else
                SnapToCorner();
        }

        private void OnDestroy()
        {
            if (_matNormal   != null) Destroy(_matNormal);
            if (_matSelected != null) Destroy(_matSelected);
        }
    }
}
