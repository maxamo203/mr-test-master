using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// Owns the AR plane layer: discovers tracked planes, classifies them
    /// (ARCore classification with a geometry fallback) and bakes them into a
    /// persistent visible layer once scanning starts. Live AR planes are hidden;
    /// only the baked layer is ever rendered, so the map stays put when ARCore
    /// merges/re-evaluates planes.
    ///
    /// Replaces: <c>PlanePersistenceManager</c> + the plane helpers
    /// (<c>LargestPlane</c>, <c>IsFloor</c>) embedded in <c>DepthWallAnalyzer</c>.
    /// </summary>
    [RequireComponent(typeof(ARPlaneManager))]
    public class PlaneCollector : MonoBehaviour
    {
        [SerializeField] Material persistedPlaneMaterial;
        [Tooltip("Planes smaller than this area (m²) are not baked.")]
        [SerializeField] float minPlaneArea = 0.25f;
        [Tooltip("Skip baking tables/seats/couches — keep the initial map to floor + walls + ceiling.")]
        [SerializeField] bool bakeFloorAndWallsOnly = true;

        ARPlaneManager _planeManager;
        Transform _persistenceRoot;
        bool _isBaking;

        // Planes already baked successfully.
        readonly Dictionary<TrackableId, GameObject> _bakedPlanes = new();
        // Planes we tried to bake but had no mesh yet — retried in LateUpdate.
        readonly HashSet<TrackableId> _pendingBake = new();

        public bool IsBaking => _isBaking;

        /// <summary>All currently tracked AR planes.</summary>
        public TrackableCollection<ARPlane> Planes => _planeManager.trackables;

        void Awake()
        {
            _planeManager = GetComponent<ARPlaneManager>();
            _persistenceRoot = new GameObject("PersistedPlanes").transform;
        }

        void OnEnable()  => _planeManager.trackablesChanged.AddListener(OnPlanesChanged);
        void OnDisable() => _planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);

        // ── Baking lifecycle ──────────────────────────────────────────────────

        /// <summary>
        /// Begin baking planes into the persistent layer (called when the origin is set).
        /// Schedules all current trackables; the actual bake runs in LateUpdate so the
        /// ARPlaneMeshVisualizer has already produced meshes this frame.
        /// </summary>
        public void StartBaking()
        {
            if (_isBaking) return;
            _isBaking = true;
            PlaneClassificationVisualizer.ResetFloorReference();

            foreach (var plane in _planeManager.trackables)
                _pendingBake.Add(plane.trackableId);

            Debug.Log($"[PlaneCollector] Baking started — {_pendingBake.Count} planes queued.");
        }

        public void Clear()
        {
            foreach (var kv in _bakedPlanes)
                if (kv.Value != null) Destroy(kv.Value);
            _bakedPlanes.Clear();
            _pendingBake.Clear();
        }

        void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            if (!_isBaking) return;

            foreach (var plane in args.added)
                _pendingBake.Add(plane.trackableId);

            foreach (var plane in args.updated)
                TryBake(plane); // updated planes have a ready mesh — bake immediately
            // Removed planes: the baked copy intentionally stays.
        }

        void LateUpdate()
        {
            if (!_isBaking || _pendingBake.Count == 0) return;

            var done = new List<TrackableId>();
            foreach (var id in _pendingBake)
            {
                ARPlane found = null;
                foreach (var p in _planeManager.trackables)
                    if (p.trackableId == id) { found = p; break; }

                if (found == null) { done.Add(id); continue; } // plane gone
                if (TryBake(found)) done.Add(id);
            }

            foreach (var id in done) _pendingBake.Remove(id);
        }

        bool TryBake(ARPlane plane)
        {
            float area = plane.size.x * plane.size.y;
            if (area < minPlaneArea) return true; // too small — drop from pending

            // During the initial scan keep only walls + the real floor (skip tables,
            // chairs, ceilings and ARCore's mid-air horizontal phantoms).
            if (bakeFloorAndWallsOnly && !PlaneClassificationVisualizer.IsInitialKeeper(plane))
                return true;

            var srcMF = plane.GetComponent<MeshFilter>();
            if (srcMF == null || srcMF.sharedMesh == null || srcMF.sharedMesh.vertexCount == 0)
                return false; // mesh not ready this frame — retried silently in LateUpdate

            UpsertBakedPlane(plane, srcMF.sharedMesh);
            return true;
        }

        void UpsertBakedPlane(ARPlane plane, Mesh srcMesh)
        {
            if (!_bakedPlanes.TryGetValue(plane.trackableId, out var bakedGO))
            {
                bakedGO = CreateBakedObject(plane.trackableId);
                _bakedPlanes[plane.trackableId] = bakedGO;
                Debug.Log($"[PlaneCollector] Baked new plane {plane.trackableId} at {plane.transform.position}");
            }

            bakedGO.transform.SetPositionAndRotation(plane.transform.position, plane.transform.rotation);
            bakedGO.GetComponent<MeshFilter>().mesh = CopyMesh(srcMesh);
            TintMaterial(bakedGO, plane);
            HideLivePlane(plane);
        }

        GameObject CreateBakedObject(TrackableId id)
        {
            var go = new GameObject("Baked_" + id);
            go.transform.SetParent(_persistenceRoot, worldPositionStays: true);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.material = persistedPlaneMaterial != null
                ? new Material(persistedPlaneMaterial)
                : new Material(Shader.Find("Mortuorium/ARPlaneGrid"));
            return go;
        }

        void TintMaterial(GameObject bakedGO, ARPlane plane)
        {
            var mr = bakedGO.GetComponent<MeshRenderer>();
            if (mr == null) return;

            var c = plane.classifications;
            if (c == PlaneClassifications.None || c == PlaneClassifications.Other)
                c = PlaneClassificationVisualizer.GeometryClassification(plane);

            mr.material.SetColor("_GridColor", PlaneClassificationVisualizer.ColorForClassification(c));
            mr.material.SetFloat("_FillAlpha", 0.04f);
            mr.material.SetFloat("_LineWidth", 0.022f);
        }

        static void HideLivePlane(ARPlane plane)
        {
            var viz = plane.GetComponent<PlaneClassificationVisualizer>();
            if (viz != null) viz.enabled = false;
            var mr = plane.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
            var lr = plane.GetComponent<LineRenderer>();
            if (lr != null) lr.enabled = false;
        }

        static Mesh CopyMesh(Mesh src)
        {
            var m = new Mesh
            {
                vertices  = src.vertices,
                triangles = src.triangles,
                normals   = src.normals,
                uv        = src.uv
            };
            m.RecalculateBounds();
            // Inflate bounds slightly so a zero-height floor plane isn't falsely culled.
            var b = m.bounds;
            b.Expand(0.02f);
            m.bounds = b;
            return m;
        }

        // ── Queries used by CornerDetector / RoomBuilder / DoorDetector ───────

        /// <summary>The largest plane matching <paramref name="predicate"/> by area, or null.</summary>
        public ARPlane LargestPlane(System.Func<ARPlane, bool> predicate)
        {
            ARPlane best = null;
            float bestArea = 0f;
            foreach (var plane in _planeManager.trackables)
            {
                if (!predicate(plane)) continue;
                float area = plane.size.x * plane.size.y;
                if (area > bestArea) { bestArea = area; best = plane; }
            }
            return best;
        }

        public ARPlane LargestFloor() => LargestPlane(IsFloor);

        /// <summary>True if the plane is (or geometrically resolves to) a floor.</summary>
        public static bool IsFloor(ARPlane plane)
        {
            var c = plane.classifications;
            if (c.HasFlag(PlaneClassifications.Floor)) return true;
            if (c == PlaneClassifications.None || c == PlaneClassifications.Other)
                return PlaneClassificationVisualizer.GeometryClassification(plane) == PlaneClassifications.Floor;
            return false;
        }

        /// <summary>True if the plane is (or geometrically resolves to) a wall.</summary>
        public static bool IsWall(ARPlane plane)
        {
            var c = plane.classifications;
            if (c == PlaneClassifications.None || c == PlaneClassifications.Other)
                c = PlaneClassificationVisualizer.GeometryClassification(plane);
            return c.HasFlag(PlaneClassifications.WallFace)
                || c.HasFlag(PlaneClassifications.InnerWallFace)
                || c.HasFlag(PlaneClassifications.InvisibleWallFace);
        }

        // ── Wall seeding (hybrid reconstruction) ──────────────────────────────

        /// <summary>
        /// Projects every tracked vertical plane onto the origin-local floor plane as a
        /// candidate wall line, largest first.
        ///
        /// These read the <em>live</em> trackables, which are deliberately unaffected by
        /// <see cref="PlaneClassificationVisualizer.ShowWallPlanes"/> and by baking — so
        /// vertical planes can seed geometry while still never being rendered. A seed is
        /// only a hypothesis; <see cref="DepthOccupancyMapper"/> confirms it against the
        /// depth occupancy grid before any wall is built.
        /// </summary>
        public List<WallSeed> VerticalWallSeeds(Transform mapOrigin, float minSeedArea)
        {
            var seeds = new List<WallSeed>();
            if (mapOrigin == null || _planeManager == null) return seeds;

            var w2o = mapOrigin.worldToLocalMatrix;

            foreach (var plane in _planeManager.trackables)
            {
                if (!IsWall(plane)) continue;

                float area = plane.size.x * plane.size.y;
                if (area < minSeedArea) continue;

                // Horizontal direction along the wall = normal × up, in origin-local space.
                var nLocal = w2o.MultiplyVector(plane.normal);
                var d = Vector3.Cross(nLocal, Vector3.up);
                var dir = new Vector2(d.x, d.z);
                if (dir.sqrMagnitude < 1e-6f) continue; // plane is horizontal after all
                dir.Normalize();

                var c3 = w2o.MultiplyPoint3x4(plane.center);
                var centre = new Vector2(c3.x, c3.z);

                if (!TryExtentAlong(plane, w2o, centre, dir, out float tMin, out float tMax))
                    continue;

                seeds.Add(new WallSeed
                {
                    point      = centre + dir * ((tMin + tMax) * 0.5f),
                    dir        = dir,
                    halfLength = (tMax - tMin) * 0.5f,
                    area       = area,
                });
            }

            seeds.Sort((a, b) => b.area.CompareTo(a.area)); // strongest evidence first
            return seeds;
        }

        /// <summary>
        /// Signed extent of a plane's boundary polygon along <paramref name="dir"/>,
        /// measured from <paramref name="origin"/> in the origin-local XZ plane.
        /// Boundary points are in plane space: (x, y) ⇒ plane-local (x, 0, y).
        /// </summary>
        static bool TryExtentAlong(ARPlane plane, Matrix4x4 w2o, Vector2 origin, Vector2 dir,
                                   out float tMin, out float tMax)
        {
            tMin = float.MaxValue; tMax = float.MinValue;

            var boundary = plane.boundary;
            if (!boundary.IsCreated || boundary.Length < 3) return false;

            var planeToWorld = plane.transform.localToWorldMatrix;
            for (int i = 0; i < boundary.Length; i++)
            {
                var bp = boundary[i];
                var world = planeToWorld.MultiplyPoint3x4(new Vector3(bp.x, 0f, bp.y));
                var local = w2o.MultiplyPoint3x4(world);
                float t = Vector2.Dot(new Vector2(local.x, local.z) - origin, dir);
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
            }

            return tMax > tMin;
        }

        /// <summary>
        /// Every floor-classified plane's own boundary polygon, walked as an ordered ring of
        /// edges and transformed to origin-local XZ — a candidate wall line per edge, for
        /// <see cref="DepthOccupancyMapper"/>'s floor-boundary wall sources. Unconfirmed: a
        /// seam where two floor fragments meet, or a stairstep in ARCore's floor mesh, comes
        /// through just like a real wall edge — the caller decides whether to trust it
        /// outright or require depth backing first. The floor can fragment into several
        /// `ARPlane` trackables with no merge between them, so every one is walked separately.
        /// </summary>
        public List<(Vector2 a, Vector2 b)> FloorBoundaryEdges(Transform mapOrigin, float minEdgeLength)
        {
            var edges = new List<(Vector2 a, Vector2 b)>();
            if (mapOrigin == null || _planeManager == null) return edges;

            var w2o = mapOrigin.worldToLocalMatrix;
            float minLenSqr = minEdgeLength * minEdgeLength;

            foreach (var plane in _planeManager.trackables)
            {
                if (!IsFloor(plane)) continue;

                var boundary = plane.boundary;
                if (!boundary.IsCreated || boundary.Length < 3) continue;

                var planeToWorld = plane.transform.localToWorldMatrix;
                for (int i = 0; i < boundary.Length; i++)
                {
                    var a = boundary[i];
                    var b = boundary[(i + 1) % boundary.Length];
                    var aO = w2o.MultiplyPoint3x4(planeToWorld.MultiplyPoint3x4(new Vector3(a.x, 0f, a.y)));
                    var bO = w2o.MultiplyPoint3x4(planeToWorld.MultiplyPoint3x4(new Vector3(b.x, 0f, b.y)));
                    var a2 = new Vector2(aO.x, aO.z);
                    var b2 = new Vector2(bO.x, bO.z);
                    if ((b2 - a2).sqrMagnitude < minLenSqr) continue;
                    edges.Add((a2, b2));
                }
            }

            return edges;
        }
    }
}
