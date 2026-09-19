using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// All environment-depth analysis for room scanning. It gathers near-vertical
    /// wall points above the floor, RANSAC-fits wall lines and reports:
    ///   • <see cref="TryDetectCorner"/> — two walls' intersection = a corner, plus
    ///     each wall's direction/extent (<see cref="WallFit"/>);
    ///   • <see cref="TryMeasureCeiling"/> — ceiling height above a locked corner;
    ///   • <see cref="DetectBoundaryWalls"/> — wall clusters along floor-boundary
    ///     edges during continuous mapping (returns raw <see cref="DetectedWall"/>s
    ///     for <see cref="RoomBuilder"/> to mesh).
    ///
    /// Replaces: all depth/RANSAC machinery in <c>DepthWallAnalyzer</c>.
    /// </summary>
    [RequireComponent(typeof(ARPlaneManager))]
    public class CornerDetector : MonoBehaviour
    {
        public enum MinConfidence { Low = 0, Medium = 1, High = 2 }

        [Header("Corner scan")]
        [SerializeField] float minWallAngle = 25f;
        [SerializeField] float maxCornerRange = 5f;
        [SerializeField] float cornerLateralCorrection = 0f;

        [Header("Depth wall fitting")]
        [SerializeField] float lineInlierDist = 0.06f;
        [SerializeField] int minLinePoints = 40;
        [SerializeField] int lineIterations = 200;
        [SerializeField] float wallNormalMax = 0.35f;
        [SerializeField] float minWallHeight = 0.10f;
        [SerializeField] float wallScanMaxHeight = 2.5f;

        [Header("Ceiling measurement")]
        [SerializeField] float ceilingSearchRadius = 0.7f;
        [SerializeField] int minCeilingPoints = 20;
        [SerializeField] float minRoomHeight = 1.8f;
        [SerializeField] float maxRoomHeight = 5f;

        [Header("Floor boundary → wall matching")]
        [SerializeField] float boundaryMatchRadius = 0.30f;
        [SerializeField] int minPointsToConfirmWall = 10;
        [SerializeField] float gridCellSize = 0.15f;

        [Header("Depth sampling")]
        [SerializeField] int sampleStep = 4;
        [SerializeField] MinConfidence minConfidence = MinConfidence.Low;
        [SerializeField] float minDepth = 0.25f;
        [SerializeField] float maxDepth = 6f;

        ARPlaneManager _planeManager;
        AROcclusionManager _occlusionManager;
        Camera _cam;

        // Walls captured by the last successful TryDetectCorner, for the caller to read.
        public Vector3 LastWall1Dir { get; private set; }
        public Vector3 LastWall2Dir { get; private set; }

        void Awake()
        {
            _planeManager = GetComponent<ARPlaneManager>();
            _occlusionManager = GetComponentInChildren<AROcclusionManager>(includeInactive: true);
        }

        Camera Cam => _cam != null ? _cam : (_cam = Camera.main);

        // ── Corner detection ──────────────────────────────────────────────────

        /// <summary>
        /// Detects a corner from depth at floor level <paramref name="floorY"/>. On
        /// success returns the world-space corner, the first wall's forward direction,
        /// and both fitted walls (direction + signed extents from the corner).
        /// </summary>
        public bool TryDetectCorner(float floorY, out Vector3 corner, out Vector3 forward,
                                    out WallFit wallA, out WallFit wallB)
        {
            corner = Vector3.zero;
            forward = Vector3.forward;
            wallA = default;
            wallB = default;

            if (Cam == null) return false;

            var pts = CollectWallPointsXZ(floorY);
            if (pts.Count < minLinePoints * 2) return false;

            if (!Ransac2DLine(pts, out var p1, out var d1, out var in1) || in1.Count < minLinePoints)
                return false;

            // Remove line-1 inliers, fit the second wall on what remains.
            var remaining = new List<Vector2>(pts.Count - in1.Count);
            var inSet = new HashSet<int>(in1);
            for (int i = 0; i < pts.Count; i++) if (!inSet.Contains(i)) remaining.Add(pts[i]);

            if (!Ransac2DLine(remaining, out var p2, out var d2, out var in2) || in2.Count < minLinePoints)
                return false;

            float angle = Vector2.Angle(d1, d2);
            angle = Mathf.Min(angle, 180f - angle);
            if (angle < minWallAngle) return false;

            if (!IntersectLines2D(p1, d1, p2, d2, out var xz)) return false;

            var c = new Vector3(xz.x, floorY, xz.y);

            if (Mathf.Abs(cornerLateralCorrection) > 1e-4f)
            {
                var right = Cam.transform.right; right.y = 0f; right.Normalize();
                c += right * cornerLateralCorrection;
                xz = new Vector2(c.x, c.z);
            }

            if ((c - Cam.transform.position).magnitude > maxCornerRange) return false;

            corner = c;
            forward = new Vector3(d1.x, 0f, d1.y).normalized;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;

            var dir1 = new Vector3(d1.x, 0f, d1.y).normalized;
            var dir2 = new Vector3(d2.x, 0f, d2.y).normalized;
            ExtentAlong(pts, in1, xz, d1, out float min1, out float max1);
            ExtentAlong(remaining, in2, xz, d2, out float min2, out float max2);

            wallA = new WallFit { direction = dir1, min = min1, max = max1 };
            wallB = new WallFit { direction = dir2, min = min2, max = max2 };
            LastWall1Dir = dir1;
            LastWall2Dir = dir2;

            Debug.Log($"[CornerDetector] ✔ pts={pts.Count} line1={in1.Count} line2={in2.Count} " +
                      $"angle={angle:F0}° corner={c:F2}");
            return true;
        }

        /// <summary>
        /// Public access to the raw depth stream (world-space point + estimated normal)
        /// so accumulation mappers (e.g. <see cref="DepthOccupancyMapper"/>) can build
        /// occupancy grids without duplicating the GPU→CPU readback.
        /// </summary>
        public void SampleDepth(System.Action<Vector3, Vector3> onPoint)
        {
            if (Cam == null) return;
            ReadDepthPoints(onPoint);
        }

        /// <summary>Measures ceiling height from depth points stacked above the corner XZ.</summary>
        public bool TryMeasureCeiling(Vector3 cornerWorld, float floorY, out float ceilingY)
        {
            ceilingY = 0f;
            if (Cam == null) return false;

            var cornerXZ = new Vector2(cornerWorld.x, cornerWorld.z);
            var ys = new List<float>();

            ReadDepthPoints((wp, _) =>
            {
                if (wp.y < floorY + 0.3f) return;
                var d = new Vector2(wp.x, wp.z) - cornerXZ;
                if (d.sqrMagnitude > ceilingSearchRadius * ceilingSearchRadius) return;
                ys.Add(wp.y);
            });

            if (ys.Count < minCeilingPoints) return false;

            ys.Sort();
            ceilingY = ys[Mathf.Clamp(Mathf.FloorToInt(ys.Count * 0.90f), 0, ys.Count - 1)];

            float h = ceilingY - floorY;
            if (h < minRoomHeight || h > maxRoomHeight) return false;
            return true;
        }

        // ── Continuous mapping: walls along floor boundary edges ─────────────

        /// <summary>
        /// Collects depth points into an origin-local XZ grid, then confirms wall
        /// clusters along the boundary edges of detected floor planes. Returns raw
        /// detected walls for <see cref="RoomBuilder"/> to mesh and de-duplicate.
        /// </summary>
        public List<DetectedWall> DetectBoundaryWalls(Transform mapOrigin)
        {
            var result = new List<DetectedWall>();
            if (mapOrigin == null || Cam == null) return result;

            var worldToOrigin = mapOrigin.worldToLocalMatrix;

            // Phase 1: depth pixels → origin-local XZ grid.
            var grid = new Dictionary<(int, int), List<Vector3>>();
            ReadDepthPoints((wp, _) =>
            {
                var local = worldToOrigin.MultiplyPoint3x4(wp);
                var key = ToCell(local);
                if (!grid.TryGetValue(key, out var cell)) grid[key] = cell = new List<Vector3>();
                cell.Add(local);
            });
            if (grid.Count == 0) return result;

            // Phase 2: confirm walls along floor boundary edges.
            foreach (var plane in _planeManager.trackables)
            {
                if (!PlaneCollector.IsFloor(plane)) continue;

                var boundary = plane.boundary;
                float floorY = plane.transform.position.y;
                float floorYLocal = worldToOrigin.MultiplyPoint3x4(new Vector3(0f, floorY, 0f)).y;

                for (int i = 0; i < boundary.Length; i++)
                {
                    var a = boundary[i];
                    var b = boundary[(i + 1) % boundary.Length];
                    var aO = worldToOrigin.MultiplyPoint3x4(plane.transform.TransformPoint(new Vector3(a.x, 0f, a.y)));
                    var bO = worldToOrigin.MultiplyPoint3x4(plane.transform.TransformPoint(new Vector3(b.x, 0f, b.y)));

                    var pts = CollectNearEdge(grid, aO, bO, floorYLocal);
                    if (pts.Count < minPointsToConfirmWall) continue;

                    var centre = Centroid(pts);
                    var edgeDir = bO - aO; edgeDir.y = 0f; edgeDir = edgeDir.normalized;
                    var normal = new Vector3(-edgeDir.z, 0f, edgeDir.x).normalized;
                    var camLocal = worldToOrigin.MultiplyPoint3x4(Cam.transform.position);
                    if (Vector3.Dot(normal, camLocal - centre) < 0f) normal = -normal;

                    result.Add(new DetectedWall { normal = normal, centre = centre, points = pts });
                }
            }

            return result;
        }

        // ── Wall point collection ─────────────────────────────────────────────

        List<Vector2> CollectWallPointsXZ(float floorRefY)
        {
            var result = new List<Vector2>();
            var camPos = Cam.transform.position;
            var camFwd = Cam.transform.forward;

            ReadDepthPoints((wp, normal) =>
            {
                if (wp.y < floorRefY + minWallHeight) return;
                if (wp.y > floorRefY + wallScanMaxHeight) return;
                if (Mathf.Abs(normal.y) > wallNormalMax) return;

                var to = wp - camPos;
                if (Vector3.Dot(camFwd, to.normalized) < 0.2f) return;
                if (to.magnitude > maxCornerRange) return;

                result.Add(new Vector2(wp.x, wp.z));
            });
            return result;
        }

        List<Vector3> CollectNearEdge(Dictionary<(int, int), List<Vector3>> grid,
                                      Vector3 edgeA, Vector3 edgeB, float floorYLocal)
        {
            var result = new List<Vector3>();
            var checkedCells = new HashSet<(int, int)>();

            float edgeLen = Vector3.Distance(edgeA, edgeB);
            int steps = Mathf.Max(1, Mathf.CeilToInt(edgeLen / gridCellSize));
            int radius = Mathf.CeilToInt(boundaryMatchRadius / gridCellSize) + 1;

            for (int s = 0; s <= steps; s++)
            {
                var centerCell = ToCell(Vector3.Lerp(edgeA, edgeB, (float)s / steps));
                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    var cell = (centerCell.Item1 + dx, centerCell.Item2 + dz);
                    if (!checkedCells.Add(cell)) continue;
                    if (!grid.TryGetValue(cell, out var pts)) continue;
                    foreach (var p in pts)
                    {
                        if (p.y < floorYLocal + minWallHeight) continue;
                        if (DistanceToSegmentXZ(p, edgeA, edgeB) <= boundaryMatchRadius)
                            result.Add(p);
                    }
                }
            }
            return result;
        }

        // ── Depth readback ────────────────────────────────────────────────────

        /// <summary>
        /// Guarded GPU→CPU depth readback. Unprojects every sampled valid pixel to a
        /// world-space point, estimates its surface normal from neighbours, and hands
        /// both to <paramref name="onPoint"/>.
        /// </summary>
        void ReadDepthPoints(System.Action<Vector3, Vector3> onPoint)
        {
            Texture depthTex, confTex;
            Texture2D depthMap, confMap;
            try
            {
                if (_occlusionManager == null ||
                    !_occlusionManager.TryGetEnvironmentDepthTexture(out depthTex) || depthTex == null)
                    return;

                bool hasConf = _occlusionManager.TryGetEnvironmentDepthConfidenceTexture(out var confExt);
                confTex = hasConf ? confExt.texture : null;
                depthMap = Readback(depthTex, RenderTextureFormat.RFloat, TextureFormat.RFloat);
                if (depthMap == null) return;
                confMap = confTex != null ? Readback(confTex, RenderTextureFormat.ARGB32, TextureFormat.RGBA32) : null;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CornerDetector] depth read failed: {e.Message}");
                return;
            }

            int W = depthTex.width, H = depthTex.height;
            int confThreshold = (int)minConfidence;

            for (int y = sampleStep; y < H - sampleStep; y += sampleStep)
            for (int x = sampleStep; x < W - sampleStep; x += sampleStep)
            {
                if (confMap != null &&
                    Mathf.RoundToInt(confMap.GetPixel(x, y).r * 255f) < confThreshold) continue;

                float depth = depthMap.GetPixel(x, y).r;
                if (depth < minDepth || depth > maxDepth) continue;

                float dR = depthMap.GetPixel(x + sampleStep, y).r;
                float dU = depthMap.GetPixel(x, y + sampleStep).r;
                if (dR < minDepth || dR > maxDepth || dU < minDepth || dU > maxDepth) continue;

                var pC = Unproject(x,              y,              depth, W, H);
                var pR = Unproject(x + sampleStep, y,              dR,    W, H);
                var pU = Unproject(x,              y + sampleStep, dU,    W, H);
                var normal = Vector3.Cross(pR - pC, pU - pC).normalized;

                onPoint(pC, normal);
            }

            Destroy(depthMap);
            if (confMap != null) Destroy(confMap);
        }

        Vector3 Unproject(int px, int py, float depth, int W, int H)
        {
            float u = (px + 0.5f) / W;
            float v = (py + 0.5f) / H;
            return Cam.ScreenToWorldPoint(new Vector3(u * Screen.width, v * Screen.height, depth));
        }

        static Texture2D Readback(Texture src, RenderTextureFormat rtFmt, TextureFormat texFmt)
        {
            if (src == null) return null;

            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, rtFmt);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(src.width, src.height, texFmt, false);
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        // ── 2D line fitting + geometry helpers ───────────────────────────────

        bool Ransac2DLine(List<Vector2> pts, out Vector2 point, out Vector2 dir, out List<int> inliers)
        {
            point = Vector2.zero; dir = Vector2.right; inliers = null;
            if (pts.Count < 2) return false;

            int bestCount = 0;
            Vector2 bestP = Vector2.zero, bestD = Vector2.right;

            for (int iter = 0; iter < lineIterations; iter++)
            {
                int a = Random.Range(0, pts.Count);
                int b = Random.Range(0, pts.Count);
                if (a == b) continue;

                var d = pts[b] - pts[a];
                if (d.sqrMagnitude < 1e-6f) continue;
                d.Normalize();
                var n = new Vector2(-d.y, d.x);

                int count = 0;
                for (int k = 0; k < pts.Count; k++)
                    if (Mathf.Abs(Vector2.Dot(n, pts[k] - pts[a])) < lineInlierDist) count++;

                if (count > bestCount) { bestCount = count; bestP = pts[a]; bestD = d; }
            }

            if (bestCount < 2) return false;

            var bestN = new Vector2(-bestD.y, bestD.x);
            inliers = new List<int>(bestCount);
            for (int k = 0; k < pts.Count; k++)
                if (Mathf.Abs(Vector2.Dot(bestN, pts[k] - bestP)) < lineInlierDist) inliers.Add(k);

            point = bestP; dir = bestD;
            return true;
        }

        static bool IntersectLines2D(Vector2 p1, Vector2 d1, Vector2 p2, Vector2 d2, out Vector2 hit)
        {
            hit = Vector2.zero;
            float cross = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(cross) < 1e-6f) return false;

            var diff = p2 - p1;
            float t = (diff.x * d2.y - diff.y * d2.x) / cross;
            hit = p1 + t * d1;
            return true;
        }

        static void ExtentAlong(List<Vector2> pts, List<int> inliers, Vector2 corner, Vector2 dir,
                                out float min, out float max)
        {
            min = 0f; max = 0f;
            foreach (var idx in inliers)
            {
                float t = Vector2.Dot(pts[idx] - corner, dir);
                if (t < min) min = t;
                if (t > max) max = t;
            }
        }

        (int, int) ToCell(Vector3 p)
            => (Mathf.FloorToInt(p.x / gridCellSize), Mathf.FloorToInt(p.z / gridCellSize));

        static float DistanceToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float lenSq = dx * dx + dz * dz;
            float t = lenSq < 1e-6f ? 0f
                : Mathf.Clamp01(((p.x - a.x) * dx + (p.z - a.z) * dz) / lenSq);
            float cx = a.x + t * dx, cz = a.z + t * dz;
            float ex = p.x - cx, ez = p.z - cz;
            return Mathf.Sqrt(ex * ex + ez * ez);
        }

        static Vector3 Centroid(List<Vector3> pts)
        {
            var sum = Vector3.zero;
            foreach (var p in pts) sum += p;
            return sum / pts.Count;
        }
    }
}
