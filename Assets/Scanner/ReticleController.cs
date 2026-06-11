using UnityEngine;

namespace Scanner
{
    // Reticula central + boton Colocar + slider de distancia para fallback.
    // Dibuja todo con OnGUI/IMGUI (cero dependencia de UI Toolkit/uGUI prefab).
    // Tambien muestra el RaycastSource actual para diagnostico.
    public class ReticleController : MonoBehaviour
    {
        [SerializeField] private WallBuilder _wallBuilder;
        [SerializeField] private DoorBuilder _doorBuilder;
        [SerializeField] private CubeBuilder _cubeBuilder;

        private ScanStateMachine _fsm;
        private ResolvedHit _lastHit;
        private Camera _camera;

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            _camera = Camera.main;
            if (_wallBuilder == null) _wallBuilder = FindFirstObjectByType<WallBuilder>();
            if (_doorBuilder == null) _doorBuilder = FindFirstObjectByType<DoorBuilder>();
            if (_cubeBuilder == null) _cubeBuilder = FindFirstObjectByType<CubeBuilder>();

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
            m == ScannerMode.Floor_Place || m == ScannerMode.EditMoveTarget;

        private static Texture2D _bgTex;
        private static Texture2D BG()
        {
            if (_bgTex == null) { _bgTex = new Texture2D(1,1); _bgTex.SetPixel(0,0, new Color(0,0,0,0.7f)); _bgTex.Apply(); }
            return _bgTex;
        }

