using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// The four corners of a wall box that the user can grab, in the wall's own frame.
    ///
    /// A wall box has eight geometric corners (two ends × floor/top × near/far face), but
    /// the near and far corner of a pair are only <c>width</c> apart — about 12 cm — and
    /// project to almost the same pixel on a phone screen, so offering all eight would make
    /// the two impossible to tell apart under a fingertip. Only the near-face silhouette is
    /// offered: the near face is the surface the scan actually measured and the one the user
    /// is looking at, so it is the one worth correcting. Thickness is edited separately,
    /// with buttons, where there is no ambiguity to resolve.
    /// </summary>
    public enum WallCornerId
    {
        /// <summary>Bottom of the wall at <see cref="WallSegment.start"/>.</summary>
        BaseStart = 0,
        /// <summary>Bottom of the wall at <see cref="WallSegment.end"/>.</summary>
        BaseEnd = 1,
        /// <summary>Top of the wall above <see cref="WallSegment.start"/>.</summary>
        TopStart = 2,
        /// <summary>Top of the wall above <see cref="WallSegment.end"/>.</summary>
        TopEnd = 3,
    }

    /// <summary>
    /// Corner geometry for a <see cref="WallSegment"/>, shared by the renderer
    /// (<see cref="WallCornerHandles"/>) and the interaction code
    /// (<see cref="WallEditor"/>).
    ///
    /// Both need to agree to the millimetre about where a corner is — the handle you see is
    /// the handle you grab — so the arithmetic lives here once instead of being written out
    /// twice and drifting.
    /// </summary>
    public static class WallCorners
    {
        public const int Count = 4;

        /// <summary>True for the two corners that sit on the wall's bottom edge.</summary>
        public static bool IsBase(WallCornerId corner) =>
            corner == WallCornerId.BaseStart || corner == WallCornerId.BaseEnd;

        /// <summary>True for the two corners at the <see cref="WallSegment.end"/> of the wall.</summary>
        public static bool IsEnd(WallCornerId corner) =>
            corner == WallCornerId.BaseEnd || corner == WallCornerId.TopEnd;

        /// <summary>
        /// Position of one corner in map-origin-local space, on the wall's near face.
        /// </summary>
        public static Vector3 Local(in WallSegment wall, WallCornerId corner)
        {
            var p = IsEnd(corner) ? wall.end : wall.start;
            p.y = IsBase(corner) ? wall.baseY : wall.baseY + Mathf.Max(0f, wall.height);
            return p;
        }

        /// <summary>
        /// Writes all four corner positions into <paramref name="into"/>, indexed by
        /// <see cref="WallCornerId"/>. The array must hold at least <see cref="Count"/>.
        /// </summary>
        public static void All(in WallSegment wall, Vector3[] into)
        {
            for (int i = 0; i < Count; i++) into[i] = Local(wall, (WallCornerId)i);
        }
    }

    /// <summary>
    /// Draws the grab handles for the wall currently being corner-edited.
    ///
    /// Pure view: it renders four small cubes at the corners and knows nothing about
    /// touches. Picking and dragging live in <see cref="WallEditor"/>, which computes corner
    /// positions from the same <see cref="WallCorners"/> helper, so what is drawn and what
    /// is grabbable cannot diverge.
    ///
    /// Two constraints inherited from the rest of the project:
    ///
    /// - <b>No colliders and no <c>GameObject.CreatePrimitive</c>.</b> The player strips
    ///   engine code and the Physics module goes with it, so a primitive's automatic
    ///   collider throws under IL2CPP. The cube mesh is built by hand, exactly as
    ///   <c>RoomScanningManager.UnitCube</c> does for the origin gizmo.
    /// - <b>Unlit, always-on-top material.</b> A handle that disappears behind the wall it
    ///   belongs to cannot be aimed at, and a wall seen from its far side hides its own
    ///   corners.
    ///
    /// Handles are scaled by distance every frame so they stay roughly a constant size on
    /// screen: a corner three metres away must still be a comfortable thumb target, and a
    /// corner half a metre away must not fill the view.
    /// </summary>
    public class WallCornerHandles : MonoBehaviour
    {
        [Tooltip("Apparent handle size as a fraction of distance from the camera. " +
                 "0.03 is a 6 cm cube at 2 m.")]
        [SerializeField] float angularSize = 0.030f;

        [Tooltip("Clamp on the distance-scaled handle size (m), so handles stay visible " +
                 "close up and grabbable far away.")]
        [SerializeField] float minSize = 0.035f;
        [SerializeField] float maxSize = 0.14f;

        // Base handles move the footprint, top handles move the ceiling line: different
        // colours because they answer different questions ("is the wall in the right
        // place?" vs "is it the right height?").
        static readonly Color BaseColor   = new Color(0.20f, 0.85f, 1f, 1f);
        static readonly Color TopColor    = new Color(1f, 0.55f, 0.15f, 1f);
        static readonly Color ActiveColor = new Color(1f, 0.95f, 0.25f, 1f);

        Transform _root;
        Transform _mapOrigin;
        Camera _cam;

        readonly Transform[] _handles = new Transform[WallCorners.Count];
        readonly Material[] _materials = new Material[WallCorners.Count];
        readonly Vector3[] _corners = new Vector3[WallCorners.Count];

        bool _visible;

        /// <summary>Binds the handles to the map origin, so they follow it like the walls do.</summary>
        public void Attach(Transform mapOrigin)
        {
            _mapOrigin = mapOrigin;
            EnsureBuilt();
            if (_root != null && _mapOrigin != null)
            {
                _root.SetParent(_mapOrigin, worldPositionStays: false);
                _root.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                _root.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// Moves the handles onto <paramref name="wall"/> and shows them.
        /// <paramref name="active"/> is the corner being dragged, highlighted so it is
        /// obvious which one the finger caught; pass null when nothing is being dragged.
        /// </summary>
        public void Show(in WallSegment wall, WallCornerId? active)
        {
            EnsureBuilt();
            if (_root == null) return;

            WallCorners.All(wall, _corners);
            for (int i = 0; i < WallCorners.Count; i++)
            {
                var handle = _handles[i];
                if (handle == null) continue;
                handle.localPosition = _corners[i];
                handle.gameObject.SetActive(true);
                SetColor(i, active.HasValue && (int)active.Value == i
                            ? ActiveColor
                            : (WallCorners.IsBase((WallCornerId)i) ? BaseColor : TopColor));
            }
            _visible = true;
            Rescale();
        }

        /// <summary>Hides every handle. Cheap enough to call each frame nothing is selected.</summary>
        public void Hide()
        {
            if (!_visible) return;
            foreach (var h in _handles) if (h != null) h.gameObject.SetActive(false);
            _visible = false;
        }

        // Rescaling runs after everything else has moved the camera for this frame, so a
        // handle is never a frame behind the view it is sized for.
        void LateUpdate()
        {
            if (_visible) Rescale();
        }

        void Rescale()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            foreach (var h in _handles)
            {
                if (h == null || !h.gameObject.activeSelf) continue;
                float dist = Vector3.Distance(_cam.transform.position, h.position);
                float size = Mathf.Clamp(dist * angularSize, minSize, maxSize);
                // The parent chain up to the map origin is unscaled, so a local scale is a
                // size in metres — the same convention the wall meshes are built in.
                h.localScale = Vector3.one * size;
            }
        }

        void EnsureBuilt()
        {
            if (_root != null) return;

            _root = new GameObject("WallCornerHandles").transform;
            for (int i = 0; i < WallCorners.Count; i++)
            {
                var go = new GameObject("Corner_" + (WallCornerId)i);
                go.transform.SetParent(_root, worldPositionStays: false);
                go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.AddComponent<MeshFilter>().sharedMesh = HandleMesh();

                var color = WallCorners.IsBase((WallCornerId)i) ? BaseColor : TopColor;
                var mat = MakeUnlit(color);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;

                _handles[i] = go.transform;
                _materials[i] = mat;
                go.SetActive(false);
            }
        }

        void SetColor(int index, Color c)
        {
            var m = _materials[index];
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        void OnDestroy()
        {
            foreach (var m in _materials) if (m != null) Destroy(m);
            if (_root != null) Destroy(_root.gameObject);
        }

        // ── Primitive geometry (no Physics module, see the class comment) ─────

        static Mesh _cube;

        /// <summary>
        /// A unit cube centred on the origin. Deliberately a hand-built mesh, not
        /// <c>GameObject.CreatePrimitive</c>: that adds a <c>BoxCollider</c>, and the
        /// Physics module is stripped from the player.
        /// </summary>
        static Mesh HandleMesh()
        {
            if (_cube != null) return _cube;
            var v = new[]
            {
                new Vector3(-0.5f,-0.5f,-0.5f), new Vector3(0.5f,-0.5f,-0.5f),
                new Vector3(0.5f, 0.5f,-0.5f),  new Vector3(-0.5f,0.5f,-0.5f),
                new Vector3(-0.5f,-0.5f, 0.5f), new Vector3(0.5f,-0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),  new Vector3(-0.5f,0.5f, 0.5f),
            };
            var t = new[]
            {
                0,2,1, 0,3,2,  4,5,6, 4,6,7,
                0,1,5, 0,5,4,  3,7,6, 3,6,2,
                0,4,7, 0,7,3,  1,2,6, 1,6,5,
            };
            _cube = new Mesh { name = "HandleCube", vertices = v, triangles = t };
            _cube.RecalculateNormals();
            _cube.RecalculateBounds();
            return _cube;
        }

        /// <summary>
        /// Same shader search order as the origin gizmo's, so handles and origin markers
        /// draw the same way (and survive the same stripping) rather than each picking
        /// their own fallback.
        /// </summary>
        static Material MakeUnlit(Color c)
        {
            var shader = Shader.Find("Mortuorium/OriginGizmo");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var m = new Material(shader) { name = "WallCornerHandle (runtime)" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }
    }
}
