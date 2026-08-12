using System;
using UnityEngine;
using T = MortuoriumTheme;
using UnityEngine.InputSystem;
using Scanner.AutoScan;

namespace Scanner
{
    // HUD del escáner con la estética del prototipo (Assets/Prototipo Navegacion):
    //   - Barra superior con gradiente: volver al menú + título + hint.
    //   - Retícula central (color según la fuente del raycast).
    //   - Panel inferior con gradiente: fila contextual del flujo activo,
    //     herramientas (PARED / CUBO / AGUJERO PUERTA / IDENTIFICAR / PISO) y los
    //     botones COLOCAR + GUARDAR ESCANEO.
    //
    // La cámara AR se ve detrás (acá solo se dibujan overlays IMGUI). Los datos de
    // debug que antes vivían acá (fuente del raycast, modo FSM, posición del
    // jugador, slider de distancia fallback) se movieron al GameObject DebugHud.
    public class ReticleController : MonoBehaviour
    {
        [SerializeField] private WallBuilder _wallBuilder;
        [SerializeField] private DoorBuilder _doorBuilder;
        [SerializeField] private CubeBuilder _cubeBuilder;
        [SerializeField] private MarkerBuilder _markerBuilder;
        [SerializeField] private ARImageAnchor _imageAnchor;
        [SerializeField] private AutoScanController _autoScan;

        private ScanStateMachine _fsm;
        private ResolvedHit _lastHit;
        private Camera _camera;

        // Último hit resuelto desde el centro (para DebugHud/DebugRaycastUI).
        public ResolvedHit LastHit => _lastHit;

        // Submenú de "Identificar" (tipos de marcador del catálogo) abierto.
        private bool _markerSubmenuOpen;

        // Scroll horizontal de la botonera de herramientas (drag con touch/mouse).
        private float _toolScroll;
        private bool  _toolDragging;
        private bool  _toolDragMoved;   // pasó el umbral de arrastre -> suprime el tap
        private float _toolLastX;
        // X en pantalla (virtual) del botón "Identificar", para colgarle el submenú.
        private float _identScreenX;
        private bool  _identVisible;

        // Si al terminar la polilinea se cierra el bucle (ultimo vertice -> primero).
        // En memoria: se mantiene entre polilineas, no persiste entre sesiones.
        private bool _closePolyline = true;

        private const float Pad = 16f;

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            _camera = Camera.main;
            if (_wallBuilder == null) _wallBuilder = FindAnyObjectByType<WallBuilder>();
            if (_doorBuilder == null) _doorBuilder = FindAnyObjectByType<DoorBuilder>();
            if (_cubeBuilder == null) _cubeBuilder = FindAnyObjectByType<CubeBuilder>();
            if (_markerBuilder == null) _markerBuilder = FindAnyObjectByType<MarkerBuilder>();
            if (_imageAnchor == null) _imageAnchor = FindAnyObjectByType<ARImageAnchor>();
            if (_autoScan == null) _autoScan = AutoScanController.Ensure(gameObject);

            // Fantasma en vivo de lo que se va a colocar. Lo instanciamos acá para no
            // tener que cablearlo en la escena (encuentra los builders por su cuenta).
            if (GetComponent<PlacementPreview>() == null) gameObject.AddComponent<PlacementPreview>();
        }

