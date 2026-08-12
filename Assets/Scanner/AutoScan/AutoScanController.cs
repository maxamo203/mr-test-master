using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Scanner.AutoScan
{
    // Modelo y núcleo geométrico puro, compartido por runtime y pruebas de Editor.
    public enum AutoScanPlaneKind
    {
        HorizontalUp,
        HorizontalDown,
        Vertical,
    }

    // Observación ya convertida a espacio local de WorldOrigin. No es persistente:
    // sólo alimenta la consolidación que finalmente crea objetos Scanner normales.
    [Serializable]
    public struct AutoScanPlaneSample
    {
        public string sourceId;
        public AutoScanPlaneKind kind;
        public Vector3 centerLocal;
        public Vector3 normalLocal;
        public float width;
        public float height;
        public float minY;
        public float maxY;
        public int observations;
        public float area;
    }

    [Serializable]
    public struct AutoScanWallCandidate
    {
        public Vector3 aLocal;
        public Vector3 bLocal;
        public Vector3 normalLocal;
        public float height;
        public float confidence;
    }

    // Núcleo geométrico puro: no mantiene estado ni depende del ciclo de vida de Unity.
    public static class AutoScanModel
    {
        private sealed class WallInterval
        {
            public Vector3 normal;
            public Vector3 tangent;
            public float planeOffset;
            public float minAlong;
            public float maxAlong;
            public float minY;
            public float maxY;
            public float weightedConfidence;
            public float weight;
        }

        public static List<AutoScanWallCandidate> BuildWalls(
            IReadOnlyList<AutoScanPlaneSample> samples,
            int minObservations,
            float minWallWidth,
            float minWallHeight,
            float normalToleranceDegrees,
            float planeDistanceTolerance,
            float maxMergeGap,
            float? floorY = null)
        {
            var intervals = new List<WallInterval>();
            float minNormalDot = Mathf.Cos(normalToleranceDegrees * Mathf.Deg2Rad);

            if (samples != null)
            {
                foreach (var sample in samples)
                {
                    if (sample.kind != AutoScanPlaneKind.Vertical ||
                        !IsFinite(sample) ||
                        sample.observations < minObservations ||
                        sample.width < minWallWidth ||
                        sample.maxY - sample.minY < minWallHeight)
                        continue;

                    var normal = Horizontal(sample.normalLocal);
                    if (normal.sqrMagnitude < 1e-5f) continue;
                    normal.Normalize();
                    Canonicalize(ref normal);
                    var tangent = Vector3.Cross(Vector3.up, normal).normalized;

                    float centerAlong = Vector3.Dot(sample.centerLocal, tangent);
                    float halfWidth = sample.width * 0.5f;
                    var incoming = new WallInterval
                    {
                        normal = normal,
                        tangent = tangent,
                        planeOffset = Vector3.Dot(sample.centerLocal, normal),
                        minAlong = centerAlong - halfWidth,
                        maxAlong = centerAlong + halfWidth,
                        minY = sample.minY,
                        maxY = sample.maxY,
                        weightedConfidence = Confidence(sample) * Mathf.Max(0.01f, sample.area),
                        weight = Mathf.Max(0.01f, sample.area),
                    };

                    MergeOrAdd(intervals, incoming, minNormalDot,
                               planeDistanceTolerance, maxMergeGap);
                }
            }

            // Una segunda pasada une cadenas que quedaron separadas por el orden de
            // entrada (A une B y luego B une C). La cantidad de planos AR es pequeña.
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < intervals.Count && !changed; i++)
                {
                    for (int j = i + 1; j < intervals.Count; j++)
                    {
                        if (!Compatible(intervals[i], intervals[j], minNormalDot,
                                        planeDistanceTolerance, maxMergeGap))
                            continue;
                        Absorb(intervals[i], intervals[j]);
                        intervals.RemoveAt(j);
                        changed = true;
                        break;
                    }
                }
            } while (changed);

            var result = new List<AutoScanWallCandidate>(intervals.Count);
            foreach (var interval in intervals)
            {
                float baseY = interval.minY;
                if (floorY.HasValue && Mathf.Abs(interval.minY - floorY.Value) <= 0.45f)
                    baseY = floorY.Value;
                float h = interval.maxY - baseY;
                if (h < minWallHeight) continue;

                var planePoint = interval.normal * interval.planeOffset;
                var a = planePoint + interval.tangent * interval.minAlong;
                var b = planePoint + interval.tangent * interval.maxAlong;
                a.y = b.y = baseY;
                result.Add(new AutoScanWallCandidate
                {
                    aLocal = a,
                    bLocal = b,
                    normalLocal = interval.normal,
                    height = h,
                    confidence = Mathf.Clamp01(interval.weightedConfidence / interval.weight),
                });
            }
            return result;
        }

        public static bool TryFindFloor(IReadOnlyList<AutoScanPlaneSample> samples,
                                        int minObservations, float minArea,
                                        out Vector3 floorPointLocal)
        {
            floorPointLocal = default;
            bool found = false;
            float bestY = float.PositiveInfinity;
            float bestArea = 0f;
            if (samples == null) return false;

            foreach (var sample in samples)
            {
                if (sample.kind != AutoScanPlaneKind.HorizontalUp ||
                    !IsFinite(sample) ||
                    sample.observations < minObservations || sample.area < minArea)
                    continue;

                // Priorizamos el plano horizontal más bajo. Para alturas casi iguales,
                // gana el de mayor área; esto evita elegir mesas frente al piso.
                float y = sample.centerLocal.y;
                if (!found || y < bestY - 0.12f ||
                    (Mathf.Abs(y - bestY) <= 0.12f && sample.area > bestArea))
                {
                    found = true;
                    bestY = y;
                    bestArea = sample.area;
                    floorPointLocal = sample.centerLocal;
                }
            }
            return found;
        }

        public static bool IsDuplicate(WallObject wall, AutoScanWallCandidate candidate,
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
            Sort(ref e0, ref e1);
            Sort(ref c0, ref c1);
            float overlap = Mathf.Max(0f, Mathf.Min(e1, c1) - Mathf.Max(e0, c0));
            float shortest = Mathf.Min(e1 - e0, c1 - c0);
            return shortest > 1e-4f && overlap / shortest >= overlapThreshold;
        }

        public static bool IsConsistentObservation(
            AutoScanPlaneSample previous, AutoScanPlaneSample current,
            float maxCenterDrift, float maxNormalAngleDegrees,
            float maxRelativeSizeChange)
        {
            if (previous.kind != current.kind) return false;
            if (Vector3.Distance(previous.centerLocal, current.centerLocal) > maxCenterDrift)
                return false;

            float normalDot = Mathf.Abs(Vector3.Dot(
                previous.normalLocal.normalized, current.normalLocal.normalized));
            float normalAngle = Mathf.Acos(Mathf.Clamp(normalDot, -1f, 1f)) * Mathf.Rad2Deg;
            if (normalAngle > maxNormalAngleDegrees) return false;

            return RelativeChange(previous.width, current.width) <= maxRelativeSizeChange &&
                   RelativeChange(previous.height, current.height) <= maxRelativeSizeChange;
        }

        public static float PolygonAreaXZ(IReadOnlyList<Vector3> boundary)
        {
            if (boundary == null || boundary.Count < 3) return 0f;
            double twiceArea = 0d;
            for (int i = 0; i < boundary.Count; i++)
            {
                Vector3 a = boundary[i];
                Vector3 b = boundary[(i + 1) % boundary.Count];
                twiceArea += (double)a.x * b.z - (double)b.x * a.z;
            }
            return (float)(Math.Abs(twiceArea) * 0.5d);
        }

        private static float RelativeChange(float a, float b) =>
            Mathf.Abs(a - b) / Mathf.Max(0.05f, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)));

        private static bool IsFinite(AutoScanPlaneSample sample) =>
            IsFinite(sample.centerLocal.x) && IsFinite(sample.centerLocal.y) &&
            IsFinite(sample.centerLocal.z) && IsFinite(sample.normalLocal.x) &&
            IsFinite(sample.normalLocal.y) && IsFinite(sample.normalLocal.z) &&
            IsFinite(sample.width) && IsFinite(sample.height) && IsFinite(sample.minY) &&
            IsFinite(sample.maxY) && IsFinite(sample.area);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static float Confidence(AutoScanPlaneSample sample) =>
            Mathf.Clamp01(sample.observations / 12f) *
            Mathf.Clamp01(sample.area / 2f);

        private static void MergeOrAdd(List<WallInterval> intervals, WallInterval incoming,
                                       float minNormalDot, float planeTolerance, float maxGap)
        {
            foreach (var current in intervals)
            {
                if (!Compatible(current, incoming, minNormalDot, planeTolerance, maxGap)) continue;
                Absorb(current, incoming);
                return;
            }
            intervals.Add(incoming);
        }

        private static bool Compatible(WallInterval a, WallInterval b, float minNormalDot,
                                       float planeTolerance, float maxGap)
        {
            if (Vector3.Dot(a.normal, b.normal) < minNormalDot) return false;
            if (Mathf.Abs(a.planeOffset - b.planeOffset) > planeTolerance) return false;

            // Reproyectamos B al eje de A por si las normales difieren algunos grados.
            float centerB = Vector3.Dot(
                b.normal * b.planeOffset + b.tangent * ((b.minAlong + b.maxAlong) * 0.5f),
                a.tangent);
            float halfB = (b.maxAlong - b.minAlong) * 0.5f;
            float bMin = centerB - halfB;
            float bMax = centerB + halfB;
            return !(bMin > a.maxAlong + maxGap || bMax < a.minAlong - maxGap);
        }

        private static void Absorb(WallInterval target, WallInterval other)
        {
            float center = Vector3.Dot(
                other.normal * other.planeOffset +
                other.tangent * ((other.minAlong + other.maxAlong) * 0.5f),
                target.tangent);
            float half = (other.maxAlong - other.minAlong) * 0.5f;
            target.minAlong = Mathf.Min(target.minAlong, center - half);
            target.maxAlong = Mathf.Max(target.maxAlong, center + half);
            target.minY = Mathf.Min(target.minY, other.minY);
            target.maxY = Mathf.Max(target.maxY, other.maxY);
            float totalWeight = target.weight + other.weight;
            target.planeOffset = totalWeight > 0f
                ? (target.planeOffset * target.weight + other.planeOffset * other.weight) / totalWeight
                : target.planeOffset;
            target.weightedConfidence += other.weightedConfidence;
            target.weight = totalWeight;
        }

        private static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static void Canonicalize(ref Vector3 normal)
        {
            if (normal.x < -1e-4f || (Mathf.Abs(normal.x) <= 1e-4f && normal.z < 0f))
                normal = -normal;
        }

        private static void Sort(ref float a, ref float b)
        {
            if (a <= b) return;
            (a, b) = (b, a);
        }
    }

    // Orquesta el modo automático sin introducir un segundo modelo de escena.
    // ARPlane y las previews son temporales; FinishCapture materializa únicamente
    // WallObject/FloorPoint, que SceneRegistry guarda como si fueran manuales.
    [DefaultExecutionOrder(-35)]
    public sealed class AutoScanController : MonoBehaviour
    {
        public static AutoScanController Instance { get; private set; }

        [Header("Estabilización")]
        [SerializeField, Min(1)] private int _minObservations = 3;
        [SerializeField, Min(0.1f)] private float _minWallWidth = 0.45f;
        [SerializeField, Min(0.1f)] private float _minWallHeight = 0.65f;
        [SerializeField, Min(0.05f)] private float _minFloorArea = 0.8f;
        [SerializeField, Min(0.005f)] private float _maxStableCenterDrift = 0.08f;
        [SerializeField, Range(0.5f, 30f)] private float _maxStableNormalAngle = 6f;
        [SerializeField, Range(0.01f, 1f)] private float _maxStableSizeChange = 0.20f;

        [Header("Consolidación")]
        [SerializeField, Range(1f, 25f)] private float _normalToleranceDegrees = 10f;
        [SerializeField, Min(0.01f)] private float _planeDistanceTolerance = 0.16f;
        [SerializeField, Min(0f)] private float _maxMergeGap = 0.30f;
        [SerializeField, Min(0.02f)] private float _wallWidth = 0.15f;
        [SerializeField, Min(0.02f)] private float _joinTolerance = 0.35f;

        [Header("Exploración")]
        [SerializeField, Range(8, 72)] private int _headingBins = 24;
        [SerializeField, Min(0.01f)] private float _minCameraMove = 0.08f;
        [SerializeField, Range(1f, 45f)] private float _minCameraTurnDegrees = 7f;

        private sealed class TrackedSurface
        {
            public AutoScanPlaneSample sample;
            public bool hasSample;
            public Vector3[] boundaryLocal;
            public LineRenderer preview;
        }

        private readonly Dictionary<string, TrackedSurface> _surfaces = new();
        private readonly List<UnityEngine.Object> _lastCreated = new();
        private bool[] _visitedHeadings;
        private ARPlaneManager _planeManager;
        private PlaneDetectionMode _previousDetectionMode;
        private Camera _camera;
        private Material _previewMaterial;
        private Vector3 _lastCameraPosition;
        private Vector3 _lastCameraForward;
        private bool _hasCameraSample;
        private bool _materializableCountDirty = true;
        private int _cachedMaterializableCount;

        public bool IsCapturing { get; private set; }
        public int ObservedSurfaceCount => _surfaces.Count;
        public int StableSurfaceCount
        {
            get
            {
                int count = 0;
                foreach (var state in _surfaces.Values)
                    if (state.sample.observations >= _minObservations) count++;
                return count;
            }
        }
        public int LastCreatedCount => _lastCreated.Count;
        public int MaterializableObjectCount
        {
            get
            {
                if (!_materializableCountDirty) return _cachedMaterializableCount;
                _cachedMaterializableCount = CountMaterializableObjects();
                _materializableCountDirty = false;
                return _cachedMaterializableCount;
            }
        }
        public bool CanFinish => MaterializableObjectCount > 0;
        public float Coverage01
        {
            get
            {
                if (_visitedHeadings == null || _visitedHeadings.Length == 0) return 0f;
                int visited = 0;
                foreach (bool value in _visitedHeadings) if (value) visited++;
                return visited / (float)_visitedHeadings.Length;
            }
        }

        public static AutoScanController Ensure(GameObject host = null)
        {
            if (Instance != null) return Instance;
            if (host == null)
            {
                var existing = FindAnyObjectByType<ScannerSceneBootstrap>();
                host = existing != null ? existing.gameObject : new GameObject("AutoScan");
            }
            return host.GetComponent<AutoScanController>() ?? host.AddComponent<AutoScanController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _camera = Camera.main;
            _planeManager = FindAnyObjectByType<ARPlaneManager>();
            ResetCoverage();
        }

        private void OnEnable()
        {
            BindPlaneManager();
        }

        private void OnDisable()
        {
            if (_planeManager != null)
                _planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
            if (IsCapturing) StopCapture(ScannerMode.Idle);
        }

        private void Update()
        {
            if (!IsCapturing) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || WorldOrigin.Instance == null) return;

            var position = _camera.transform.position;
            var forward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
            if (!_hasCameraSample ||
                Vector3.Distance(position, _lastCameraPosition) >= _minCameraMove ||
                Vector3.Angle(forward, _lastCameraForward) >= _minCameraTurnDegrees)
            {
                MarkHeading(forward);
                _lastCameraPosition = position;
                _lastCameraForward = forward;
                _hasCameraSample = true;
            }
        }

        public bool StartCapture()
        {
            var fsm = ScanStateMachine.Instance;
            if (fsm == null || fsm.Current != ScannerMode.Idle || WorldOrigin.Instance == null)
                return false;

            BindPlaneManager();
            ClearTemporarySurfaces();
            ResetCoverage();
            _hasCameraSample = false;
            IsCapturing = true;

            if (_planeManager != null)
            {
                _previousDetectionMode = _planeManager.requestedDetectionMode;
                _planeManager.requestedDetectionMode =
                    PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
                foreach (var plane in _planeManager.trackables) RegisterOrUpdate(plane);
            }
            else
            {
                Debug.LogWarning("[AutoScan] No hay ARPlaneManager. En dispositivo no se recibirán superficies.");
            }

            fsm.ClearSelection();
            fsm.SetMode(ScannerMode.AutoScan_Capturing);
            Debug.Log("[AutoScan] Captura local iniciada.");
            return true;
        }

        public int FinishCapture()
        {
            if (!IsCapturing || !CanFinish)
            {
                Debug.LogWarning("[AutoScan] Todavia no hay superficies estables para materializar.");
                return 0;
            }
            int created = MaterializeCurrentSurfaces();
            StopCapture(ScannerMode.Idle);
            Debug.Log($"[AutoScan] Captura finalizada. Objetos creados: {created}.");
            return created;
        }

        public void CancelCapture()
        {
            if (!IsCapturing) return;
            StopCapture(ScannerMode.Idle);
            Debug.Log("[AutoScan] Captura cancelada; no se modificó la escena editable.");
        }

        public void UndoLastMaterialization()
        {
            foreach (var item in _lastCreated)
            {
                if (item is WallObject wall && wall != null) wall.Delete();
                else if (item is FloorPoint floor && floor != null) floor.Delete();
            }
            _lastCreated.Clear();
        }

        private void StopCapture(ScannerMode nextMode)
        {
            IsCapturing = false;
            if (_planeManager != null)
                _planeManager.requestedDetectionMode = _previousDetectionMode;
            ClearTemporarySurfaces();
            var fsm = ScanStateMachine.Instance;
            if (fsm != null && fsm.Current == ScannerMode.AutoScan_Capturing)
                fsm.SetMode(nextMode);
        }

        private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            if (!IsCapturing) return;
            foreach (var plane in args.added) RegisterOrUpdate(plane);
            foreach (var plane in args.updated) RegisterOrUpdate(plane);
            // Conservamos la última observación de los removidos: ARKit/ARCore suelen
            // remover un plano al fusionarlo con otro y no queremos perder cobertura.
        }

        private void RegisterOrUpdate(ARPlane plane)
        {
            if (plane == null || WorldOrigin.Instance == null) return;
            string id = plane.trackableId.ToString();
            _surfaces.TryGetValue(id, out var state);
            state ??= new TrackedSurface();

            var boundary = BuildBoundaryLocal(plane);
            if (boundary.Length < 3) return;

            var normalWorld = plane.transform.up;
            var normalLocal = WorldOrigin.Instance.transform.InverseTransformDirection(normalWorld).normalized;
            var kind = plane.alignment switch
            {
                PlaneAlignment.Vertical => AutoScanPlaneKind.Vertical,
                PlaneAlignment.HorizontalDown => AutoScanPlaneKind.HorizontalDown,
                _ => AutoScanPlaneKind.HorizontalUp,
            };

            ComputeBounds(kind, boundary, normalLocal, out var center, out float width,
                          out float height, out float minY, out float maxY, out float area);
            var current = new AutoScanPlaneSample
            {
                sourceId = id,
                kind = kind,
                centerLocal = center,
                normalLocal = normalLocal,
                width = width,
                height = height,
                minY = minY,
                maxY = maxY,
                observations = 1,
                area = area,
            };
            if (state.hasSample && AutoScanModel.IsConsistentObservation(
                    state.sample, current, _maxStableCenterDrift,
                    _maxStableNormalAngle, _maxStableSizeChange))
                current.observations = state.sample.observations + 1;
            state.sample = current;
            state.hasSample = true;
            state.boundaryLocal = boundary;
            _surfaces[id] = state;
            _materializableCountDirty = true;
            UpdatePreview(state);
        }

        private static Vector3[] BuildBoundaryLocal(ARPlane plane)
        {
            var wo = WorldOrigin.Instance;
            var boundary = plane.boundary;
            if (boundary.IsCreated && boundary.Length >= 3)
            {
                var result = new Vector3[boundary.Length];
                for (int i = 0; i < boundary.Length; i++)
                {
                    var point = boundary[i];
                    result[i] = wo.ToRelative(plane.transform.TransformPoint(new Vector3(point.x, 0f, point.y)));
                }
                return result;
            }

            var c = plane.center;
            var h = plane.size * 0.5f;
            var fallback = new[]
            {
                new Vector3(c.x - h.x, 0f, c.y - h.y),
                new Vector3(c.x + h.x, 0f, c.y - h.y),
                new Vector3(c.x + h.x, 0f, c.y + h.y),
                new Vector3(c.x - h.x, 0f, c.y + h.y),
            };
            for (int i = 0; i < fallback.Length; i++)
                fallback[i] = wo.ToRelative(plane.transform.TransformPoint(fallback[i]));
            return fallback;
        }

        private static void ComputeBounds(AutoScanPlaneKind kind, Vector3[] boundary,
                                          Vector3 normal, out Vector3 center, out float width,
                                          out float height, out float minY, out float maxY,
                                          out float area)
        {
            center = Vector3.zero;
            minY = float.PositiveInfinity;
            maxY = float.NegativeInfinity;
            foreach (var p in boundary)
            {
                center += p;
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            center /= boundary.Length;

            if (kind == AutoScanPlaneKind.Vertical)
            {
                var horizontalNormal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
                var tangent = Vector3.Cross(Vector3.up, horizontalNormal).normalized;
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                foreach (var p in boundary)
                {
                    float d = Vector3.Dot(p, tangent);
                    min = Mathf.Min(min, d);
                    max = Mathf.Max(max, d);
                }
                width = Mathf.Max(0f, max - min);
                height = Mathf.Max(0f, maxY - minY);
                area = width * height;
            }
            else
            {
                float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
                float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
                foreach (var p in boundary)
                {
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                }
                width = Mathf.Max(0f, maxX - minX);
                height = Mathf.Max(0f, maxZ - minZ);
                area = AutoScanModel.PolygonAreaXZ(boundary);
            }
        }

        private int MaterializeCurrentSurfaces()
        {
            var registry = SceneRegistry.Instance;
            if (registry == null || WorldOrigin.Instance == null) return 0;
            _lastCreated.Clear();

            var samples = new List<AutoScanPlaneSample>(_surfaces.Count);
            foreach (var surface in _surfaces.Values) samples.Add(surface.sample);

            float? floorY = FloorPoint.Instance != null ? FloorPoint.Instance.LocalY : null;
            if (!floorY.HasValue &&
                AutoScanModel.TryFindFloor(samples, _minObservations, _minFloorArea,
                                           out var floorPoint))
            {
                floorY = floorPoint.y;
                _lastCreated.Add(FloorPoint.Create(floorPoint));
            }

            var walls = AutoScanModel.BuildWalls(
                samples, _minObservations, _minWallWidth, _minWallHeight,
                _normalToleranceDegrees, _planeDistanceTolerance, _maxMergeGap, floorY);
            var groups = BuildConnectedGroups(walls, _joinTolerance);

            for (int i = 0; i < walls.Count; i++)
            {
                var candidate = walls[i];
                bool duplicate = false;
                foreach (var existing in registry.Walls)
                {
                    if (!AutoScanModel.IsDuplicate(existing, candidate)) continue;
                    duplicate = true;
                    break;
                }
                if (duplicate) continue;

                var baseDirection = (candidate.bLocal - candidate.aLocal).normalized;
                var baseNormal = Vector3.Cross(Vector3.up, baseDirection).normalized;
                int side = Vector3.Dot(baseNormal, candidate.normalLocal) >= 0f ? 1 : -1;
                var wall = WallObject.Create(candidate.aLocal, candidate.bLocal,
                                             candidate.height, _wallWidth, side);
                wall.PolylineId = groups[i];
                _lastCreated.Add(wall);
            }
            return _lastCreated.Count;
        }

        private int CountMaterializableObjects()
        {
            var registry = SceneRegistry.Instance;
            if (!IsCapturing || registry == null || WorldOrigin.Instance == null) return 0;

            var samples = new List<AutoScanPlaneSample>(_surfaces.Count);
            foreach (var surface in _surfaces.Values) samples.Add(surface.sample);

            int count = 0;
            float? floorY = FloorPoint.Instance != null ? FloorPoint.Instance.LocalY : null;
            if (!floorY.HasValue && AutoScanModel.TryFindFloor(
                    samples, _minObservations, _minFloorArea, out var floorPoint))
            {
                floorY = floorPoint.y;
                count++;
            }

            var walls = AutoScanModel.BuildWalls(
                samples, _minObservations, _minWallWidth, _minWallHeight,
                _normalToleranceDegrees, _planeDistanceTolerance, _maxMergeGap, floorY);
            foreach (var candidate in walls)
            {
                bool duplicate = false;
                foreach (var existing in registry.Walls)
                {
                    if (!AutoScanModel.IsDuplicate(existing, candidate)) continue;
                    duplicate = true;
                    break;
                }
                if (!duplicate) count++;
            }
            return count;
        }

        private void BindPlaneManager()
        {
            var manager = FindAnyObjectByType<ARPlaneManager>();
            if (_planeManager != null)
                _planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
            _planeManager = manager;
            if (_planeManager != null && isActiveAndEnabled)
                _planeManager.trackablesChanged.AddListener(OnPlanesChanged);
        }

        private static string[] BuildConnectedGroups(IReadOnlyList<AutoScanWallCandidate> walls,
                                                      float tolerance)
        {
            int count = walls.Count;
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;

            int Find(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }
            void Union(int a, int b)
            {
                a = Find(a); b = Find(b);
                if (a != b) parent[b] = a;
            }

            float toleranceSq = tolerance * tolerance;
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    if ((walls[i].aLocal - walls[j].aLocal).sqrMagnitude <= toleranceSq ||
                        (walls[i].aLocal - walls[j].bLocal).sqrMagnitude <= toleranceSq ||
                        (walls[i].bLocal - walls[j].aLocal).sqrMagnitude <= toleranceSq ||
                        (walls[i].bLocal - walls[j].bLocal).sqrMagnitude <= toleranceSq)
                        Union(i, j);

            var ids = new Dictionary<int, string>();
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                int root = Find(i);
                if (!ids.TryGetValue(root, out string id))
                {
                    id = Guid.NewGuid().ToString("N").Substring(0, 8);
                    ids[root] = id;
                }
                result[i] = id;
            }
            return result;
        }

        private void UpdatePreview(TrackedSurface state)
        {
            if (WorldOrigin.Instance == null || state.boundaryLocal == null) return;
            if (state.preview == null)
            {
                var go = new GameObject($"AutoScanPreview_{state.sample.sourceId}");
                go.transform.SetParent(WorldOrigin.Instance.transform, false);
                state.preview = go.AddComponent<LineRenderer>();
                state.preview.useWorldSpace = false;
                state.preview.loop = true;
                state.preview.widthMultiplier = 0.018f;
                state.preview.numCapVertices = 2;
                state.preview.sharedMaterial = GetPreviewMaterial();
            }
            state.preview.positionCount = state.boundaryLocal.Length;
            state.preview.SetPositions(state.boundaryLocal);
            bool stable = state.sample.observations >= _minObservations;
            var color = stable ? new Color(0.25f, 1f, 0.45f, 0.9f)
                               : new Color(1f, 0.65f, 0.2f, 0.65f);
            state.preview.startColor = state.preview.endColor = color;
        }

        private Material GetPreviewMaterial()
        {
            if (_previewMaterial != null) return _previewMaterial;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _previewMaterial = new Material(shader) { name = "AutoScanPreviewMat (runtime)" };
            return _previewMaterial;
        }

        private void MarkHeading(Vector3 worldForward)
        {
            if (worldForward.sqrMagnitude < 1e-5f || _visitedHeadings == null) return;
            var localForward = WorldOrigin.Instance.transform.InverseTransformDirection(worldForward);
            float angle = Mathf.Atan2(localForward.x, localForward.z) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            int index = Mathf.Clamp(Mathf.FloorToInt(angle / 360f * _visitedHeadings.Length),
                                    0, _visitedHeadings.Length - 1);
            _visitedHeadings[index] = true;
        }

        private void ResetCoverage() => _visitedHeadings = new bool[Mathf.Max(8, _headingBins)];

        private void ClearTemporarySurfaces()
        {
            foreach (var state in _surfaces.Values)
                if (state?.preview != null) Destroy(state.preview.gameObject);
            _surfaces.Clear();
            _cachedMaterializableCount = 0;
            _materializableCountDirty = true;
        }

