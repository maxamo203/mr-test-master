using UnityEngine;

namespace Scanner
{
    // Panel lateral derecho que aparece cuando hay un objeto seleccionado.
    // Botones: Borrar, Mover, Altura (slider solo para paredes), Deseleccionar.
    public class EditPanelUI : MonoBehaviour
    {
        private ScanStateMachine _fsm;

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            _fsm.OnSelectionChanged += OnSelectionChanged;
        }

        private void OnDestroy()
        {
            if (_fsm != null) _fsm.OnSelectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged(ISelectable sel)
        {
            if (sel is CubeObject cube && TransformGizmoController.Instance != null)
                TransformGizmoController.Instance.Attach(cube.transform, moveOnly: false);
            // Para WallVertex el gizmo lo activa el propio handle (con moveOnly=true).
            // Para Wall el gizmo no se usa (la edicion es via panel).
            else if (sel == null || sel is WallObject)
                TransformGizmoController.Instance?.Detach();
        }

        private static Texture2D _bgTex;
        private static Texture2D BG()
        {
            if (_bgTex == null) { _bgTex = new Texture2D(1,1); _bgTex.SetPixel(0,0, new Color(0,0,0,0.8f)); _bgTex.Apply(); }
            return _bgTex;
        }

        private void OnGUI()
        {
            if (_fsm == null || _fsm.CurrentSelection == null) return;
            var sel = _fsm.CurrentSelection;

            UIScale.Begin();

            float w = 280;
            var bgStyle = new GUIStyle(GUI.skin.box) { normal = { background = BG() } };

            GUILayout.BeginArea(new Rect(UIScale.VirtualWidth - w - 10, 10, w, 360), GUIContent.none, bgStyle);

            var title = new GUIStyle { fontSize = 22, normal = { textColor = Color.white } };
            GUILayout.Label($"Sel: {sel.Kind}", title);
            GUILayout.Space(8);

            // Vertice de pared: panel minimal. La edicion es por gizmo MoveOnly.
            if (sel is WallVertexHandle vh)
            {
                GUILayout.Label("Arrastra el gizmo para mover el vertice.");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Listo", GUILayout.Height(50))) _fsm.ClearSelection();
                GUILayout.EndArea();
                return;
            }

            // Vertice (esquina diagonal) de cubo: idem, arrastrar reforma el cubo.
            if (sel is CubeVertexHandle)
            {
                GUILayout.Label("Arrastra el gizmo para reformar el cubo\n(la esquina opuesta queda fija).");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Listo", GUILayout.Height(50))) _fsm.ClearSelection();
                GUILayout.EndArea();
                return;
            }

            // Para Wall y Cube, opciones generales.
            if (GUILayout.Button("Mover", GUILayout.Height(50)))
                _fsm.SetMode(ScannerMode.EditMoveTarget);

            if (sel is WallObject wall)
            {
                bool inPolyline = !string.IsNullOrEmpty(wall.PolylineId);
                string altLabel = inPolyline
                    ? $"Altura (polilinea): {wall.Height:F2} m"
                    : $"Altura: {wall.Height:F2} m";
                GUILayout.Label(altLabel);
                var newH = GUILayout.HorizontalSlider(wall.Height, 0.5f, 5f);
                if (Mathf.Abs(newH - wall.Height) > 0.001f)
                    wall.SetHeightForPolyline(newH); // propaga a toda la polilinea

                if (GUILayout.Button("Quitar todas las puertas", GUILayout.Height(40)))
                    wall.ClearDoors();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Borrar", GUILayout.Height(50)))
            {
                if (sel is WallObject w2) w2.Delete();
                else if (sel is CubeObject c2) c2.Delete();
                _fsm.ClearSelection();
            }

            if (GUILayout.Button("Deseleccionar", GUILayout.Height(40)))
                _fsm.ClearSelection();

            GUILayout.EndArea();
        }
    }
}
