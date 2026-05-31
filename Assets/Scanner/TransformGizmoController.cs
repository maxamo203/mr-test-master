using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
        [Tooltip("Tamanio aparente del gizmo: a 1m de la camara, el gizmo ocupa este factor (en metros).")]
        [SerializeField] private float _screenSizeFactor = 0.22f;

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

            BuildGizmoRoot();
            _root.SetActive(false);
        }

        public void Attach(Transform target, bool moveOnly = false)
        {
            _target   = target;
            _moveOnly = moveOnly;
            _root.SetActive(target != null);
            // Mostrar/ocultar handles segun el modo.
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

        private void HandleInput()
        {
            if (Input.touchCount == 0) { _activeHandle = null; return; }
            var touch = Input.GetTouch(0);

            switch (touch.phase)
            {
                case TouchPhase.Began:
                {
                    var ray = _camera.ScreenPointToRay(touch.position);
                    if (_gizmoLayerMask != 0 && Physics.Raycast(ray, out var hit, 100f, _gizmoLayerMask))
                    {
                        var h = hit.collider.GetComponentInParent<GizmoHandle>();
                        if (h != null)
                        {
                            _activeHandle = h;
                            _lastTouchPos = touch.position;
                        }
                    }
                    break;
                }

                case TouchPhase.Moved:
                {
                    if (_activeHandle == null) return;
                    var delta = touch.position - _lastTouchPos;
                    _lastTouchPos = touch.position;

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
                            ApplyRotateY(worldAxis, touch.position, delta);
                            break;
                    }
                    break;
                }

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    _activeHandle = null;
                    break;
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
            var sh  = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            var mat = new Material(sh) { name = "GizmoMat" };
            if (mat.HasProperty("_Color"))     mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            mat.renderQueue = 4000;
            return mat;
        }

        private static void SetupRenderer(MeshRenderer mr)
        {
            mr.shadowCastingMode    = ShadowCastingMode.Off;
            mr.receiveShadows       = false;
            mr.lightProbeUsage      = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // Move = flecha (cubo alargado) en el eje.
        private void CreateMoveHandle(GizmoAxis ax)
        {
            var go = NewHandleGO($"Move_{ax}");
            _moveHandles.Add(go);
            var h  = go.AddComponent<GizmoHandle>();
            h.Operation = GizmoOperation.Move;
            h.Axis      = ax;

            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrow.name = "ArrowMesh";
            Destroy(arrow.GetComponent<BoxCollider>());
            arrow.transform.SetParent(go.transform, worldPositionStays: false);
            arrow.transform.localPosition = h.LocalAxis() * 0.45f;
            arrow.transform.localScale    = AxisToScale(ax, length: 0.9f, thickness: 0.06f);
            if (_gizmoLayer >= 0) arrow.layer = _gizmoLayer;

            var mr = arrow.GetComponent<MeshRenderer>();
            mr.sharedMaterial = BuildUnlitMat(h.AxisColor());
            SetupRenderer(mr);

            var col = go.AddComponent<BoxCollider>();
            col.center = h.LocalAxis() * 0.45f;
            col.size   = AxisToScale(ax, length: 0.9f, thickness: 0.12f);
        }

        // Scale = cubo solido al final del eje, mas alla del Move handle.
        private void CreateScaleHandle(GizmoAxis ax)
        {
            var go = NewHandleGO($"Scale_{ax}");
            _scaleHandles.Add(go);
            var h  = go.AddComponent<GizmoHandle>();
            h.Operation = GizmoOperation.Scale;
            h.Axis      = ax;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ScaleMesh";
            Destroy(cube.GetComponent<BoxCollider>());
            cube.transform.SetParent(go.transform, worldPositionStays: false);
            cube.transform.localPosition = h.LocalAxis() * 1.1f;
            cube.transform.localScale    = new Vector3(0.18f, 0.18f, 0.18f);
            if (_gizmoLayer >= 0) cube.layer = _gizmoLayer;

            var mr = cube.GetComponent<MeshRenderer>();
            var c  = h.AxisColor(); c.a = 0.95f;
            mr.sharedMaterial = BuildUnlitMat(c);
            SetupRenderer(mr);

            var box = go.AddComponent<BoxCollider>();
            box.center = h.LocalAxis() * 1.1f;
            box.size   = new Vector3(0.24f, 0.24f, 0.24f);
        }

        // Rotate Y = un toro horizontal (en plano XZ del objeto).
        private void CreateRotateY()
        {
            var go = NewHandleGO("Rotate_Y");
            _rotateHandles.Add(go);
            var h  = go.AddComponent<GizmoHandle>();
            h.Operation = GizmoOperation.Rotate;
            h.Axis      = GizmoAxis.Y;

            var mesh = BuildTorus(majorRadius: 0.75f, minorRadius: 0.05f, majorSegments: 36, minorSegments: 8);

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