#if UNITY_EDITOR
        // Punto de entrada para escenarios y pruebas del Editor sin subsistemas AR.
        public void AddSyntheticSample(AutoScanPlaneSample sample, Vector3[] boundaryLocal = null)
        {
            string id = string.IsNullOrEmpty(sample.sourceId)
                ? Guid.NewGuid().ToString("N") : sample.sourceId;
            sample.sourceId = id;
            _surfaces[id] = new TrackedSurface
            {
                sample = sample,
                hasSample = true,
                boundaryLocal = boundaryLocal ?? BuildSyntheticBoundary(sample),
            };
            _materializableCountDirty = true;
            if (IsCapturing) UpdatePreview(_surfaces[id]);
        }

        public void AddSyntheticRoomForEditor()
        {
            if (!IsCapturing) return;
            const float roomWidth = 4f, roomDepth = 3f, roomHeight = 2.5f;
            AddSyntheticSample(Synthetic("demo-floor", AutoScanPlaneKind.HorizontalUp,
                new Vector3(0f, 0f, roomDepth * 0.5f), Vector3.up,
                roomWidth, roomDepth, 0f, 0f));
            AddSyntheticSample(Synthetic("demo-wall-n", AutoScanPlaneKind.Vertical,
                new Vector3(0f, roomHeight * 0.5f, 0f), Vector3.forward,
                roomWidth, roomHeight, 0f, roomHeight));
            AddSyntheticSample(Synthetic("demo-wall-s", AutoScanPlaneKind.Vertical,
                new Vector3(0f, roomHeight * 0.5f, roomDepth), Vector3.back,
                roomWidth, roomHeight, 0f, roomHeight));
            AddSyntheticSample(Synthetic("demo-wall-w", AutoScanPlaneKind.Vertical,
                new Vector3(-roomWidth * 0.5f, roomHeight * 0.5f, roomDepth * 0.5f), Vector3.right,
                roomDepth, roomHeight, 0f, roomHeight));
            AddSyntheticSample(Synthetic("demo-wall-e", AutoScanPlaneKind.Vertical,
                new Vector3(roomWidth * 0.5f, roomHeight * 0.5f, roomDepth * 0.5f), Vector3.left,
                roomDepth, roomHeight, 0f, roomHeight));
        }

        private AutoScanPlaneSample Synthetic(string id, AutoScanPlaneKind kind,
                                              Vector3 center, Vector3 normal,
                                              float width, float height,
                                              float minY, float maxY) =>
            new AutoScanPlaneSample
            {
                sourceId = id,
                kind = kind,
                centerLocal = center,
                normalLocal = normal,
                width = width,
                height = height,
                minY = minY,
                maxY = maxY,
                observations = _minObservations + 2,
                area = width * Mathf.Max(0.01f, height),
            };

        private static Vector3[] BuildSyntheticBoundary(AutoScanPlaneSample sample)
        {
            if (sample.kind == AutoScanPlaneKind.Vertical)
            {
                var n = Vector3.ProjectOnPlane(sample.normalLocal, Vector3.up).normalized;
                var t = Vector3.Cross(Vector3.up, n).normalized;
                var bottom = sample.centerLocal; bottom.y = sample.minY;
                var top = sample.centerLocal; top.y = sample.maxY;
                return new[]
                {
                    bottom - t * sample.width * 0.5f,
                    bottom + t * sample.width * 0.5f,
                    top + t * sample.width * 0.5f,
                    top - t * sample.width * 0.5f,
                };
            }
            float hx = sample.width * 0.5f, hz = sample.height * 0.5f;
            return new[]
            {
                sample.centerLocal + new Vector3(-hx, 0f, -hz),
                sample.centerLocal + new Vector3(hx, 0f, -hz),
                sample.centerLocal + new Vector3(hx, 0f, hz),
                sample.centerLocal + new Vector3(-hx, 0f, hz),
            };
        }
#endif

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ClearTemporarySurfaces();
            if (_previewMaterial != null) Destroy(_previewMaterial);
        }
    }
}
