using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Scanner.ScanV2
{
    [DefaultExecutionOrder(-34)]
    public sealed class ScanV2Controller : MonoBehaviour
    {
        public static ScanV2Controller Instance { get; private set; }

        [Header("Keyframes")]
        [SerializeField, Min(0.05f)] private float _minimumMove = 0.18f;
        [SerializeField, Range(2f, 45f)] private float _minimumTurn = 10f;
        [SerializeField, Min(0.05f)] private float _minimumInterval = 0.35f;

        [Header("Profundidad")]
        [SerializeField, Range(2, 24)] private int _depthSamplingStride = 6;
        [SerializeField, Range(3, 20)] private int _raycastColumns = 10;
        [SerializeField, Range(3, 20)] private int _raycastRows = 8;
        [SerializeField, Min(0.05f)] private float _minimumDepth = 0.25f;
        [SerializeField, Min(0.5f)] private float _maximumDepth = 7f;

        [Header("Fusion y estructura")]
        [SerializeField, Range(0.02f, 0.25f)] private float _voxelSize = 0.08f;
        [SerializeField, Range(10000, 500000)] private int _maximumVoxels = 150000;
        [SerializeField, Range(1, 8)] private int _minimumVoxelObservations = 2;
        [SerializeField, Range(4, 100)] private int _minimumPlanePoints = 12;
        [SerializeField, Range(2f, 25f)] private float _normalTolerance = 12f;
        [SerializeField, Range(0.03f, 0.4f)] private float _planeTolerance = 0.14f;
        [SerializeField, Min(0.1f)] private float _minimumWallLength = 0.55f;
        [SerializeField, Min(0.1f)] private float _minimumWallHeight = 0.65f;
        [SerializeField, Min(0.02f)] private float _wallWidth = 0.15f;

        private readonly List<ScanV2Observation> _frame = new();
        private readonly List<UnityEngine.Object> _lastCreated = new();
        private SparseSurfelVolume _volume;
        private IScanV2DepthSource _nativeDepth;
        private IScanV2DepthSource _raycastDepth;
        private Camera _camera;
        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private float _lastAttemptTime;
        private bool _hasKeyframe;
        private ScanV2StructuralResult _cachedStructure;
        private bool _structureDirty = true;
        private List<ScanV2Surfel> _cachedSurfels = new();
        private bool _surfelsDirty = true;
        private EnvironmentDepthMode _previousDepthMode;
        private AROcclusionManager _occlusion;

        public bool IsCapturing { get; private set; }
        public int KeyframeCount { get; private set; }
        public int RawObservationCount { get; private set; }
        public int FusedVoxelCount => _volume?.Count ?? 0;
        public bool VolumeIsFull => _volume?.IsFull ?? false;
        public int StableSurfelCount => GetStableSurfels().Count;
        public string LastSource { get; private set; } = "ninguna";
        public int MaterializableObjectCount => CountMaterializableObjects();
        public bool CanFinish => IsCapturing && MaterializableObjectCount > 0;
        public int LastCreatedCount => _lastCreated.Count;

        public static ScanV2Controller Ensure(GameObject host = null)
        {
            if (Instance != null) return Instance;
            if (host == null)
            {
                var bootstrap = FindAnyObjectByType<ScannerSceneBootstrap>();
                host = bootstrap != null ? bootstrap.gameObject : new GameObject("ScanV2");
            }
            return host.GetComponent<ScanV2Controller>() ?? host.AddComponent<ScanV2Controller>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _camera = Camera.main;
            BuildSources();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDisable()
        {
            if (IsCapturing) StopCapture(ScannerMode.Idle);
        }

        private void Update()
        {
            if (!IsCapturing) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || WorldOrigin.Instance == null) return;
            if (!ShouldCaptureKeyframe()) return;
            CaptureKeyframe();
        }

        public bool StartCapture()
        {
            var fsm = ScanStateMachine.Instance;
            if (fsm == null || fsm.Current != ScannerMode.Idle || WorldOrigin.Instance == null)
                return false;

            BuildSources();
            _volume = new SparseSurfelVolume(_voxelSize, _maximumVoxels);
            _cachedStructure = null;
            _structureDirty = true;
            _cachedSurfels.Clear();
            _surfelsDirty = true;
            _hasKeyframe = false;
            KeyframeCount = 0;
            RawObservationCount = 0;
            LastSource = "ninguna";
            _lastAttemptTime = Time.unscaledTime - _minimumInterval;
            IsCapturing = true;

            if (_occlusion != null)
            {
                _previousDepthMode = _occlusion.requestedEnvironmentDepthMode;
                _occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
            }
            fsm.ClearSelection();
            fsm.SetMode(ScannerMode.ScanV2_Capturing);
            Debug.Log("[ScanV2] Captura multivista local iniciada.");
            return true;
        }

        public int FinishCapture()
        {
            if (!CanFinish)
            {
                Debug.LogWarning("[ScanV2] Todavia no hay estructura confiable para materializar.");
                return 0;
            }
            int created = Materialize();
            StopCapture(ScannerMode.Idle);
            Debug.Log($"[ScanV2] Finalizado. Objetos editables creados: {created}.");
            return created;
        }

        public void CancelCapture()
        {
            if (!IsCapturing) return;
            StopCapture(ScannerMode.Idle);
            Debug.Log("[ScanV2] Captura cancelada sin modificar la escena.");
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

        private void BuildSources()
        {
            _occlusion = FindAnyObjectByType<AROcclusionManager>();
            var cameraManager = FindAnyObjectByType<ARCameraManager>();
            var raycasts = FindAnyObjectByType<ARRaycastManager>();
            _nativeDepth = new NativeEnvironmentDepthSource(
                _occlusion, cameraManager, _depthSamplingStride, _minimumDepth, _maximumDepth);
            _raycastDepth = new ARRaycastDepthSource(raycasts, _raycastColumns, _raycastRows);
        }

        private bool ShouldCaptureKeyframe()
        {
            if (Time.unscaledTime - _lastAttemptTime < _minimumInterval) return false;
            if (!_hasKeyframe) return true;
            return Vector3.Distance(_camera.transform.position, _lastPosition) >= _minimumMove ||
                   Quaternion.Angle(_camera.transform.rotation, _lastRotation) >= _minimumTurn;
        }

        private void CaptureKeyframe()
        {
            _lastAttemptTime = Time.unscaledTime;
            _frame.Clear();
            bool nativeCaptured = _nativeDepth != null && _nativeDepth.TryCapture(_camera, _frame);
            bool raycastCaptured = _raycastDepth != null && _raycastDepth.TryCapture(_camera, _frame);
            bool captured = nativeCaptured || raycastCaptured;
            LastSource = nativeCaptured && raycastCaptured ? "nativa + raycasts" :
                         nativeCaptured ? _nativeDepth.Name :
                         raycastCaptured ? _raycastDepth.Name : "ninguna";
            if (!captured || _frame.Count == 0) return;

            _volume.Integrate(_frame);
            RawObservationCount += _frame.Count;
            KeyframeCount++;
            _lastPosition = _camera.transform.position;
            _lastRotation = _camera.transform.rotation;
            _hasKeyframe = true;
            _structureDirty = true;
            _surfelsDirty = true;
        }

        private void StopCapture(ScannerMode next)
        {
            IsCapturing = false;
            if (_occlusion != null)
                _occlusion.requestedEnvironmentDepthMode = _previousDepthMode;
            var fsm = ScanStateMachine.Instance;
            if (fsm != null && fsm.Current == ScannerMode.ScanV2_Capturing)
                fsm.SetMode(next);
        }

        private List<ScanV2Surfel> GetStableSurfels()
        {
            if (!_surfelsDirty) return _cachedSurfels;
            _cachedSurfels = _volume?.Extract(_minimumVoxelObservations) ?? new List<ScanV2Surfel>();
            _surfelsDirty = false;
            return _cachedSurfels;
        }

        private ScanV2StructuralResult GetStructure()
        {
            if (!_structureDirty && _cachedStructure != null) return _cachedStructure;
            _cachedStructure = ScanV2Geometry.ExtractStructure(
                GetStableSurfels(), _minimumPlanePoints, _minimumPlanePoints,
                _normalTolerance, _planeTolerance, _minimumWallLength, _minimumWallHeight);
            _structureDirty = false;
            return _cachedStructure;
        }

        private int CountMaterializableObjects()
        {
            if (!IsCapturing || SceneRegistry.Instance == null) return 0;
            var structure = GetStructure();
            int count = structure.hasFloor && FloorPoint.Instance == null ? 1 : 0;
            foreach (var candidate in structure.walls)
                if (!IsDuplicate(candidate)) count++;
            return count;
        }

        private int Materialize()
        {
            var structure = GetStructure();
            _lastCreated.Clear();
            float? floorY = FloorPoint.Instance != null ? FloorPoint.Instance.LocalY : null;
            if (!floorY.HasValue && structure.hasFloor)
            {
                var floor = FloorPoint.Create(new Vector3(0f, structure.floorY, 0f));
                _lastCreated.Add(floor);
                floorY = structure.floorY;
            }

            string group = Guid.NewGuid().ToString("N");
            foreach (var source in structure.walls)
            {
                var candidate = source;
                if (floorY.HasValue && Mathf.Abs(candidate.aLocal.y - floorY.Value) <= 0.40f)
                {
                    float top = candidate.aLocal.y + candidate.height;
                    candidate.aLocal.y = candidate.bLocal.y = floorY.Value;
                    candidate.height = Mathf.Max(_minimumWallHeight, top - floorY.Value);
                }
                if (IsDuplicate(candidate)) continue;
                var direction = (candidate.bLocal - candidate.aLocal).normalized;
                var baseNormal = Vector3.Cross(Vector3.up, direction).normalized;
                int side = Vector3.Dot(baseNormal, candidate.normalLocal) >= 0f ? 1 : -1;
                var wall = WallObject.Create(candidate.aLocal, candidate.bLocal,
                                             candidate.height, _wallWidth, side);
                wall.PolylineId = group;
                _lastCreated.Add(wall);
            }
            return _lastCreated.Count;
        }

        private static bool IsDuplicate(ScanV2WallCandidate candidate)
        {
            var registry = SceneRegistry.Instance;
            if (registry == null) return false;
            foreach (var wall in registry.Walls)
                if (ScanV2Geometry.IsDuplicate(wall, candidate)) return true;
            return false;
        }

#if UNITY_EDITOR
        public void AddSyntheticRoomForEditor(float width = 4f, float depth = 3f,
                                              float height = 2.5f)
        {
            if (!IsCapturing || _volume == null) return;
            const float step = 0.10f;
            for (int pass = 0; pass < Mathf.Max(2, _minimumVoxelObservations); pass++)
            {
                var samples = new List<ScanV2Observation>();
                AddPlane(samples, new Vector3(-width / 2f, 0f, 0f), Vector3.up,
                         width, depth, step);
                AddWall(samples, new Vector3(-width / 2f, 0f, 0f), Vector3.forward,
                        width, height, step);
                AddWall(samples, new Vector3(width / 2f, 0f, depth), Vector3.back,
                        width, height, step);
                AddWall(samples, new Vector3(-width / 2f, 0f, depth), Vector3.right,
                        depth, height, step);
                AddWall(samples, new Vector3(width / 2f, 0f, 0f), Vector3.left,
                        depth, height, step);
                _volume.Integrate(samples);
                RawObservationCount += samples.Count;
            }
            KeyframeCount += Mathf.Max(2, _minimumVoxelObservations);
            LastSource = "habitacion sintetica";
            _structureDirty = true;
            _surfelsDirty = true;
        }

        private static void AddPlane(List<ScanV2Observation> output, Vector3 origin,
                                     Vector3 normal, float width, float depth,
                                     float step)
        {
            for (float x = 0f; x <= width; x += step)
            for (float z = 0f; z <= depth; z += step)
                output.Add(new ScanV2Observation(origin + new Vector3(x, 0f, z), normal));
        }

        private static void AddWall(List<ScanV2Observation> output, Vector3 origin,
                                    Vector3 normal, float length, float height, float step)
        {
            var tangent = Vector3.Cross(Vector3.up, normal).normalized;
            for (float along = 0f; along <= length; along += step)
            for (float y = 0f; y <= height; y += step)
                output.Add(new ScanV2Observation(origin + tangent * along + Vector3.up * y, normal));
        }
#endif
    }
}
