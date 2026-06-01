using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Scanner
{
    // Gizmos on-screen estilo Unity Editor.
    //
    //   - 3 flechas (cubos alargados) en +X / +Y / +Z para Move.
    //   - 3 cubitos al final de cada eje para Scale.
    //   - 1 anillo horizontal (en plano XZ del objeto) para rotar en Y.
    //     (Solo Y porque casi siempre los muebles estan apoyados; no querras
    //      rotarlos en X/Z.)
    //
    // El gizmoRoot se posiciona/orienta sobre el target y escala segun la
    // distancia a la camara para tamano constante en pantalla.
    public class TransformGizmoController : MonoBehaviour
    {
        public static TransformGizmoController Instance { get; private set; }

        [SerializeField] private Camera _camera;
        [Tooltip("Tamanio aparente del gizmo: a 1m de la camara, el gizmo ocupa este factor (en metros). Se sube por encima del tamanio del objeto para que sus handles salgan del cubo y sean tappeables.")]
        [SerializeField] private float _screenSizeFactor = 0.4f;

        private Transform _target;
        private GameObject _root;
        private int _gizmoLayer = -1;
        private int _gizmoLayerMask = 0;
        private bool _moveOnly;

        // Estado de drag
        private GizmoHandle _activeHandle;
        private Vector2 _lastTouchPos;

        private readonly System.Collections.Generic.List<GameObject> _moveHandles   = new();
        private readonly System.Collections.Generic.List<GameObject> _scaleHandles  = new();
        private readonly System.Collections.Generic.List<GameObject> _rotateHandles = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            if (_camera == null) _camera = Camera.main;

            _gizmoLayer = LayerMask.NameToLayer("Gizmo");
            if (_gizmoLayer < 0)
                Debug.LogWarning("[TransformGizmoController] Layer 'Gizmo' no existe. Crearla en Tags and Layers.");
            else
                _gizmoLayerMask = 1 << _gizmoLayer;

            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();

            BuildGizmoRoot();
            _root.SetActive(false);
        }

        public void Attach(Transform target, bool moveOnly = false)
        {
            _target   = target;
            _moveOnly = moveOnly;
            _root.SetActive(target != null);
            // Mostrar/ocultar handles segun el modo.
            foreach (var h in _moveHandles)   if (h != null) h.SetActive(true);
            foreach (var h in _scaleHandles)  if (h != null) h.SetActive(!moveOnly);
            foreach (var h in _rotateHandles) if (h != null) h.SetActive(!moveOnly);
        }

        public void Detach()
        {
            _activeHandle = null;
            _target = null;
            _root.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_target == null) { _root.SetActive(false); return; }
            _root.SetActive(true);

            _root.transform.position = _target.position;
            _root.transform.rotation = _target.rotation;

            float dist = Vector3.Distance(_camera.transform.position, _target.position);
            float s    = Mathf.Max(0.05f, dist * _screenSizeFactor);
            _root.transform.localScale = new Vector3(s, s, s);

            HandleInput();
        }

        // Priority Scale/Rotate sobre Move para que la flecha larga del Move
        // no se coma los taps en los handles Scale/Rotate cercanos.
        private static int PriorityOf(GizmoOperation op)
        {
            switch (op)
            {
                case GizmoOperation.Scale:  return 2;
                case GizmoOperation.Rotate: return 2;
                default:                    return 1;
            }
        }

        // Lee el "puntero primario" via EnhancedTouchSupport (mas robusto en Android)
        // o Mouse.current como fallback de editor.
        private bool ReadPointer(out Vector2 pos, out bool began, out bool held, out bool released)
        {
            pos = Vector2.zero; began = false; held = false; released = false;

            if (ETouch.activeTouches.Count > 0)
            {
                var t = ETouch.activeTouches[0];
                pos      = t.screenPosition;
                began    = t.phase == UnityEngine.InputSystem.TouchPhase.Began;
                released = t.phase == UnityEngine.InputSystem.TouchPhase.Ended
                        || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled;
                held     = !released;
                return true;
            }

            var ms = Mouse.current;
            if (ms != null)
            {
                bool pressed = ms.leftButton.isPressed;
                began    = ms.leftButton.wasPressedThisFrame;
                released = ms.leftButton.wasReleasedThisFrame;
                if (pressed || began || released)
                {
                    pos  = ms.position.ReadValue();
                    held = pressed;
                    return true;
                }
            }
            return false;
        }

        private void HandleInput()
        {
            if (!ReadPointer(out var pos, out var began, out var held, out var released))
            {
                _activeHandle = null;
                return;
            }

            if (began)
            {
                // Sync transforms para que los colliders de los handles esten
                // donde el transform los puso. El proyecto tiene autoSync=false.
                Physics.SyncTransforms();

                var ray = _camera.ScreenPointToRay(pos);
                if (_gizmoLayerMask != 0)
                {
                    var hits = Physics.RaycastAll(ray, 100f, _gizmoLayerMask);
                    GizmoHandle best = null;
                    int bestPriority = -1;
                    float bestDist = float.MaxValue;
                    foreach (var hit in hits)
                    {
                        var h = hit.collider.GetComponentInParent<GizmoHandle>();
                        if (h == null) continue;
                        int pri = PriorityOf(h.Operation);
                        if (pri > bestPriority || (pri == bestPriority && hit.distance < bestDist))
                        {
                            best = h;
                            bestPriority = pri;
                            bestDist = hit.distance;
                        }
                    }
                    if (best != null)
                    {
                        _activeHandle = best;
                        _lastTouchPos = pos;
                    }
                }
            }
            else if (released)
            {
                _activeHandle = null;
            }
            else if (held && _activeHandle != null)
            {
                var delta = pos - _lastTouchPos;
                _lastTouchPos = pos;
                if (delta.sqrMagnitude < 0.5f) return; // sin movimiento real

                var worldAxis = _root.transform.rotation * _activeHandle.LocalAxis();
                switch (_activeHandle.Operation)
                {
                    case GizmoOperation.Move:
                        ApplyMove(worldAxis, delta);
                        break;
                    case GizmoOperation.Scale:
                        ApplyScale(_activeHandle.Axis, worldAxis, delta);
                        break;
                    case GizmoOperation.Rotate:
                        ApplyRotateY(worldAxis, pos, delta);
                        break;
                }
            }
        }

        private void ApplyMove(Vector3 worldAxis, Vector2 screenDelta)
        {
            var p0 = _camera.WorldToScreenPoint(_target.position);
            var p1 = _camera.WorldToScreenPoint(_target.position + worldAxis);
            var axisScreen = (Vector2)(p1 - p0);
            if (axisScreen.sqrMagnitude < 1f) return;

            float pixelsPerMeter = axisScreen.magnitude;
            float fingerProj     = Vector2.Dot(screenDelta, axisScreen.normalized);
            float meters         = fingerProj / pixelsPerMeter;

            _target.position += worldAxis * meters;
        }

        private void ApplyScale(GizmoAxis axis, Vector3 worldAxis, Vector2 screenDelta)
        {
            var p0 = _camera.WorldToScreenPoint(_target.position);
            var p1 = _camera.WorldToScreenPoint(_target.position + worldAxis);
            var axisScreen = (Vector2)(p1 - p0);
            if (axisScreen.sqrMagnitude < 1f) return;

            float fingerProj = Vector2.Dot(screenDelta, axisScreen.normalized);
            float factor     = 1f + fingerProj * 0.005f;

            var s = _target.localScale;
            switch (axis)
            {
                case GizmoAxis.X: s.x = Mathf.Max(0.01f, s.x * factor); break;
                case GizmoAxis.Y: s.y = Mathf.Max(0.01f, s.y * factor); break;
                case GizmoAxis.Z: s.z = Mathf.Max(0.01f, s.z * factor); break;
            }
            _target.localScale = s;
        }

        private void ApplyRotateY(Vector3 worldAxis, Vector2 currentScreen, Vector2 screenDelta)
        {
            var center = (Vector2)_camera.WorldToScreenPoint(_target.position);
            var prev   = (currentScreen - screenDelta) - center;
            var curr   = currentScreen - center;
            if (prev.sqrMagnitude < 1f || curr.sqrMagnitude < 1f) return;

            float angle = Vector2.SignedAngle(prev, curr);
            float facing = Vector3.Dot(_camera.transform.forward, worldAxis);
            if (facing > 0f) angle = -angle;

            _target.Rotate(worldAxis, angle, Space.World);
        }

        // ── Construccion de geometria ─────────────────────────────────────────

        private void BuildGizmoRoot()
        {
            _root = new GameObject("TransformGizmoRoot");
            _root.transform.SetParent(transform, worldPositionStays: false);

            foreach (GizmoAxis ax in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
            {
                CreateMoveHandle(ax);
                CreateScaleHandle(ax);
            }
            CreateRotateY();
        }

        private GameObject NewHandleGO(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, worldPositionStays: false);
            go.transform.localScale = Vector3.one; // sin nested scale
            if (_gizmoLayer >= 0) go.layer = _gizmoLayer;
            return go;
        }

        private static Material BuildUnlitMat(Color c)
        {
            // GizmoOverlay shader: ZTest Always asi los handles del gizmo siempre
            // se ven por encima de la geometria (cubos, paredes, etc.).
            var sh  = Resources.Load<Shader>("GizmoOverlay")
                   ?? Shader.Find("Hidden/GizmoOverlay")
                   ?? Shader.Find("Unlit/Color")
                   ?? Shader.Find("Standard");
            var mat = new Material(sh) { name = "GizmoMat" };
            if (mat.HasProperty("_Color"))     mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            mat.renderQueue = 5000;
            return mat;
        }

        private static void SetupRenderer(MeshRenderer mr)
        {
            mr.shadowCastingMode    = ShadowCastingMode.Off;
            mr.receiveShadows       = false;
            mr.lightProbeUsage      = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // Move = flecha (cubo alargado) en el eje. Mantenemos el collider del
        // primitive en su tamano default (1x1x1) que con la localScale del arrow
        // queda ajustado al visual — sin extension generosa, asi NO pisa los
        // taps al Scale/Rotate cercanos. La priorizacion en HandleInput hace
        // el resto.
        private void CreateMoveHandle(GizmoAxis ax)
        {
            var go = NewHandleGO($"Move_{ax}");
            _moveHandles.Add(go);
            var h  = go.AddComponent<GizmoHandle>();
            h.Operation = GizmoOperation.Move;
            h.Axis      = ax;

            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrow.name = "ArrowMesh";
            arrow.transform.SetParent(go.transform, worldPositionStays: false);
            arrow.transform.localPosition = h.LocalAxis() * 0.45f;
            arrow.transform.localScale    = AxisToScale(ax, length: 0.8f, thickness: 0.08f);
            if (_gizmoLayer >= 0) arrow.layer = _gizmoLayer;

            var mr = arrow.GetComponent<MeshRenderer>();
            mr.sharedMaterial = BuildUnlitMat(h.AxisColor());
            SetupRenderer(mr);

            // Mantenemos la longitud del collider igual al visual (no se extiende
            // hacia donde estan Scale/Rotate) pero ensanchamos en perpendicular
            // para que sea facil de tappear sin ser sobre-greedy.
            var bc = arrow.GetComponent<BoxCollider>();
            bc.center = Vector3.zero;
            bc.size   = AxisToScale(ax, length: 1.0f, thickness: 2.5f);
        }

        // Scale = cubo solido al final del eje, BIEN mas alla del Move handle.
        // Collider en el propio visual.
        private void CreateScaleHandle(GizmoAxis ax)
        {
            var go = NewHandleGO($"Scale_{ax}");
            _scaleHandles.Add(go);
            var h  = go.AddComponent<GizmoHandle>();
            h.Operation = GizmoOperation.Scale;
            h.Axis      = ax;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ScaleMesh";
            cube.transform.SetParent(go.transform, worldPositionStays: false);
            cube.transform.localPosition = h.LocalAxis() * 1.5f; // muy afuera del Move (0.85 max)
            cube.transform.localScale    = new Vector3(0.28f, 0.28f, 0.28f);
            if (_gizmoLayer >= 0) cube.layer = _gizmoLayer;

            var mr = cube.GetComponent<MeshRenderer>();
            var c  = h.AxisColor(); c.a = 0.95f;
            mr.sharedMaterial = BuildUnlitMat(c);
            SetupRenderer(mr);

            // Agrandar collider para tap mas facil. Como el Move ya no llega
            // hasta aca y la prioridad en HandleInput es Scale > Move, no hay
            // riesgo de conflicto.
            var bc = cube.GetComponent<BoxCollider>();
            bc.center = Vector3.zero;
            bc.size   = new Vector3(2.2f, 2.2f, 2.2f);
        }

        // Rotate Y = un toro horizontal (en plano XZ del objeto). El anillo
        // pasa por entre Move y Scale (radio 1.15) para que la mayor parte del
        // anillo este en zona "vacia" sin colisionar con otros handles.
        private void CreateRotateY()
        {
            var go = NewHandleGO("Rotate_Y");
            _rotateHandles.Add(go);
            var h  = go.AddComponent<GizmoHandle>();
            h.Operation = GizmoOperation.Rotate;
            h.Axis      = GizmoAxis.Y;

            var mesh = BuildTorus(majorRadius: 1.15f, minorRadius: 0.11f, majorSegments: 36, minorSegments: 8);

            var ring = new GameObject("RotateMesh");
            ring.transform.SetParent(go.transform, worldPositionStays: false);
            if (_gizmoLayer >= 0) ring.layer = _gizmoLayer;
            var mf = ring.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = ring.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BuildUnlitMat(h.AxisColor());
            SetupRenderer(mr);

            var mc = ring.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex     = false;
        }

        private static Vector3 AxisToScale(GizmoAxis ax, float length, float thickness)
        {
            switch (ax)
            {
                case GizmoAxis.X: return new Vector3(length, thickness, thickness);
                case GizmoAxis.Y: return new Vector3(thickness, length, thickness);
                default:          return new Vector3(thickness, thickness, length);
            }
        }

        // Toro en plano XZ (perpendicular al eje Y).
        private static Mesh BuildTorus(float majorRadius, float minorRadius, int majorSegments, int minorSegments)
        {
            var verts = new List<Vector3>();
            var tris  = new List<int>();

            for (int i = 0; i < majorSegments; i++)
            {
                float u = (float)i / majorSegments * Mathf.PI * 2f;
                var center = new Vector3(Mathf.Cos(u) * majorRadius, 0f, Mathf.Sin(u) * majorRadius);
                var tangent = new Vector3(-Mathf.Sin(u), 0f, Mathf.Cos(u));
                var normal  = Vector3.Cross(tangent, Vector3.up).normalized;

                for (int j = 0; j < minorSegments; j++)
                {
                    float v = (float)j / minorSegments * Mathf.PI * 2f;
                    var offset = (Mathf.Cos(v) * normal + Mathf.Sin(v) * Vector3.up) * minorRadius;
                    verts.Add(center + offset);
                }
            }

            for (int i = 0; i < majorSegments; i++)
            {
                int ni = (i + 1) % majorSegments;
                for (int j = 0; j < minorSegments; j++)
                {
                    int nj = (j + 1) % minorSegments;
                    int a = i  * minorSegments + j;
                    int b = ni * minorSegments + j;
                    int c = ni * minorSegments + nj;
                    int d = i  * minorSegments + nj;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(d);
                }
            }

            var mesh = new Mesh { name = "GizmoTorusY" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
