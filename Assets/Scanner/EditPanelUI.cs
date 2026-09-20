using UnityEngine;
using T = MortuoriumTheme;

namespace Scanner
{
    // Panel de edición del objeto seleccionado (pared / cubo / piso / marcador /
    // vértices). Con la estética Mortuorium y anclado ARRIBA de la botonera de
    // herramientas — el mismo lugar donde aparece el panel contextual al colocar
    // algo (ver ReticleController.ContextBottomY). Crece hacia arriba desde ahí.
    public class EditPanelUI : MonoBehaviour
    {
        private ScanStateMachine _fsm;

        private const float Pad = 16f;      // igual que ReticleController (alineación)
        private const float PadIn = 14f, TitleBlock = 34f, G = 8f, BtnH = 46f, SlH = 32f, HintH = 40f;

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
            // Para los handles-esfera (WallVertex/CubeVertex/Door/Floor) y para Wall el
            // gizmo lo maneja el propio objeto en su OnSelect. El marcador no usa gizmo.
            else if (sel == null || sel is MarkerObject)
                TransformGizmoController.Instance?.Detach();
        }

        private void OnGUI()
        {
            if (_fsm == null || _fsm.CurrentSelection == null) return;
            // Solo en modo Selected: al mover (EditMoveTarget) manda el panel
            // contextual de "colocar" de ReticleController; no queremos ambos.
            if (_fsm.Current != ScannerMode.Selected) return;
            // Con overlay de guardado abierto no dibujamos.
            if (SaveLoadUI.EstaAbierto) return;

            var sel = _fsm.CurrentSelection;
            UIScale.Begin();
            float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;

            switch (sel)
            {
                case WallVertexHandle: DrawHandle(vw, vh, "VÉRTICE DE PARED",
                    "Arrastrá el gizmo para mover el vértice"); break;
                case CubeVertexHandle: DrawHandle(vw, vh, "ESQUINA DE CUBO",
                    "Arrastrá el gizmo para reformar el cubo (la esquina opuesta queda fija)"); break;
                case DoorHandle: DrawHandle(vw, vh, "ESQUINA DE PUERTA",
                    "Arrastrá el gizmo para mover la esquina del hueco de la puerta"); break;
                case FloorPoint floor:   DrawFloor(vw, vh, floor);   break;
                case MarkerObject marker: DrawMarker(vw, vh, marker); break;
                case WallObject wall:    DrawWall(vw, vh, wall);     break;
                case CubeObject cube:    DrawCube(vw, vh, cube);     break;
            }
        }

        // ── Panels por tipo ───────────────────────────────────────────────────

        private void DrawHandle(float vw, float vh, string titulo, string hint)
        {
            float panelH = TitleBlock + G + HintH + G + BtnH + PadIn;
            var p = Frame(vw, vh, panelH, titulo, out float x, out float iw, out float y);
            GUI.Label(new Rect(x, y, iw, HintH), hint,
                      T.Estilo(T.FMono, 11, T.CreamDim, TextAnchor.UpperLeft, wrap: true));
            Btn(new Rect(x, p.yMax - PadIn - BtnH, iw, BtnH), "LISTO", true, () => _fsm.ClearSelection());
        }

        private void DrawFloor(float vw, float vh, FloorPoint floor)
        {
            float panelH = TitleBlock + G + HintH + G + BtnH + PadIn;
            var p = Frame(vw, vh, panelH, "PISO", out float x, out float iw, out float y);
            GUI.Label(new Rect(x, y, iw, HintH),
                      "Arrastrá el gizmo para ubicar el punto sobre el piso real",
                      T.Estilo(T.FMono, 11, T.CreamDim, TextAnchor.UpperLeft, wrap: true));
            float half = (iw - 10f) * 0.5f;
            float by = p.yMax - PadIn - BtnH;
            Btn(new Rect(x, by, half, BtnH), "BORRAR PISO", false, () => floor.Delete());
            Btn(new Rect(x + half + 10f, by, half, BtnH), "LISTO", true, () => _fsm.ClearSelection());
        }

        private void DrawCube(float vw, float vh, CubeObject cube)
        {
            float panelH = TitleBlock + G + BtnH + G + BtnH + PadIn;
            var p = Frame(vw, vh, panelH, "CUBO", out float x, out float iw, out float y);
            Btn(new Rect(x, y, iw, BtnH), "MOVER", false, () => _fsm.SetMode(ScannerMode.EditMoveTarget));
            float half = (iw - 10f) * 0.5f;
            float by = p.yMax - PadIn - BtnH;
            Btn(new Rect(x, by, half, BtnH), "BORRAR", true, () => { cube.Delete(); _fsm.ClearSelection(); });
            Btn(new Rect(x + half + 10f, by, half, BtnH), "DESELECCIONAR", false, () => _fsm.ClearSelection());
        }

