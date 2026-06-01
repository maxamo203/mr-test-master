using UnityEngine;

namespace Scanner
{
    // Esfera marker visible durante la polilinea, ANTES de que el wall exista
    // (V1 floor y V2 ceiling). Es ISelectable: tap -> gizmo MoveOnly -> drag
    // actualiza el estado interno del WallBuilder.
    //
    // Una vez que la pared correspondiente se crea (al colocar V3), esta preview
    // se destruye porque el WallVertexHandle del wall toma su lugar.
    [DefaultExecutionOrder(50)]
    public class PolylinePreviewHandle : MonoBehaviour, ISelectable
    {
        public enum PreviewKind { V1Floor, Ceiling }

        public WallBuilder Owner;
        public PreviewKind Type;

        public SelectableKind Kind => SelectableKind.WallVertex;
        public Transform Transform => transform;

        private MeshRenderer _mr;
        private Material     _matNormal;
        private Material     _matSelected;
        private bool         _selected;

        public void Init(WallBuilder owner, PreviewKind kind, Vector3 anchorLocal, Color color, float radius)
        {
            Owner = owner;
            Type  = kind;

            transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            transform.localPosition = anchorLocal;
            transform.localRotation = Quaternion.identity;
            transform.localScale    = Vector3.one * (radius * 2f);

            int placedLayer = LayerMask.NameToLayer("Placed");
            if (placedLayer >= 0) gameObject.layer = placedLayer;

            _mr = GetComponent<MeshRenderer>();

            var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            _matNormal = new Material(sh) { name = "PreviewMat (runtime)" };
            if (_matNormal.HasProperty("_Color"))     _matNormal.color = color;
            if (_matNormal.HasProperty("_BaseColor")) _matNormal.SetColor("_BaseColor", color);

            _matSelected = new Material(_matNormal) { name = "PreviewMatSelected (runtime)" };
            var sel = new Color(1f, 1f, 0.2f, 1f);
            if (_matSelected.HasProperty("_Color"))     _matSelected.color = sel;
            if (_matSelected.HasProperty("_BaseColor")) _matSelected.SetColor("_BaseColor", sel);

            _mr.sharedMaterial = _matNormal;
        }

        public void OnSelect()
        {
            _selected = true;
            if (_mr != null) _mr.sharedMaterial = _matSelected;
            TransformGizmoController.Instance?.Attach(transform, moveOnly: true);
        }

        public void OnDeselect()
        {
            _selected = false;
            if (_mr != null) _mr.sharedMaterial = _matNormal;
            TransformGizmoController.Instance?.Detach();
        }

        private void LateUpdate()
        {
            if (!_selected || Owner == null) return;
            var wo = WorldOrigin.Instance;
            if (wo == null) return;
            var local = wo.ToRelative(transform.position);
            if (Type == PreviewKind.V1Floor) Owner.UpdateV1Floor(local);
            else                              Owner.UpdateCeiling(local);
        }

        private void OnDestroy()
        {
            if (_matNormal   != null) Destroy(_matNormal);
            if (_matSelected != null) Destroy(_matSelected);
        }
    }
}
