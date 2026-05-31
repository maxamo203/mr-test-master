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

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            if (_wallBuilder == null) _wallBuilder = FindFirstObjectByType<WallBuilder>();
            if (_doorBuilder == null) _doorBuilder = FindFirstObjectByType<DoorBuilder>();
            if (_cubeBuilder == null) _cubeBuilder = FindFirstObjectByType<CubeBuilder>();
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
            m == ScannerMode.Cube_Place || m == ScannerMode.EditMoveTarget;

        private static Texture2D _bgTex;
        private static Texture2D BG()
        {
            if (_bgTex == null) { _bgTex = new Texture2D(1,1); _bgTex.SetPixel(0,0, new Color(0,0,0,0.7f)); _bgTex.Apply(); }
            return _bgTex;
        }

        private void OnGUI()
        {
            if (_fsm == null) return;

            // ── Reticula central ──────────────────────────────────────────
            if (IsPlacingMode(_fsm.Current))
            {
                float cx = Screen.width  * 0.5f;
                float cy = Screen.height * 0.5f;
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
            GUILayout.BeginArea(new Rect(10, 10, 220, Screen.height - 20));
            DrawModeButtons();
            GUILayout.EndArea();

            // ── Boton Colocar (centro inferior, grande, solo en modos placing)
            if (IsPlacingMode(_fsm.Current))
            {
                float w = 220, h = 90;
                if (GUI.Button(new Rect((Screen.width - w) * 0.5f, Screen.height - h - 30, w, h), "COLOCAR"))
                    OnPlace();
            }

            // ── Slider de fallback distance (esquina inf-izq, solo placing)
            if (IsPlacingMode(_fsm.Current) && RaycastResolver.Instance != null)
            {
                GUILayout.BeginArea(new Rect(10, Screen.height - 90, 320, 80), GUIContent.none);
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
            }

            GUI.enabled = _fsm.Current != ScannerMode.Idle && _fsm.Current != ScannerMode.Selected;
            if (GUILayout.Button("Cancelar", GUILayout.Height(40))) _fsm.SetMode(ScannerMode.Idle);

            GUI.enabled = true;
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
                case ScannerMode.Cube_Place:
                    _cubeBuilder?.PlaceCubeAtCurrentReticle();
                    break;
                case ScannerMode.EditMoveTarget:
                    MoveTargetToCurrentReticle();
                    break;
            }
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
