using UnityEngine;

namespace Scanner
{
    // Esfera-handle de una esquina de puerta. Hay 2 por puerta:
    //   - Floor: pegada al piso de la pared (v=0); arrastrarla desliza el borde en u.
    //   - Free : libre dentro de la pared (u,v); define el borde opuesto y la altura.
    //
    // Es hija de WorldOrigin (como WallVertexHandle). Su posicion se calcula a partir
    // del UV de la puerta sobre la CARA CERCANA de la pared, asi si la pared se mueve
    // o se edita, el handle (y el hueco) la siguen.
    [DefaultExecutionOrder(50)]
    public class DoorHandle : MonoBehaviour, ISelectable
    {
        public enum Corner { Floor, Free }

        public WallObject Owner;
        public string DoorId;
        public Corner Type;

        public SelectableKind Kind => SelectableKind.DoorVertex;
        public Transform Transform => transform;

        private const float Radius = 0.045f;
        private const float MinGap = 0.05f; // tamano minimo de puerta en u/v (m)

        private MeshRenderer _mr;
        private Material     _matNormal;
        private Material     _matSelected;
        private bool         _selected;

        public static DoorHandle Create(WallObject owner, string doorId, Corner corner)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"DoorVertex_{corner}_{doorId}";
            var h = go.AddComponent<DoorHandle>();
            h.Init(owner, doorId, corner);
            return h;
        }

        private void Init(WallObject owner, string doorId, Corner corner)
        {
            Owner  = owner;
            DoorId = doorId;
            Type   = corner;

            transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            transform.localScale = Vector3.one * (Radius * 2f);

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
            _matNormal = new Material(sh) { name = "DoorHandleMat (runtime)" };
            // Floor = celeste (piso); Free = naranja (libre).
            var col = Type == Corner.Floor ? new Color(0.3f, 0.8f, 1f, 1f) : new Color(1f, 0.6f, 0.2f, 1f);
            if (_matNormal.HasProperty("_Color"))     _matNormal.color = col;
            if (_matNormal.HasProperty("_BaseColor")) _matNormal.SetColor("_BaseColor", col);

            _matSelected = new Material(_matNormal) { name = "DoorHandleMatSelected (runtime)" };
            var sel = new Color(1f, 1f, 0.2f, 1f);
            if (_matSelected.HasProperty("_Color"))     _matSelected.color = sel;
            if (_matSelected.HasProperty("_BaseColor")) _matSelected.SetColor("_BaseColor", sel);
        }

        public void OnSelect()
        {
            _selected = true;
            if (_mr != null && _matSelected != null) _mr.sharedMaterial = _matSelected;
            // Orientamos el gizmo al frame de la pared: X = base (horizontal a lo
            // largo de la pared), Y = vertical, Z = normal (grosor). Asi las flechas
            // se mueven en los ejes de la puerta, no en los del mundo.
            Quaternion? orient = null;
            if (Owner != null)
            {
                var n = Owner.Normal;
                if (n.sqrMagnitude > 1e-6f) orient = Quaternion.LookRotation(n, Vector3.up);
            }
            TransformGizmoController.Instance?.Attach(transform, moveOnly: true, orient);
        }

        public void OnDeselect()
        {
            _selected = false;
            if (_mr != null && _matNormal != null) _mr.sharedMaterial = _matNormal;
            TransformGizmoController.Instance?.Detach();
        }

        // Posiciona el handle en la esquina que le corresponde, sobre la cara cercana.
        public void SnapToCorner()
        {
            if (Owner == null) return;
            var d = Owner.GetDoor(DoorId);
            if (d == null) return;
            transform.position = Type == Corner.Floor
                ? Owner.WallUVToWorld(d.uMin, 0f)
                : Owner.WallUVToWorld(d.uMax, d.vMax);
        }

        private void LateUpdate()
        {
            if (Owner == null) return;
            var d = Owner.GetDoor(DoorId);
            if (d == null) { Destroy(gameObject); return; }

            if (!_selected) { SnapToCorner(); return; }

            // Arrastrando: proyectar la posicion del handle al UV de la pared.
            if (!Owner.WorldPointToWallUV(transform.position, out float u, out float v)) return;

            if (Type == Corner.Floor)
            {
                d.uMin = Mathf.Clamp(u, 0f, d.uMax - MinGap);
                d.vMin = 0f; // siempre pegada al piso
            }
            else // Free
            {
                d.uMax = Mathf.Clamp(u, d.uMin + MinGap, Owner.Length);
                d.vMax = Mathf.Clamp(v, MinGap, Owner.Height);
            }
            Owner.UpdateDoor(DoorId);
        }

        private void OnDestroy()
        {
            if (_matNormal   != null) Destroy(_matNormal);
            if (_matSelected != null) Destroy(_matSelected);
        }
    }
}
