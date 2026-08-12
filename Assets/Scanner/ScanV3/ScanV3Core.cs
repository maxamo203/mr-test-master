using System;
using System.Collections.Generic;
using UnityEngine;
using Scanner.ScanV3;

namespace Scanner.ScanV3
{
    [Serializable]
    public sealed class ScanV3Keyframe
    {
        public int id;
        public double timestamp;
        public Vector3 initialPositionLocal;
        public Quaternion initialRotationLocal = Quaternion.identity;
        public Vector3 optimizedPositionLocal;
        public Quaternion optimizedRotationLocal = Quaternion.identity;
        public Vector2 focalLength;
        public Vector2 principalPoint;
        public Vector2Int imageResolution;
        public float sharpness;
        public float meanLuminance;
        public float trackingConfidence;
        public string imageFile;
        public string observationFile;
        public float[] descriptor;
        [NonSerialized] public List<ScanV3CameraObservation> observations = new();
    }

    [Serializable]
    public struct ScanV3CameraObservation
    {
        public Vector3 positionCamera;
        public Vector3 normalCamera;
        public float confidence;
    }

    [Serializable]
    public sealed class ScanV3BundleManifest
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public string captureId;
        public string createdUtc;
        public bool completed;
        public List<ScanV3Keyframe> keyframes = new();
    }

    public readonly struct ScanV3FrameQuality
    {
        public readonly float MeanLuminance;
        public readonly float Sharpness;
        public readonly bool Acceptable;
        public readonly string Rejection;

        public ScanV3FrameQuality(float mean, float sharpness, bool acceptable, string rejection)
        {
            MeanLuminance = mean;
            Sharpness = sharpness;
            Acceptable = acceptable;
            Rejection = rejection;
        }
    }

    public static class ScanV3Vision
    {
        // Descriptor luminoso 8x8 normalizado. Es deliberadamente pequeno: sirve
        // para proponer candidatos de loop; la geometria decide si se aceptan.
        public static float[] BuildDescriptor(byte[] luminance, int width, int height)
        {
            const int bins = 8;
            var descriptor = new float[bins * bins];
            if (luminance == null || width < bins || height < bins) return descriptor;
            for (int by = 0; by < bins; by++)
            for (int bx = 0; bx < bins; bx++)
            {
                int x0 = bx * width / bins, x1 = (bx + 1) * width / bins;
                int y0 = by * height / bins, y1 = (by + 1) * height / bins;
                double sum = 0d;
                int count = 0;
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    sum += luminance[y * width + x];
                    count++;
                }
                descriptor[by * bins + bx] = count > 0 ? (float)(sum / count / 255d) : 0f;
            }
            Normalize(descriptor);
            return descriptor;
        }

        public static ScanV3FrameQuality Evaluate(byte[] luminance, int width, int height,
                                                   float minimumLuminance = 0.08f,
                                                   float maximumLuminance = 0.94f,
                                                   float minimumSharpness = 0.018f)
        {
            if (luminance == null || luminance.Length < width * height || width < 3 || height < 3)
                return new ScanV3FrameQuality(0f, 0f, false, "imagen no disponible");
            double sum = 0d, gradient = 0d;
            int gradients = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                sum += luminance[index];
                if (x == 0 || y == 0) continue;
                gradient += Math.Abs(luminance[index] - luminance[index - 1]);
                gradient += Math.Abs(luminance[index] - luminance[index - width]);
                gradients += 2;
            }
            float mean = (float)(sum / (width * height) / 255d);
            float sharpness = gradients > 0 ? (float)(gradient / gradients / 255d) : 0f;
            if (mean < minimumLuminance)
                return new ScanV3FrameQuality(mean, sharpness, false, "muy oscuro");
            if (mean > maximumLuminance)
                return new ScanV3FrameQuality(mean, sharpness, false, "sobreexpuesto");
            if (sharpness < minimumSharpness)
                return new ScanV3FrameQuality(mean, sharpness, false, "imagen borrosa o sin textura");
            return new ScanV3FrameQuality(mean, sharpness, true, null);
        }

        public static float Similarity(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length == 0 || a.Length != b.Length) return -1f;
            double dot = 0d, aa = 0d, bb = 0d;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                aa += a[i] * a[i];
                bb += b[i] * b[i];
            }
            if (aa < 1e-10 || bb < 1e-10) return -1f;
            return (float)(dot / Math.Sqrt(aa * bb));
        }

        private static void Normalize(float[] values)
        {
            float mean = 0f;
            for (int i = 0; i < values.Length; i++) mean += values[i];
            mean /= Mathf.Max(1, values.Length);
            float norm = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                values[i] -= mean;
                norm += values[i] * values[i];
            }
            norm = Mathf.Sqrt(norm);
            if (norm < 1e-6f) return;
            for (int i = 0; i < values.Length; i++) values[i] /= norm;
        }
    }

    public enum ScanV3EdgeKind { Odometry, LoopClosure, AnchorPrior }

    [Serializable]
    public struct ScanV3PoseEdge
    {
        public int from;
        public int to;
        public Vector3 expectedWorldDelta;
        public float expectedYawDelta;
        public float weight;
        public ScanV3EdgeKind kind;
    }

    public sealed class ScanV3PoseGraphResult
    {
        public bool accepted;
        public float initialResidual;
        public float finalResidual;
        public int iterations;
        public Vector3[] positions;
        public float[] yaws;
    }

    // Optimizador compacto para habitaciones: gravedad viene de AR, por lo que se
    // optimizan posicion 3D y yaw. Conserva el nodo 0 como prior metrico fijo.
    public static class ScanV3PoseGraph
    {
        public static ScanV3PoseGraphResult Optimize(IReadOnlyList<ScanV3Keyframe> nodes,
                                                      IReadOnlyList<ScanV3PoseEdge> edges,
                                                      int maximumIterations = 80,
                                                      float learningRate = 0.22f,
                                                      float huberMeters = 0.25f)
        {
            var result = new ScanV3PoseGraphResult();
            int count = nodes?.Count ?? 0;
            result.positions = new Vector3[count];
            result.yaws = new float[count];
            if (count == 0) return result;
            for (int i = 0; i < count; i++)
            {
                result.positions[i] = nodes[i].initialPositionLocal;
                result.yaws[i] = NormalizeYaw(nodes[i].initialRotationLocal.eulerAngles.y);
            }
            result.initialResidual = Residual(result.positions, result.yaws, edges, huberMeters);
            var bestPositions = (Vector3[])result.positions.Clone();
            var bestYaws = (float[])result.yaws.Clone();
            float best = result.initialResidual;

            for (int iteration = 0; iteration < maximumIterations; iteration++)
            {
                for (int e = 0; e < (edges?.Count ?? 0); e++)
                {
                    var edge = edges[e];
                    if (edge.from < 0 || edge.to < 0 || edge.from >= count || edge.to >= count ||
                        edge.from == edge.to) continue;
                    Vector3 error = (result.positions[edge.to] - result.positions[edge.from]) -
                                    edge.expectedWorldDelta;
                    float yawError = Mathf.DeltaAngle(
                        edge.expectedYawDelta,
                        Mathf.DeltaAngle(result.yaws[edge.from], result.yaws[edge.to]));
                    float robust = HuberWeight(error.magnitude, huberMeters) * Mathf.Max(0f, edge.weight);
                    Vector3 correction = error * (learningRate * robust * 0.5f);
                    float yawCorrection = yawError * (learningRate * robust * 0.5f);
                    if (edge.from != 0)
                    {
                        result.positions[edge.from] += correction;
                        result.yaws[edge.from] = NormalizeYaw(result.yaws[edge.from] + yawCorrection);
                    }
                    result.positions[edge.to] -= correction;
                    result.yaws[edge.to] = NormalizeYaw(result.yaws[edge.to] - yawCorrection);
                }
                float residual = Residual(result.positions, result.yaws, edges, huberMeters);
                result.iterations = iteration + 1;
                if (!float.IsFinite(residual)) break;
                if (residual < best)
                {
                    best = residual;
                    Array.Copy(result.positions, bestPositions, count);
                    Array.Copy(result.yaws, bestYaws, count);
                }
            }
            result.finalResidual = best;
            result.accepted = float.IsFinite(best) && best <= result.initialResidual + 1e-5f;
            result.positions = result.accepted ? bestPositions : InitialPositions(nodes);
            result.yaws = result.accepted ? bestYaws : InitialYaws(nodes);
            return result;
        }

        public static bool TryCreateLoopEdge(IReadOnlyList<ScanV3Keyframe> frames,
                                             int currentIndex, out ScanV3PoseEdge edge,
                                             int minimumSeparation = 8,
                                             float minimumSimilarity = 0.965f,
                                             float maximumPositionDistance = 0.45f,
                                             float maximumViewAngle = 25f)
        {
            edge = default;
            if (frames == null || currentIndex < minimumSeparation || currentIndex >= frames.Count)
                return false;
            var current = frames[currentIndex];
            int bestIndex = -1;
            float bestSimilarity = minimumSimilarity;
            for (int i = 0; i <= currentIndex - minimumSeparation; i++)
            {
                var candidate = frames[i];
                if (Vector3.Distance(candidate.initialPositionLocal, current.initialPositionLocal) >
                    maximumPositionDistance) continue;
                float viewAngle = Quaternion.Angle(candidate.initialRotationLocal,
                                                   current.initialRotationLocal);
                if (viewAngle > maximumViewAngle) continue;
                float similarity = ScanV3Vision.Similarity(candidate.descriptor, current.descriptor);
                if (similarity <= bestSimilarity) continue;
                if (!HasGeometricOverlap(candidate, current, 0.20f, 0.18f)) continue;
                bestSimilarity = similarity;
                bestIndex = i;
            }
            if (bestIndex < 0) return false;
            // La misma apariencia y orientacion desde posiciones cercanas propone
            // reobservacion del mismo lugar. El peso conservador evita sobrecorregir.
            edge = new ScanV3PoseEdge
            {
                from = bestIndex,
                to = currentIndex,
                expectedWorldDelta = Vector3.zero,
                expectedYawDelta = 0f,
                weight = Mathf.Lerp(0.35f, 0.75f, Mathf.InverseLerp(minimumSimilarity, 1f, bestSimilarity)),
                kind = ScanV3EdgeKind.LoopClosure,
            };
            return true;
        }

        private static bool HasGeometricOverlap(ScanV3Keyframe a, ScanV3Keyframe b,
                                                float voxelSize, float minimumRatio)
        {
            if (a.observations == null || b.observations == null ||
                a.observations.Count < 12 || b.observations.Count < 12) return false;
            var occupied = new HashSet<Vector3Int>();
            for (int i = 0; i < a.observations.Count; i++)
            {
                Vector3 point = a.initialPositionLocal +
                                a.initialRotationLocal * a.observations[i].positionCamera;
                occupied.Add(Quantize(point, voxelSize));
            }
            int tested = 0, matching = 0;
            int step = Mathf.Max(1, b.observations.Count / 256);
            for (int i = 0; i < b.observations.Count; i += step)
            {
                Vector3 point = b.initialPositionLocal +
                                b.initialRotationLocal * b.observations[i].positionCamera;
                var key = Quantize(point, voxelSize);
                tested++;
                bool found = false;
                for (int x = -1; x <= 1 && !found; x++)
                for (int y = -1; y <= 1 && !found; y++)
                for (int z = -1; z <= 1; z++)
                    if (occupied.Contains(key + new Vector3Int(x, y, z))) { found = true; break; }
                if (found) matching++;
            }
            return tested > 0 && matching / (float)tested >= minimumRatio;
        }

        private static Vector3Int Quantize(Vector3 point, float size) => new(
            Mathf.RoundToInt(point.x / size),
            Mathf.RoundToInt(point.y / size),
            Mathf.RoundToInt(point.z / size));

        private static float Residual(Vector3[] positions, float[] yaws,
                                      IReadOnlyList<ScanV3PoseEdge> edges, float huber)
        {
            if (edges == null || edges.Count == 0) return 0f;
            double total = 0d;
            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (edge.from < 0 || edge.to < 0 || edge.from >= positions.Length ||
                    edge.to >= positions.Length) continue;
                float distance = ((positions[edge.to] - positions[edge.from]) -
                                  edge.expectedWorldDelta).magnitude;
                float yaw = Mathf.Abs(Mathf.DeltaAngle(
                    edge.expectedYawDelta, Mathf.DeltaAngle(yaws[edge.from], yaws[edge.to]))) / 90f;
                float spatial = distance <= huber ? 0.5f * distance * distance :
                    huber * (distance - 0.5f * huber);
                total += Mathf.Max(0f, edge.weight) * (spatial + 0.05f * yaw * yaw);
            }
            return (float)(total / Mathf.Max(1, edges.Count));
        }

        private static float HuberWeight(float residual, float delta) =>
            residual <= delta || residual < 1e-6f ? 1f : delta / residual;
        private static float NormalizeYaw(float yaw) => Mathf.DeltaAngle(0f, yaw);
        private static Vector3[] InitialPositions(IReadOnlyList<ScanV3Keyframe> nodes)
        {
            var values = new Vector3[nodes.Count];
            for (int i = 0; i < values.Length; i++) values[i] = nodes[i].initialPositionLocal;
            return values;
        }
        private static float[] InitialYaws(IReadOnlyList<ScanV3Keyframe> nodes)
        {
            var values = new float[nodes.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = NormalizeYaw(nodes[i].initialRotationLocal.eulerAngles.y);
            return values;
        }
    }
}
