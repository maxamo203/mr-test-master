using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Scanner.ScanV3
{
    public interface IScanV3DepthSource
    {
        string Name { get; }
        bool TryCapture(Camera camera, List<ScanV3Observation> output);
    }

    // Fuente densa opcional: ARCore Depth funciona tambien en muchos telefonos sin
    // ToF; en iOS se usa cuando ARKit expone scene depth (dispositivos con LiDAR).
    public sealed class NativeEnvironmentDepthSource : IScanV3DepthSource
    {
        private readonly AROcclusionManager _occlusion;
        private readonly ARCameraManager _cameraManager;
        private readonly int _samplingStride;
        private readonly float _minDepth;
        private readonly float _maxDepth;

        public string Name => "profundidad nativa";

        public NativeEnvironmentDepthSource(AROcclusionManager occlusion,
                                            ARCameraManager cameraManager,
                                            int samplingStride,
                                            float minDepth,
                                            float maxDepth)
        {
            _occlusion = occlusion;
            _cameraManager = cameraManager;
            _samplingStride = Mathf.Max(2, samplingStride);
            _minDepth = minDepth;
            _maxDepth = maxDepth;
        }

        public bool TryCapture(Camera camera, List<ScanV3Observation> output)
        {
            if (_occlusion == null || _cameraManager == null || camera == null ||
                WorldOrigin.Instance == null ||
                !_cameraManager.TryGetIntrinsics(out var intrinsics) ||
                !_occlusion.TryAcquireEnvironmentDepthCpuImage(out var image))
                return false;

            try
            {
                if (image.planeCount < 1 ||
                    (image.format != XRCpuImage.Format.DepthFloat32 &&
                     image.format != XRCpuImage.Format.DepthUint16))
                    return false;

                var plane = image.GetPlane(0);
                int columns = Mathf.Max(2, (image.width - 1) / _samplingStride + 1);
                int rows = Mathf.Max(2, (image.height - 1) / _samplingStride + 1);
                var points = new Vector3[rows, columns];
                var valid = new bool[rows, columns];

                float scaleX = image.width / (float)Mathf.Max(1, intrinsics.resolution.x);
                float scaleY = image.height / (float)Mathf.Max(1, intrinsics.resolution.y);
                float fx = intrinsics.focalLength.x * scaleX;
                float fy = intrinsics.focalLength.y * scaleY;
                float cx = intrinsics.principalPoint.x * scaleX;
                float cy = intrinsics.principalPoint.y * scaleY;

                for (int row = 0; row < rows; row++)
                {
                    int y = Mathf.Min(image.height - 1, row * _samplingStride);
                    for (int column = 0; column < columns; column++)
                    {
                        int x = Mathf.Min(image.width - 1, column * _samplingStride);
                        float depth = ReadDepth(plane, image.format, x, y);
                        if (!float.IsFinite(depth) || depth < _minDepth || depth > _maxDepth)
                            continue;

                        // Las imagenes CPU usan origen superior izquierdo; Unity usa Y arriba.
                        var cameraPoint = new Vector3((x - cx) / fx * depth,
                                                      -(y - cy) / fy * depth,
                                                      depth);
                        points[row, column] = camera.transform.TransformPoint(cameraPoint);
                        valid[row, column] = true;
                    }
                }

                int before = output.Count;
                for (int row = 0; row < rows - 1; row++)
                for (int column = 0; column < columns - 1; column++)
                {
                    if (!valid[row, column] || !valid[row, column + 1] || !valid[row + 1, column])
                        continue;
                    var worldPoint = points[row, column];
                    var dx = points[row, column + 1] - worldPoint;
                    var dy = points[row + 1, column] - worldPoint;
                    var normalWorld = Vector3.Cross(dy, dx).normalized;
                    if (normalWorld.sqrMagnitude < 1e-6f) continue;
                    if (Vector3.Dot(normalWorld, camera.transform.position - worldPoint) < 0f)
                        normalWorld = -normalWorld;

                    output.Add(new ScanV3Observation(
                        WorldOrigin.Instance.ToRelative(worldPoint),
                        WorldOrigin.Instance.transform.InverseTransformDirection(normalWorld),
                        1f));
                }
                return output.Count > before;
            }
            finally
            {
                image.Dispose();
            }
        }

        private static float ReadDepth(XRCpuImage.Plane plane, XRCpuImage.Format format,
                                       int x, int y)
        {
            int index = y * plane.rowStride + x * plane.pixelStride;
            NativeArray<byte> data = plane.data;
            if (format == XRCpuImage.Format.DepthUint16)
            {
                if (index < 0 || index + 1 >= data.Length) return float.NaN;
                ushort millimeters = (ushort)(data[index] | data[index + 1] << 8);
                return millimeters * 0.001f;
            }
            if (index < 0 || index + 3 >= data.Length) return float.NaN;
            int bits = data[index] | data[index + 1] << 8 |
                       data[index + 2] << 16 | data[index + 3] << 24;
            return BitConverter.Int32BitsToSingle(bits);
        }
    }

    // Fallback universal y metrico. Acumula profundidad, planos y feature points
    // entregados por el proveedor AR sin asumir que existe LiDAR.
    public sealed class ARRaycastDepthSource : IScanV3DepthSource
    {
        private readonly ARRaycastManager _raycasts;
        private readonly int _columns;
        private readonly int _rows;
        private readonly List<ARRaycastHit> _hits = new();

        public string Name => "raycasts multivista";

        public ARRaycastDepthSource(ARRaycastManager raycasts, int columns, int rows)
        {
            _raycasts = raycasts;
            _columns = Mathf.Clamp(columns, 3, 24);
            _rows = Mathf.Clamp(rows, 3, 24);
        }

        public bool TryCapture(Camera camera, List<ScanV3Observation> output)
        {
            if (_raycasts == null || camera == null || WorldOrigin.Instance == null) return false;
            int before = output.Count;
            const TrackableType types = TrackableType.Depth | TrackableType.PlaneWithinPolygon |
                                        TrackableType.FeaturePoint;
            var positions = new Vector3[_rows, _columns];
            var normals = new Vector3[_rows, _columns];
            var hitTypes = new TrackableType[_rows, _columns];
            var valid = new bool[_rows, _columns];
            for (int row = 0; row < _rows; row++)
            for (int column = 0; column < _columns; column++)
            {
                var screen = new Vector2(
                    Screen.width * (column + 0.5f) / _columns,
                    Screen.height * (row + 0.5f) / _rows);
                _hits.Clear();
                if (!_raycasts.Raycast(screen, _hits, types) || _hits.Count == 0) continue;
                var hit = _hits[0];
                positions[row, column] = hit.pose.position;
                normals[row, column] = hit.pose.up;
                hitTypes[row, column] = hit.hitType;
                valid[row, column] = true;
            }

            for (int row = 0; row < _rows; row++)
            for (int column = 0; column < _columns; column++)
            {
                if (!valid[row, column]) continue;
                var worldPoint = positions[row, column];
                var hitType = hitTypes[row, column];
                Vector3 normalWorld;
                float confidence;
                if ((hitType & (TrackableType.Depth | TrackableType.PlaneWithinPolygon)) != 0)
                {
                    normalWorld = normals[row, column];
                    confidence = (hitType & TrackableType.Depth) != 0 ? 0.9f : 0.7f;
                }
                else if (!TryEstimateFeatureNormal(positions, valid, row, column,
                                                   camera.transform.position, out normalWorld))
                {
                    // La pose de un feature point no garantiza una normal de superficie.
                    // Sin vecinos geometricos se descarta para evitar paredes fantasma.
                    continue;
                }
                else confidence = 0.45f;

                var toCamera = camera.transform.position - worldPoint;
                if (Vector3.Dot(normalWorld, toCamera) < 0f) normalWorld = -normalWorld;
                output.Add(new ScanV3Observation(
                    WorldOrigin.Instance.ToRelative(worldPoint),
                    WorldOrigin.Instance.transform.InverseTransformDirection(normalWorld),
                    confidence));
            }
            return output.Count > before;
        }

        private bool TryEstimateFeatureNormal(Vector3[,] positions, bool[,] valid,
                                              int row, int column, Vector3 cameraPosition,
                                              out Vector3 normal)
        {
            normal = default;
            int otherColumn = column + 1 < _columns && valid[row, column + 1]
                ? column + 1 : column - 1;
            int otherRow = row + 1 < _rows && valid[row + 1, column]
                ? row + 1 : row - 1;
            if (otherColumn < 0 || otherRow < 0 ||
                !valid[row, otherColumn] || !valid[otherRow, column]) return false;
            Vector3 dx = positions[row, otherColumn] - positions[row, column];
            Vector3 dy = positions[otherRow, column] - positions[row, column];
            // Vecinos muy alejados probablemente pertenecen a objetos distintos.
            if (dx.magnitude > 0.45f || dy.magnitude > 0.45f ||
                dx.sqrMagnitude < 1e-5f || dy.sqrMagnitude < 1e-5f) return false;
            normal = Vector3.Cross(dy, dx).normalized;
            if (normal.sqrMagnitude < 1e-5f) return false;
            if (Vector3.Dot(normal, cameraPosition - positions[row, column]) < 0f)
                normal = -normal;
            return true;
        }
    }
}
