using System.Collections.Generic;
using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// A wall in the scene, as a real Unity object rather than an anonymous mesh.
    ///
    /// Carries its own identity and geometry, sits on the <see cref="PlacedLayerName"/>
    /// layer, and rebuilds its mesh through <see cref="AutoWallMeshBuilder"/> so openings
    /// are cut properly. This is what lets anything else in the scene — raycasts,
    /// occlusion, an exporter, eventually a game layer — find a wall and ask it
    /// questions, instead of pattern-matching GameObject names.
    ///
    /// Modelled on the reference game's Scanner/AutoWallObject so the two agree on what a
    /// wall *is*: the floor line start→end is the near face, the box grows to
    /// <see cref="Width"/> along <see cref="Normal"/>, and openings are through-holes
    /// in (u,v). Deliberately trimmed: the game's selection/gizmo/handle components are
    /// not ported, because Mortuorium already edits walls through
    /// <see cref="WallEditor"/> and porting them would duplicate a whole second UI.
    ///
    /// Geometry is map-origin-local and baked into the mesh vertices; the transform is
    /// forced to identity on every rebuild, matching the convention the rest of
    /// <see cref="RoomBuilder"/> already uses.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class AutoWallObject : MonoBehaviour
    {
        /// <summary>
        /// Layer for placed room geometry. Same name and index (8) as the game's, so a
        /// wall's layer means the same thing on both sides.
        /// </summary>
        public const string PlacedLayerName = "Placed";

        /// <summary>
        /// Shortest wall box (m) this object will hold. Every height setter clamps to it,
        /// and <see cref="RoomBuilder"/> clamps the model to the same value, so a stored
        /// <see cref="WallSegment"/> can never describe a box the renderer silently
        /// enlarged.
        /// </summary>
        public const float MinHeight = 0.1f;

        public int Id { get; private set; }

        /// <summary>
        /// Floor-line endpoints, origin-local. Their <c>y</c> is always <see cref="BaseY"/>:
        /// the pair defines the bottom edge of the near face, and the box grows upward from
        /// there. Keeping the height in the endpoints (rather than assuming y=0) is what
        /// lets a wall be lifted off the floor without a second code path.
        /// </summary>
        public Vector3 ALocal { get; private set; }
        public Vector3 BLocal { get; private set; }

        /// <summary>Height of the bottom edge above the origin floor (m). Usually 0.</summary>
        public float BaseY { get; private set; }

        public float Height { get; private set; } = 2.5f;
        public float Width { get; private set; } = 0.12f;
        public int Side { get; private set; } = 1;

        /// <summary>Height of the top edge above the origin floor (m).</summary>
        public float TopY => BaseY + Height;

        /// <summary>Doors and windows cut through this wall, in its own (u,v) frame.</summary>
        public IReadOnlyList<WallOpening> Openings => _openings;
        readonly List<WallOpening> _openings = new();

        /// <summary>Horizontal direction along the floor line, start → end.</summary>
        public Vector3 BaseHat
        {
            get
            {
                var d = BLocal - ALocal;
                float m = d.magnitude;
                return m > 1e-5f ? d / m : Vector3.forward;
            }
        }

        /// <summary>Unit horizontal extrusion normal. The near face is at w=0.</summary>
        public Vector3 Normal
        {
            get
            {
                var n = Vector3.Cross(Vector3.up, BaseHat);
                if (n.sqrMagnitude < 1e-6f) n = Vector3.right;
                return (Side >= 0 ? 1f : -1f) * n.normalized;
            }
        }

        public float Length => (BLocal - ALocal).magnitude;

        MeshFilter _mf;
        MeshRenderer _mr;
        MeshCollider _mc;
        Mesh _mesh;
        Material _material;
        bool _ownsMaterial;   // runtime-created materials must be destroyed with us

        void Awake()
        {
            // Normally the factory has already set these. This covers a component added by
            // hand in the inspector, so Rebuild cannot null-ref.
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mr == null) _mr = GetComponent<MeshRenderer>();
            if (_mc == null) _mc = GetComponent<MeshCollider>();
        }

        /// <summary>
        /// Creates a wall under <paramref name="parent"/> (the map origin).
        /// <paramref name="template"/>, when given, is instanced per wall so tinting one
        /// wall cannot bleed into the others.
        /// </summary>
        public static AutoWallObject Create(Transform parent, int id,
                                        Vector3 aLocal, Vector3 bLocal,
                                        float height, float width, int side,
                                        Material template,
                                        IReadOnlyList<WallOpening> openings = null,
                                        float baseY = 0f)
        {
            var go = new GameObject("Wall_" + id);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            int layer = LayerMask.NameToLayer(PlacedLayerName);
            if (layer >= 0) go.layer = layer;

            var w = go.AddComponent<AutoWallObject>();
            w._mf = go.GetComponent<MeshFilter>();
            w._mr = go.GetComponent<MeshRenderer>();
            w._mc = go.GetComponent<MeshCollider>();

            // Mesh cleaning off: the mesh passes through briefly degenerate states while a
            // wall is dragged, and PhysX warns on every one of them otherwise.
            w._mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                                 | MeshColliderCookingOptions.WeldColocatedVertices;

            w.Id = id;
            w.BaseY = baseY;
            // The endpoints are forced onto the base plane here rather than trusted, so a
            // caller that still passes floor-level points (every automatic path does) and
            // one that passes a lifted wall cannot disagree about where the bottom edge is.
            aLocal.y = baseY; bLocal.y = baseY;
            w.ALocal = aLocal;
            w.BLocal = bLocal;
            w.Height = Mathf.Max(MinHeight, height);
            w.Width = Mathf.Max(AutoWallMeshBuilder.MinDimension, width);
            w.Side = side >= 0 ? 1 : -1;

            // Adopted before the first Rebuild, so a wall with three doors is meshed once
            // rather than four times.
            if (openings != null) w._openings.AddRange(openings);

            w.EnsureMaterial(template);
            w.Rebuild();
            return w;
        }

        /// <summary>
        /// Picks the extrusion side so the wall grows away from <paramref name="viewer"/>,
        /// i.e. the face you are looking at is the one you placed. Same rule the game
        /// applies to the first segment of a polyline.
        /// </summary>
        public static int DecideSide(Vector3 aLocal, Vector3 bLocal, Vector3 viewerLocal)
        {
            var d = bLocal - aLocal;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return 1;
            var n = Vector3.Cross(Vector3.up, d.normalized);
            var toViewer = viewerLocal - (aLocal + bLocal) * 0.5f;
            toViewer.y = 0f;
            return Vector3.Dot(n, toViewer) > 0f ? -1 : 1;
        }

        public void SetEndpoints(Vector3 aLocal, Vector3 bLocal)
        {
            aLocal.y = BaseY; bLocal.y = BaseY;
            ALocal = aLocal;
            BLocal = bLocal;
            Rebuild();
        }

        public void SetHeight(float height)
        {
            Height = Mathf.Max(MinHeight, height);
            Rebuild();
        }

        /// <summary>
        /// Sets the bottom and top of the box in one rebuild. Corner editing moves both at
        /// once (dragging the base up while holding the top still changes height too), and
        /// doing it in two calls would remesh twice per drag frame.
        /// </summary>
        public void SetVerticalExtent(float baseY, float height)
        {
            BaseY = baseY;
            Height = Mathf.Max(MinHeight, height);

            var a = ALocal; a.y = baseY; ALocal = a;
            var b = BLocal; b.y = baseY; BLocal = b;
            Rebuild();
        }

        public void SetWidth(float width)
        {
            Width = Mathf.Max(AutoWallMeshBuilder.MinDimension, width);
            Rebuild();
        }

        /// <summary>
        /// Sets the whole box — footprint, vertical extent and thickness — in a single
        /// rebuild. Corner editing changes several of these at once (dragging a top corner
        /// moves the top edge and nothing else; dragging a base corner moves an endpoint),
        /// and calling the individual setters in sequence would remesh and re-cook the
        /// collider two or three times per drag frame.
        /// </summary>
        public void SetGeometry(Vector3 aLocal, Vector3 bLocal, float baseY, float height, float width)
        {
            BaseY = baseY;
            aLocal.y = baseY; bLocal.y = baseY;
            ALocal = aLocal;
            BLocal = bLocal;
            Height = Mathf.Max(MinHeight, height);
            Width = Mathf.Max(AutoWallMeshBuilder.MinDimension, width);
            Rebuild();
        }

        public void SetSide(int side)
        {
            Side = side >= 0 ? 1 : -1;
            Rebuild();
        }

        public void AddOpening(WallOpening opening)
        {
            _openings.Add(opening);
            Rebuild();
        }

        /// <summary>
        /// Replaces the whole opening set in a single rebuild. Reconstruction hands a wall
        /// its doors as a batch, so adding them one at a time would remesh per door.
        /// </summary>
        public void SetOpenings(IReadOnlyList<WallOpening> openings)
        {
            _openings.Clear();
            if (openings != null) _openings.AddRange(openings);
            Rebuild();
        }

        public void ClearOpenings()
        {
            if (_openings.Count == 0) return;
            _openings.Clear();
            Rebuild();
        }

        /// <summary>How far (m) the rendered wall floats toward the camera off its measured
        /// near face, so environment-depth occlusion does not swallow it against the real
        /// wall. Mesh verts and the model segment stay on the detection line. ARCore's
        /// environment depth is itself only accurate to within a few cm, so a nudge smaller
        /// than that is below the noise floor and does nothing — this has to be big enough
        /// to beat typical depth error, not just avoid exact z-fighting.</summary>
        const float RenderNudge = 0.05f;

        /// <summary>Regenerates the mesh in place — no GameObject churn during a drag.</summary>
        public void Rebuild()
        {
            transform.SetLocalPositionAndRotation(-Normal * RenderNudge, Quaternion.identity);
            transform.localScale = Vector3.one;

            var mesh = AutoWallMeshBuilder.Build(ALocal, BLocal, Height, Width, Normal, _openings);

            // A degenerate wall renders and collides as nothing rather than as garbage.
            if (mesh == null)
            {
                _mf.sharedMesh = null;
                _mc.sharedMesh = null;
                DestroyMesh();
                return;
            }

            DestroyMesh();
            _mesh = mesh;
            _mf.sharedMesh = _mesh;
            // Null first so PhysX re-cooks against the new shape rather than keeping the
            // previous cook for the same reference.
            _mc.sharedMesh = null;
            _mc.sharedMesh = _mesh;
        }

        /// <summary>
        /// Applies a state tint to this wall's own material instance. What the two
        /// arguments mean depends on the shader, which is <see cref="ScanMaterials.Tint"/>'s
        /// job to know.
        /// </summary>
        public void SetTint(Color color, float fillAlpha)
            => ScanMaterials.Tint(_material, color, fillAlpha);

        void EnsureMaterial(Material template)
        {
            if (template != null && IsShaderUsable(template.shader, template.name))
            {
                _material = new Material(template);
                _ownsMaterial = true;
            }
            else
            {
                _material = ScanMaterials.CreateRuntime(ScanSurface.Wall);
                _ownsMaterial = true;
                if (_material != null) _material.name = "WallMat (runtime)";
            }

            // A wall's grid comes from the (u,v,w) AutoWallMeshBuilder bakes into the mesh.
            // Asserted here rather than trusted from the material asset, so a wall wired to
            // the cube material still renders as a wall.
            ScanMaterials.ConfigureEdgeGrid(_material, ScanSurface.Wall);
            _mr.sharedMaterial = _material;
        }

        /// <summary>
        /// A shader stripped from the build resolves to the internal error shader and the
        /// wall renders magenta. Detect it and say so, rather than shipping pink walls.
        /// </summary>
        internal static bool IsShaderUsable(Shader shader, string materialName)
        {
            if (shader == null || shader.name == "Hidden/InternalErrorShader")
            {
                Debug.LogError(
                    $"[RoomScanning] Material '{materialName}' has an invalid shader " +
                    $"('{(shader != null ? shader.name : "null")}') — probably stripped from the " +
                    $"build. Add it to Project Settings > Graphics > Always Included Shaders. " +
                    $"Falling back to a runtime material.");
                return false;
            }
            return true;
        }

        void DestroyMesh()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
            _mesh = null;
        }

        void OnDestroy()
        {
            DestroyMesh();
            if (_ownsMaterial && _material != null)
            {
                if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material);
                _material = null;
            }
        }
    }
}