        private void Update()
        {
            if (_fsm == null || RaycastResolver.Instance == null) return;
            if (!IsPlacingMode(_fsm.Current)) return;

            // En modo anclas el hit lo resuelve AnchorPointManager (además mide la
            // calidad del punto); reusamos el suyo para no raycastear dos veces.
            _lastHit = _fsm.Current == ScannerMode.Anchor_Place && AnchorPointManager.Instance != null
                ? AnchorPointManager.Instance.UltimoHit
                : RaycastResolver.Instance.ResolveFromScreenCenter();

            // Botón A del VR Box (click izquierdo) → Colocar, solo si el menú de pausa está cerrado.
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !Gamepad.PauseMenuController.IsOpen)
                OnPlace();
        }

        private bool IsPlacingMode(ScannerMode m) =>
            m == ScannerMode.Wall_V1 || m == ScannerMode.Wall_Height || m == ScannerMode.Wall_Vn ||
            m == ScannerMode.Door_V1 || m == ScannerMode.Door_V2 ||
            m == ScannerMode.Cube_V1 || m == ScannerMode.Cube_V2 || m == ScannerMode.Cube_V3 ||
            m == ScannerMode.Floor_Place || m == ScannerMode.Marker_Place || m == ScannerMode.EditMoveTarget ||
            m == ScannerMode.Anchor_Place;

        // ------------------------------------------------------------- OnGUI
        private void OnGUI()
        {
            if (_fsm == null) return;
            // Durante la calibración la pantalla es de ReferenceCaptureUI; con el
            // overlay de guardado abierto, de SaveLoadUI.
            if (_fsm.Current == ScannerMode.Calibrating) return;
            if (SaveLoadUI.EstaAbierto) return;

            UIScale.Begin();
            float vw = UIScale.VirtualWidth;
            float vh = UIScale.VirtualHeight;

            // Las franjas fuera del área segura (notch / home indicator) van en negro
            // para que no se vea la cámara "cortada" junto al degradado de la UI.
            T.FillOutsideSafeArea(T.Bg);

            DrawReticula(vw, vh);
            DrawBarraSuperior(vw);
            DrawPanelInferior(vw, vh);
        }

        // Retícula del prototipo: 4 ticks alrededor del centro, color según la
        // calidad/fuente del hit (feedback útil al escanear).
        private void DrawReticula(float vw, float vh)
        {
            if (!IsPlacingMode(_fsm.Current)) return;

            float cx = vw * 0.5f, cy = vh * 0.5f;
            var color = _fsm.Current switch
            {
                _ when !_lastHit.Hit => T.CreamDim,
                _ => _lastHit.Source switch
                {
                    RaycastSource.LidarMesh      => Color.green,
                    RaycastSource.ArDepth        => new Color(0.5f, 1f, 0.5f),
                    RaycastSource.ArPlane        => new Color(0.7f, 1f, 1f),
                    RaycastSource.ArFeaturePoint => Color.yellow,
                    RaycastSource.Fallback       => new Color(1f, 0.6f, 0.4f),
                    _                            => T.Cream,
                },
            };

            // Colocando anclas la mira se tiñe por la CALIDAD del punto (que es el dato
            // que importa ahí), no por la fuente del raycast.
            var calidad = _fsm.Current == ScannerMode.Anchor_Place ? AnchorQuality.Instance : null;
            if (calidad != null) color = calidad.Color;

            const float r = 24f, tick = 14f, g = 2f;   // radio, largo del tick, grosor/2
            T.Fill(new Rect(cx - g, cy - r - tick, g * 2f, tick), color);   // arriba
            T.Fill(new Rect(cx - g, cy + r, g * 2f, tick), color);          // abajo
            T.Fill(new Rect(cx - r - tick, cy - g, tick, g * 2f), color);   // izq
            T.Fill(new Rect(cx + r, cy - g, tick, g * 2f), color);          // der
            T.Fill(new Rect(cx - g, cy - g, g * 2f, g * 2f), color);        // centro

            if (calidad != null) calidad.DibujarBajoLaMira(cx, cy);
        }

        private void DrawBarraSuperior(float vw)
        {
            T.Gradiente(new Rect(0, 0, vw, 150f), 0.8f, haciaAbajo: true);

            var back = new Rect(Pad, 14f, 56f, 44f);
            UIBlocker.AddVirtualRect(back);
            T.BotonVolver(null, VolverAlMenu, back.x, back.y);

            string titulo = !string.IsNullOrEmpty(SaveLoadUI.NombreEdicion)
                ? "EDITANDO ESCANEO" : "ESCANEO DE ENTORNO";
            GUI.Label(new Rect(84f, 18f, vw - 100f, 36f), titulo, T.Estilo(T.FBebas, 22, T.Cream));
            GUI.Label(new Rect(84f, 56f, vw - 100f, 40f),
                      "apuntá con el centro de la pantalla al punto real y tocá COLOCAR",
                      T.Estilo(T.FMono, 11, T.CreamDim, TextAnchor.UpperLeft, wrap: true));
        }

        private void VolverAlMenu()
        {
            // Lo no guardado se pierde (mismo criterio que el prototipo).
            SceneFlow.GoTo(SceneFlow.EscenaMenu);
        }

        // Geometría compartida del panel inferior. La expongo para que EditPanelUI
        // ancle su panel de edición (al seleccionar pared/cubo/piso) en el mismo lugar
        // que el panel contextual de colocación: justo ARRIBA de la botonera.
        public const float BottomMargin = 40f, BotonesH = 56f, ContextGap = 12f, ToolsH = 96f;
        public static float ToolsY(float vh) => vh - BottomMargin - BotonesH - ContextGap - ToolsH;
        // Borde INFERIOR del panel contextual/edición (crece hacia arriba desde acá).
        public static float ContextBottomY(float vh) => ToolsY(vh) - 20f;

        // ---------------------------------------------------- panel inferior
        private void DrawPanelInferior(float vw, float vh)
        {
            var modo = _fsm.Current;

            float botonesH = BotonesH;
            float toolsH = ToolsH;
            float yBotones = vh - BottomMargin - botonesH;
            float yTools = ToolsY(vh);

            T.Gradiente(new Rect(0, yTools - 60f, vw, vh - (yTools - 60f)), 0.92f, haciaAbajo: false);
            UIBlocker.AddVirtualRect(new Rect(0, yTools - 12f, vw, vh - yTools + 12f));

            DrawContextual(vw, yTools - 20f);
            DrawHerramientas(vw, yTools, toolsH);

            // COLOCAR + GUARDAR ESCANEO.
            float bw = (vw - Pad * 2f - 10f) / 2f;
            bool enAutoScan = modo == ScannerMode.AutoScan_Capturing;
            bool puedeColocar = IsPlacingMode(modo) ||
                                (enAutoScan && _autoScan != null && _autoScan.CanFinish);
            bool puedeGuardar = (modo == ScannerMode.Idle || modo == ScannerMode.Selected) && HayContenido();

            var rColocar = new Rect(Pad, yBotones, bw, botonesH);
            bool focoColocar = puedeColocar;
            T.Fill(rColocar, focoColocar ? new Color(T.Tan.r, T.Tan.g, T.Tan.b, 0.18f)
                                         : new Color(0f, 0f, 0f, 0.4f));
            T.Borde(rColocar, puedeColocar ? T.Tan : T.BorderDim);
            string textoAccion = enAutoScan ? "FINALIZAR" : "COLOCAR";
            if (GUI.Button(rColocar, textoAccion,
                           T.Estilo(T.FBebas, 18, puedeColocar ? T.Cream : T.Disabled, TextAnchor.MiddleCenter))
                && puedeColocar)
            {
                if (enAutoScan) _autoScan?.FinishCapture();
                else OnPlace();
            }

            var rGuardar = new Rect(Pad + bw + 10f, yBotones, bw, botonesH);
            T.Fill(rGuardar, puedeGuardar ? new Color(T.Red.r, T.Red.g, T.Red.b, 0.20f)
                                          : new Color(0f, 0f, 0f, 0.4f));
            T.Borde(rGuardar, puedeGuardar ? T.Red : T.BorderDim);
            if (GUI.Button(rGuardar, "GUARDAR ESCANEO",
                           T.Estilo(T.FBebas, 16, puedeGuardar ? T.Cream : T.Disabled, TextAnchor.MiddleCenter))
                && puedeGuardar)
                SaveLoadUI.Abrir();
        }

        private bool HayContenido()
        {
            var reg = SceneRegistry.Instance;
            if (reg == null) return false;
            return reg.Walls.Count > 0 || reg.Cubes.Count > 0 || reg.Markers.Count > 0
                   || FloorPoint.Instance != null;
        }

        // Botonera de herramientas: fila HORIZONTAL SCROLLEABLE (drag con el dedo),
        // cada botón con su ícono 3D + etiqueta completa. Incluye las herramientas de
        // escaneo y, al final, los dos botones de recalibrar el anchor.
        private const float ToolW = 84f, ToolGap = 8f;

        private void DrawHerramientas(float vw, float y, float h)
        {
            var modo = _fsm.Current;
            bool idle = modo == ScannerMode.Idle;
            bool enAutoScan = modo == ScannerMode.AutoScan_Capturing;
            bool puedeRecal = !IsPlacingMode(modo) && !enAutoScan;

            bool enPared  = modo == ScannerMode.Wall_V1 || modo == ScannerMode.Wall_Height || modo == ScannerMode.Wall_Vn;
            bool enCubo   = modo == ScannerMode.Cube_V1 || modo == ScannerMode.Cube_V2 || modo == ScannerMode.Cube_V3;
            bool enPuerta = modo == ScannerMode.DoorPickWall || modo == ScannerMode.Door_V1 || modo == ScannerMode.Door_V2;
            bool enMarker = modo == ScannerMode.Marker_Place;
            bool enPiso   = modo == ScannerMode.Floor_Place;
            bool enAncla  = modo == ScannerMode.Anchor_Place;

            // Entradas de la botonera (en orden).
            var items = new (string label, MortuoriumIcons.Icon icon, bool activo, bool enabled, Action onTap)[]
            {
                ("AUTO\nESCANEO", MortuoriumIcons.Icon.AutoScan, enAutoScan, idle, StartAutoScan),
                ("PARED",        MortuoriumIcons.Icon.Pared,  enPared,  idle, () => _wallBuilder?.StartPolyline()),
                ("CUBO",         MortuoriumIcons.Icon.Cubo,   enCubo,   idle, () => _cubeBuilder?.StartCube()),
                ("AGUJERO\nPUERTA", MortuoriumIcons.Icon.Puerta, enPuerta, idle, () => _doorBuilder?.StartDoor()),
                ("IDENTIFICAR",  MortuoriumIcons.Icon.Marca,  enMarker || _markerSubmenuOpen, idle,
                                 () => _markerSubmenuOpen = !_markerSubmenuOpen),
                (FloorPoint.Instance != null ? "PISO\n(REUBICAR)" : "PISO", MortuoriumIcons.Icon.Piso, enPiso, idle,
                                 () => _fsm.SetMode(ScannerMode.Floor_Place)),
                ("ANCLAS",       MortuoriumIcons.Icon.Ancla,  enAncla,  idle, AbrirColocacionAnclas),
                ("RECALIBRAR\n(fijo)", MortuoriumIcons.Icon.RecalFijo, false, puedeRecal, () => Recalibrar(true)),
                ("RECALIBRAR\n(+mover)", MortuoriumIcons.Icon.RecalMover, false, puedeRecal, () => Recalibrar(false)),
            };

            var viewport = new Rect(Pad, y, vw - Pad * 2f, h);
            float contentW = items.Length * (ToolW + ToolGap) - ToolGap;
            float maxScroll = Mathf.Max(0f, contentW - viewport.width);

            HandleToolDrag(viewport, maxScroll);
            _toolScroll = Mathf.Clamp(_toolScroll, 0f, maxScroll);

            // Índice de "Identificar" para colgar el submenú (4º item).
            const int identIdx = 4;
            _identScreenX = viewport.x + identIdx * (ToolW + ToolGap) - _toolScroll;
            _identVisible = _identScreenX + ToolW > viewport.x && _identScreenX < viewport.xMax;

            GUI.BeginGroup(viewport);
            for (int i = 0; i < items.Length; i++)
            {
                float bx = i * (ToolW + ToolGap) - _toolScroll;
                if (bx + ToolW < 0f || bx > viewport.width) continue;   // fuera de vista
                DrawTool(new Rect(bx, 0f, ToolW, h), items[i].label, items[i].icon,
                         items[i].activo, items[i].enabled, items[i].onTap);
            }
            GUI.EndGroup();

            // Barrita de scroll (indicador) si hay overflow.
            if (maxScroll > 1f)
            {
                float t = viewport.width / contentW;
                float sw = viewport.width * t;
                float sx = viewport.x + (viewport.width - sw) * (_toolScroll / maxScroll);
                T.Fill(new Rect(viewport.x, viewport.yMax - 3f, viewport.width, 2f), new Color(1f, 1f, 1f, 0.06f));
                T.Fill(new Rect(sx, viewport.yMax - 3f, sw, 2f), new Color(T.Tan.r, T.Tan.g, T.Tan.b, 0.7f));
            }

            if (_markerSubmenuOpen && idle && _identVisible && _markerBuilder != null)
                DrawMarkerSubmenu(new Rect(_identScreenX, y, ToolW, h));
            else if (!idle)
                _markerSubmenuOpen = false;
        }

        // Drag horizontal de la botonera (touch o mouse, vía Event.current — IMGUI
        // mapea el primer touch a eventos de mouse). Marca _toolDragMoved para que el
        // release tras un arrastre no dispare el botón de abajo.
        private void HandleToolDrag(Rect viewport, float maxScroll)
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (viewport.Contains(e.mousePosition))
                    { _toolDragging = true; _toolDragMoved = false; _toolLastX = e.mousePosition.x; }
                    break;
                case EventType.MouseDrag:
                    if (_toolDragging)
                    {
                        float dx = e.mousePosition.x - _toolLastX;
                        _toolLastX = e.mousePosition.x;
                        _toolScroll = Mathf.Clamp(_toolScroll - dx, 0f, maxScroll);
                        if (Mathf.Abs(dx) > 1f) _toolDragMoved = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    _toolDragging = false;
                    break;
            }
        }

        private void DrawTool(Rect r, string label, MortuoriumIcons.Icon icon,
                              bool activo, bool enabled, Action onTap)
        {
            Color borde = activo ? T.Red : enabled ? T.Border : T.BorderDim;
            T.Fill(r, activo ? new Color(T.Red.r, T.Red.g, T.Red.b, 0.18f)
                             : new Color(0f, 0f, 0f, 0.55f));
            T.Borde(r, borde);

            // Ícono arriba (cuadrado centrado), etiqueta abajo.
            float ico = 42f;
            MortuoriumIcons.Draw(new Rect(r.x + (r.width - ico) * 0.5f, r.y + 8f, ico, ico),
                                 icon, enabled ? 1f : 0.4f);
            var st = T.Estilo(T.FMono, 10, activo ? T.Cream : enabled ? T.CreamDim : T.Disabled,
                              TextAnchor.UpperCenter, wrap: true);
            GUI.Label(new Rect(r.x + 2f, r.y + ico + 12f, r.width - 4f, r.height - ico - 14f), label, st);

            // Tap (suprimido si venías arrastrando).
            if (GUI.Button(r, GUIContent.none, GUIStyle.none) && enabled && !_toolDragMoved)
                onTap?.Invoke();
        }

        // Herramienta ANCLAS: reabre la colocación (agregar más anclas después de haber
        // cerrado). Mientras esté abierta el loop de corrección queda en pausa, así el
        // mapa no se mueve bajo los pies del jugador justo cuando está apuntando.
        private void AbrirColocacionAnclas()
        {
            _markerSubmenuOpen = false;
            AnchorPointManager.Ensure().AbrirColocacion();
            _fsm.SetMode(ScannerMode.Anchor_Place);
        }

        private void Recalibrar(bool keepVisualPosition)
        {
            if (_imageAnchor == null) _imageAnchor = FindAnyObjectByType<ARImageAnchor>();
            if (_imageAnchor == null) return;
            _markerSubmenuOpen = false;
            ScanStateMachine.Instance?.SetMode(ScannerMode.Calibrating);
            _imageAnchor.RestartTracking(keepVisualPosition);
        }

        // Submenú con los tipos de marcador del catálogo, colgado ARRIBA del botón
        // IDENTIFICAR (el panel inferior está abajo de todo).
        private void DrawMarkerSubmenu(Rect btnIdent)
        {
            var catalog = _markerBuilder.Catalog;
            var types = catalog != null ? catalog.Types : null;
            int n = types != null ? types.Count : 0;

            const float bw = 190f, bh = 46f, gap = 6f;
            float totalH = n == 0 ? 48f : n * bh + (n - 1) * gap;
            float x = Mathf.Min(btnIdent.x, UIScale.VirtualWidth - bw - 8f);
            float y = btnIdent.y - totalH - 14f;

            var panel = new Rect(x - 5f, y - 5f, bw + 10f, totalH + 10f);
            UIBlocker.AddVirtualRect(panel);
            T.Panel(panel, T.BgPanel, T.Border);

            if (n == 0)
            {
                GUI.Label(new Rect(x, y, bw, 44f), "catálogo vacío",
                          T.Estilo(T.FMono, 12, T.Dim, TextAnchor.MiddleCenter));
                return;
            }

            float cy = y;
            foreach (var t in types)
            {
                if (t == null) { cy += bh + gap; continue; }
                var r = new Rect(x, cy, bw, bh);
                if (GUI.Button(r, t.DisplayName, T.Estilo(T.FMono, 13, T.Cream, TextAnchor.MiddleCenter)))
                {
                    _markerBuilder.StartMarker(t);
                    _markerSubmenuOpen = false;
                }
                T.Borde(new Rect(r.x, r.yMax - 1f, r.width, 1f), T.BorderDim, 1f);
                cy += bh + gap;
            }
        }

        // ------------------------------------------- fila contextual del flujo
        // Panel con hints y controles del flujo activo, apilado arriba de las
        // herramientas. yBase es el borde INFERIOR del panel.
        private void DrawContextual(float vw, float yBase)
        {
            var modo = _fsm.Current;

            // El flujo de anclas tiene su propio panel (contador + LISTO / OMITIR).
            if (modo == ScannerMode.Anchor_Place) { DrawContextualAnclas(vw, yBase); return; }
            if (modo == ScannerMode.AutoScan_Capturing) { DrawContextualAutoScan(vw, yBase); return; }

            bool enPared = modo == ScannerMode.Wall_V1 || modo == ScannerMode.Wall_Height || modo == ScannerMode.Wall_Vn;
            bool enCubo  = modo == ScannerMode.Cube_V1 || modo == ScannerMode.Cube_V2 || modo == ScannerMode.Cube_V3;

            string hint = modo switch
            {
                ScannerMode.Wall_V1        => "apuntá al primer vértice de la pared (piso) y tocá COLOCAR",
                ScannerMode.Wall_Height    => "apuntá arriba (cielorraso) y tocá COLOCAR para fijar la altura",
                ScannerMode.Wall_Vn        => $"altura de la polilínea: {(_wallBuilder != null ? _wallBuilder.CurrentPolylineHeight : 0f):F2} m — siguiente vértice y COLOCAR",
                ScannerMode.DoorPickWall   => "tocá la pared donde va el agujero de la puerta",
                ScannerMode.Door_V1        => "apuntá a la esquina inferior de la puerta y tocá COLOCAR",
                ScannerMode.Door_V2        => "apuntá a la esquina superior opuesta y tocá COLOCAR",
                ScannerMode.Cube_V1        => "apuntá a la 1ra esquina del cubo y tocá COLOCAR",
                ScannerMode.Cube_V2        => "apuntá a la esquina opuesta (diagonal) y tocá COLOCAR",
                ScannerMode.Cube_V3        => "apuntá a un 3er punto para fijar la rotación y tocá COLOCAR",
                ScannerMode.Floor_Place    => "apuntá al piso real y tocá COLOCAR (después podés arrastrarlo)",
                ScannerMode.Marker_Place   => $"apuntá a la PARED donde hay {(_markerBuilder != null && _markerBuilder.PendingType != null ? _markerBuilder.PendingType.DisplayName : "el punto")} y tocá COLOCAR",
                ScannerMode.EditMoveTarget => "apuntá a la nueva posición y tocá COLOCAR",
                _ => null,
            };
            if (hint == null) return;

            // Altura del panel según los controles extra del modo.
            float hExtra = 0f;
            if (enPared) hExtra = 44f /*slider ancho*/ + 48f /*terminar*/ + 34f /*cerrar*/;
            if (modo == ScannerMode.Cube_V3) hExtra = 48f;
            float hPanel = 30f /*hint*/ + hExtra + 48f /*cancelar*/ + 24f;

            var panel = new Rect(Pad, yBase - hPanel, vw - Pad * 2f, hPanel);
            UIBlocker.AddVirtualRect(panel);
            T.Fill(panel, new Color(0f, 0f, 0f, 0.65f));
            T.Borde(panel, T.BorderDim);

            float x = panel.x + 12f, w = panel.width - 24f;
            float y = panel.y + 10f;

            GUI.Label(new Rect(x, y, w, 34f), hint,
                      T.Estilo(T.FElite, 12, T.Tan, TextAnchor.UpperLeft, wrap: true));
            y += 34f;

            if (enPared && _wallBuilder != null)
            {
                // Ancho (grosor) de la pared en vivo: propaga a toda la polilinea.
                GUI.Label(new Rect(x, y, w * 0.55f, 20f),
                          $"ancho pared: {_wallBuilder.CurrentPolylineWidth:F2} m",
                          T.Estilo(T.FMono, 11, T.CreamDim));
                float nuevoW = T.Slider(new Rect(x + w * 0.58f, y - 4f, w * 0.42f, 26f),
                                        _wallBuilder.CurrentPolylineWidth, 0.05f, 0.5f);
                if (Mathf.Abs(nuevoW - _wallBuilder.CurrentPolylineWidth) > 0.001f)
                    _wallBuilder.SetPolylineWidth(nuevoW);
                y += 40f;

                // Check de cierre automatico (en memoria, se mantiene entre ediciones).
                var rCerrar = new Rect(x, y, w, 26f);
                var box = new Rect(x, y + 4f, 18f, 18f);
                T.Borde(box, T.Border);
                if (_closePolyline) T.Fill(new Rect(box.x + 4f, box.y + 4f, 10f, 10f), T.Tan);
                if (GUI.Button(rCerrar, GUIContent.none, GUIStyle.none))
                    _closePolyline = !_closePolyline;
                GUI.Label(new Rect(x + 26f, y, w - 26f, 26f), "cerrar (unir al inicio)",
                          T.Estilo(T.FMono, 11, T.CreamDim));
                y += 32f;

                T.Boton(null, new Rect(x, y, w, 40f), "TERMINAR POLILÍNEA", primario: false,
                        () => _wallBuilder.EndPolyline(_closePolyline), fontSize: 15);
                y += 48f;
            }

            if (modo == ScannerMode.Cube_V3)
            {
                T.Boton(null, new Rect(x, y, w, 40f), "CONFIRMAR SIN ROTAR", primario: false,
                        () => _cubeBuilder?.ConfirmCubeAxisAligned(), fontSize: 15);
                y += 48f;
            }

            T.Boton(null, new Rect(x, y, w, 40f), "CANCELAR", primario: false,
                    () => _fsm.SetMode(ScannerMode.Idle), fontSize: 14,
                    textColor: T.Muted);
        }

        // Panel del flujo de anclas: contador, motivo del último rechazo y los dos
        // cierres (LISTO con el mínimo, OMITIR siempre — un cuarto sin superficies
        // trackeables no puede dejar al jugador sin poder escanear).
        private string _errorAnclas;

        private void DrawContextualAutoScan(float vw, float yBase)
        {
            var scan = _autoScan ?? AutoScanController.Instance;
#if UNITY_EDITOR
            const float hPanel = 184f;
#else
            const float hPanel = 142f;
#endif
            var panel = new Rect(Pad, yBase - hPanel, vw - Pad * 2f, hPanel);
            UIBlocker.AddVirtualRect(panel);
            T.Fill(panel, new Color(0f, 0f, 0f, 0.72f));
            T.Borde(panel, T.BorderDim);

            float x = panel.x + 12f, w = panel.width - 24f;
            GUI.Label(new Rect(x, panel.y + 10f, w, 38f),
                      "recorré lentamente todo el ambiente; mirá piso, techo, esquinas y cada pared desde más de un ángulo",
                      T.Estilo(T.FElite, 12, T.Tan, TextAnchor.UpperLeft, wrap: true));

            float coverage = scan != null ? scan.Coverage01 : 0f;
            string status = scan == null
                ? "AutoScan no disponible"
                : $"cobertura: {coverage * 100f:F0}%  ·  superficies: {scan.ObservedSurfaceCount}  ·  " +
                  $"estables: {scan.StableSurfaceCount}  ·  objetos listos: {scan.MaterializableObjectCount}";
            GUI.Label(new Rect(x, panel.y + 52f, w, 22f), status,
                      T.Estilo(T.FMono, 11, T.CreamDim));

            var track = new Rect(x, panel.y + 78f, w, 10f);
            T.Fill(track, new Color(1f, 1f, 1f, 0.08f));
            T.Fill(new Rect(track.x, track.y, track.width * coverage, track.height), T.Green);

#if UNITY_EDITOR
            T.Boton(null, new Rect(x, panel.y + 96f, w, 34f), "SIMULAR HABITACIÓN EN EDITOR",
                    primario: false, () => scan?.AddSyntheticRoomForEditor(), fontSize: 13);
            float cancelY = panel.y + 138f;
#else
            float cancelY = panel.y + 96f;
#endif
            T.Boton(null, new Rect(x, cancelY, w, 34f), "CANCELAR SIN CREAR",
                    primario: false, () => scan?.CancelCapture(), fontSize: 13,
                    textColor: T.Muted);
        }

        private void DrawContextualAnclas(float vw, float yBase)
        {
            var mgr = AnchorPointManager.Ensure();

            const float hPanel = 30f + 26f + 48f + 40f + 24f;
            var panel = new Rect(Pad, yBase - hPanel, vw - Pad * 2f, hPanel);
            UIBlocker.AddVirtualRect(panel);
            T.Fill(panel, new Color(0f, 0f, 0f, 0.65f));
            T.Borde(panel, T.BorderDim);

            float x = panel.x + 12f, w = panel.width - 24f;
            float y = panel.y + 10f;

            GUI.Label(new Rect(x, y, w, 30f),
                      "colocá anclas repartidas por el cuarto y tocá COLOCAR; " +
                      "sirven para que el escaneo no se corra al alejarte",
                      T.Estilo(T.FElite, 12, T.Tan, TextAnchor.UpperLeft, wrap: true));
            y += 32f;

            string estado = mgr.Count < AnchorPointManager.MinAnclas
                ? $"anclas: {mgr.Count} / {AnchorPointManager.MaxAnclas}  ·  faltan {AnchorPointManager.MinAnclas - mgr.Count}"
                : $"anclas: {mgr.Count} / {AnchorPointManager.MaxAnclas}";
            GUI.Label(new Rect(x, y, w * 0.6f, 22f), estado,
                      T.Estilo(T.FMono, 12, mgr.PuedeCerrar ? T.Green : T.Tan));
            if (!string.IsNullOrEmpty(_errorAnclas))
                GUI.Label(new Rect(x + w * 0.6f, y, w * 0.4f, 22f), _errorAnclas,
                          T.Estilo(T.FMono, 10, T.Red, TextAnchor.MiddleRight));
            y += 26f;

            float bw = (w - 10f) * 0.5f;
            T.Boton(null, new Rect(x, y, bw, 40f), "DESHACER", primario: false,
                    () => { mgr.DeshacerUltima(); _errorAnclas = null; },
                    enabled: mgr.Count > 0, fontSize: 14);
            T.Boton(null, new Rect(x + bw + 10f, y, bw, 40f), "LISTO", primario: false,
                    () => CerrarAnclas(cerrar: true), enabled: mgr.PuedeCerrar, fontSize: 14);
            y += 48f;

            T.Boton(null, new Rect(x, y, w, 32f), "OMITIR ANCLAS", primario: false,
                    () => CerrarAnclas(cerrar: false), fontSize: 12, textColor: T.Muted);
        }

        private void CerrarAnclas(bool cerrar)
        {
            var mgr = AnchorPointManager.Ensure();
            if (cerrar) mgr.MarcarListo(); else mgr.Omitir();
            _errorAnclas = null;
            _fsm.SetMode(ScannerMode.Idle);
        }

        private void StartAutoScan()
        {
            _markerSubmenuOpen = false;
            if (_autoScan == null) _autoScan = AutoScanController.Ensure(gameObject);
            _autoScan.StartCapture();
        }

        // -------------------------------------------------------- acciones
        private void OnPlace()
        {
            switch (_fsm.Current)
            {
                case ScannerMode.Wall_V1:
                case ScannerMode.Wall_Height:
                case ScannerMode.Wall_Vn:
                    _wallBuilder?.PlaceVertexAtCurrentReticle();
                    break;
                case ScannerMode.Door_V1:
                case ScannerMode.Door_V2:
                    _doorBuilder?.PlaceCornerAtCurrentReticle();
                    break;
                case ScannerMode.Cube_V1:
                case ScannerMode.Cube_V2:
                case ScannerMode.Cube_V3:
                    _cubeBuilder?.PlaceCubeVertexAtCurrentReticle();
                    break;
                case ScannerMode.Floor_Place:
                    PlaceFloorAtCurrentReticle();
                    break;
                case ScannerMode.Marker_Place:
                    _markerBuilder?.PlaceAtCurrentReticle();
                    break;
                case ScannerMode.EditMoveTarget:
                    MoveTargetToCurrentReticle();
                    break;
                case ScannerMode.Anchor_Place:
                    _errorAnclas = AnchorPointManager.Ensure().TryColocar(out var err) ? null : err;
                    break;
            }
        }

        private void PlaceFloorAtCurrentReticle()
        {
            if (WorldOrigin.Instance == null) return;
            var hit = RaycastResolver.Instance?.ResolveFromScreenCenter() ?? ResolvedHit.Miss;
            if (!hit.Hit) return;
            FloorPoint.Create(WorldOrigin.Instance.ToRelative(hit.Position));
            _fsm.SetMode(ScannerMode.Idle);
        }

        private void MoveTargetToCurrentReticle()
        {
            var sel = _fsm.CurrentSelection;
            if (sel == null) { _fsm.SetMode(ScannerMode.Idle); return; }
            var hit = RaycastResolver.Instance.ResolveFromScreenCenter();
            if (!hit.Hit) return;

            // El marcador se mueve relativo a SU pared: usamos el mismo raycast fisico
            // contra la pared que la colocacion (NO el hit AR, que no cae sobre la
            // pared y dejaba el marcador corrido). No salta a otra pared ni cambia de cara.
            if (sel is MarkerObject marker)
            {
                var mwall = marker.Wall;
                if (mwall != null && _markerBuilder != null &&
                    _markerBuilder.TryResolveOnSpecificWall(mwall, out var mu, out var mv))
                    marker.SetUV(mu, mv);
                _fsm.SetMode(ScannerMode.Selected);
                return;
            }

            var local = WorldOrigin.Instance.ToRelative(hit.Position);
            if (sel is CubeObject cube)
            {
                cube.transform.localPosition = local;
            }
            else if (sel is WallObject wall)
            {
                // Mover una pared: trasladamos preservando direccion (movemos aLocal a local,
                // y bLocal por el mismo delta).
                var delta = local - wall.ALocal;
                wall.SetEndpoints(wall.ALocal + delta, wall.BLocal + delta);
            }
            _fsm.SetMode(ScannerMode.Selected);
        }
    }
}
