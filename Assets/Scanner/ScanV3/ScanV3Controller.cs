using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Scanner.ScanV3
{
    public enum ScanV3ProcessingState { Idle, Capturing, Optimizing, Reintegrating, Ready, Failed }

    [DefaultExecutionOrder(-33)]
    public sealed class ScanV3Controller : MonoBehaviour
    {
        public static ScanV3Controller Instance { get; private set; }

        [Header("Captura activa")]
        [SerializeField, Min(0.05f)] private float _minimumMove = 0.16f;
        [SerializeField, Range(2f, 45f)] private float _minimumTurn = 9f;
        [SerializeField, Min(0.1f)] private float _minimumInterval = 0.45f;
        [SerializeField, Range(48, 256)] private int _analysisWidth = 96;
        [SerializeField, Range(25, 95)] private int _jpegQuality = 65;
        [SerializeField, Range(20, 500)] private int _maximumKeyframes = 180;

        [Header("Reconstruccion")]
        [SerializeField, Range(0.02f, 0.20f)] private float _voxelSize = 0.06f;
        [SerializeField, Range(10000, 500000)] private int _maximumVoxels = 220000;
        [SerializeField, Range(1, 6)] private int _minimumViews = 2;
        [SerializeField, Range(4, 100)] private int _minimumPlanePoints = 14;
        [SerializeField, Min(0.02f)] private float _wallWidth = 0.15f;

        private readonly List<ScanV3Keyframe> _frames = new();
        private readonly List<ScanV3PoseEdge> _edges = new();
        private readonly List<ScanV3Observation> _worldObservations = new();
        private readonly List<UnityEngine.Object> _lastCreated = new();
        private Camera _camera;
        private ARCameraManager _cameraManager;
        private AROcclusionManager _occlusion;
        private IScanV3DepthSource _nativeDepth;
        private IScanV3DepthSource _raycastDepth;
        private ScanV3BundleStore _bundle;
        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private float _lastAttempt;
        private bool _hasKeyframe;
        private ScanV3StructuralResult _structure;
        private EnvironmentDepthMode _previousDepthMode;
#if UNITY_EDITOR
        public bool ForceMaterializationFailureForEditor { get; set; }
#endif

        public bool IsCapturing { get; private set; }
        public ScanV3ProcessingState State { get; private set; }
        public int AcceptedKeyframes => _frames.Count;
        public int RejectedKeyframes { get; private set; }
        public int LoopClosureCount { get; private set; }
        public int RawObservationCount { get; private set; }
        public int FusedVoxelCount { get; private set; }
        public int ProposedObjectCount => (_structure?.walls.Count ?? 0) +
                                          (_structure?.hasFloor == true ? 1 : 0);
        public string LastGuidance { get; private set; } = "inicia el recorrido";
        public string CapturePath => _bundle?.RootPath;
        public float InitialGraphResidual { get; private set; }
        public float FinalGraphResidual { get; private set; }
        public bool CanFinish => IsCapturing && AcceptedKeyframes >= 2 && RawObservationCount > 0;

        public static ScanV3Controller Ensure(GameObject host = null)
        {
            if (Instance != null) return Instance;
            if (host == null)
            {
                var bootstrap = FindAnyObjectByType<ScannerSceneBootstrap>();
                host = bootstrap != null ? bootstrap.gameObject : new GameObject("ScanV3-Atlas");
            }
            return host.GetComponent<ScanV3Controller>() ?? host.AddComponent<ScanV3Controller>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            BindSources();
        }

        private void OnDestroy()
        {
            // Un cierre inesperado conserva el bundle para recuperacion futura.
            RestoreDepthMode();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!IsCapturing || State != ScanV3ProcessingState.Capturing) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || WorldOrigin.Instance == null ||
                _frames.Count >= _maximumKeyframes) return;
            if (Time.unscaledTime - _lastAttempt < _minimumInterval || !HasNewView()) return;
            _lastAttempt = Time.unscaledTime;
            TryCaptureKeyframe();
        }

        public bool StartCapture()
        {
            var fsm = ScanStateMachine.Instance;
            if (fsm == null || fsm.Current != ScannerMode.Idle || WorldOrigin.Instance == null)
                return false;
            _frames.Clear();
            _edges.Clear();
            try
            {
                BindSources();
                if (ScanV3BundleStore.TryOpenLatestIncomplete(out var recovered))
                {
                    _bundle = recovered;
                    RestoreRecoveredFrames(recovered.Manifest);
                }
                else _bundle = new ScanV3BundleStore();
            }
            catch (Exception exception)
            {
                State = ScanV3ProcessingState.Failed;
                LastGuidance = "sin almacenamiento para iniciar";
                Debug.LogError($"[ScanV3] No se pudo crear bundle local: {exception.Message}");
                return false;
            }

            bool recovering = _frames.Count > 0;
            if (!recovering)
            {
                _frames.Clear();
                _edges.Clear();
                _hasKeyframe = false;
                RejectedKeyframes = 0;
                LoopClosureCount = 0;
                RawObservationCount = 0;
            }
            _structure = null;
            FusedVoxelCount = 0;
            InitialGraphResidual = FinalGraphResidual = 0f;
            LastGuidance = recovering ? "captura recuperada; continua el recorrido" :
                                       "recorre lentamente y vuelve al punto inicial";
            IsCapturing = true;
            State = ScanV3ProcessingState.Capturing;
            _lastAttempt = Time.unscaledTime - _minimumInterval;
            if (_occlusion != null)
            {
                _previousDepthMode = _occlusion.requestedEnvironmentDepthMode;
                _occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
            }
            fsm.ClearSelection();
            fsm.SetMode(ScannerMode.ScanV3_Capturing);
            Debug.Log($"[ScanV3] Atlas iniciado. Bundle local: {_bundle.RootPath}");
            return true;
        }

        private void RestoreRecoveredFrames(ScanV3BundleManifest manifest)
        {
            _frames.Clear();
            _edges.Clear();
            RawObservationCount = 0;
            LoopClosureCount = 0;
            if (manifest?.keyframes == null) return;
            foreach (var frame in manifest.keyframes)
            {
                frame.observations ??= new List<ScanV3CameraObservation>();
                _frames.Add(frame);
                RawObservationCount += frame.observations.Count;
                if (_frames.Count > 1)
                {
                    var previous = _frames[^2];
                    _edges.Add(new ScanV3PoseEdge
                    {
                        from = previous.id, to = frame.id,
                        expectedWorldDelta = frame.initialPositionLocal - previous.initialPositionLocal,
                        expectedYawDelta = Mathf.DeltaAngle(previous.initialRotationLocal.eulerAngles.y,
                                                           frame.initialRotationLocal.eulerAngles.y),
                        weight = 1f, kind = ScanV3EdgeKind.Odometry,
                    });
                }
                if (ScanV3PoseGraph.TryCreateLoopEdge(_frames, _frames.Count - 1, out var loop))
                { _edges.Add(loop); LoopClosureCount++; }
            }
            _hasKeyframe = _frames.Count > 0;
            if (_hasKeyframe)
            {
                var last = _frames[^1];
                _lastPosition = WorldOrigin.Instance != null
                    ? WorldOrigin.Instance.ToWorld(last.initialPositionLocal) : last.initialPositionLocal;
                _lastRotation = WorldOrigin.Instance != null
                    ? WorldOrigin.Instance.transform.rotation * last.initialRotationLocal : last.initialRotationLocal;
            }
        }

        public int FinishCapture()
        {
            if (!CanFinish)
            {
                LastGuidance = "faltan al menos dos vistas con geometria";
                return 0;
            }
            try
            {
                State = ScanV3ProcessingState.Optimizing;
                OptimizeGraph();
                State = ScanV3ProcessingState.Reintegrating;
                Reintegrate();
                if (_structure == null || ProposedObjectCount == 0)
                {
                    State = ScanV3ProcessingState.Capturing;
                    LastGuidance = "no hay estructura suficiente; continua recorriendo";
                    return 0;
                }
                State = ScanV3ProcessingState.Ready;
                int created = MaterializeTransactional();
                if (created == 0)
                {
                    State = ScanV3ProcessingState.Capturing;
                    LastGuidance = "todo lo detectado ya existia o fue rechazado";
                    return 0;
                }
                _bundle.Complete();
                _bundle.Delete();
                _bundle = null;
                IsCapturing = false;
                RestoreDepthMode();
                ScanStateMachine.Instance?.SetMode(ScannerMode.Idle);
                State = ScanV3ProcessingState.Idle;
                Debug.Log($"[ScanV3] Atlas materializo {created} objetos con rollback habilitado.");
                return created;
            }
            catch (Exception exception)
            {
                RollbackCreated();
                State = IsCapturing ? ScanV3ProcessingState.Capturing : ScanV3ProcessingState.Failed;
                LastGuidance = "fallo el procesamiento; evidencia conservada para reintentar";
                Debug.LogError($"[ScanV3] Rollback por error: {exception.Message}\n{exception.StackTrace}");
                return 0;
            }
        }

        public void CancelCapture()
        {
            if (!IsCapturing && _bundle == null) return;
            IsCapturing = false;
            RestoreDepthMode();
            try { _bundle?.Delete(); }
            catch (Exception exception) { Debug.LogWarning($"[ScanV3] No se pudo borrar bundle: {exception.Message}"); }
            _bundle = null;
            _frames.Clear();
            _edges.Clear();
            _structure = null;
            State = ScanV3ProcessingState.Idle;
            if (ScanStateMachine.Instance?.Current == ScannerMode.ScanV3_Capturing)
                ScanStateMachine.Instance.SetMode(ScannerMode.Idle);
        }

        public void UndoLastMaterialization() => RollbackCreated();

        private void BindSources()
        {
            _camera = Camera.main;
            _cameraManager = FindAnyObjectByType<ARCameraManager>();
            _occlusion = FindAnyObjectByType<AROcclusionManager>();
            var raycasts = FindAnyObjectByType<ARRaycastManager>();
            _nativeDepth = new NativeEnvironmentDepthSource(_occlusion, _cameraManager, 7, 0.25f, 7f);
            _raycastDepth = new ARRaycastDepthSource(raycasts, 12, 9);
        }

        private bool HasNewView()
        {
            if (!_hasKeyframe) return true;
            return Vector3.Distance(_camera.transform.position, _lastPosition) >= _minimumMove ||
                   Quaternion.Angle(_camera.transform.rotation, _lastRotation) >= _minimumTurn;
        }

        private void TryCaptureKeyframe()
        {
#if !UNITY_EDITOR
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                RejectedKeyframes++;
                LastGuidance = "tracking AR inestable; espera antes de continuar";
                return;
            }
