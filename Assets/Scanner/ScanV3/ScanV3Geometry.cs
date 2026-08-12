using System;
using System.Collections.Generic;
using UnityEngine;

namespace Scanner.ScanV3
{
    [Serializable]
    public struct ScanV3Observation
    {
        public Vector3 positionLocal;
        public Vector3 normalLocal;
        [Range(0f, 1f)] public float confidence;

        public ScanV3Observation(Vector3 position, Vector3 normal, float confidence = 1f)
        {
            positionLocal = position;
            normalLocal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            this.confidence = Mathf.Clamp01(confidence);
        }
    }

    [Serializable]
    public struct ScanV3Surfel
    {
        public Vector3 positionLocal;
        public Vector3 normalLocal;
        public float confidence;
        public int observations;
    }

    [Serializable]
    public struct ScanV3WallCandidate
    {
        public Vector3 aLocal;
        public Vector3 bLocal;
        public Vector3 normalLocal;
        public float height;
        public float confidence;
    }

    // Volumen sparse de surfels. Cada voxel conserva una superficie promedio y su
    // normal; acumular varias vistas reduce el ruido sin crear geometria persistente.
    public sealed class SparseSurfelVolume
    {
        private struct Cell
        {
            public Vector3 weightedPosition;
            public Vector3 weightedNormal;
            public float weight;
            public int observations;
        }

        private readonly Dictionary<Vector3Int, Cell> _cells = new();
        public float VoxelSize { get; }
        public int MaximumCells { get; }
        public int Count => _cells.Count;
        public bool IsFull => _cells.Count >= MaximumCells;

        public SparseSurfelVolume(float voxelSize, int maximumCells = 150000)
        {
            VoxelSize = Mathf.Clamp(voxelSize, 0.02f, 0.25f);
            MaximumCells = Mathf.Max(100, maximumCells);
        }

        public void Clear() => _cells.Clear();

        public void Integrate(IReadOnlyList<ScanV3Observation> observations)
        {
            if (observations == null) return;
            // Una llamada representa un keyframe. Primero consolidamos todas sus
            // muestras por voxel para que cien pixels del MISMO frame cuenten como
            // una sola observacion temporal, no como cien vistas independientes.
            var frameCells = new Dictionary<Vector3Int, Cell>();
            for (int i = 0; i < observations.Count; i++)
            {
                var incoming = observations[i];
                if (!IsFinite(incoming.positionLocal) || !IsFinite(incoming.normalLocal) ||
                    incoming.normalLocal.sqrMagnitude < 1e-6f || incoming.confidence <= 0f)
                    continue;

                var key = Quantize(incoming.positionLocal);
                frameCells.TryGetValue(key, out var cell);
                var normal = incoming.normalLocal.normalized;
                if (cell.weight > 0f && Vector3.Dot(cell.weightedNormal, normal) < 0f)
                    normal = -normal;

                float weight = Mathf.Max(0.05f, incoming.confidence);
                cell.weightedPosition += incoming.positionLocal * weight;
                cell.weightedNormal += normal * weight;
                cell.weight += weight;
                cell.observations++;
                frameCells[key] = cell;
            }

            foreach (var pair in frameCells)
            {
                if (!_cells.TryGetValue(pair.Key, out var accumulated) && IsFull)
                    continue;
                var frame = pair.Value;
                var position = frame.weightedPosition / Mathf.Max(0.0001f, frame.weight);
                var normal = frame.weightedNormal.normalized;
                if (accumulated.weight > 0f && Vector3.Dot(accumulated.weightedNormal, normal) < 0f)
                    normal = -normal;
                float frameWeight = Mathf.Clamp01(frame.weight / Mathf.Max(1, frame.observations));
                frameWeight = Mathf.Max(0.05f, frameWeight);
                accumulated.weightedPosition += position * frameWeight;
                accumulated.weightedNormal += normal * frameWeight;
                accumulated.weight += frameWeight;
                accumulated.observations++;
                _cells[pair.Key] = accumulated;
            }
        }

