using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// Top-level orchestrator for room scanning. Owns the scan state machine and
    /// drives the pipeline:
    ///
    ///   PlaneCollector ─→ CornerDetector ─→ RoomBuilder ─→ DoorDetector ─→ JsonExporter
    ///
    /// Flow when <c>autoOrigin</c> is set (the default):
    ///   1. Bootstrap — the origin is derived from geometry, not from the user: a real
    ///      corner if two walls are visible, otherwise a single wall for the axis with
    ///      the origin under the camera. It locks once successive proposals agree.
    ///      Crucially the yaw comes from a wall, never from the camera — the occupancy
    ///      grid is axis-aligned in origin-local space, so a wall-aligned frame keeps
    ///      cells running parallel to the room instead of cutting across it.
    ///   2. ReviewOrigin — mandatory: the auto-detected origin is draggable exactly like
    ///      "ADJUST ORIGIN" later, but locking-and-mapping can't proceed without a
    ///      CONFIRM ORIGIN tap first.
    ///   3. ChooseMode — mandatory: pick a <see cref="WallSourceMode"/> before mapping
    ///      starts. Changing it later resets the scan (see below) — this first choice is
    ///      just that reset with nothing yet to lose.
    ///   4. Mapping   — room height is measured from the same depth pass that feeds
    ///      occupancy and refined as the user walks. The origin stays re-grabbable
    ///      ("ADJUST ORIGIN") so a correction shifts the whole map. Floor wall sources
    ///      (<see cref="DepthOccupancyMapper.IsFloorMode"/>) build once, walls only, on a
    ///      CONFIRM FLOOR MAPPED press, instead of on the usual auto-build timer.
    ///   5. Export    — doors are detected, folded in, and the model written to disk.
    ///
    /// Clearing <c>autoOrigin</c> restores the older manual flow: ScanBottom (drag a
    /// marker onto the bottom corner, CONFIRM anchors it) then ScanTop (drag to the
    /// ceiling, CONFIRM locks room height and builds the two corner walls) — this path
    /// skips ReviewOrigin (placement was already deliberate) but still passes through
    /// ChooseMode.
    ///
    /// Switching <see cref="WallSourceMode"/> via the Mapping HUD's MODE: button — at
    /// ChooseMode or later — resets the occupancy grid and every wall/surface
    /// (<see cref="ResetScan"/>), keeping only the origin/anchor: each mode is meant to be
    /// its own clean playground, not five ways of refitting one shared scan.
    ///
    /// The map origin is a movable child of the tracked ARAnchor, so the user's drag
    /// correction is recorded as the origin's local offset while the anchor keeps it
    /// locked to the world. Replaces the flow in <c>DepthWallAnalyzer</c> /
    /// <c>TouchAnchorPlacer</c>.
    /// </summary>
    [RequireComponent(typeof(ARAnchorManager))]
    [RequireComponent(typeof(ARRaycastManager))]
    public class RoomScanningManager : MonoBehaviour
    {
        enum ScanState { Bootstrap, ScanBottom, ScanTop, ReviewOrigin, ChooseMode, Mapping, Done }

        [Header("Pipeline components")]
        [SerializeField] PlaneCollector planeCollector;
        [SerializeField] CornerDetector cornerDetector;
        [SerializeField] DepthOccupancyMapper occupancyMapper;
        [SerializeField] RoomBuilder roomBuilder;
        [SerializeField] DoorDetector doorDetector;
        [SerializeField] JsonExporter jsonExporter;
        [SerializeField] RoomRelocalizer relocalizer;
        [SerializeField] WallEditor wallEditor;
        [SerializeField] ScanDataExporter scanExporter;
        [SerializeField] DetectionTuningMenu tuningMenu;

        [Header("Grab handles")]
        [Tooltip("Touch must start within this fraction of screen width of a marker to grab it.")]
        [SerializeField] float grabRadiusFraction = 0.12f;
        [Tooltip("Metres of vertical movement per screen-height of two-finger drag.")]
        [SerializeField] float verticalDragMetres = 2.5f;

        [Header("Corner stability (top corner auto-measure)")]
        [SerializeField] float cornerStableRadius = 0.15f;
        [SerializeField] int cornerStableCount = 3;

        [Header("Automatic origin")]
        [Tooltip("Detect the origin from floor + wall geometry instead of the manual " +
                 "drag-the-corner flow. Turn off to fall back to the two-marker flow.")]
        [SerializeField] bool autoOrigin = true;
        [Tooltip("Successive origin proposals must land within this distance (m) to count as stable.")]
        [SerializeField] float originStableRadius = 0.25f;
        [Tooltip("…and within this yaw difference (deg).")]
        [SerializeField] float originStableAngle = 12f;
        [Tooltip("Stable proposals required before the origin anchor is locked.")]
        [SerializeField] int originStableCount = 3;

        [Header("Visuals")]
        [SerializeField] GameObject originMarkerPrefab;
        [SerializeField] float originAxisLength = 0.4f;

        [Header("Timing")]
        [SerializeField] float runInterval = 2f;
        [Tooltip("How often (s) wall/furniture occupancy is sampled while mapping.")]
        [SerializeField] float mappingInterval = 0.3f;

        [Header("Automatic build")]
        [Tooltip("Rebuild walls on their own while mapping, instead of waiting for BUILD " +
                 "ROOM. Walls that stay put across rebuilds are anchored and left alone. " +
                 "The buttons still force a rebuild.")]
        [SerializeField] bool autoBuild = true;
        [Tooltip("Also detect horizontal surfaces in the automatic loop. Off by default — " +
                 "left to the BUILD SURFACES / BUILD ROOM buttons.")]
        [SerializeField] bool autoBuildSurfaces = false;
        [Tooltip("Seconds between automatic rebuilds while mapping.")]
        [SerializeField] float autoBuildInterval = 5f;
        float _nextBuild;

        ARAnchorManager _anchorManager;
        ARRaycastManager _raycastManager;
        Camera _cam;

        Transform _mapOrigin;       // movable child of the anchor — its local offset = the correction
        ARAnchor _originAnchor;

        ScanState _state;
        float _bottomY;
        float _roomHeight;
        float _nextRun;
        float _nextAccum;

        // Bottom-corner draft handle.
        Transform _bottomMarker;
        bool _bottomSeeded;
        bool _userTouchedBottom;
        bool _originRequested;

        // Top-corner handle.
        Transform _topMarker;
        bool _userTouchedTop;
        float _candidateHeight;
        int _stableHits;
        bool _hasCandidate;

        // Origin re-adjust mode (ScanTop / Mapping).
        bool _adjustingOrigin;

        // Manual wall-correction mode (Mapping).
        bool _editingWalls;

        // User-held pause: freezes depth accumulation and auto-rebuilds without entering
        // an edit mode. The manual BUILD buttons still work.
        bool _mappingPaused;

        // Floor wall sources (FloorRaw/FloorConfirmed) build once, on demand, instead of
        // on the auto-rebuild timer: sweep the floor, confirm, then build from it.
        bool _floorSweepConfirmed;

        // Persistence.
        float _lastSaveTime = -1f;
        string _relocalizeStatus;

        // Automatic bootstrap candidate.
        Pose _bootstrapPose;
        OriginFix _bootstrapFix = OriginFix.None;
        int _bootstrapHits;
        OriginFix _lockedFix = OriginFix.None;

        // Grab state.
        bool _grabbing;
        Vector3 _grabFloorOffset; // marker - hit at grab moment (keeps it from jumping under finger)
        float _grabPlaneY;

        GUIStyle _style;

        public bool HasOrigin => _mapOrigin != null;

        void Awake()
        {
            _anchorManager  = GetComponent<ARAnchorManager>();
            _raycastManager = GetComponent<ARRaycastManager>();

            if (planeCollector == null) planeCollector = GetComponent<PlaneCollector>();
            if (cornerDetector == null) cornerDetector = GetComponent<CornerDetector>();
            if (occupancyMapper == null) occupancyMapper = GetComponent<DepthOccupancyMapper>();
            if (roomBuilder == null)    roomBuilder    = GetComponent<RoomBuilder>();
            if (doorDetector == null)   doorDetector   = GetComponent<DoorDetector>();
            if (jsonExporter == null)   jsonExporter   = GetComponent<JsonExporter>();
            if (relocalizer == null)    relocalizer    = GetComponent<RoomRelocalizer>();
            // The editor is optional in the scene: add it if nobody wired one up, so the
            // feature works without a scene edit. Add it to XR Origin by hand if you want
            // its thresholds exposed in the inspector — the scene wins over these defaults.
            if (wallEditor == null) wallEditor = GetComponent<WallEditor>();
            if (wallEditor == null) wallEditor = gameObject.AddComponent<WallEditor>();
            wallEditor.Edited += AutoSave;

            if (scanExporter == null) scanExporter = GetComponent<ScanDataExporter>();
            if (scanExporter == null) scanExporter = gameObject.AddComponent<ScanDataExporter>();

            if (tuningMenu == null) tuningMenu = GetComponent<DetectionTuningMenu>();
            if (tuningMenu == null && Debug.isDebugBuild) tuningMenu = gameObject.AddComponent<DetectionTuningMenu>();

            _state = autoOrigin ? ScanState.Bootstrap : ScanState.ScanBottom;
        }

        void OnEnable()  => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        void Update()
        {
            _cam = Camera.main;
            if (_cam == null) return;

            // Per-frame grab-drag of whichever handle is active.
            HandleActiveDrag();

            // Wall/furniture occupancy accumulates on its own faster cadence so cells
            // are observed across many frames (frame-count is the noise filter).
            // Editing pauses accumulation for the same reason adjusting the origin does:
            // the grid must not shift underneath a correction in progress.
            if (_state == ScanState.Mapping && !_adjustingOrigin && !_editingWalls && !_mappingPaused
                && Time.time >= _nextAccum)
            {
                _nextAccum = Time.time + mappingInterval;
                occupancyMapper?.Accumulate(_mapOrigin, _roomHeight);
            }

            // Automatic reconstruction: walls on a timer, so they appear (and then anchor)
            // as the user walks rather than on a tap. Furniture only if opted in. Floor
            // wall sources never auto-rebuild — they sweep, confirm, then build once.
            bool floorSourced = occupancyMapper != null && DepthOccupancyMapper.IsFloorMode(occupancyMapper.WallSource);
            if (autoBuild && _state == ScanState.Mapping && !_adjustingOrigin && !_editingWalls
                && !_mappingPaused && !floorSourced && Time.time >= _nextBuild)
            {
                _nextBuild = Time.time + autoBuildInterval;
                AutoBuild();
            }

            if (Time.time < _nextRun) return;
            _nextRun = Time.time + runInterval;

            switch (_state)
            {
                case ScanState.Bootstrap:  TickBootstrap();  break;
                case ScanState.ScanBottom: TickScanBottom(); break;
                case ScanState.ScanTop:    TickScanTop();    break;
                case ScanState.Mapping:    RefreshRoomHeight(); break;
            }
        }

        // ── Grab-and-drag input ───────────────────────────────────────────────

        void HandleActiveDrag()
        {
            // Wall editing owns the touch while it is active (WallEditor.Update).
            if (_editingWalls) return;

            if (_adjustingOrigin && _mapOrigin != null)
            {
                // Walls/visuals are parented under the origin, so they follow it for free.
                DragHandle(_mapOrigin, allowVertical: true);
                return;
            }

            switch (_state)
            {
                case ScanState.ScanBottom:
                    if (_bottomMarker != null && DragHandle(_bottomMarker, allowVertical: true))
                        _userTouchedBottom = true;
                    break;
                case ScanState.ScanTop:
                    // Top marker is height-only: vertical drag adjusts room height.
                    if (_topMarker != null && DragHandleVertical(_topMarker))
                        _userTouchedTop = true;
                    break;
            }
        }

        /// <summary>
        /// Grab the target if a touch starts on it, then drag it across the floor
        /// (one finger → X/Z) or vertically (two fingers → Y). Returns true while
        /// the user is actively moving it.
        /// </summary>
        bool DragHandle(Transform target, bool allowVertical)
        {
            var touches = Touch.activeTouches;
            if (touches.Count == 0) { _grabbing = false; return false; }

            if (!_grabbing && !TryBeginGrab(target, touches[0])) return false;

            // Two fingers → vertical.
            if (allowVertical && touches.Count >= 2)
            {
                MoveVertical(target, touches[0].delta.y);
                return true;
            }

            // One finger → slide across the floor at the marker's current height.
            if (RayToHorizontalPlane(touches[0].screenPosition, _grabPlaneY, out var hit))
            {
                var p = hit + _grabFloorOffset;
                p.y = target.position.y;
                target.position = p;
                return true;
            }
            return false;
        }

        /// <summary>Vertical-only handle (the top corner): one-finger up/down drag changes Y.</summary>
        bool DragHandleVertical(Transform target)
        {
            var touches = Touch.activeTouches;
            if (touches.Count == 0) { _grabbing = false; return false; }
            if (!_grabbing && !TryBeginGrab(target, touches[0])) return false;

            MoveVertical(target, touches[0].delta.y);
            return true;
        }

        bool TryBeginGrab(Transform target, Touch primary)
        {
            var sp = _cam.WorldToScreenPoint(target.position);
            if (sp.z <= 0f) return false; // behind camera

            float grabPx = Screen.width * grabRadiusFraction;
            if (Vector2.Distance(primary.screenPosition, new Vector2(sp.x, sp.y)) > grabPx)
                return false;

            _grabbing = true;
            _grabPlaneY = target.position.y;
            _grabFloorOffset = Vector3.zero;
            if (RayToHorizontalPlane(primary.screenPosition, _grabPlaneY, out var hit))
                _grabFloorOffset = target.position - hit; // preserve where you grabbed it
            return true;
        }

        void MoveVertical(Transform target, float screenDeltaY)
        {
            var p = target.position;
            p.y += screenDeltaY * (verticalDragMetres / Screen.height);
            target.position = p;
            _grabPlaneY = p.y;
        }

        /// <summary>Intersects the camera ray through a screen point with the horizontal plane y = planeY.</summary>
        bool RayToHorizontalPlane(Vector2 screenPos, float planeY, out Vector3 hit)
        {
            hit = default;
            var ray = _cam.ScreenPointToRay(screenPos);
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = (planeY - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }

        // ── Automatic origin bootstrap ────────────────────────────────────────

        /// <summary>How an origin candidate was derived; higher is better evidence.</summary>
        enum OriginFix { None = 0, Wall = 1, Corner = 2 }

        /// <summary>
        /// Proposes a map origin from what AR has found so far, best evidence first:
        /// a true corner (two walls) → a single wall → nothing yet.
        ///
        /// The yaw is the point of this. It used to come from the camera's forward vector,
        /// i.e. wherever the user happened to be looking, which left the occupancy grid
        /// (axis-aligned in origin-local space) cutting diagonally across every wall.
        /// Aligning to a real wall makes cells run parallel to the room. There is
        /// deliberately no camera-forward fallback: no wall means no proposal.
        /// </summary>
        bool TryProposeOrigin(out Pose pose, out OriginFix fix)
        {
            pose = default;
            fix = OriginFix.None;

            var floor = planeCollector.LargestFloor();
            if (floor == null) return false;
            float floorY = floor.transform.position.y;

            // Best: a real corner, giving both a position and an axis.
            if (cornerDetector.TryDetectCorner(floorY, out var corner, out _, out var wallA, out _))
            {
                pose = new Pose(corner, AlignedRotation(wallA.direction, corner));
                fix = OriginFix.Corner;
                return true;
            }

            // Fallback: one wall fixes the axis; put the origin under the camera.
            var wall = planeCollector.LargestPlane(PlaneCollector.IsWall);
            if (wall == null) return false;

            var dir = Vector3.Cross(wall.normal, Vector3.up);
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();

            var origin = _cam.transform.position;
            origin.y = floorY;

            pose = new Pose(origin, AlignedRotation(dir, origin));
            fix = OriginFix.Wall;
            return true;
        }

        /// <summary>
        /// Rotation whose forward runs along <paramref name="wallDir"/>, turned by whichever
        /// multiple of 90° puts the camera in the origin's +X/+Z quadrant — so the room
        /// lands in positive coordinates and the occupancy grid uses positive keys.
        /// </summary>
        Quaternion AlignedRotation(Vector3 wallDir, Vector3 originPos)
        {
            var best = Quaternion.LookRotation(wallDir, Vector3.up);
            var toCam = _cam.transform.position - originPos;
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 1e-4f) return best;

            for (int q = 0; q < 4; q++)
            {
                var candidate = Quaternion.LookRotation(wallDir, Vector3.up) * Quaternion.Euler(0f, q * 90f, 0f);
                var local = Quaternion.Inverse(candidate) * toCam;
                if (local.x >= 0f && local.z >= 0f) return candidate;
            }
            return best;
        }

        /// <summary>
        /// Drives the automatic bootstrap: proposes an origin each tick and locks it once
        /// successive proposals agree. Hysteresis on both position and yaw, mirroring
        /// <see cref="Stabilize"/> for ceiling height. A Corner proposal always supersedes
        /// a Wall candidate (better evidence) and restarts the count.
        /// </summary>
        void TickBootstrap()
        {
            if (_originRequested) return;

            if (!TryProposeOrigin(out var pose, out var fix))
            {
                _bootstrapFix = OriginFix.None;
                _bootstrapHits = 0;
                return;
            }

            bool upgraded = fix > _bootstrapFix;
            bool close = !upgraded
                && _bootstrapHits > 0
                && Vector3.Distance(pose.position, _bootstrapPose.position) < originStableRadius
                && Quaternion.Angle(pose.rotation, _bootstrapPose.rotation) < originStableAngle;

            if (close)
            {
                _bootstrapPose = new Pose(
                    Vector3.Lerp(_bootstrapPose.position, pose.position, 0.5f),
                    Quaternion.Slerp(_bootstrapPose.rotation, pose.rotation, 0.5f));
                _bootstrapHits++;
            }
            else
            {
                _bootstrapPose = pose;
                _bootstrapFix = fix;
                _bootstrapHits = 1;
            }

            if (_bootstrapHits < originStableCount) return;

            _originRequested = true;
            _lockedFix = _bootstrapFix;
            Debug.Log($"[RoomScan] Auto origin locked ({_bootstrapFix}) at {_bootstrapPose.position:F2} " +
                      $"after {_bootstrapHits} stable ticks.");
            ConfirmOrigin(_bootstrapPose);
        }

        /// <summary>The shared final step out of <see cref="ScanState.ChooseMode"/>, for both
        /// the auto-origin path (no manual ceiling step, height refined as we go — the corner
        /// walls seed here) and the manual path (height and any corner walls are already
        /// fixed from <see cref="ConfirmTopCorner"/>, so that block below is a no-op there).</summary>
        void BeginMapping()
        {
            if (_lockedFix == OriginFix.Corner &&
                cornerDetector.TryDetectCorner(_bottomY, out var corner, out _, out var wallA, out var wallB))
                roomBuilder.BuildCornerWalls(corner, wallA, wallB, _roomHeight > 0.1f ? _roomHeight : 2.4f);

            occupancyMapper?.ResetGrid();
            _grabbing = false;
            _mappingPaused = false;
            _floorSweepConfirmed = false;
            _nextBuild = Time.time + autoBuildInterval;
            _state = ScanState.Mapping;
            Debug.Log("[RoomScan] ✔ Mapping. Walk the room.");
        }

        /// <summary>Leaves the mandatory <see cref="ScanState.ReviewOrigin"/> screen — the
        /// auto-detected origin was draggable exactly like ADJUST ORIGIN while here.</summary>
        void ConfirmReviewedOrigin()
        {
            _adjustingOrigin = false;
            _grabbing = false;
            _state = ScanState.ChooseMode;
        }

        /// <summary>Leaves the mandatory <see cref="ScanState.ChooseMode"/> screen and
        /// commits to whichever <see cref="WallSourceMode"/> is currently selected.</summary>
        void ConfirmChosenMode() => BeginMapping();

        // ── State 1: bottom corner ────────────────────────────────────────────

        void TickScanBottom()
        {
            if (_originRequested) return;

            var floor = planeCollector.LargestFloor();
            if (floor == null) return;
            float floorY = floor.transform.position.y;

            // Seed the marker once, in front of the camera on the floor. After the
            // user touches it, we never reposition it automatically.
            if (!_bottomSeeded)
            {
                var fwd = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
                var p = _cam.transform.position + fwd * 1.5f;
                p.y = floorY;
                EnsureBottomMarker(p);
                _bottomSeeded = true;
            }
        }

        void ConfirmBottomCorner()
        {
            if (_bottomMarker == null || _originRequested) return;

            var pos = _bottomMarker.position;
            _bottomY = pos.y;
            var fwd = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;

            _originRequested = true;
            Debug.Log($"[RoomScan] BOTTOM corner confirmed at {pos:F2}. Creating origin anchor…");
            ConfirmOrigin(new Pose(pos, Quaternion.LookRotation(fwd, Vector3.up)));
        }

        /// <summary>Creates the origin anchor (with a movable child origin) and starts plane baking.</summary>
        public async void ConfirmOrigin(Pose pose)
        {
            _bottomY = pose.position.y;

            var result = await _anchorManager.TryAddAnchorAsync(pose);
            if (!result.status.IsSuccess())
            {
                Debug.LogWarning($"[RoomScan] Origin anchor failed: {result.status}. Retry.");
                _originRequested = false;
                return;
            }

            _originAnchor = result.value;

            // Movable origin: a child of the tracked anchor. Dragging it later records
            // the correction as its local offset while the anchor keeps world lock.
            var originGO = new GameObject("MapOrigin");
            originGO.transform.SetParent(_originAnchor.transform, worldPositionStays: false);
            originGO.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            _mapOrigin = originGO.transform;

            if (_bottomMarker != null) Destroy(_bottomMarker.gameObject);

            roomBuilder.SetOrigin(_mapOrigin, _bottomY);
            planeCollector.StartBaking();
            CreateOriginVisual();

            _hasCandidate = false;
            _stableHits = 0;
            _grabbing = false;

            if (autoOrigin)
            {
                // No manual ceiling step, but the auto-detected origin still needs a human
                // look before mapping commits to it — mandatory, unlike ADJUST ORIGIN later.
                _state = ScanState.ReviewOrigin;
                _adjustingOrigin = true;
                return;
            }

            _state = ScanState.ScanTop;
            Debug.Log($"[RoomScan] ✔ ORIGIN set at {pose.position:F2}. Now match the TOP corner.");
        }

        /// <summary>
        /// Pulls the ceiling estimate off the occupancy accumulation. Automatic walls are
        /// not left stale: <c>Reconstruct</c> rebuilds them from the grid each time, so a
        /// corrected height applies retroactively. Pinned walls keep the height they were
        /// built with — their geometry is the user's, and silently restretching it would
        /// be an edit nobody asked for.
        /// </summary>
        void RefreshRoomHeight()
        {
            if (occupancyMapper == null || !occupancyMapper.TryEstimateRoomHeight(out float h)) return;
            if (Mathf.Abs(h - _roomHeight) < 0.02f) return;

            _roomHeight = h;
            roomBuilder.Room.roomHeight = h;
            if (wallEditor != null) wallEditor.SetRoomHeight(h);
        }

        // ── State 2: top corner / room height ─────────────────────────────────

        void TickScanTop()
        {
            if (_mapOrigin == null) return;

            // Auto-measure ceiling, but only drive the marker until the user grabs it.
            if (!_userTouchedTop &&
                cornerDetector.TryMeasureCeiling(_mapOrigin.position, _bottomY, out float ceilingY) &&
                Stabilize(ceilingY))
            {
                EnsureTopMarker(ceilingY);
            }
            else if (_topMarker == null)
            {
                // Show a starting top marker even before a measurement succeeds.
                EnsureTopMarker(_bottomY + 2.4f);
            }
        }

        void ConfirmTopCorner()
        {
            if (_topMarker == null || _mapOrigin == null) return;

            _roomHeight = Mathf.Max(0.1f, _topMarker.position.y - _mapOrigin.position.y);

            if (cornerDetector.TryDetectCorner(_bottomY, out var corner, out _, out var wallA, out var wallB))
                roomBuilder.BuildCornerWalls(corner, wallA, wallB, _roomHeight);
            else
                roomBuilder.Room.roomHeight = _roomHeight;

            AddTopVisual(_roomHeight);
            if (_topMarker != null) Destroy(_topMarker.gameObject);
            _grabbing = false;
            // BeginMapping (run once a mode is confirmed) does the grid reset and the
            // actual transition into Mapping — this just gets the origin/height ready.
            _state = ScanState.ChooseMode;
            Debug.Log($"[RoomScan] TOP confirmed. ROOM HEIGHT = {_roomHeight:F2}m. Choose a detection mode.");
        }

        bool Stabilize(float height)
        {
            if (_hasCandidate && Mathf.Abs(height - _candidateHeight) < cornerStableRadius)
            {
                _stableHits++;
                _candidateHeight = Mathf.Lerp(_candidateHeight, height, 0.5f);
            }
            else
            {
                _candidateHeight = height;
                _stableHits = 1;
                _hasCandidate = true;
            }
            return _stableHits >= cornerStableCount;
        }

        // ── State 3: continuous wall mapping ──────────────────────────────────


        // ── Finalise ──────────────────────────────────────────────────────────

        /// <summary>
        /// Walls, then horizontal surfaces (their footprints excluded from a wall refit),
        /// then walls once more. Anchored geometry is frozen throughout.
        /// </summary>
        void BuildRoom()
        {
            if (_mapOrigin == null || occupancyMapper == null) return;
            occupancyMapper.Reconstruct(_mapOrigin, _roomHeight);
            occupancyMapper.ReconstructSurfaces(_mapOrigin, _roomHeight);
            occupancyMapper.Reconstruct(_mapOrigin, _roomHeight);
            AutoSave();
        }

        // ── Tuning-menu hooks (kept thin so the menu needs no internals) ──────

        /// <summary>Mapping and not mid-correction — when the tuning menu may show and act.</summary>
        public bool IsMapping => _state == ScanState.Mapping && !_editingWalls && !_adjustingOrigin;

        /// <summary>Re-run the full reconstruction with the current thresholds; keeps the grid.</summary>
        public void RebuildRoom() => BuildRoom();

        /// <summary>Drop the accumulated occupancy and all non-pinned geometry — needed after
        /// changing an accumulate-time knob (normal split, band). The room must be re-walked.</summary>
        public void ResetScan()
        {
            occupancyMapper?.ResetGrid();
            roomBuilder?.ClearWalls(keepPinned: false);
            roomBuilder?.ClearSurfaces(keepPinned: false);
            _nextBuild = Time.time + autoBuildInterval;
        }

        /// <summary>
        /// The timed rebuild: walls always, surfaces only when <see cref="autoBuildSurfaces"/>
        /// is set. Kept separate from <see cref="BuildRoom"/> so the buttons still do the
        /// full job on demand.
        /// </summary>
        void AutoBuild()
        {
            if (_mapOrigin == null || occupancyMapper == null) return;
            occupancyMapper.Reconstruct(_mapOrigin, _roomHeight);
            if (autoBuildSurfaces)
            {
                occupancyMapper.ReconstructSurfaces(_mapOrigin, _roomHeight);
                occupancyMapper.Reconstruct(_mapOrigin, _roomHeight);
            }
            AutoSave();
        }

        /// <summary>
        /// Persists the latest reconstruction under a stable name, so the newest good scan
        /// survives the app being killed. The timestamped <c>EXPORT MAP</c> files stay as
        /// the archive; this is what a later session reloads.
        /// </summary>
        void AutoSave()
        {
            if (jsonExporter == null || roomBuilder == null) return;
            if (jsonExporter.SaveCurrent(roomBuilder.Room))
                _lastSaveTime = Time.time;
        }

        /// <summary>
        /// Loads the saved room and aligns it to the live scan, applying the correction to
        /// the map origin — the same effect as ADJUST ORIGIN, computed instead of dragged.
        /// Refuses rather than guesses when the room is too symmetric to place confidently.
        /// </summary>
        void Relocalize()
        {
            if (relocalizer == null || jsonExporter == null || _mapOrigin == null) return;

            if (!jsonExporter.TryLoadCurrent(out var saved))
            {
                _relocalizeStatus = "no saved room";
                return;
            }

            if (!relocalizer.TryAlign(saved, roomBuilder.Room, out var correction))
            {
                _relocalizeStatus = relocalizer.LastScore <= 0f
                    ? "no match — scan more"
                    : $"ambiguous ({relocalizer.LastMargin:P0} margin)";
                return;
            }

            // Compose the correction onto the origin: saved coordinates then read directly
            // in the live session.
            _mapOrigin.localRotation *= correction.rotation;
            _mapOrigin.localPosition += _mapOrigin.localRotation * correction.position;

            // Rebuild the saved geometry in its own colour. Overlaying it on the live scan
            // is the check: if the two sets of walls sit on top of each other, the
            // alignment landed; if they are offset or 90° out, it did not.
            int restored = roomBuilder.RestoreRoom(saved);

            _relocalizeStatus = $"aligned {relocalizer.LastScore:F2} · {restored} restored";
            Debug.Log($"[RoomScan] Relocalized onto saved room: {_relocalizeStatus}");
        }

        public string FinishAndExport()
        {
            var doors = doorDetector.DetectDoors(roomBuilder.Room.walls, _mapOrigin);
            roomBuilder.AddDoors(doors);

            _state = ScanState.Done;
            AutoSave();   // keep the reloadable save in step with the archive
            return jsonExporter.Export(roomBuilder.Room);
        }

        // ── Markers + visuals ─────────────────────────────────────────────────

        void EnsureBottomMarker(Vector3 worldPos)
        {
            if (_bottomMarker == null)
            {
                var root = new GameObject("BottomCornerHandle").transform;
                float s = originAxisLength * 0.25f;
                AddBar(root, new Vector3(0, s, 0), new Vector3(s, s * 2f, s), new Color(1f, 0.2f, 0.9f));
                AddBar(root, Vector3.zero,         Vector3.one * s,           new Color(1f, 0.9f, 0.2f));
                _bottomMarker = root;
            }
            _bottomMarker.position = worldPos;
        }

        void EnsureTopMarker(float worldY)
        {
            if (_topMarker == null)
            {
                var root = new GameObject("TopCornerHandle").transform;
                float s = originAxisLength * 0.22f;
                AddBar(root, Vector3.zero, Vector3.one * s, new Color(0.3f, 1f, 0.6f));
                _topMarker = root;
            }
            var p = _mapOrigin.position; // top stays directly above the origin XZ
            p.y = worldY;
            _topMarker.position = p;
        }

        void CreateOriginVisual()
        {
            if (originMarkerPrefab != null)
            {
                var marker = Instantiate(originMarkerPrefab, _mapOrigin);
                marker.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                return;
            }

            var root = new GameObject("MapOriginVisual");
            root.transform.SetParent(_mapOrigin, worldPositionStays: false);
            root.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            float L = originAxisLength;
            float t = L * 0.05f;
            AddBar(root.transform, Vector3.zero,                Vector3.one * (t * 2.5f), new Color(1f, 0.92f, 0.1f));
            AddBar(root.transform, new Vector3(L * 0.5f, 0, 0), new Vector3(L, t, t),     new Color(1f, 0.2f, 0.2f));
            AddBar(root.transform, new Vector3(0, L * 0.5f, 0), new Vector3(t, L, t),     new Color(0.2f, 1f, 0.2f));
            AddBar(root.transform, new Vector3(0, 0, L * 0.5f), new Vector3(t, t, L),     new Color(0.3f, 0.5f, 1f));

            // A bright always-on-top beacon (tall post + top cube) so the origin is
            // unmistakable through the camera, even behind walls. Built from the manual
            // cube mesh (NOT GameObject.CreatePrimitive — its collider throws under IL2CPP).
            var magenta = new Color(1f, 0.2f, 0.9f);
            float postH = L * 1.5f;
            AddBar(root.transform, new Vector3(0, postH * 0.5f, 0), new Vector3(t * 1.5f, postH, t * 1.5f), magenta);
            AddBar(root.transform, new Vector3(0, postH, 0),        Vector3.one * (L * 0.30f),              magenta);
        }

        void AddTopVisual(float height)
        {
            if (_mapOrigin == null || height <= 0.05f) return;

            var root = new GameObject("MapHeightVisual");
            root.transform.SetParent(_mapOrigin, worldPositionStays: false);
            root.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            float t = originAxisLength * 0.04f;
            AddBar(root.transform, new Vector3(0, height * 0.5f, 0), new Vector3(t, height, t), new Color(0.2f, 1f, 0.2f));
            AddBar(root.transform, new Vector3(0, height, 0),        Vector3.one * (t * 3f),    new Color(1f, 0.92f, 0.1f));
        }

        static void AddBar(Transform parent, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = new GameObject("bar");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.SetLocalPositionAndRotation(localPos, Quaternion.identity);
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = UnitCube();
            go.AddComponent<MeshRenderer>().material = MakeUnlit(color);
        }

        static Mesh _unitCube;
        static Mesh UnitCube()
        {
            if (_unitCube != null) return _unitCube;
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
            _unitCube = new Mesh { vertices = v, triangles = t };
            _unitCube.RecalculateNormals();
            _unitCube.RecalculateBounds();
            return _unitCube;
        }

        static Material MakeUnlit(Color c)
        {
            var shader = Shader.Find("Mortuorium/OriginGizmo");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var m = new Material(shader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
            return m;
        }

        // ── On-screen UI ──────────────────────────────────────────────────────

        void OnGUI()
        {
            EnsureStyle();

            switch (_state)
            {
                case ScanState.Bootstrap:
                    DrawPrompt(BootstrapStatus(), new Color(1f, 0.85f, 0.2f));
                    break;

                case ScanState.ScanBottom:
                    DrawPrompt("GRAB THE MARKER AND DRAG IT ONTO THE BOTTOM CORNER\n" +
                               "1 finger = slide on floor · 2 fingers = up/down", new Color(1f, 0.85f, 0.2f));
                    if (_bottomMarker != null && Button(0.30f, 0.88f, 0.40f, 0.08f, "CONFIRM CORNER"))
                        ConfirmBottomCorner();
                    break;

                case ScanState.ScanTop:
                    float h = _topMarker != null && _mapOrigin != null
                        ? _topMarker.position.y - _mapOrigin.position.y : 0f;
                    DrawPrompt($"DRAG THE TOP MARKER UP/DOWN TO THE CEILING CORNER\n" +
                               $"height = {h:F2} m", new Color(0.4f, 1f, 0.6f));
                    if (_topMarker != null && Button(0.30f, 0.88f, 0.40f, 0.08f, "CONFIRM TOP"))
                        ConfirmTopCorner();
                    DrawAdjustOriginToggle();
                    break;

                case ScanState.ReviewOrigin:
                    DrawPrompt("REVIEW THE ORIGIN\n" +
                               "1 finger = slide on floor · 2 fingers = up/down · then CONFIRM",
                               new Color(1f, 0.6f, 0.9f));
                    if (Button(0.30f, 0.88f, 0.40f, 0.08f, "CONFIRM ORIGIN"))
                        ConfirmReviewedOrigin();
                    break;

                case ScanState.ChooseMode:
                    DrawPrompt("CHOOSE A DETECTION MODE, THEN CONFIRM", new Color(0.6f, 0.9f, 1f));
                    DrawWallSourceToggle();
                    if (Button(0.30f, 0.88f, 0.40f, 0.08f, "CONFIRM MODE"))
                        ConfirmChosenMode();
                    break;

                case ScanState.Mapping:
                    // Editing and origin-adjust are mutually exclusive: both drag the
                    // same touch, so only one may offer its controls at a time.
                    if (_editingWalls)
                    {
                        DrawEditWallsToggle();
                        DrawPrompt(wallEditor.Prompt(), new Color(1f, 0.9f, 0.3f));
                        break;
                    }
                    // The tuning panel fills the right half; hide anything that would sit
                    // under it so the two menus never overlap.
                    bool tuneOpen = tuningMenu != null && tuningMenu.Open;
                    if (!tuneOpen) DrawAdjustOriginToggle();
                    if (!_adjustingOrigin)
                    {
                        bool floorSweepPending = occupancyMapper != null
                            && DepthOccupancyMapper.IsFloorMode(occupancyMapper.WallSource)
                            && !_floorSweepConfirmed;
                        if (floorSweepPending)
                        {
                            // Floor modes build once, deliberately, from a finished sweep —
                            // no BUILD/EXPORT/EDIT buttons until there is anything to act on.
                            DrawPrompt("SWEEP THE FLOOR YOU WANT TO MAP, THEN CONFIRM\n" +
                                       "walk its full perimeter first", new Color(0.6f, 0.9f, 1f), tuneOpen);
                            if (Button(0.05f, 0.23f, 0.40f, 0.07f,
                                       _mappingPaused ? "RESUME SCAN" : "PAUSE SCAN"))
                                _mappingPaused = !_mappingPaused;
                            if (Button(0.30f, 0.88f, 0.40f, 0.08f, "CONFIRM FLOOR MAPPED"))
                            {
                                _floorSweepConfirmed = true;
                                // Walls only — floor mode is specifically about walls, not
                                // furniture-top detection. BUILD SURFACES/BUILD ROOM are
                                // available afterward (normal HUD) if surfaces are wanted too.
                                occupancyMapper?.Reconstruct(_mapOrigin, _roomHeight);
                                AutoSave();
                            }
                            if (tuningMenu != null && Button(0.05f, 0.31f, 0.40f, 0.07f,
                                                             tuningMenu.Open ? "CLOSE TUNE" : "TUNE"))
                                tuningMenu.Open = !tuningMenu.Open;
                            if (!tuneOpen) DrawWallSourceToggle();
                            break;
                        }

                        int cells = occupancyMapper != null ? occupancyMapper.OccupiedCells : 0;
                        string walls = occupancyMapper != null
                            ? $" | walls {occupancyMapper.LastSeededWalls} seeded / {occupancyMapper.LastDiscoveredWalls} found"
                            : "";
                        string height = _roomHeight > 0.1f ? $"{_roomHeight:F2} m" : "measuring…";
                        string saved = _lastSaveTime >= 0f ? " | saved" : "";
                        string reloc = string.IsNullOrEmpty(_relocalizeStatus) ? "" : $"\n{_relocalizeStatus}";
                        string mode = _mappingPaused ? "PAUSED"
                            : !autoBuild ? "manual build"
                            : autoBuildSurfaces ? "auto walls+surfaces" : "auto walls";
                        DrawPrompt($"WALK THE ROOM TO SCAN THE WALLS\n" +
                                   $"solid cells: {cells}{walls} | height {height} | {mode}{saved}{reloc}",
                                   new Color(0.6f, 0.9f, 1f), tuneOpen);
                        if (Button(0.05f, 0.23f, 0.40f, 0.07f,
                                   _mappingPaused ? "RESUME SCAN" : "PAUSE SCAN"))
                            _mappingPaused = !_mappingPaused;
                        // The buttons still force a rebuild now — auto-build makes them optional.
                        if (Button(0.05f, 0.47f, 0.40f, 0.07f, "BUILD ROOM"))
                            BuildRoom();
                        if (Button(0.05f, 0.55f, 0.40f, 0.07f, "BUILD WALLS"))
                        {
                            occupancyMapper?.Reconstruct(_mapOrigin, _roomHeight);
                            AutoSave();
                        }
                        if (Button(0.05f, 0.63f, 0.40f, 0.07f, "BUILD SURFACES"))
                        {
                            occupancyMapper?.ReconstructSurfaces(_mapOrigin, _roomHeight);
                            AutoSave();
                        }
                        if (Debug.isDebugBuild && Button(0.05f, 0.71f, 0.40f, 0.07f, "EXPORT MAP"))
                            FinishAndExport();
                        // Separate from EXPORT MAP: that ends the scan and writes this
                        // project's own archive; this writes the game's scan format and
                        // leaves you scanning.
                        if (Button(0.05f, 0.39f, 0.40f, 0.07f, "GUARDAR Y EDITAR"))
                            scanExporter?.ExportAndOpenInScanner();
                        if (tuningMenu != null && Button(0.05f, 0.31f, 0.40f, 0.07f,
                                                         tuningMenu.Open ? "CLOSE TUNE" : "TUNE"))
                            tuningMenu.Open = !tuningMenu.Open;
                        if (!tuneOpen)
                        {
                            DrawWallSourceToggle();
                            DrawRelocalizeButton();
                            DrawEditWallsToggle();
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Offers alignment onto a previously saved room. Only shown once the live scan has
        /// walls of its own — there is nothing to match against before that.
        /// </summary>
        void DrawRelocalizeButton()
        {
            if (jsonExporter == null || !jsonExporter.HasSavedRoom) return;
            if (roomBuilder == null || roomBuilder.Room.walls.Count == 0) return;

            if (roomBuilder.HasRestoredRoom)
            {
                if (Button(0.60f, 0.47f, 0.35f, 0.07f, "HIDE SAVED ROOM"))
                {
                    roomBuilder.ClearRestored();
                    _relocalizeStatus = null;
                }
                return;
            }

            if (Button(0.60f, 0.47f, 0.35f, 0.07f, "LOAD SAVED ROOM"))
                Relocalize();
        }

        /// <summary>
        /// What the bootstrap is still waiting for. Without this a stall looks identical to
        /// a hang on device — the user needs to know whether to find a floor or a wall.
        /// </summary>
        string BootstrapStatus()
        {
            if (planeCollector.LargestFloor() == null)
                return "POINT AT THE FLOOR\nlooking for a floor plane…";

            if (_bootstrapFix == OriginFix.None)
                return "FLOOR OK — NOW POINT AT A WALL\nlooking for a wall to align to…";

            return $"{_bootstrapFix.ToString().ToUpper()} FOUND — HOLD STEADY\n" +
                   $"locking {_bootstrapHits}/{originStableCount}";
        }

        /// <summary>
        /// Cycles where wall lines come from. The occupancy grid is untouched, so all three
        /// modes can be compared against one accumulated scan of the same physical room by
        /// pressing BUILD WALLS again after each switch — no rebuild, no second walk.
        /// </summary>
        void DrawWallSourceToggle()
        {
            if (occupancyMapper == null) return;

            var mode = occupancyMapper.WallSource;
            if (Button(0.60f, 0.55f, 0.35f, 0.07f, $"MODE: {ModeLabel(mode)}"))
            {
                var next = NextMode(mode);
                occupancyMapper.SetWallSource(next);
                // Each mode is its own clean playground — mixing one mode's fitted geometry
                // into another's attempt would defeat comparing them. Origin/anchor survive.
                ResetScan();
                _floorSweepConfirmed = false;
                Debug.Log($"[RoomScan] Wall source → {ModeLabel(next)}; scan reset (origin kept).");
            }
        }

        static string ModeLabel(WallSourceMode m) => m switch
        {
            WallSourceMode.DepthOnly       => "DEPTH",
            WallSourceMode.PlanesOnly      => "PLANES",
            WallSourceMode.FloorRaw        => "FLOOR",
            WallSourceMode.FloorConfirmed  => "FLOOR+DEPTH",
            _                              => "HYBRID",
        };

        static WallSourceMode NextMode(WallSourceMode m) => m switch
        {
            WallSourceMode.Hybrid         => WallSourceMode.DepthOnly,
            WallSourceMode.DepthOnly      => WallSourceMode.PlanesOnly,
            WallSourceMode.PlanesOnly     => WallSourceMode.FloorRaw,
            WallSourceMode.FloorRaw       => WallSourceMode.FloorConfirmed,
            _                             => WallSourceMode.Hybrid,
        };

        void DrawAdjustOriginToggle()
        {
            if (_mapOrigin == null) return;
            string label = _adjustingOrigin ? "DONE ADJUSTING" : "ADJUST ORIGIN";
            if (Button(0.60f, 0.70f, 0.35f, 0.07f, label))
            {
                _adjustingOrigin = !_adjustingOrigin;
                _grabbing = false;
            }
            if (_adjustingOrigin)
                DrawPrompt("GRAB THE ORIGIN AND DRAG TO CORRECT THE WHOLE MAP\n" +
                           "1 finger = floor · 2 fingers = up/down", new Color(1f, 0.6f, 0.9f));
        }

        /// <summary>
        /// Enters/leaves manual wall correction. Sits in the free right-column slot between
        /// MODE: and ADJUST ORIGIN. Only offered once there are walls to correct.
        /// </summary>
        void DrawEditWallsToggle()
        {
            if (wallEditor == null || _mapOrigin == null) return;
            if (!_editingWalls && (roomBuilder == null || roomBuilder.Room.walls.Count == 0)) return;

            string label = _editingWalls ? "DONE EDITING" : "EDIT WALLS";
            if (!Button(0.60f, 0.63f, 0.35f, 0.07f, label)) return;

            _editingWalls = !_editingWalls;
            _grabbing = false; // drop any half-finished origin grab, as ADJUST ORIGIN does

            if (_editingWalls)
            {
                _adjustingOrigin = false;
                wallEditor.Activate(_mapOrigin, _roomHeight);
            }
            else
            {
                wallEditor.Deactivate();
                AutoSave();
            }
        }

        void DrawPrompt(string msg, Color color, bool narrow = false)
        {
            GUI.color = color;
            GUI.Label(SR(0.05f, 0.78f, narrow ? 0.42f : 0.90f, 0.16f), msg, _style);
            GUI.color = Color.white;
        }

        bool Button(float x, float y, float w, float h, string label)
            => GUI.Button(SR(x, y, w, h), label);

        void EnsureStyle()
        {
            if (_style != null) return;
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize  = Mathf.RoundToInt(Screen.height * 0.028f),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap  = true
            };
        }

        static Rect SR(float x, float y, float w, float h) =>
            new Rect(Screen.width * x, Screen.height * y, Screen.width * w, Screen.height * h);
    }
}