#endif
            if (!TryCaptureImage(out var luminance, out int width, out int height,
                                 out var jpeg, out var intrinsics))
            {
                RejectedKeyframes++;
                LastGuidance = "camara todavia no disponible";
                return;
            }
            var quality = ScanV3Vision.Evaluate(luminance, width, height);
            if (!quality.Acceptable)
            {
                RejectedKeyframes++;
                LastGuidance = quality.Rejection;
                return;
            }

            _worldObservations.Clear();
            bool native = _nativeDepth.TryCapture(_camera, _worldObservations);
            bool raycast = _raycastDepth.TryCapture(_camera, _worldObservations);
            if ((!native && !raycast) || _worldObservations.Count == 0)
            {
                RejectedKeyframes++;
                LastGuidance = "mueve el telefono para obtener profundidad";
                return;
            }

            var origin = WorldOrigin.Instance;
            var frame = new ScanV3Keyframe
            {
                id = _frames.Count,
                timestamp = Time.realtimeSinceStartupAsDouble,
                initialPositionLocal = origin.ToRelative(_camera.transform.position),
                initialRotationLocal = Quaternion.Inverse(origin.transform.rotation) * _camera.transform.rotation,
                sharpness = quality.Sharpness,
                meanLuminance = quality.MeanLuminance,
                trackingConfidence = 1f,
                descriptor = ScanV3Vision.BuildDescriptor(luminance, width, height),
                focalLength = intrinsics.focalLength,
                principalPoint = intrinsics.principalPoint,
                imageResolution = intrinsics.resolution,
            };
            ConvertToCameraObservations(frame);
            AddFrame(frame, jpeg);
            LastGuidance = native ? "profundidad nativa fusionada" : "geometria AR fusionada";
        }

        private void ConvertToCameraObservations(ScanV3Keyframe frame)
        {
            var origin = WorldOrigin.Instance;
            for (int i = 0; i < _worldObservations.Count; i++)
            {
                var observation = _worldObservations[i];
                Vector3 worldPoint = origin.ToWorld(observation.positionLocal);
                Vector3 worldNormal = origin.transform.TransformDirection(observation.normalLocal);
                frame.observations.Add(new ScanV3CameraObservation
                {
                    positionCamera = _camera.transform.InverseTransformPoint(worldPoint),
                    normalCamera = _camera.transform.InverseTransformDirection(worldNormal),
                    confidence = observation.confidence,
                });
            }
            RawObservationCount += frame.observations.Count;
        }

        private void AddFrame(ScanV3Keyframe frame, byte[] jpeg)
        {
            if (_frames.Count > 0)
            {
                var previous = _frames[^1];
                _edges.Add(new ScanV3PoseEdge
                {
                    from = previous.id,
                    to = frame.id,
                    expectedWorldDelta = frame.initialPositionLocal - previous.initialPositionLocal,
                    expectedYawDelta = Mathf.DeltaAngle(previous.initialRotationLocal.eulerAngles.y,
                                                       frame.initialRotationLocal.eulerAngles.y),
                    weight = 1f,
                    kind = ScanV3EdgeKind.Odometry,
                });
            }
            _frames.Add(frame);
            _bundle?.AddKeyframe(frame, jpeg);
            if (ScanV3PoseGraph.TryCreateLoopEdge(_frames, frame.id, out var loop))
            {
                _edges.Add(loop);
                LoopClosureCount++;
            }
            _lastPosition = _camera != null ? _camera.transform.position : Vector3.zero;
            _lastRotation = _camera != null ? _camera.transform.rotation : Quaternion.identity;
            _hasKeyframe = true;
        }

        private void OptimizeGraph()
        {
            var optimized = ScanV3PoseGraph.Optimize(_frames, _edges);
            InitialGraphResidual = optimized.initialResidual;
            FinalGraphResidual = optimized.finalResidual;
            for (int i = 0; i < _frames.Count; i++)
            {
                var frame = _frames[i];
                frame.optimizedPositionLocal = optimized.positions[i];
                float initialYaw = Mathf.DeltaAngle(0f, frame.initialRotationLocal.eulerAngles.y);
                float yawCorrection = Mathf.DeltaAngle(initialYaw, optimized.yaws[i]);
                frame.optimizedRotationLocal = Quaternion.AngleAxis(yawCorrection, Vector3.up) *
                                               frame.initialRotationLocal;
            }
        }

        private void Reintegrate()
        {
            var volume = new SparseSurfelVolume(_voxelSize, _maximumVoxels);
            foreach (var frame in _frames)
            {
                var observations = new List<ScanV3Observation>(frame.observations.Count);
                foreach (var cameraObservation in frame.observations)
                {
                    observations.Add(new ScanV3Observation(
                        frame.optimizedPositionLocal +
                        frame.optimizedRotationLocal * cameraObservation.positionCamera,
                        frame.optimizedRotationLocal * cameraObservation.normalCamera,
                        cameraObservation.confidence));
                }
                volume.Integrate(observations);
            }
            FusedVoxelCount = volume.Count;
            _structure = ScanV3Geometry.ExtractStructure(
                volume.Extract(_minimumViews), _minimumPlanePoints, _minimumPlanePoints,
                10f, 0.12f, 0.5f, 0.65f, 0.38f, 0.8f);
        }

        private int MaterializeTransactional()
        {
            _lastCreated.Clear();
            float? floorY = FloorPoint.Instance != null ? FloorPoint.Instance.LocalY : null;
            if (!floorY.HasValue && _structure.hasFloor)
            {
                var floor = FloorPoint.Create(new Vector3(0f, _structure.floorY, 0f));
                _lastCreated.Add(floor);
                floorY = _structure.floorY;
            }
            string group = Guid.NewGuid().ToString("N");
            foreach (var source in _structure.walls)
            {
                var candidate = source;
                if (IsDuplicate(candidate)) continue;
                if (floorY.HasValue && Mathf.Abs(candidate.aLocal.y - floorY.Value) < 0.4f)
                {
                    float top = candidate.aLocal.y + candidate.height;
                    candidate.aLocal.y = candidate.bLocal.y = floorY.Value;
                    candidate.height = Mathf.Max(0.65f, top - floorY.Value);
                }
                var direction = (candidate.bLocal - candidate.aLocal).normalized;
                var normal = Vector3.Cross(Vector3.up, direction).normalized;
                int side = Vector3.Dot(normal, candidate.normalLocal) >= 0f ? 1 : -1;
                var wall = WallObject.Create(candidate.aLocal, candidate.bLocal,
                                             candidate.height, _wallWidth, side);
                if (wall == null || wall.GetComponent<MeshCollider>()?.sharedMesh == null)
                    throw new InvalidOperationException("pared materializada sin collider valido");
                wall.PolylineId = group;
                _lastCreated.Add(wall);
#if UNITY_EDITOR
                if (ForceMaterializationFailureForEditor)
                {
                    ForceMaterializationFailureForEditor = false;
                    throw new InvalidOperationException("fallo QA inyectado");
                }
#endif
            }
            // Verificacion minima de persistencia antes de confirmar transaccion.
            var registry = SceneRegistry.Instance ?? throw new InvalidOperationException("SceneRegistry ausente");
            string json = JsonUtility.ToJson(registry.Capture("atlas-transaction"));
            if (string.IsNullOrEmpty(json) || JsonUtility.FromJson<ScanData>(json) == null)
                throw new InvalidOperationException("fallo la verificacion de persistencia");
            return _lastCreated.Count;
        }

        private bool IsDuplicate(ScanV3WallCandidate candidate)
        {
            var registry = SceneRegistry.Instance;
            if (registry == null) return false;
            foreach (var wall in registry.Walls)
                if (ScanV3Geometry.IsDuplicate(wall, candidate)) return true;
            return false;
        }

        private void RollbackCreated()
        {
            foreach (var item in _lastCreated)
            {
                if (item is WallObject wall && wall != null) wall.Delete();
                else if (item is FloorPoint floor && floor != null) floor.Delete();
            }
            _lastCreated.Clear();
        }

        private void RestoreDepthMode()
        {
            if (_occlusion != null) _occlusion.requestedEnvironmentDepthMode = _previousDepthMode;
        }

        private bool TryCaptureImage(out byte[] luminance, out int width, out int height,
                                     out byte[] jpeg, out XRCameraIntrinsics intrinsics)
        {
            luminance = jpeg = null;
            width = height = 0;
            intrinsics = default;
            if (_cameraManager == null || !_cameraManager.TryGetIntrinsics(out intrinsics) ||
                !_cameraManager.TryAcquireLatestCpuImage(out var image)) return false;
            try
            {
                if (image.planeCount < 1) return false;
                var plane = image.GetPlane(0);
                width = Mathf.Max(16, _analysisWidth);
                height = Mathf.Max(16, Mathf.RoundToInt(width * image.height / (float)image.width));
                luminance = new byte[width * height];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int sourceX = Mathf.Min(image.width - 1, x * image.width / width);
                    int sourceY = Mathf.Min(image.height - 1, y * image.height / height);
                    int index = sourceY * plane.rowStride + sourceX * plane.pixelStride;
                    luminance[y * width + x] = index >= 0 && index < plane.data.Length
                        ? plane.data[index] : (byte)0;
                }
                var rgb = new byte[luminance.Length * 3];
                for (int i = 0; i < luminance.Length; i++)
                    rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = luminance[i];
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                try
                {
                    texture.LoadRawTextureData(rgb);
                    texture.Apply(false, false);
                    jpeg = texture.EncodeToJPG(_jpegQuality);
                }
                finally { Destroy(texture); }
                return true;
            }
            finally { image.Dispose(); }
        }

