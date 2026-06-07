using UnityEngine;

namespace Scanner
{
    // Un unico punto de piso: una esfera que el usuario ubica sobre el piso real.
    // No es un plano, solo un punto; su Y (anchor-relativa) sirve de referencia
    // para "Mover al piso" (alinear las esquinas de las paredes a ese nivel).
    //
    // Es ISelectable: se selecciona y se mueve con el gizmo (libre, alineado al
    // mundo). Hijo de WorldOrigin, asi su localPosition ya es anchor-relativa y
    // sobrevive a recalibraciones. Singleton: solo puede haber uno.
    [DefaultExecutionOrder(50)]
    public class FloorPoint : MonoBehaviour, ISelectable
    {
        public static FloorPoint Instance { get; private set; }

        public SelectableKind Kind => SelectableKind.Floor;
        public Transform Transform => transform;

        // Posicion / altura en espacio anchor (= localPosition por ser hijo de WorldOrigin).
        public Vector3 LocalPosition => transform.localPosition;
        public float   LocalY        => transform.localPosition.y;

        private const float Radius = 0.06f;
        private MeshRenderer _mr;
        private Material     _matNormal;
        private Material     _matSelected;

        // Crea el punto de piso, o reubica el existente (solo puede haber uno).
        public static FloorPoint Create(Vector3 anchorLocal)
        {
            if (Instance != null)
            {
                Instance.SetLocal(anchorLocal);
                return Instance;
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "FloorPoint";
            var f = go.AddComponent<FloorPoint>();
            f.Init(anchorLocal);
            return f;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Init(Vector3 anchorLocal)
        {
            transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            transform.localScale = Vector3.one * (Radius * 2f);
            SetLocal(anchorLocal);

            int placedLayer = LayerMask.NameToLayer("Placed");
            if (placedLayer >= 0) gameObject.layer = placedLayer;

            _mr = GetComponent<MeshRenderer>();
            EnsureMaterials();
            _mr.sharedMaterial = _matNormal;
        }

        public void SetLocal(Vector3 anchorLocal) => transform.localPosition = anchorLocal;

        private void EnsureMaterials()
        {
            var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            _matNormal = new Material(sh) { name = "FloorPointMat (runtime)" };
            var col = new Color(0.2f, 0.6f, 1f, 1f); // azul = piso
            if (_matNormal.HasProperty("_Color"))     _matNormal.color = col;
            if (_matNormal.HasProperty("_BaseColor")) _matNormal.SetColor("_BaseColor", col);

            _matSelected = new Material(_matNormal) { name = "FloorPointMatSelected (runtime)" };
            var sel = new Color(1f, 1f, 0.2f, 1f);
            if (_matSelected.HasProperty("_Color"))     _matSelected.color = sel;
            if (_matSelected.HasProperty("_BaseColor")) _matSelected.SetColor("_BaseColor", sel);
        }

        public void OnSelect()
        {
            if (_mr != null && _matSelected != null) _mr.sharedMaterial = _matSelected;
            // Movimiento libre, alineado al mundo (lo ubicas sobre el piso real).
            TransformGizmoController.Instance?.Attach(transform, moveOnly: true);
        }

        public void OnDeselect()
        {
            if (_mr != null && _matNormal != null) _mr.sharedMaterial = _matNormal;
            TransformGizmoController.Instance?.Detach();
        }

        public void Delete()
        {
            if (ScanStateMachine.Instance != null
                && ScanStateMachine.Instance.CurrentSelection == (ISelectable)this)
                ScanStateMachine.Instance.ClearSelection();
            // Nulleamos Instance YA (Destroy es diferido a fin de frame): asi un
            // FloorPoint.Create posterior en el mismo frame (ej. al cargar un scan)
            // crea uno nuevo en vez de reubicar el que se esta destruyendo.
            if (Instance == this) Instance = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_matNormal   != null) Destroy(_matNormal);
            if (_matSelected != null) Destroy(_matSelected);
        }
    }
}
