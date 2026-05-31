using UnityEngine;

namespace Scanner
{
    // Manejo de tap sobre objetos colocados. Procesa en OnGUI con MouseUp para
    // que los botones de IMGUI tengan prioridad (si algun GUI consumio el evento,
    // Event.current.type ya es Used).
    //
    // Execution order alto para correr DESPUES de todos los demas OnGUI.
    [DefaultExecutionOrder(1000)]
    public class SelectionController : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private DoorBuilder _doorBuilder;

        private int _placedLayerMask;
        private int _gizmoLayerMask;

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;
            if (_doorBuilder == null) _doorBuilder = FindFirstObjectByType<DoorBuilder>();

            int placedLayer = LayerMask.NameToLayer("Placed");
            _placedLayerMask = placedLayer >= 0 ? (1 << placedLayer) : 0;
            int gizmoLayer = LayerMask.NameToLayer("Gizmo");
            _gizmoLayerMask = gizmoLayer >= 0 ? (1 << gizmoLayer) : 0;
        }

        private void OnGUI()
        {
            var e = Event.current;
            if (e.type != EventType.MouseUp || e.button != 0) return;
            // Si un GUI button consumio el evento, e.type ahora seria Used.
            // Nosotros corremos despues por DefaultExecutionOrder(1000).

            var guiPos    = e.mousePosition;                          // top-down
            var screenPos = new Vector2(guiPos.x, Screen.height - guiPos.y); // bottom-up
            TryPick(screenPos);
        }

        private void TryPick(Vector2 screenPoint)
        {
            var fsm = ScanStateMachine.Instance;
            if (fsm == null || _camera == null) return;

            var ray = _camera.ScreenPointToRay(screenPoint);

            // Si el tap pega contra un handle de gizmo, lo ignoramos (es drag).
            if (_gizmoLayerMask != 0 && Physics.Raycast(ray, out _, 100f, _gizmoLayerMask))
                return;

            // Layer "Placed" si existe; sino, raycasteamos contra todas las layers y
            // filtramos por ISelectable.
            int mask = _placedLayerMask != 0 ? _placedLayerMask : ~0;
            var hits = Physics.RaycastAll(ray, 50f, mask);

            ISelectable picked = null;
            float bestDist = float.MaxValue;
            foreach (var hit in hits)
            {
                var sel = hit.collider.GetComponentInParent<ISelectable>();
                if (sel != null && hit.distance < bestDist)
                {
                    bestDist = hit.distance;
                    picked   = sel;
                }
            }

            if (picked == null)
            {
                // Tap a un area vacia no deselecciona (para que botones de IMGUI
                // sobre el panel no eliminen la seleccion accidentalmente). La
                // deseleccion se hace con el boton "Deseleccionar" del panel.
                return;
            }

            // Modo "elegir pared para puerta": notificar al DoorBuilder, no seleccionar.
            if (fsm.Current == ScannerMode.DoorPickWall && picked is WallObject wall)
            {
                _doorBuilder?.OnWallPicked(wall);
                return;
            }

            // Solo permitir seleccion en Idle o Selected (no en medio de colocar).
            if (fsm.Current == ScannerMode.Idle || fsm.Current == ScannerMode.Selected)
                fsm.SetSelection(picked);
        }
    }
}