#if UNITY_EDITOR
        public void AddSyntheticRoomForEditor(float width = 4f, float depth = 3f,
                                              float height = 2.5f)
        {
            if (!IsCapturing) return;
            var world = BuildSyntheticRoom(width, depth, height, 0.10f);
            for (int frameIndex = 0; frameIndex < 3; frameIndex++)
            {
                var frame = new ScanV3Keyframe
                {
                    id = _frames.Count,
                    timestamp = frameIndex,
                    initialPositionLocal = new Vector3(frameIndex * 0.02f, 0f, 0f),
                    initialRotationLocal = Quaternion.identity,
                    descriptor = SyntheticDescriptor(),
                    sharpness = 0.2f,
                    meanLuminance = 0.5f,
                    trackingConfidence = 1f,
                };
                foreach (var observation in world)
                {
                    frame.observations.Add(new ScanV3CameraObservation
                    {
                        positionCamera = observation.positionLocal - frame.initialPositionLocal,
                        normalCamera = observation.normalLocal,
                        confidence = observation.confidence,
                    });
                }
                RawObservationCount += frame.observations.Count;
                AddFrame(frame, null);
            }
            LastGuidance = "habitacion sintetica lista";
        }

        private static List<ScanV3Observation> BuildSyntheticRoom(float width, float depth,
                                                                  float height, float step)
        {
            var result = new List<ScanV3Observation>();
            for (float x = -width / 2f; x <= width / 2f; x += step)
            for (float z = 0f; z <= depth; z += step)
                result.Add(new ScanV3Observation(new Vector3(x, 0f, z), Vector3.up));
            AddSyntheticWall(result, new Vector3(-width / 2f, 0f, 0f), Vector3.forward, width, height, step);
            AddSyntheticWall(result, new Vector3(width / 2f, 0f, depth), Vector3.back, width, height, step);
            AddSyntheticWall(result, new Vector3(-width / 2f, 0f, depth), Vector3.right, depth, height, step);
            AddSyntheticWall(result, new Vector3(width / 2f, 0f, 0f), Vector3.left, depth, height, step);
            return result;
        }

        private static void AddSyntheticWall(List<ScanV3Observation> output, Vector3 origin,
                                             Vector3 normal, float length, float height, float step)
        {
            var tangent = Vector3.Cross(Vector3.up, normal).normalized;
            for (float along = 0f; along <= length; along += step)
            for (float y = 0f; y <= height; y += step)
                output.Add(new ScanV3Observation(origin + tangent * along + Vector3.up * y, normal));
        }

        private static float[] SyntheticDescriptor()
        {
            var values = new float[64];
            for (int i = 0; i < values.Length; i++) values[i] = Mathf.Sin(i * 0.7f);
            return values;
        }
#endif
    }
}