        public List<ScanV3Surfel> Extract(int minObservations)
        {
            var result = new List<ScanV3Surfel>(_cells.Count);
            foreach (var cell in _cells.Values)
            {
                if (cell.observations < minObservations || cell.weight <= 0f) continue;
                var normal = cell.weightedNormal.normalized;
                if (normal.sqrMagnitude < 1e-6f) continue;
                result.Add(new ScanV3Surfel
                {
                    positionLocal = cell.weightedPosition / cell.weight,
                    normalLocal = normal,
                    confidence = Mathf.Clamp01(cell.weight / Mathf.Max(1, cell.observations)),
                    observations = cell.observations,
                });
            }
            return result;
        }

        private Vector3Int Quantize(Vector3 point) => new(
            Mathf.RoundToInt(point.x / VoxelSize),
            Mathf.RoundToInt(point.y / VoxelSize),
            Mathf.RoundToInt(point.z / VoxelSize));

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    public sealed class ScanV3StructuralResult
    {
        public bool hasFloor;
        public float floorY;
        public readonly List<ScanV3WallCandidate> walls = new();
    }

    public static class ScanV3Geometry
    {
        private sealed class WallCluster
        {
            public Vector3 normal;
            public float offset;
            public float minAlong = float.PositiveInfinity;
            public float maxAlong = float.NegativeInfinity;
            public float minY = float.PositiveInfinity;
            public float maxY = float.NegativeInfinity;
            public float weight;
            public int count;
            public readonly List<ScanV3Surfel> points = new();
        }

        public static ScanV3StructuralResult ExtractStructure(
            IReadOnlyList<ScanV3Surfel> surfels,
            int minFloorPoints = 12,
            int minWallPoints = 12,
            float angleToleranceDegrees = 12f,
            float planeTolerance = 0.14f,
            float minWallLength = 0.55f,
            float minWallHeight = 0.65f,
            float maximumSurfaceGap = 0.45f,
            float minimumFloorArea = 0.8f)
        {
            var result = new ScanV3StructuralResult();
            if (surfels == null || surfels.Count == 0) return result;

            FindFloor(surfels, minFloorPoints, minimumFloorArea, result);
            var clusters = new List<WallCluster>();
            float minNormalDot = Mathf.Cos(angleToleranceDegrees * Mathf.Deg2Rad);

            for (int i = 0; i < surfels.Count; i++)
            {
                var surfel = surfels[i];
                var normal = Vector3.ProjectOnPlane(surfel.normalLocal, Vector3.up);
                if (Mathf.Abs(surfel.normalLocal.normalized.y) > 0.35f || normal.sqrMagnitude < 1e-5f)
                    continue;
                normal.Normalize();
                Canonicalize(ref normal);
                float offset = Vector3.Dot(surfel.positionLocal, normal);

                WallCluster cluster = null;
                for (int j = 0; j < clusters.Count; j++)
                {
                    if (Vector3.Dot(clusters[j].normal, normal) >= minNormalDot &&
                        Mathf.Abs(clusters[j].offset - offset) <= planeTolerance)
                    {
                        cluster = clusters[j];
                        break;
                    }
                }
                if (cluster == null)
                {
                    cluster = new WallCluster { normal = normal, offset = offset };
                    clusters.Add(cluster);
                }

                float weight = Mathf.Max(0.05f, surfel.confidence);
                float total = cluster.weight + weight;
                cluster.normal = (cluster.normal * cluster.weight + normal * weight).normalized;
                cluster.offset = total > 0f
                    ? (cluster.offset * cluster.weight + offset * weight) / total : offset;
                cluster.weight = total;
                cluster.count++;
                cluster.points.Add(surfel);

                var tangent = Vector3.Cross(Vector3.up, cluster.normal).normalized;
                float along = Vector3.Dot(surfel.positionLocal, tangent);
                cluster.minAlong = Mathf.Min(cluster.minAlong, along);
                cluster.maxAlong = Mathf.Max(cluster.maxAlong, along);
                cluster.minY = Mathf.Min(cluster.minY, surfel.positionLocal.y);
                cluster.maxY = Mathf.Max(cluster.maxY, surfel.positionLocal.y);
            }

            foreach (var cluster in clusters)
            {
                var tangent = Vector3.Cross(Vector3.up, cluster.normal).normalized;
                cluster.points.Sort((a, b) => Vector3.Dot(a.positionLocal, tangent)
                    .CompareTo(Vector3.Dot(b.positionLocal, tangent)));
                int segmentStart = 0;
                for (int index = 1; index <= cluster.points.Count; index++)
                {
                    bool atEnd = index == cluster.points.Count;
                    float gap = atEnd ? float.PositiveInfinity :
                        Vector3.Dot(cluster.points[index].positionLocal, tangent) -
                        Vector3.Dot(cluster.points[index - 1].positionLocal, tangent);
                    if (!atEnd && gap <= maximumSurfaceGap) continue;
                    AddWallSegment(cluster, tangent, segmentStart, index, minWallPoints,
                                   minWallLength, minWallHeight, result);
                    segmentStart = index;
                }
            }
            CloseNearbyCorners(result.walls, 0.38f);
            return result;
        }