        private void OnGUI()
        {
            if (_fsm == null) return;

            UIScale.Begin();
            float vw = UIScale.VirtualWidth;
            float vh = UIScale.VirtualHeight;

            // ── Reticula central ──────────────────────────────────────────
            if (IsPlacingMode(_fsm.Current))
            {
                float cx = vw * 0.5f;
                float cy = vh * 0.5f;
                float s  = 18f;
                var ringColor = _lastHit.Source switch
                {
                    RaycastSource.LidarMesh      => Color.green,
                    RaycastSource.ArDepth        => new Color(0.5f, 1f, 0.5f),
                    RaycastSource.ArPlane        => new Color(0.7f, 1f, 1f),
                    RaycastSource.ArFeaturePoint => Color.yellow,
                    RaycastSource.Fallback       => new Color(1f, 0.6f, 0.4f),
                    _                            => Color.white,
                };
                var prev = GUI.color;
                GUI.color = ringColor;
                GUI.Box(new Rect(cx - s, cy - 1, s * 2, 2), GUIContent.none);
                GUI.Box(new Rect(cx - 1, cy - s, 2, s * 2), GUIContent.none);
                GUI.color = prev;

                // Label fuente del raycast
                var lab = new GUIStyle { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                lab.normal.textColor = ringColor;
                GUI.Label(new Rect(cx - 200, cy + s + 4, 400, 30), $"src: {_lastHit.Source}", lab);
            }

            // ── Botonera de modo + Colocar ────────────────────────────────
            var modeArea = new Rect(10, 10, 220, vh - 20);
            UIBlocker.AddVirtualRect(modeArea);
            GUILayout.BeginArea(modeArea);
            DrawModeButtons();
            GUILayout.EndArea();

            // ── Boton Colocar (centro inferior, grande, solo en modos placing)
            if (IsPlacingMode(_fsm.Current))
            {
                float w = 220, h = 90;
                var placeRect = new Rect((vw - w) * 0.5f, vh - h - 30, w, h);
                UIBlocker.AddVirtualRect(placeRect);
                if (GUI.Button(placeRect, "COLOCAR"))
                    OnPlace();
            }

            // ── Slider de fallback distance (esquina inf-izq, solo placing)
            if (IsPlacingMode(_fsm.Current) && RaycastResolver.Instance != null)
            {
                var sliderArea = new Rect(10, vh - 90, 320, 80);
                UIBlocker.AddVirtualRect(sliderArea);
                GUILayout.BeginArea(sliderArea, GUIContent.none);
                GUILayout.Label($"Distancia fallback: {RaycastResolver.Instance.FallbackDistance:F2}m");
                RaycastResolver.Instance.FallbackDistance =
                    GUILayout.HorizontalSlider(RaycastResolver.Instance.FallbackDistance, 0.3f, 5f);
                GUILayout.EndArea();
            }
        }

        private void DrawModeButtons()
        {
            var style = GUI.skin.button;
            GUILayout.Label($"Modo: {_fsm.Current}", new GUIStyle { fontSize = 20, normal = { textColor = Color.white, background = BG() }, padding = new RectOffset(8,8,4,4) });
            GUILayout.Space(4);

            GUI.enabled = _fsm.Current == ScannerMode.Idle;
            if (GUILayout.Button("Pared (polilinea)", GUILayout.Height(50))) _wallBuilder?.StartPolyline();
            if (GUILayout.Button("Puerta",            GUILayout.Height(50))) _doorBuilder?.StartDoor();
            if (GUILayout.Button("Cubo",              GUILayout.Height(50))) _cubeBuilder?.StartCube();
            string pisoLabel = FloorPoint.Instance != null ? "Piso (reubicar)" : "Piso";
            if (GUILayout.Button(pisoLabel,           GUILayout.Height(50))) _fsm.SetMode(ScannerMode.Floor_Place);

            GUI.enabled = _fsm.Current == ScannerMode.Wall_V1
                       || _fsm.Current == ScannerMode.Wall_Height
                       || _fsm.Current == ScannerMode.Wall_Vn;
            if (GUILayout.Button("Terminar polilinea", GUILayout.Height(40))) _wallBuilder?.EndPolyline();

            // Si estamos en polilinea, mostrar la altura calibrada.
            if ((_fsm.Current == ScannerMode.Wall_Vn || _fsm.Current == ScannerMode.Wall_Height) && _wallBuilder != null)
            {
                GUI.enabled = true;
                var hStyle = new GUIStyle { fontSize = 18, normal = { textColor = Color.yellow } };
                string hint = _fsm.Current == ScannerMode.Wall_Height
                    ? "Apunta arriba (cielorraso) y COLOCAR"
                    : $"Altura polilinea: {_wallBuilder.CurrentPolylineHeight:F2}m";
                GUILayout.Label(hint, hStyle);

                // Slider de grosor (ancho) en vivo: propaga a toda la polilinea.
                GUILayout.Label($"Ancho pared: {_wallBuilder.CurrentPolylineWidth:F2}m", hStyle);
                float newW = GUILayout.HorizontalSlider(_wallBuilder.CurrentPolylineWidth, 0.05f, 0.5f);
                if (Mathf.Abs(newW - _wallBuilder.CurrentPolylineWidth) > 0.001f)
                    _wallBuilder.SetPolylineWidth(newW);
            }

            // Hint del flujo de cubo (2 esquinas de la diagonal + 3er punto de rotacion).
            if (_fsm.Current == ScannerMode.Cube_V1 || _fsm.Current == ScannerMode.Cube_V2
             || _fsm.Current == ScannerMode.Cube_V3)
            {
                GUI.enabled = true;
                var cubeHint = new GUIStyle { fontSize = 18, normal = { textColor = Color.yellow } };
                string hint = _fsm.Current switch
                {
                    ScannerMode.Cube_V1 => "Apunta a la 1ra esquina del cubo y COLOCAR",
                    ScannerMode.Cube_V2 => "Apunta a la esquina opuesta (diagonal) y COLOCAR",
                    _                   => "Apunta a un 3er punto para fijar la rotacion y COLOCAR",
                };
                GUILayout.Label(hint, cubeHint);

                // En el 3er paso, opcion de cerrar sin rotar (cubo axis-aligned).
                if (_fsm.Current == ScannerMode.Cube_V3 &&
                    GUILayout.Button("Confirmar sin rotar", GUILayout.Height(40)))
                    _cubeBuilder?.ConfirmCubeAxisAligned();
            }

            // Hint del flujo de piso.
            if (_fsm.Current == ScannerMode.Floor_Place)
            {
                GUI.enabled = true;
                var floorHint = new GUIStyle { fontSize = 18, normal = { textColor = Color.yellow } };
                GUILayout.Label("Apunta al piso real y COLOCAR\n(podes reubicarlo despues arrastrandolo)", floorHint);
            }

            GUI.enabled = _fsm.Current != ScannerMode.Idle && _fsm.Current != ScannerMode.Selected;
            if (GUILayout.Button("Cancelar", GUILayout.Height(40))) _fsm.SetMode(ScannerMode.Idle);

            GUI.enabled = true;

            // Ubicacion virtual del jugador, en coordenadas relativas al anchor.
            DrawPlayerLocation();
        }

        private void DrawPlayerLocation()
        {
            if (_camera == null) _camera = Camera.main;
            var wo = WorldOrigin.Instance;

            GUILayout.Space(10);
            var labelStyle = new GUIStyle
            {
                fontSize = 18,
                normal   = { textColor = Color.white, background = BG() },
                padding  = new RectOffset(8, 8, 6, 6),
            };

            if (_camera == null || wo == null || !wo.IsReady)
            {
                GUILayout.Label("Jugador: (sin calibrar)", labelStyle);
                return;
            }

            var p = wo.ToRelative(_camera.transform.position);
            GUILayout.Label($"Jugador (rel. anchor):\nX {p.x:F2}  Y {p.y:F2}  Z {p.z:F2} m", labelStyle);
        }

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
