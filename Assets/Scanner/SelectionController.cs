using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Scanner
{
    // Manejo de tap sobre objetos colocados.
    //
    // Lee input via EnhancedTouchSupport (touches buffereados entre frames del
    // Input System, mas robusto en Android) + Mouse.current como fallback de editor.
    //
    // Tambien mantenemos OnGUI como tercer canal por compatibilidad con plataformas
    // que sigan generando eventos IMGUI. Dedupe por frame counter.
    [DefaultExecutionOrder(1000)]
    public class SelectionController : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private DoorBuilder _doorBuilder;
        [Tooltip("Mostrar HUD de diagnostico (input state) en pantalla.")]
        [SerializeField] private bool _showDebugHud = true;

        private int _placedLayerMask;
        private int _gizmoLayerMask;
        private int _lastPickFrame = -1;

        // diagnostico
        private string _diagState = "(sin tap)";
        private Vector2 _lastTapPos;
        private int _lastTapFrame;
        private string _lastPickResult = "(nada)";

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;
            if (_doorBuilder == null) _doorBuilder = FindFirstObjectByType<DoorBuilder>();

            int placedLayer = LayerMask.NameToLayer("Placed");
            _placedLayerMask = placedLayer >= 0 ? (1 << placedLayer) : 0;
            int gizmoLayer = LayerMask.NameToLayer("Gizmo");
            _gizmoLayerMask = gizmoLayer >= 0 ? (1 << gizmoLayer) : 0;

            // EnhancedTouchSupport bufferea touches del Input System entre frames.
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        }

        private void Update()
        {
            if (_lastPickFrame == Time.frameCount) return;

            // 1) EnhancedTouch (Android / iOS)
            foreach (var t in ETouch.activeTouches)
            {
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    _diagState = $"ETouch.Ended id={t.touchId}";
                    _lastTapPos = t.screenPosition;
                    _lastTapFrame = Time.frameCount;
                    _lastPickFrame = Time.frameCount;
                    TryPick(t.screenPosition);
                    return;
                }
            }

            // 2) Mouse (editor / PC)
            var ms = Mouse.current;
            if (ms != null && ms.leftButton.wasReleasedThisFrame)
            {
                _diagState = "Mouse.Released";
                _lastTapPos = ms.position.ReadValue();
                _lastTapFrame = Time.frameCount;
                _lastPickFrame = Time.frameCount;
                TryPick(_lastTapPos);
            }
        }

        private void OnGUI()
        {
            // Fallback IMGUI por compatibilidad. No bloquea si los otros canales ya pickearon.
            if (_lastPickFrame != Time.frameCount)
            {
                var e = Event.current;
                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    _diagState = "IMGUI.MouseUp";
                    _lastTapPos = new Vector2(e.mousePosition.x, Screen.height - e.mousePosition.y);
                    _lastTapFrame = Time.frameCount;
                    _lastPickFrame = Time.frameCount;
                    TryPick(_lastTapPos);
                }
            }

            // HUD de diagnostico.
            if (_showDebugHud)
            {
                var ts = Touchscreen.current;
                var style = new GUIStyle
                {
                    fontSize = 22,
                    normal = { textColor = Color.cyan, background = MakeBg() },
                    padding = new RectOffset(8, 8, 4, 4),
                };
                string txt =
                    $"[SelDbg] Touchscreen={(ts != null ? "OK" : "NULL")}  " +
                    $"ETouches={ETouch.activeTouches.Count}  Mouse={(Mouse.current != null ? "OK" : "NULL")}\n" +
                    $"LastEvent: {_diagState}  pos={_lastTapPos}  frame={_lastTapFrame}\n" +
                    $"LastPick:  {_lastPickResult}";
                GUI.Label(new Rect(10, Screen.height - 110, Screen.width - 20, 100), txt, style);
            }
        }

        private static Texture2D _bgTex;
        private static Texture2D MakeBg()
        {
            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.85f));
                _bgTex.Apply();
            }
            return _bgTex;
        }

        [Tooltip("Pixeles de tolerancia para tap sobre una esfera-handle (screen-space picking).")]
        [SerializeField] private float _handleTapPixelRadius = 70f;

        private void TryPick(Vector2 screenPoint)
        {
            var fsm = ScanStateMachine.Instance;
            if (_camera == null) _camera = Camera.main;
            if (fsm == null || _camera == null) { _lastPickResult = "fsm/cam null"; return; }

            Physics.SyncTransforms();

            // ── 1) SCREEN-SPACE PICKING para handles (esferas) ───────────────
            // Independiente de los SphereColliders. Proyectamos la posicion world
            // de cada handle a screen-space y medimos distancia en pixels al tap.
            // Esto siempre funciona, aunque PhysX no haya sincronizado los colliders.
            ISelectable handleHit = null;
            float bestHandleDist  = _handleTapPixelRadius;
            int handlesChecked    = 0;

            foreach (var vh in FindObjectsByType<WallVertexHandle>(FindObjectsSortMode.None))
            {
                if (vh == null) continue;
                handlesChecked++;
                var sp = _camera.WorldToScreenPoint(vh.transform.position);
                if (sp.z <= 0f) continue; // detras de la camara
                var d = Vector2.Distance(new Vector2(sp.x, sp.y), screenPoint);
                if (d < bestHandleDist) { bestHandleDist = d; handleHit = vh; }
            }
            foreach (var ph in FindObjectsByType<PolylinePreviewHandle>(FindObjectsSortMode.None))
            {
                if (ph == null) continue;
                handlesChecked++;
                var sp = _camera.WorldToScreenPoint(ph.transform.position);
                if (sp.z <= 0f) continue;
                var d = Vector2.Distance(new Vector2(sp.x, sp.y), screenPoint);
                if (d < bestHandleDist) { bestHandleDist = d; handleHit = ph; }
            }
            foreach (var cv in FindObjectsByType<CubeVertexHandle>(FindObjectsSortMode.None))
            {
                if (cv == null) continue;
                handlesChecked++;
                var sp = _camera.WorldToScreenPoint(cv.transform.position);
                if (sp.z <= 0f) continue;
                var d = Vector2.Distance(new Vector2(sp.x, sp.y), screenPoint);
                if (d < bestHandleDist) { bestHandleDist = d; handleHit = cv; }
            }

            var ray = _camera.ScreenPointToRay(screenPoint);

            if (_gizmoLayerMask != 0 && Physics.Raycast(ray, out _, 100f, _gizmoLayerMask))
            {
                _lastPickResult = "gizmo-skip";
                return;
            }

            // ── 2) RAYCAST para paredes y cubos ─────────────────────────────
            int mask = _placedLayerMask != 0 ? _placedLayerMask : ~0;
            var rayHits = Physics.RaycastAll(ray, 50f, mask);

            ISelectable bestAny = null;
            float bestAnyDist   = float.MaxValue;
            foreach (var hit in rayHits)
            {
                if (hit.collider == null) continue;
                var sel = hit.collider.GetComponentInParent<ISelectable>();
                if (sel == null) continue;
                // Excluir handles del raycast: ya los manejamos por screen-space.
                if (sel.Kind == SelectableKind.WallVertex) continue;
                if (sel.Kind == SelectableKind.CubeVertex) continue;
                if (hit.distance < bestAnyDist) { bestAnyDist = hit.distance; bestAny = sel; }
            }

            // ── 3) Prioridad: el handle siempre gana si fue tocado ─────────
            ISelectable picked = handleHit ?? bestAny;

            if (picked == null)
            {
                _lastPickResult = $"miss handles={handlesChecked} ray={rayHits.Length}";
                return;
            }
            bool inPolyline = fsm.Current == ScannerMode.Wall_V1
                           || fsm.Current == ScannerMode.Wall_Height
                           || fsm.Current == ScannerMode.Wall_Vn;

            if (fsm.Current == ScannerMode.DoorPickWall && picked is WallObject wall)
            {
                _doorBuilder?.OnWallPicked(wall);
                _lastPickResult = "door pickwall";
                return;
            }

            if (inPolyline)
            {
                if (picked.Kind == SelectableKind.WallVertex)
                {
                    fsm.SetSelection(picked);
                    _lastPickResult = $"poly-vertex sel {picked.Kind}";
                }
                else
                {
                    _lastPickResult = $"poly-ignored {picked.Kind} (handles checked: {handlesChecked}, none in tap range)";
                }
                return;
            }

            if (fsm.Current == ScannerMode.Idle || fsm.Current == ScannerMode.Selected)
            {
                fsm.SetSelection(picked);
                _lastPickResult = $"sel {picked.Kind}";
            }
            else
            {
                _lastPickResult = $"mode-block {fsm.Current}";
            }
        }
    }
}