        private static void AddWallSegment(WallCluster cluster, Vector3 tangent,
                                           int start, int endExclusive, int minPoints,
                                           float minLength, float minHeight,
                                           ScanV3StructuralResult result)
        {
            if (endExclusive - start < minPoints) return;
            float minAlong = float.PositiveInfinity, maxAlong = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (int i = start; i < endExclusive; i++)
            {
                var point = cluster.points[i].positionLocal;
                float along = Vector3.Dot(point, tangent);
                minAlong = Mathf.Min(minAlong, along);
                maxAlong = Mathf.Max(maxAlong, along);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
            float baseY = minY;
            if (result.hasFloor && Mathf.Abs(baseY - result.floorY) <= 0.35f)
                baseY = result.floorY;
            float height = maxY - baseY;
            if (maxAlong - minAlong < minLength || height < minHeight) return;
            var planePoint = cluster.normal * cluster.offset;
            var a = planePoint + tangent * minAlong;
            var b = planePoint + tangent * maxAlong;
            a.y = b.y = baseY;
            result.walls.Add(new ScanV3WallCandidate
            {
                aLocal = a,
                bLocal = b,
                normalLocal = cluster.normal,
                height = height,
                confidence = Mathf.Clamp01((endExclusive - start) / 80f),
            });
        }

        public static void CloseNearbyCorners(List<ScanV3WallCandidate> walls,
                                              float endpointTolerance)
        {
            if (walls == null) return;
            for (int i = 0; i < walls.Count; i++)
            for (int j = i + 1; j < walls.Count; j++)
            {
                var first = walls[i];
                var second = walls[j];
                var firstDirection = Horizontal(first.bLocal - first.aLocal).normalized;
                var secondDirection = Horizontal(second.bLocal - second.aLocal).normalized;
                if (firstDirection.sqrMagnitude < 1e-5f || secondDirection.sqrMagnitude < 1e-5f ||
                    Mathf.Abs(Vector3.Dot(firstDirection, secondDirection)) > 0.45f)
                    continue;
                if (!TryLineIntersectionXZ(first.aLocal, firstDirection,
                                           second.aLocal, secondDirection, out var intersection))
                    continue;

                int firstEnd = NearestEndpoint(first, intersection, out float firstDistance);
                int secondEnd = NearestEndpoint(second, intersection, out float secondDistance);
                if (firstDistance > endpointTolerance || secondDistance > endpointTolerance) continue;

                if (firstEnd == 0) first.aLocal = WithY(intersection, first.aLocal.y);
                else first.bLocal = WithY(intersection, first.bLocal.y);
                if (secondEnd == 0) second.aLocal = WithY(intersection, second.aLocal.y);
                else second.bLocal = WithY(intersection, second.bLocal.y);
                walls[i] = first;
                walls[j] = second;
            }
        }

        private static bool TryLineIntersectionXZ(Vector3 p, Vector3 r, Vector3 q, Vector3 s,
                                                  out Vector3 intersection)
        {
            float cross = r.x * s.z - r.z * s.x;
            if (Mathf.Abs(cross) < 1e-5f) { intersection = default; return false; }
            var qp = q - p;
            float t = (qp.x * s.z - qp.z * s.x) / cross;
            intersection = p + r * t;
            return float.IsFinite(intersection.x) && float.IsFinite(intersection.z);
        }

        private static int NearestEndpoint(ScanV3WallCandidate wall, Vector3 point,
                                           out float distance)
        {
            float a = Vector2.Distance(new Vector2(wall.aLocal.x, wall.aLocal.z),
                                       new Vector2(point.x, point.z));
            float b = Vector2.Distance(new Vector2(wall.bLocal.x, wall.bLocal.z),
                                       new Vector2(point.x, point.z));
            distance = Mathf.Min(a, b);
            return a <= b ? 0 : 1;
        }

        private static Vector3 WithY(Vector3 value, float y) => new(value.x, y, value.z);
        private static Vector3 Horizontal(Vector3 value) => new(value.x, 0f, value.z);

        public static bool IsDuplicate(WallObject wall, ScanV3WallCandidate candidate,
                                       float planeTolerance = 0.18f,
                                       float overlapThreshold = 0.65f)
        {
            if (wall == null) return false;
            var existingDirection = Horizontal(wall.BLocal - wall.ALocal);
            var candidateDirection = Horizontal(candidate.bLocal - candidate.aLocal);
            if (existingDirection.sqrMagnitude < 1e-5f || candidateDirection.sqrMagnitude < 1e-5f)
                return false;
            existingDirection.Normalize();
            candidateDirection.Normalize();
            if (Mathf.Abs(Vector3.Dot(existingDirection, candidateDirection)) < 0.96f)
                return false;

            var normal = Vector3.Cross(Vector3.up, candidateDirection).normalized;
            float existingPlane = Vector3.Dot((wall.ALocal + wall.BLocal) * 0.5f, normal);
            float candidatePlane = Vector3.Dot((candidate.aLocal + candidate.bLocal) * 0.5f, normal);
            if (Mathf.Abs(existingPlane - candidatePlane) > planeTolerance) return false;

            float e0 = Vector3.Dot(wall.ALocal, candidateDirection);
            float e1 = Vector3.Dot(wall.BLocal, candidateDirection);
            float c0 = Vector3.Dot(candidate.aLocal, candidateDirection);
            float c1 = Vector3.Dot(candidate.bLocal, candidateDirection);
            if (e0 > e1) (e0, e1) = (e1, e0);
            if (c0 > c1) (c0, c1) = (c1, c0);
            float overlap = Mathf.Max(0f, Mathf.Min(e1, c1) - Mathf.Max(e0, c0));
            float shortest = Mathf.Min(e1 - e0, c1 - c0);
            return shortest > 1e-4f && overlap / shortest >= overlapThreshold;
        }

        private static void FindFloor(IReadOnlyList<ScanV3Surfel> surfels, int minimum,
                                      float minimumArea, ScanV3StructuralResult result)
        {
            const float binSize = 0.06f;
            var bins = new Dictionary<int, (float sum, int count)>();
            for (int i = 0; i < surfels.Count; i++)
            {
                var surfel = surfels[i];
                if (Mathf.Abs(surfel.normalLocal.normalized.y) < 0.82f) continue;
                int bin = Mathf.RoundToInt(surfel.positionLocal.y / binSize);
                bins.TryGetValue(bin, out var value);
                value.sum += surfel.positionLocal.y;
                value.count++;
                bins[bin] = value;
            }

            int selectedBin = int.MaxValue;
            int selectedCount = 0;
            foreach (var pair in bins)
            {
                if (pair.Value.count < minimum) continue;
                float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
                float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
                for (int i = 0; i < surfels.Count; i++)
                {
                    var surfel = surfels[i];
                    if (Mathf.Abs(surfel.normalLocal.normalized.y) < 0.82f ||
                        Mathf.RoundToInt(surfel.positionLocal.y / binSize) != pair.Key) continue;
                    minX = Mathf.Min(minX, surfel.positionLocal.x);
                    maxX = Mathf.Max(maxX, surfel.positionLocal.x);
                    minZ = Mathf.Min(minZ, surfel.positionLocal.z);
                    maxZ = Mathf.Max(maxZ, surfel.positionLocal.z);
                }
                if ((maxX - minX) * (maxZ - minZ) < minimumArea) continue;
                // El piso es la superficie horizontal valida mas baja. El conteo
                // desempata bins contiguos causados por ruido.
                if (pair.Key < selectedBin ||
                    (pair.Key == selectedBin && pair.Value.count > selectedCount))
                {
                    selectedBin = pair.Key;
                    selectedCount = pair.Value.count;
                }
            }
            if (selectedBin == int.MaxValue) return;
            result.hasFloor = true;
            result.floorY = bins[selectedBin].sum / bins[selectedBin].count;
        }

        private static void Canonicalize(ref Vector3 normal)
        {
            if (normal.x < -1e-4f || (Mathf.Abs(normal.x) <= 1e-4f && normal.z < 0f))
                normal = -normal;
        }
    }
}