        private void DrawWall(float vw, float vh, WallObject wall)
        {
            float panelH = TitleBlock + (G + SlH) * 2f + (G + BtnH) * 3f + PadIn;
            var p = Frame(vw, vh, panelH, "PARED", out float x, out float iw, out float y);
            bool poly = !string.IsNullOrEmpty(wall.PolylineId);

            // Altura + Ancho (label izquierda + slider derecha).
            float nh = SliderRow(x, ref y, iw, poly ? "Alto (poli)" : "Alto", wall.Height, 0.5f, 5f, "{0:0.00} m");
            if (Mathf.Abs(nh - wall.Height) > 0.001f) wall.SetHeightForPolyline(nh);

            float nw = SliderRow(x, ref y, iw, poly ? "Ancho (poli)" : "Ancho", wall.Width, 0.05f, 0.5f, "{0:0.00} m");
            if (Mathf.Abs(nw - wall.Width) > 0.001f) wall.SetWidthForPolyline(nw);

            float half = (iw - 10f) * 0.5f;
            Btn(new Rect(x, y, iw, BtnH), "MOVER AL PISO", false,
                () => wall.MoveToFloorForPolyline(FloorPoint.Instance.LocalY), FloorPoint.Instance != null);
            y += G + BtnH;

            Btn(new Rect(x, y, iw, BtnH), "QUITAR TODAS LAS PUERTAS", false, () => wall.ClearDoors());

            float by = p.yMax - PadIn - BtnH;
            Btn(new Rect(x, by, half, BtnH), "BORRAR", true, () => { wall.Delete(); _fsm.ClearSelection(); });
            Btn(new Rect(x + half + 10f, by, half, BtnH), "DESELECCIONAR", false, () => _fsm.ClearSelection());
        }

        private void DrawMarker(float vw, float vh, MarkerObject marker)
        {
            var catalog = MarkerCatalog.Active;
            int cambios = 0;
            if (catalog != null)
                foreach (var t in catalog.Types)
                    if (t != null && t != marker.Type) cambios++;

            float panelH = TitleBlock + 22f + G + cambios * (40f + G) + BtnH + G + BtnH + PadIn;
            var p = Frame(vw, vh, panelH, "MARCADOR", out float x, out float iw, out float y);

            GUI.Label(new Rect(x, y, iw, 22f),
                      $"Tipo: {(marker.Type != null ? marker.Type.DisplayName : "?")}",
                      T.Estilo(T.FMono, 12, T.Blue, TextAnchor.MiddleLeft));
            y += 22f + G;

            if (catalog != null)
                foreach (var t in catalog.Types)
                {
                    if (t == null || t == marker.Type) continue;
                    var tt = t;   // captura por iteración
                    Btn(new Rect(x, y, iw, 40f), $"CAMBIAR A {tt.DisplayName.ToUpperInvariant()}", false,
                        () => marker.SetType(tt));
                    y += 40f + G;
                }

            Btn(new Rect(x, y, iw, BtnH), "MOVER (sobre la pared)", false,
                () => _fsm.SetMode(ScannerMode.EditMoveTarget));

            float half = (iw - 10f) * 0.5f;
            float by = p.yMax - PadIn - BtnH;
            Btn(new Rect(x, by, half, BtnH), "BORRAR", true, () => marker.Delete());
            Btn(new Rect(x + half + 10f, by, half, BtnH), "DESELECCIONAR", false, () => _fsm.ClearSelection());
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // Dibuja el marco del panel (bg + borde + título) anclado arriba de la
        // botonera y devuelve el rect + el cursor de contenido (x, ancho, y).
        private Rect Frame(float vw, float vh, float panelH, string titulo,
                           out float x, out float iw, out float y)
        {
            float bottom = ReticleController.ContextBottomY(vh);
            var panel = new Rect(Pad, bottom - panelH, vw - Pad * 2f, panelH);
            UIBlocker.AddVirtualRect(panel);
            T.Fill(panel, new Color(0f, 0f, 0f, 0.82f));
            T.Borde(panel, T.Border);
            GUI.Label(new Rect(panel.x + PadIn, panel.y + 8f, panel.width - PadIn * 2f, 26f), titulo,
                      T.Estilo(T.FBebas, 18, T.Cream));
            x = panel.x + PadIn;
            iw = panel.width - PadIn * 2f;
            y = panel.y + TitleBlock;
            return panel;
        }

        // Fila de slider: etiqueta+valor a la izquierda, slider a la derecha. Avanza y.
        private float SliderRow(float x, ref float y, float iw, string label,
                                float value, float min, float max, string fmt)
        {
            GUI.Label(new Rect(x, y, iw * 0.46f, 24f),
                      $"{label}: {string.Format(fmt, value)}",
                      T.Estilo(T.FMono, 11, T.CreamDim, TextAnchor.MiddleLeft));
            float nv = T.Slider(new Rect(x + iw * 0.48f, y + 4f, iw * 0.52f, 22f), value, min, max);
            y += G + SlH;
            return nv;
        }

        private void Btn(Rect r, string label, bool primario, System.Action onClick, bool enabled = true) =>
            T.Boton(null, r, label, primario, onClick, enabled, fontSize: 14);
    }
}
