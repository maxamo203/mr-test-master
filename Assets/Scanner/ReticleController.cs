using System;
using UnityEngine;
using T = MortuoriumTheme;

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

        private ScanStateMachine _fsm;
        private ResolvedHit _lastHit;
        private Camera _camera;

        // Último hit resuelto desde el centro (para DebugHud/DebugRaycastUI).
        public ResolvedHit LastHit => _lastHit;

        // Submenú de "Identificar" (tipos de marcador del catálogo) abierto.
        private bool _markerSubmenuOpen;

        // Si al terminar la polilinea se cierra el bucle (ultimo vertice -> primero).
        // En memoria: se mantiene entre polilineas, no persiste entre sesiones.
        private bool _closePolyline = true;

        private const float Pad = 16f;

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            _camera = Camera.main;
            if (_wallBuilder == null) _wallBuilder = FindFirstObjectByType<WallBuilder>();
            if (_doorBuilder == null) _doorBuilder = FindFirstObjectByType<DoorBuilder>();
            if (_cubeBuilder == null) _cubeBuilder = FindFirstObjectByType<CubeBuilder>();
            if (_markerBuilder == null) _markerBuilder = FindFirstObjectByType<MarkerBuilder>();

            // Fantasma en vivo de lo que se va a colocar. Lo instanciamos acá para no
            // tener que cablearlo en la escena (encuentra los builders por su cuenta).
            if (GetComponent<PlacementPreview>() == null) gameObject.AddComponent<PlacementPreview>();
        }

        private void Update()
        {
            if (_fsm == null || RaycastResolver.Instance == null) return;
            if (!IsPlacingMode(_fsm.Current)) return;
            _lastHit = RaycastResolver.Instance.ResolveFromScreenCenter();
        }

        private bool IsPlacingMode(ScannerMode m) =>
            m == ScannerMode.Wall_V1 || m == ScannerMode.Wall_Height || m == ScannerMode.Wall_Vn ||
            m == ScannerMode.Door_V1 || m == ScannerMode.Door_V2 ||
            m == ScannerMode.Cube_V1 || m == ScannerMode.Cube_V2 || m == ScannerMode.Cube_V3 ||
            m == ScannerMode.Floor_Place || m == ScannerMode.Marker_Place || m == ScannerMode.EditMoveTarget;

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

            const float r = 24f, tick = 14f, g = 2f;   // radio, largo del tick, grosor/2
            T.Fill(new Rect(cx - g, cy - r - tick, g * 2f, tick), color);   // arriba
            T.Fill(new Rect(cx - g, cy + r, g * 2f, tick), color);          // abajo
            T.Fill(new Rect(cx - r - tick, cy - g, tick, g * 2f), color);   // izq
            T.Fill(new Rect(cx + r, cy - g, tick, g * 2f), color);          // der
            T.Fill(new Rect(cx - g, cy - g, g * 2f, g * 2f), color);        // centro
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

        // ---------------------------------------------------- panel inferior
        private void DrawPanelInferior(float vw, float vh)
        {
            var modo = _fsm.Current;

            float botonesH = 56f;
            float toolsH = 84f;
            float yBotones = vh - 40f - botonesH;
            float yTools = yBotones - 12f - toolsH;

            T.Gradiente(new Rect(0, yTools - 60f, vw, vh - (yTools - 60f)), 0.92f, haciaAbajo: false);
            UIBlocker.AddVirtualRect(new Rect(0, yTools - 12f, vw, vh - yTools + 12f));

            DrawContextual(vw, yTools - 20f);
            DrawHerramientas(vw, yTools, toolsH);

            // COLOCAR + GUARDAR ESCANEO.
            float bw = (vw - Pad * 2f - 10f) / 2f;
            bool puedeColocar = IsPlacingMode(modo);
            bool puedeGuardar = (modo == ScannerMode.Idle || modo == ScannerMode.Selected) && HayContenido();

            var rColocar = new Rect(Pad, yBotones, bw, botonesH);
            bool focoColocar = puedeColocar;
            T.Fill(rColocar, focoColocar ? new Color(T.Tan.r, T.Tan.g, T.Tan.b, 0.18f)
                                         : new Color(0f, 0f, 0f, 0.4f));
            T.Borde(rColocar, puedeColocar ? T.Tan : T.BorderDim);
            if (GUI.Button(rColocar, "COLOCAR",
                           T.Estilo(T.FBebas, 18, puedeColocar ? T.Cream : T.Disabled, TextAnchor.MiddleCenter))
                && puedeColocar)
                OnPlace();

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

        // Fila de herramientas (estilo prototipo). Solo inician flujo desde Idle;
        // la herramienta del flujo activo queda resaltada.
        private void DrawHerramientas(float vw, float y, float h)
        {
            var modo = _fsm.Current;
            bool idle = modo == ScannerMode.Idle;

            bool enPared  = modo == ScannerMode.Wall_V1 || modo == ScannerMode.Wall_Height || modo == ScannerMode.Wall_Vn;
            bool enCubo   = modo == ScannerMode.Cube_V1 || modo == ScannerMode.Cube_V2 || modo == ScannerMode.Cube_V3;
            bool enPuerta = modo == ScannerMode.DoorPickWall || modo == ScannerMode.Door_V1 || modo == ScannerMode.Door_V2;
            bool enMarker = modo == ScannerMode.Marker_Place;
            bool enPiso   = modo == ScannerMode.Floor_Place;

            float cw = (vw - Pad * 2f - 4f * 8f) / 5f;
            float x = Pad;

            DrawTool(new Rect(x, y, cw, h), "PARED", enPared, idle,
                     () => _wallBuilder?.StartPolyline());
            x += cw + 8f;
            DrawTool(new Rect(x, y, cw, h), "CUBO", enCubo, idle,
                     () => _cubeBuilder?.StartCube());
            x += cw + 8f;
            DrawTool(new Rect(x, y, cw, h), "AGUJERO\nPUERTA", enPuerta, idle,
                     () => _doorBuilder?.StartDoor());
            x += cw + 8f;
            var rIdent = new Rect(x, y, cw, h);
            DrawTool(rIdent, "IDENTI-\nFICAR", enMarker || _markerSubmenuOpen, idle,
                     () => _markerSubmenuOpen = !_markerSubmenuOpen);
            x += cw + 8f;
            string lblPiso = FloorPoint.Instance != null ? "PISO\n(REUBICAR)" : "PISO";
            DrawTool(new Rect(x, y, cw, h), lblPiso, enPiso, idle,
                     () => _fsm.SetMode(ScannerMode.Floor_Place));

            if (_markerSubmenuOpen && idle && _markerBuilder != null)
                DrawMarkerSubmenu(rIdent);
            else if (!idle)
                _markerSubmenuOpen = false;
        }

        private void DrawTool(Rect r, string label, bool activo, bool enabled, Action onTap)
        {
            Color borde = activo ? T.Red : enabled ? T.Border : T.BorderDim;
            T.Fill(r, activo ? new Color(T.Red.r, T.Red.g, T.Red.b, 0.18f)
                             : new Color(0f, 0f, 0f, 0.55f));
            T.Borde(r, borde);
            var st = T.Estilo(T.FMono, 11, activo ? T.Cream : enabled ? T.CreamDim : T.Disabled,
                              TextAnchor.MiddleCenter);
            if (GUI.Button(r, label, st) && enabled) onTap?.Invoke();
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
