using System.Collections.Generic;
using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>Where wall lines come from during <see cref="DepthOccupancyMapper.Reconstruct"/>.</summary>
    public enum WallSourceMode
    {
        /// <summary>Blind RANSAC over the occupancy grid only. Accurate but needs a long sweep.</summary>
        DepthOnly,
        /// <summary>ARCore vertical planes trusted outright. Fast, but phantom-prone — kept for A/B.</summary>
        PlanesOnly,
        /// <summary>Planes propose wall lines; the occupancy grid must confirm them. Blind RANSAC still runs on the remainder.</summary>
        Hybrid,
        /// <summary>The floor plane's own boundary edges trusted outright, no depth check. Experimental — for A/B against the others.</summary>
        FloorRaw,
        /// <summary>Floor boundary edges propose wall lines; the occupancy grid must confirm them, same bar as Hybrid. Experimental.</summary>
        FloorConfirmed,
    }

    /// <summary>
    /// Automatic wall reconstruction from accumulated environment depth.
    ///
    /// Per-frame wall fitting is noisy and produces phantom walls. Instead this
    /// accumulates the dense depth stream across the whole scan into a 2D floor-plan
    /// occupancy grid — keeping only near-vertical points inside the wall-height band
    /// (above the floor, below the ceiling). A grid cell becomes "solid" once it has
    /// been hit X times (X low, as requested), which denoises transient points.
    ///
    /// On <see cref="Reconstruct"/>, the solid cells are fitted into straight wall
    /// segments and <see cref="RoomBuilder"/> extrudes each into a solid wall box.
    /// Fitting runs in two passes:
    ///
    /// 1. <b>Seeded</b> — each ARCore vertical plane proposes a candidate line, which is
    ///    only accepted if enough occupancy cells actually lie along it. Discovering a
    ///    line blind takes many accumulated frames; confirming one the plane already
    ///    oriented takes far fewer, so this is what makes a short sweep viable. The
    ///    confirmation test is also what keeps ARCore's phantom walls out.
    /// 2. <b>Discovery</b> — the original blind multi-line RANSAC, over the cells the
    ///    seeds did not claim. This still catches blank walls ARCore cannot see.
    ///
    /// <see cref="WallSourceMode"/> selects which passes run, for on-device A/B.
    /// </summary>
    [RequireComponent(typeof(CornerDetector))]
    public class DepthOccupancyMapper : MonoBehaviour
    {
        [SerializeField] CornerDetector cornerDetector;
        [SerializeField] RoomBuilder roomBuilder;

        [Header("Occupancy grid")]
        [Tooltip("Floor-plan cell size (m).")]
        [SerializeField] float cellSize = 0.08f;
        [Tooltip("Only count points at least this far above the floor (m).")]
        [SerializeField] float bandBottom = 0.30f;
        [Tooltip("Stop the wall band this far below the ceiling (m).")]
        [SerializeField] float bandTopMargin = 0.20f;
        [Tooltip("|normal.y| below this ⇒ vertical surface (wall). Lower = stricter.")]
        [SerializeField] float wallNormalMax = 0.35f;
        [Tooltip("Hits before a cell counts as solid wall (keep low).")]
        [SerializeField] int minCellHits = 3;
        [Tooltip("A wall cell must span at least this fraction of the room height — " +
                 "rejects short furniture faces (couch backs, cabinets) in the wall band.")]
        [SerializeField] float minVerticalSpanFraction = 0.45f;

        [Header("Wall fitting")]
        [Tooltip("Max distance (m) from a fitted line for a cell to be an inlier.")]
        [SerializeField] float lineInlierDist = 0.10f;
        [SerializeField] int lineIterations = 300;
        [Tooltip("Minimum occupied cells along a line to accept it as a wall.")]
        [SerializeField] int minSegmentCells = 6;
        [Tooltip("Min fraction of cells along a segment that must be occupied — rejects " +
                 "sparse phantom lines drawn through empty space.")]
        [SerializeField] float minDensityFraction = 0.35f;
        [Tooltip("Gap (m) along a fitted line that splits it into separate walls — a real " +
                 "wall is continuous, so a big gap means empty space (or a doorway).")]
        [SerializeField] float maxRunGap = 0.4f;
        [Tooltip("Regularise walls to one room frame: every near-aligned wall is rotated to " +
                 "a single shared 0°/90° orientation and near-equal offsets are pulled onto " +
                 "one line, so fragments of the same wall coincide and merge. Off = the old " +
                 "per-segment snap to the longest wall.")]
        [SerializeField] bool snapToRightAngles = true;
        [Tooltip("Only snap a wall whose angle is already within this many degrees of the frame.")]
        [SerializeField] float snapAngleTolerance = 20f;
        [Tooltip("Two wall fragments meeting end-to-end within this many degrees of dead " +
                 "straight are one physical wall, fused into a single built wall instead of " +
                 "two. Restricted to plain pass-throughs — corners, T-junctions and dividers " +
                 "are never affected.")]
        [SerializeField] float collinearMergeAngle = 10f;
        [Tooltip("Clean up obviously-wrong automatic walls before building: drop ones diagonal " +
                 "to the room frame, and keep only the longer of two near-collinear overlapping " +
                 "segments of the same wall. Off for non-orthogonal rooms.")]
        [SerializeField] bool dropUnattachedWalls = true;
        [Tooltip("Ignore new detection in a band beside a wall built last pass, so a phantom " +
                 "cannot spawn from its noisy occupancy ridge. A thin core on the wall line " +
                 "is kept so the wall still refits.")]
        [SerializeField] bool claimBuiltWallFootprint = true;
        [Tooltip("Half-width (m) of the ignored band either side of a wall built last pass.")]
        [SerializeField] float wallRidgeClaimMargin = 0.30f;
        [Tooltip("Half-width (m) of the core kept on the wall line so it still refits.")]
        [SerializeField] float wallRidgeCoreKeep = 0.08f;
        [Tooltip("Perpendicular offsets within this (m) of each other collapse onto one wall " +
                 "line during regularisation — absorbs the scan spread that made one wall " +
                 "come out as two parallel ones.")]
        [SerializeField] float wallOffsetCluster = 0.15f;
        [Tooltip("With regularisation on, collinear fragments up to this far apart (m) are " +
                 "one wall (door-sized gaps become openings, the rest stays solid). Without " +
                 "it, the smaller maxBridgeGap is used and a bigger gap stays two walls.")]
        [SerializeField] float coplanarMaxGap = 5.0f;
        [SerializeField] float wallThickness = 0.12f;
        [SerializeField] int maxWalls = 32;
        [Tooltip("A wall cell's column must have occupancy reaching at least this fraction " +
                 "of the room height. A real wall runs to the ceiling; a chair back, a " +
                 "cabinet or a stack of boxes tops out well below it. Lower this if walls " +
                 "you only scanned at eye level go missing.")]
        [SerializeField] float wallTopReachFraction = 0.6f;
        [Tooltip("…and at least this fraction of the column from the wall band up to that " +
                 "highest point must be occupied — a continuous vertical run, not a low " +
                 "furniture band plus a stray high speck of noise.")]
        [SerializeField] float wallColumnFillFraction = 0.45f;
        [Tooltip("Frames a voxel must be seen across before it counts toward a wall column. " +
                 "1 = any single glance; raise it so one noisy frame's flying-pixel specks " +
                 "cannot make furniture pass the reach/continuity test as a wall.")]
        [SerializeField] int wallColumnMinFrames = 2;
        [Tooltip("Margin (m) added around a built wall's box when claiming that volume: " +
                 "occupancy inside it is no longer offered to wall fitting, so a wall is " +
                 "not doubled by a parallel line from its own noisy ridge.")]
        [SerializeField] float wallClaimMargin = 0.12f;

        [Header("Plane seeding (hybrid)")]
        [Tooltip("Which passes run: depth-only (blind RANSAC), planes-only (trust ARCore), " +
                 "or hybrid (planes propose, depth confirms).")]
        [SerializeField] WallSourceMode wallSource = WallSourceMode.Hybrid;
        [SerializeField] PlaneCollector planeCollector;
        [Tooltip("Ignore vertical planes smaller than this (m²).")]
        [SerializeField] float seedMinArea = 0.20f;
        [Tooltip("Occupancy cells needed along a seed line to confirm it. Lower than " +
                 "minSegmentCells — the plane already supplies the orientation, so we are " +
                 "confirming a line rather than discovering one.")]
        [SerializeField] int seedMinCells = 4;
        [Tooltip("Min fraction of the seed's own extent that must be backed by occupancy.")]
        [SerializeField] float seedMinDensityFraction = 0.25f;
        [Tooltip("Vertical span a cell needs to count as evidence for a seed, as a fraction " +
                 "of room height. Lower than minVerticalSpanFraction: ARCore has already " +
                 "asserted a vertical surface here, so partial observation is enough.")]
        [SerializeField] float seedMinSpanFraction = 0.25f;
        [Tooltip("Drop a floor-boundary edge shorter than this (m) before it ever becomes a " +
                 "wall candidate — filters the smallest stairstep artifacts in ARCore's " +
                 "floor mesh. Used by the FloorRaw/FloorConfirmed wall sources.")]
        [SerializeField] float floorEdgeMinLength = 0.3f;
        [Tooltip("Occupancy cells needed along a floor-boundary edge to confirm it, for " +
                 "FloorConfirmed. Separate from seedMinCells (vertical planes) so trusting " +
                 "the floor more doesn't also loosen Hybrid.")]
        [SerializeField] int floorSeedMinCells = 2;
        [Tooltip("Min fraction of a floor-boundary edge's own length that must be backed by " +
                 "occupancy, for FloorConfirmed. Lower than seedMinDensityFraction by " +
                 "default — the floor boundary is already trusted geometry.")]
        [SerializeField] float floorSeedMinDensityFraction = 0.12f;
        [Tooltip("FloorConfirmed only: after confirming the floor-boundary edges, also look for " +
                 "full-height walls the floor does not outline — an interior wall or hallway " +
                 "entrance with floor continuing behind it. Off: floor edges are the only walls.")]
        [SerializeField] bool floorDepthFindsInteriorWalls = false;

        [Header("Room height (automatic ceiling estimate)")]
        [Tooltip("|normal.y| above this ⇒ horizontal surface (floor / ceiling / table top).")]
        [SerializeField] float ceilingNormalMin = 0.80f;
        [Tooltip("Height histogram bin size (m).")]
        [SerializeField] float heightBinSize = 0.05f;
        [Tooltip("Points needed in a height band before it can be called the ceiling.")]
        [SerializeField] int minCeilingHits = 40;
        [Tooltip("Accepted room height range (m) — matches CornerDetector's own limits.")]
        [SerializeField] float minRoomHeight = 1.8f;
        [SerializeField] float maxRoomHeight = 5f;

        [Header("Wall-line merge (fuse fragments of one wall)")]
        [Tooltip("Max angle (deg) between two fragments for them to share a wall line.")]
        [SerializeField] float mergeMaxAngle = 12f;
        [Tooltip("Max difference (m) in perpendicular offset-from-origin for two fragments " +
                 "to share a wall line. This is a line-to-line distance, so unlike the old " +
                 "segment-anchored test it does not degrade with wall length.")]
        [SerializeField] float mergeOffsetTolerance = 0.20f;

        [Tooltip("How much (m) an automatic wall must overlap a pinned one along their shared " +
                 "line before it counts as a duplicate and is suppressed. Stops two pieces of " +
                 "one wall either side of a doorway from cancelling each other out.")]
        [SerializeField] float pinnedOverlapMin = 0.25f;
        [Tooltip("Gap (m) along a wall line that is bridged into one wall. Larger gaps stay " +
                 "separate walls, so doorways and open passages are not sealed over. Also the " +
                 "reach of the synthetic edges that let the room outline close over openings.")]
        [SerializeField] float maxBridgeGap = 2.5f;

        [Header("Doorways")]
        [Tooltip("Cut bridged gaps in a wall as doorways. Off leaves every wall solid.")]
        [SerializeField] bool detectDoorways = true;
        [Tooltip("Narrowest bridged gap (m) accepted as a doorway. Below this it is scan " +
                 "noise or the shadow of a piece of furniture, not a door.")]
        [SerializeField] float minDoorWidth = 0.6f;
        [Tooltip("Widest bridged gap (m) accepted as a doorway. Above this it is more likely " +
                 "a missing wall section or an open-plan boundary, and cutting a hole that " +
                 "wide looks worse than leaving the wall solid.")]
        [SerializeField] float maxDoorWidth = 1.3f;
        [Tooltip("Height (m) given to a detected doorway. The floor-level occupancy grid " +
                 "carries no height information for the opening, so this is a standard door " +
                 "height rather than a measurement.")]
        [SerializeField] float doorHeight = 2.0f;

        [Header("Horizontal surfaces (spawn targets)")]
        [Tooltip("Voxel size for the 3D occupancy grid (m).")]
        [SerializeField] float voxelSize = 0.10f;
        [Tooltip("Floor of the occupancy band (m above the origin floor).")]
        [SerializeField] float furnitureBandBottom = 0.05f;
        [Tooltip("Frames a voxel must be observed across before it counts as solid. Low " +
                 "values let a passing hand or a moment of depth noise condense into a surface.")]
        [SerializeField] int minVoxelHits = 3;
        [Tooltip("Frames a voxel must be seen as horizontal before it counts as a surface " +
                 "voxel. The horizontal/vertical split comes from the point normal.")]
        [SerializeField] int minSurfaceHits = 2;
        [Tooltip("Minimum connected surface voxels for a patch to become a surface.")]
        [SerializeField] int minSurfaceVoxels = 6;
        [Tooltip("A patch must fill at least this fraction of its own XZ footprint. A real " +
                 "surface is a contiguous sheet; depth-edge noise can flood-fill into a " +
                 "patch that spans a plausible area while barely touching it.")]
        [SerializeField] float surfaceFillFraction = 0.5f;
        [Tooltip("A surface must sit at least this far above the floor (m) — below it is the floor itself.")]
        [SerializeField] float surfaceMinHeight = 0.15f;
        [Tooltip("…and no higher than this fraction of the room height — above it is the ceiling.")]
        [SerializeField] float surfaceMaxHeightFraction = 0.85f;
        [Tooltip("Smallest / largest side of a surface rectangle (m). Wider than the max is " +
                 "the floor or several surfaces merged; narrower than the min is noise.")]
        [SerializeField] float minBoxDimension = 0.15f;
        [SerializeField] float maxBoxDimension = 2.5f;
        [Tooltip("Skip voxels within this distance (m) of a built wall — they are the wall.")]
        [SerializeField] float wallExcludeMargin = 0.15f;
        [Tooltip("Skip wall cells within this distance (m) of a detected surface — stops a " +
                 "table near a wall from also being drawn as a wall.")]
        [SerializeField] float furnitureExcludeMargin = 0.10f;

        [Header("Automatic anchoring")]
        [Tooltip("Anchor an automatic wall or furniture box once it has been rebuilt this " +
                 "many times in ~the same place: it is then frozen and its cells/voxels are " +
                 "kept out of later discovery. 0 disables automatic anchoring.")]
        [SerializeField] int autoAnchorHits = 3;
        [Tooltip("How close (m) two rebuilds must land for them to count as the same wall " +
                 "or box when deciding whether to anchor.")]
        [SerializeField] float autoAnchorTolerance = 0.20f;
        [Tooltip("A wall that fails to refit for up to this many consecutive rebuilds keeps " +
                 "its anchor progress instead of resetting to zero — one occluded/noisy " +
                 "cycle should not undo several good ones. 0 reproduces the old hard reset.")]
        [SerializeField] int autoAnchorMissGrace = 1;

        [Header("Room graph (junctions + outline)")]
        [Tooltip("Snap intersecting walls to a shared corner and trace a room outline after " +
                 "fitting. Off reproduces the previous independent-segment behaviour.")]
        [SerializeField] bool buildRoomGraph = true;
        [Tooltip("Wall ends within this (m) are treated as one corner node.")]
        [SerializeField] float joinRadius = 0.35f;
        [Tooltip("Largest trim/extend (m) applied to a wall end to reach an intersection.")]
        [SerializeField] float junctionExtendMax = 0.6f;
        [Tooltip("Reject a corner between two walls whose acute angle is below this (deg).")]
        [SerializeField] float junctionPerpMinAngle = 15f;
        [Tooltip("Draw the traced outline + a post at each corner.")]
        [SerializeField] bool drawOutline = true;

        struct CellInfo { public int frames; public float minY; public float maxY; } // vertical extent per wall cell

        readonly Dictionary<(int, int), CellInfo> _hits = new();     // 2D wall occupancy
        /// <summary>
        /// Per-voxel occupancy, counted in <b>distinct frames</b>, not raw depth samples:
        /// <c>hits</c> is how many separate accumulation cycles put any point here,
        /// <c>horiz</c>/<c>vert</c> how many of those cycles saw it as a horizontal or a
        /// vertical surface (from the point normal). A single noisy frame — a specular
        /// reflection, a flying-pixel artefact at a depth edge — can drop dozens of samples
        /// into one voxel; only counting it once, the same way the wall grid already does,
        /// is what keeps that from reading as three confirmed observations.
        /// </summary>
        struct VoxInfo { public int hits; public int horiz; public int vert; }

        readonly Dictionary<(int, int, int), VoxInfo> _voxels = new();   // 3D furniture occupancy
        readonly HashSet<(int, int)> _frameCells = new();            // wall cells seen this frame
        /// <summary>Voxels touched this frame, and whether by a horizontal/vertical point —
        /// folded into <see cref="_voxels"/> at most once per voxel per frame.</summary>
        readonly Dictionary<(int, int, int), (bool horiz, bool vert)> _frameVoxels = new();
        int[] _heightBins;                                           // horizontal-surface heights

        // Cross-rebuild stability, for automatic anchoring: how many consecutive rebuilds
        // an automatic wall / box has landed in the same place. `side` is carried so a
        // wall's extrusion direction does not flip as the camera moves around it.
        readonly List<(Vector2 a, Vector2 b, int hits, int side, int misses)> _wallSeen = new();
        readonly List<(Vector3 center, Vector2 size, int hits)> _surfSeen = new();

        /// <summary>Most recent room height passed to a reconstruction, for helpers that
        /// run outside the call that has it (e.g. <see cref="OnWallColumn"/>). 0 until set.</summary>
        float _roomHeightHint;

        /// <summary>Room-frame orientation (deg, 0..90), low-passed across rebuilds so it
        /// does not jitter. -1000 = not yet established.</summary>
        float _roomFrameAngle = -1000f;

        public int OccupiedCells
        {
            get
            {
                int n = 0;
                foreach (var kv in _hits) if (kv.Value.frames >= minCellHits) n++;
                return n;
            }
        }

        /// <summary>Walls confirmed from a plane seed by the last <see cref="Reconstruct"/>.</summary>
        public int LastSeededWalls { get; private set; }

        /// <summary>Walls found by blind RANSAC in the last <see cref="Reconstruct"/>.</summary>
        public int LastDiscoveredWalls { get; private set; }

        public WallSourceMode WallSource => wallSource;
        public void SetWallSource(WallSourceMode mode) => wallSource = mode;

        /// <summary>True for the two floor-boundary sources, which run a deliberate
        /// sweep-then-build-once flow instead of the timed auto-rebuild the other three
        /// modes use — see <c>RoomScanningManager</c>'s floor-sweep gating.</summary>
        public static bool IsFloorMode(WallSourceMode m)
            => m == WallSourceMode.FloorRaw || m == WallSourceMode.FloorConfirmed;

        /// <summary>The detection thresholds the runtime tuning menu can move. A transfer
        /// object only — the real storage stays the serialized fields, so the scene remains
        /// the source of truth.</summary>
        [System.Serializable]
        public struct Tuning
        {
            public float wallNormalMax, bandBottom, bandTopMargin;
            public int minCellHits;
            public float minVerticalSpanFraction, wallTopReachFraction, wallColumnFillFraction;
            public int wallColumnMinFrames, minSegmentCells;
            public float minDensityFraction, maxRunGap, maxBridgeGap, collinearMergeAngle;
            public bool dropUnattachedWalls, claimBuiltWallFootprint;
            public float wallRidgeClaimMargin, wallRidgeCoreKeep;
            public int autoAnchorHits, autoAnchorMissGrace;
            public float autoAnchorTolerance;
            public float floorEdgeMinLength;
            public int floorSeedMinCells;
            public float floorSeedMinDensityFraction;
            public bool floorDepthFindsInteriorWalls;
            public int minVoxelHits, minSurfaceHits;
            public float surfaceFillFraction;
            public int minSurfaceVoxels;
            public float surfaceMinHeight, surfaceMaxHeightFraction;
        }

        /// <summary>Compile-time defaults, kept beside the field declarations so the two stay
        /// in sync. The scene overrides these on load; this is the menu's DEFAULTS button.</summary>
        public static Tuning DefaultTuning() => new Tuning
        {
            wallNormalMax = 0.35f, bandBottom = 0.30f, bandTopMargin = 0.20f,
            minCellHits = 3,
            minVerticalSpanFraction = 0.45f, wallTopReachFraction = 0.6f, wallColumnFillFraction = 0.45f,
            wallColumnMinFrames = 2, minSegmentCells = 6,
            minDensityFraction = 0.35f, maxRunGap = 0.4f, maxBridgeGap = 2.5f, collinearMergeAngle = 10f,
            dropUnattachedWalls = true, claimBuiltWallFootprint = true,
            wallRidgeClaimMargin = 0.30f, wallRidgeCoreKeep = 0.08f,
            autoAnchorHits = 3, autoAnchorTolerance = 0.20f, autoAnchorMissGrace = 1,
            floorEdgeMinLength = 0.3f, floorSeedMinCells = 2, floorSeedMinDensityFraction = 0.12f,
            floorDepthFindsInteriorWalls = false,
            minVoxelHits = 3, minSurfaceHits = 2,
            surfaceFillFraction = 0.5f,
            minSurfaceVoxels = 6,
            surfaceMinHeight = 0.15f, surfaceMaxHeightFraction = 0.85f,
        };

        public Tuning ReadTuning() => new Tuning
        {
            wallNormalMax = wallNormalMax, bandBottom = bandBottom, bandTopMargin = bandTopMargin,
            minCellHits = minCellHits,
            minVerticalSpanFraction = minVerticalSpanFraction, wallTopReachFraction = wallTopReachFraction,
            wallColumnFillFraction = wallColumnFillFraction,
            wallColumnMinFrames = wallColumnMinFrames, minSegmentCells = minSegmentCells,
            minDensityFraction = minDensityFraction, maxRunGap = maxRunGap, maxBridgeGap = maxBridgeGap,
            collinearMergeAngle = collinearMergeAngle,
            dropUnattachedWalls = dropUnattachedWalls, claimBuiltWallFootprint = claimBuiltWallFootprint,
            wallRidgeClaimMargin = wallRidgeClaimMargin, wallRidgeCoreKeep = wallRidgeCoreKeep,
            autoAnchorHits = autoAnchorHits, autoAnchorTolerance = autoAnchorTolerance,
            autoAnchorMissGrace = autoAnchorMissGrace,
            floorEdgeMinLength = floorEdgeMinLength,
            floorSeedMinCells = floorSeedMinCells, floorSeedMinDensityFraction = floorSeedMinDensityFraction,
            floorDepthFindsInteriorWalls = floorDepthFindsInteriorWalls,
            minVoxelHits = minVoxelHits, minSurfaceHits = minSurfaceHits,
            surfaceFillFraction = surfaceFillFraction,
            minSurfaceVoxels = minSurfaceVoxels,
            surfaceMinHeight = surfaceMinHeight, surfaceMaxHeightFraction = surfaceMaxHeightFraction,
        };

        public void WriteTuning(Tuning t)
        {
            wallNormalMax = t.wallNormalMax; bandBottom = t.bandBottom; bandTopMargin = t.bandTopMargin;
            minCellHits = t.minCellHits;
            minVerticalSpanFraction = t.minVerticalSpanFraction;
            wallTopReachFraction = t.wallTopReachFraction;
            wallColumnFillFraction = t.wallColumnFillFraction;
            wallColumnMinFrames = t.wallColumnMinFrames; minSegmentCells = t.minSegmentCells;
            minDensityFraction = t.minDensityFraction; maxRunGap = t.maxRunGap; maxBridgeGap = t.maxBridgeGap;
            collinearMergeAngle = t.collinearMergeAngle;
            dropUnattachedWalls = t.dropUnattachedWalls; claimBuiltWallFootprint = t.claimBuiltWallFootprint;
            wallRidgeClaimMargin = t.wallRidgeClaimMargin; wallRidgeCoreKeep = t.wallRidgeCoreKeep;
            autoAnchorHits = t.autoAnchorHits; autoAnchorTolerance = t.autoAnchorTolerance;
            autoAnchorMissGrace = t.autoAnchorMissGrace;
            floorEdgeMinLength = t.floorEdgeMinLength;
            floorSeedMinCells = t.floorSeedMinCells; floorSeedMinDensityFraction = t.floorSeedMinDensityFraction;
            floorDepthFindsInteriorWalls = t.floorDepthFindsInteriorWalls;
            minVoxelHits = t.minVoxelHits; minSurfaceHits = t.minSurfaceHits;
            surfaceFillFraction = t.surfaceFillFraction;
            minSurfaceVoxels = t.minSurfaceVoxels;
            surfaceMinHeight = t.surfaceMinHeight; surfaceMaxHeightFraction = t.surfaceMaxHeightFraction;
        }

        void Awake()
        {
            if (cornerDetector == null) cornerDetector = GetComponent<CornerDetector>();
            if (roomBuilder == null)    roomBuilder    = GetComponent<RoomBuilder>();
            if (planeCollector == null) planeCollector = GetComponent<PlaneCollector>();

            _heightBins = new int[Mathf.CeilToInt(maxRoomHeight / heightBinSize) + 1];
        }

        public void ResetGrid()
        {
            _hits.Clear();
            _voxels.Clear();
            _wallSeen.Clear();
            _surfSeen.Clear();
            _roomFrameAngle = -1000f;
            if (_heightBins != null) System.Array.Clear(_heightBins, 0, _heightBins.Length);
        }

        /// <summary>
        /// Height of the ceiling above the map origin, from horizontal surfaces observed
        /// during <see cref="Accumulate"/>.
        ///
        /// Takes the <em>highest</em> band with real support rather than a percentile: a
        /// percentile over all points is dominated by the walls and lands partway up them,
        /// whereas the ceiling is by definition the topmost horizontal surface that many
        /// points agree on.
        /// </summary>
        public bool TryEstimateRoomHeight(out float height)
        {
            height = 0f;
            if (_heightBins == null) return false;

            for (int b = _heightBins.Length - 1; b >= 0; b--)
            {
                if (_heightBins[b] < minCeilingHits) continue;

                float h = (b + 0.5f) * heightBinSize;
                if (h < minRoomHeight || h > maxRoomHeight) return false;

                height = h;
                return true;
            }

            return false;
        }

        /// <summary>Accumulate one cycle of depth into both occupancy grids (origin-local).</summary>
        public void Accumulate(Transform mapOrigin, float roomHeight)
        {
            if (mapOrigin == null || cornerDetector == null) return;

            var w2o = mapOrigin.worldToLocalMatrix;
            float top = roomHeight > 0.1f ? roomHeight - bandTopMargin : float.MaxValue;

            // Resolved walls (anchored or hand-corrected): their box is off-limits for new
            // wall evidence, so a finished wall is not re-detected or doubled.
            var claimed = roomBuilder != null ? roomBuilder.Room.walls.FindAll(w => w.pinned) : null;
            bool hasClaimed = claimed != null && claimed.Count > 0;

            _frameCells.Clear();
            _frameVoxels.Clear();
            cornerDetector.SampleDepth((wp, n) =>
            {
                var lp = w2o.MultiplyPoint3x4(wp);
                var ln = w2o.MultiplyVector(n);

                // Ceiling evidence: horizontal surfaces, binned by height. Deliberately
                // before the `top` cutoff below, so the estimate can still be revised
                // upward once a room height is in use.
                if (lp.y > bandBottom && Mathf.Abs(ln.y) > ceilingNormalMin && _heightBins != null)
                {
                    int b = Mathf.FloorToInt(lp.y / heightBinSize);
                    if (b >= 0 && b < _heightBins.Length) _heightBins[b]++;
                }

                if (lp.y > top) return; // above the wall/furniture band (ceiling)

                // 3D furniture occupancy: every in-room point above the floor, tagged by
                // whether it sits on a horizontal or a vertical surface. Folded into
                // _voxels once per frame below — a single noisy frame must not look like
                // several independent confirmations.
                if (lp.y >= furnitureBandBottom)
                {
                    var vkey = Voxel(lp);
                    bool isHoriz = Mathf.Abs(ln.y) >= ceilingNormalMin;
                    bool isVert = !isHoriz && Mathf.Abs(ln.y) <= wallNormalMax;
                    _frameVoxels.TryGetValue(vkey, out var flags);
                    _frameVoxels[vkey] = (flags.horiz || isHoriz, flags.vert || isVert);
                }

                // 2D wall occupancy: near-vertical points in the wall band only.
                if (lp.y < bandBottom) return;
                if (Mathf.Abs(ln.y) > wallNormalMax) return;
                if (hasClaimed && InAnyClaimedWall(lp, claimed)) return;

                var cell = Cell(lp);
                if (!_hits.TryGetValue(cell, out var info))
                    info = new CellInfo { frames = 0, minY = lp.y, maxY = lp.y };
                if (lp.y < info.minY) info.minY = lp.y;   // track vertical extent (per point)
                if (lp.y > info.maxY) info.maxY = lp.y;
                _hits[cell] = info;
                _frameCells.Add(cell);                    // frame dedupe
            });

            // A cell counts at most once per frame, so minCellHits = number of distinct
            // frames it was observed — single-frame noise clusters never accumulate.
            foreach (var c in _frameCells)
            {
                var info = _hits[c];
                info.frames++;
                _hits[c] = info;
            }

            // Same discipline for voxels: minVoxelHits / minSurfaceHits count frames, not
            // raw samples, so a single glance full of depth noise cannot pass on its own.
            foreach (var kv in _frameVoxels)
            {
                _voxels.TryGetValue(kv.Key, out var vi);
                vi.hits++;
                if (kv.Value.horiz) vi.horiz++;
                if (kv.Value.vert) vi.vert++;
                _voxels[kv.Key] = vi;
            }
        }

        /// <summary>Fit the accumulated occupancy into wall boxes via RoomBuilder. Returns walls built.</summary>
        public int Reconstruct(Transform mapOrigin, float roomHeight)
        {
            if (roomBuilder == null) return 0;

            float h = roomHeight > 0.1f ? roomHeight : 2.4f;
            _roomHeightHint = h;

            // Strict evidence: a cell must be seen across enough frames AND span enough of
            // the room height — the height test is what rejects furniture faces in the wall
            // band. This is the set blind discovery works from, where nothing is known yet.
            var pts = CollectCells(h * minVerticalSpanFraction, h);

            LastSeededWalls = 0;
            LastDiscoveredWalls = 0;
            var segments = new List<(Vector2 a, Vector2 b)>();
            // Index-parallel with segments: the bridged gaps swallowed by each merged wall.
            var gapsPerSeg = new List<List<(Vector2 a, Vector2 b)>>();

            // Experimental: the floor plane's own boundary edges, instead of vertical planes
            // or blind RANSAC — a pure source each, for a clean A/B against the three above.
            if (IsFloorMode(wallSource))
            {
                var edges = planeCollector != null
                    ? planeCollector.FloorBoundaryEdges(mapOrigin, floorEdgeMinLength)
                    : new List<(Vector2 a, Vector2 b)>();

                if (wallSource == WallSourceMode.FloorRaw)
                {
                    segments.AddRange(edges);
                    LastSeededWalls = edges.Count;
                }
                else
                {
                    var ptsLoose = CollectCells(h * seedMinSpanFraction, h);
                    LastSeededWalls = SeedFloorBoundary(edges, ptsLoose, segments);

                    // The floor polygon has no edge where floor continues behind a wall, so
                    // those walls are only visible to depth: discover them from the strict
                    // full-height cells the floor-edge walls did not already explain.
                    if (floorDepthFindsInteriorWalls)
                    {
                        var floorWalls = new List<(Vector2 a, Vector2 b)>(segments);
                        pts.RemoveAll(p => NearAnySegment(p, floorWalls, Mathf.Max(lineInlierDist, wallRidgeClaimMargin)));
                        LastDiscoveredWalls = DiscoverWalls(pts, segments);
                    }
                }
            }
            else
            {
                // 1) Seeded pass — vertical planes propose lines, occupancy confirms them.
                if (wallSource != WallSourceMode.DepthOnly)
                {
                    // Looser evidence set: a plane has already asserted a vertical surface
                    // here, so a cell need not span the full wall height to corroborate it.
                    var ptsLoose = CollectCells(h * seedMinSpanFraction, h);
                    LastSeededWalls = SeedWalls(mapOrigin, ptsLoose, pts, segments);
                }

                // 2) Discovery pass — blind RANSAC over the cells no seed claimed. This is
                //    what still catches blank walls ARCore cannot see.
                if (wallSource != WallSourceMode.PlanesOnly)
                    LastDiscoveredWalls = DiscoverWalls(pts, segments);
            }

            if (segments.Count == 0)
            {
                // Nothing fitted this pass — but a fully hand-corrected room still has a
                // shape worth tracing from its pinned walls.
                var pinnedOnly = PinnedWallSegments();
                if (buildRoomGraph && pinnedOnly.Count >= 2)
                {
                    roomBuilder.ClearWalls(keepPinned: true);   // also clears stale corners/outline
                    roomBuilder.SetDrawOutline(drawOutline);
                    var g0 = RoomGraph.Build(new List<(Vector2 a, Vector2 b)>(), pinnedOnly, GraphSettings());
                    EmitCornersAndOutline(g0, System.Array.Empty<int>());
                }
                else
                {
                    roomBuilder.SetCorners(null);
                    roomBuilder.SetRoomOutline(null, false);
                }
                Debug.Log($"[OccupancyMapper] {pts.Count} solid cells, no walls fitted ({wallSource}) — keep scanning.");
                return 0;
            }

            // Hand-corrected walls are kept: the user has already told us where they are,
            // and refitting them from the grid would throw that away.
            roomBuilder.ClearWalls(keepPinned: true);

            // 1b) Regularise to one room frame (rooms are mostly rectilinear), so fragments
            //     of the same wall come out at the same angle and offset and then fuse.
            if (snapToRightAngles && segments.Count > 0)
                RegularizeToRoomFrame(segments);

            // 2) Fuse fragments that lie on the same wall line. After regularisation a
            //    collinear group is genuinely one wall, so bridge across large gaps too.
            int beforeMerge = segments.Count;
            MergeCollinear(segments, gapsPerSeg, snapToRightAngles ? coplanarMaxGap : maxBridgeGap);

            // 2a) Drop fragments that stayed diagonal to the room frame — regularisation
            //     already refused to snap them, so in a rectilinear room they are misfits
            //     off a real wall's noisy occupancy ridge, not walls.
            int skippedLoose = 0;
            if (dropUnattachedWalls && snapToRightAngles && _roomFrameAngle > -900f)
                for (int i = segments.Count - 1; i >= 0; i--)
                    if (IsDiagonalToFrame(segments[i]))
                    {
                        segments.RemoveAt(i);
                        if (i < gapsPerSeg.Count) gapsPerSeg.RemoveAt(i);
                        skippedLoose++;
                    }

            // 2c) A real wall sometimes survives regularisation as two near-collinear
            //     overlapping segments that MergeCollinear did not fuse — both would build,
            //     and one often snaps inward at a junction. Keep the longer of each pair.
            int dupWalls = dropUnattachedWalls ? DedupeOverlappingWalls(segments, gapsPerSeg) : 0;

            // 2b) Room graph — snap intersecting walls to shared corners, fuse collinear
            //     pass-throughs, trace the outline. Gaps follow every fused wall's sources.
            RoomGraph graph = null;
            if (buildRoomGraph)
            {
                roomBuilder.SetDrawOutline(drawOutline);
                graph = RoomGraph.Build(segments, PinnedWallSegments(), GraphSettings());

                // A fused wall (see RoomGraph.MergeCollinearPassThroughs) carries every
                // fragment's doorway candidates forward, unioned, so a doorway that landed
                // on either half of a straight pass-through still gets cut.
                var newGaps = new List<List<(Vector2 a, Vector2 b)>>(graph.Segments.Count);
                for (int i = 0; i < graph.Segments.Count; i++)
                {
                    List<(Vector2 a, Vector2 b)> merged = null;
                    foreach (int src in graph.SourceIndices[i])
                    {
                        if (src < 0 || src >= gapsPerSeg.Count || gapsPerSeg[src] == null) continue;
                        merged ??= new List<(Vector2 a, Vector2 b)>();
                        merged.AddRange(gapsPerSeg[src]);
                    }
                    newGaps.Add(merged);
                }
                segments.Clear(); segments.AddRange(graph.Segments);
                gapsPerSeg.Clear(); gapsPerSeg.AddRange(newGaps);
            }

            // 3) Build a box per merged wall, skipping any that just re-states a wall the
            //    user already placed by hand — otherwise every rebuild drops the original
            //    mis-detected wall straight back on top of the correction.
            int built = 0, suppressed = 0, anchored = 0, doorsCut = 0, doorsRejected = 0;
            var seenNext = new List<(Vector2 a, Vector2 b, int hits, int side, int misses)>();
            var seenMatched = new bool[_wallSeen.Count];
            var builtIdBySeg = new int[segments.Count];
            for (int i = 0; i < builtIdBySeg.Length; i++) builtIdBySeg[i] = -1;
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                if (DuplicatesPinnedWall(s.a, s.b)) { suppressed++; continue; }

                var gaps = i < gapsPerSeg.Count ? gapsPerSeg[i] : null;
                var openings = DoorwaysFor(s.a, s.b, gaps, h, out int rejected);
                doorsRejected += rejected;
                doorsCut += openings != null ? openings.Count : 0;

                // Stability across rebuilds: a wall that keeps landing in the same place is
                // anchored — frozen, and from now on suppressed as a pinned duplicate. The
                // side is reused once known, so the box does not flip across the line as
                // the camera moves around the wall.
                var (prevHits, prevSide, matchedIdx) = WallSeenLookup(s.a, s.b);
                if (matchedIdx >= 0) seenMatched[matchedIdx] = true;
                int hits = prevHits + 1;
                bool anchor = autoAnchorHits > 0 && hits >= autoAnchorHits;

                var a3 = new Vector3(s.a.x, 0f, s.a.y);
                var b3 = new Vector3(s.b.x, 0f, s.b.y);
                int id = roomBuilder.BuildWallBox(a3, b3, h, wallThickness, openings, anchor, prevSide);
                builtIdBySeg[i] = id;
                if (id < 0) continue; // build refused (no origin / no shader)
                built++;

                int side = roomBuilder.TryGetWall(id, out var bw) ? bw.side : (prevSide != 0 ? prevSide : 1);

                if (anchor)
                {
                    anchored++;
                    // Resolved: take its volume out of the grid so the next fit cannot draw
                    // a parallel phantom from the wall's own noisy occupancy ridge.
                    ClaimWallVolume(a3, b3, wallThickness, side, 0f, h);
                }
                else
                {
                    seenNext.Add((s.a, s.b, hits, side, 0));
                }
            }
            // A wall that wasn't matched this pass keeps its progress for a few more
            // rebuilds rather than resetting to 0 — one occluded/noisy cycle among several
            // good ones should not send an almost-anchored wall back to the start.
            for (int k = 0; k < _wallSeen.Count; k++)
            {
                if (seenMatched[k]) continue;
                var old = _wallSeen[k];
                if (old.misses + 1 <= autoAnchorMissGrace)
                    seenNext.Add((old.a, old.b, old.hits, old.side, old.misses + 1));
            }
            _wallSeen.Clear();
            _wallSeen.AddRange(seenNext);

            if (buildRoomGraph && graph != null)
                EmitCornersAndOutline(graph, builtIdBySeg);

            if (Debug.isDebugBuild)
            Debug.Log($"[OccupancyMapper] Reconstructed {built} wall(s) from {pts.Count} solid cells " +
                      $"({wallSource}: {LastSeededWalls} seeded + {LastDiscoveredWalls} discovered, " +
                      $"{beforeMerge} fragments merged to {segments.Count}" +
                      (anchored > 0 ? $", {anchored} anchored" : "") +
                      (suppressed > 0 ? $", {suppressed} suppressed by pinned walls" : "") +
                      (skippedLoose > 0 ? $", {skippedLoose} loose skipped" : "") +
                      (dupWalls > 0 ? $", {dupWalls} duplicate(s) merged" : "") +
                      (doorsCut + doorsRejected > 0
                          ? $", {doorsCut} doorway(s) cut, {doorsRejected} gap(s) rejected on width"
                          : "") +
                      (buildRoomGraph && graph != null
                          ? $", graph: {graph.Junctions.Count} junction(s), outline "
                            + (graph.OutlineClosed ? "closed" : graph.Outline.Count > 0 ? "open" : "none")
                            + (graph.CollinearMerges > 0 ? $", {graph.CollinearMerges} collinear fused" : "")
                          : "") + ").");
            return built;
        }

        /// <summary>Pinned (hand-corrected) walls as origin-local XZ segments — the fixed
        /// constraints the room graph must not move.</summary>
        List<(Vector2 a, Vector2 b)> PinnedWallSegments()
        {
            var list = new List<(Vector2 a, Vector2 b)>();
            foreach (var w in roomBuilder.Room.walls)
                if (w.pinned)
                    list.Add((new Vector2(w.start.x, w.start.z), new Vector2(w.end.x, w.end.z)));
            return list;
        }

        RoomGraphSettings GraphSettings() => new RoomGraphSettings
        {
            joinRadius = joinRadius,
            extendMax = junctionExtendMax,
            perpMinAngleDeg = junctionPerpMinAngle,
            outlineGapBridge = maxBridgeGap,
            collinearMergeAngle = collinearMergeAngle,
        };

        /// <summary>Segment more than <see cref="snapAngleTolerance"/> off the nearest
        /// <c>k·90°</c> of the room frame — the same test <see cref="RegularizeToRoomFrame"/>
        /// uses to leave a segment un-snapped.</summary>
        bool IsDiagonalToFrame((Vector2 a, Vector2 b) s)
        {
            var d = s.b - s.a;
            if (d.sqrMagnitude < 1e-6f) return false;
            float theta = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float rel = Mathf.DeltaAngle(_roomFrameAngle, theta);
            return Mathf.Abs(rel - Mathf.Round(rel / 90f) * 90f) > snapAngleTolerance;
        }

        /// <summary>Translate a solved <see cref="RoomGraph"/> into model corners + outline.</summary>
        void EmitCornersAndOutline(RoomGraph graph, IReadOnlyList<int> builtIdBySeg)
        {
            float floorY = roomBuilder.Room.floorY;

            var corners = new List<Corner>(graph.Junctions.Count);
            foreach (var j in graph.Junctions)
            {
                int idA = j.segmentIndices.Count > 0 ? WallIdAt(builtIdBySeg, j.segmentIndices[0]) : 0;
                int idB = j.segmentIndices.Count > 1 ? WallIdAt(builtIdBySeg, j.segmentIndices[1]) : 0;
                corners.Add(new Corner
                {
                    position = new Vector3(j.pos.x, floorY, j.pos.y),
                    wallA = new Vector3(j.dirA.x, 0f, j.dirA.y),
                    wallB = new Vector3(j.dirB.x, 0f, j.dirB.y),
                    wallAId = idA, wallBId = idB,
                });
            }
            roomBuilder.SetCorners(corners);

            var outline = new List<Vector3>(graph.Outline.Count);
            foreach (var p in graph.Outline) outline.Add(new Vector3(p.x, floorY, p.y));

            var bridges = new List<(Vector3 a, Vector3 b)>(graph.BridgedSpans.Count);
            foreach (var span in graph.BridgedSpans)
                bridges.Add((new Vector3(span.a.x, floorY, span.a.y), new Vector3(span.b.x, floorY, span.b.y)));

            roomBuilder.SetRoomOutline(outline, graph.OutlineClosed, bridges);
        }

        static int WallIdAt(IReadOnlyList<int> ids, int i)
            => i >= 0 && i < ids.Count && ids[i] > 0 ? ids[i] : 0;

        /// <summary>
        /// Solid cells as origin-local XZ points: seen across enough frames, spanning at
        /// least <paramref name="requiredSpan"/> vertically, whose column reaches the
        /// ceiling band (a wall does; furniture does not), and not under a detected surface.
        /// </summary>
        List<Vector2> CollectCells(float requiredSpan, float roomHeight)
        {
            var pts = new List<Vector2>();
            foreach (var kv in _hits)
            {
                if (kv.Value.frames < minCellHits) continue;
                if ((kv.Value.maxY - kv.Value.minY) < requiredSpan) continue;
                float cx = (kv.Key.Item1 + 0.5f) * cellSize, cz = (kv.Key.Item2 + 0.5f) * cellSize;
                if (!ColumnLooksLikeWall(cx, cz, roomHeight)) continue;
                pts.Add(new Vector2(cx, cz));
            }

            // Drop cells under a detected surface (build surfaces first): keeps a table near
            // a wall from also being fitted as a wall.
            var surfaces = roomBuilder.Room.surfaces;
            if (surfaces.Count > 0)
                pts.RemoveAll(p =>
                {
                    foreach (var s in surfaces)
                        if (Mathf.Abs(p.x - s.center.x) <= s.size.x * 0.5f + furnitureExcludeMargin &&
                            Mathf.Abs(p.y - s.center.z) <= s.size.y * 0.5f + furnitureExcludeMargin)
                            return true;
                    return false;
                });

            // Drop cells inside an anchored/hand-corrected wall's whole box: it is frozen,
            // so refitting it — or drawing a parallel phantom from the wide, camera-biased
            // occupancy ridge beside it — is wasted work and is where doubled walls came from.
            var pinned = roomBuilder.Room.walls.FindAll(w => w.pinned);
            if (pinned.Count > 0)
                pts.RemoveAll(p =>
                {
                    foreach (var w in pinned)
                        if (InWallSlabXZ(p, w.start, w.end,
                                         w.width > 0f ? w.width : wallThickness,
                                         w.side != 0 ? w.side : 1, wallClaimMargin))
                            return true;
                    return false;
                });

            // Ignore new detection beside a wall built last pass — that ridge is where the
            // phantoms come from — but keep a thin core on the line so the wall still refits.
            if (claimBuiltWallFootprint && _wallSeen.Count > 0)
                pts.RemoveAll(p =>
                {
                    foreach (var w in _wallSeen)
                    {
                        var a3 = new Vector3(w.a.x, 0f, w.a.y);
                        var b3 = new Vector3(w.b.x, 0f, w.b.y);
                        if (InWallSlabXZ(p, a3, b3, 0f, w.side, wallRidgeClaimMargin)
                            && !InWallSlabXZ(p, a3, b3, 0f, w.side, wallRidgeCoreKeep))
                            return true;
                    }
                    return false;
                });

            return pts;
        }

        /// <summary>
        /// Whether the voxel column at this XZ has the vertical profile of a wall rather
        /// than a piece of furniture. Two conditions, both on the ± one-voxel XZ
        /// neighbourhood (the wall grid and voxel grid are not aligned):
        ///
        ///  • <b>reach</b> — the highest occupied layer is at or above
        ///    <see cref="wallTopReachFraction"/> of the room height. A wall runs to the
        ///    ceiling; a chair back, a cabinet or a box tops out below it.
        ///  • <b>continuity</b> — at least <see cref="wallColumnFillFraction"/> of the
        ///    layers from the wall band up to that highest layer are occupied. This rejects
        ///    a low furniture band plus a stray high point (depth noise, a reflection),
        ///    which would pass reach alone but leaves an empty gap in between.
        /// </summary>
        bool ColumnLooksLikeWall(float cx, float cz, float roomHeight)
        {
            float h = roomHeight > 0.1f ? roomHeight : 2.4f;
            int vx = Mathf.FloorToInt(cx / voxelSize), vz = Mathf.FloorToInt(cz / voxelSize);
            int lo = Mathf.FloorToInt(bandBottom / voxelSize);
            int hi = Mathf.FloorToInt((h - bandTopMargin) / voxelSize);
            if (hi <= lo) return false;
            int reachLayer = Mathf.FloorToInt(h * wallTopReachFraction / voxelSize);

            int occupied = 0, highest = -1;
            for (int vy = lo; vy <= hi; vy++)
            {
                bool any = false;
                for (int dx = -1; dx <= 1 && !any; dx++)
                for (int dz = -1; dz <= 1 && !any; dz++)
                    if (_voxels.TryGetValue((vx + dx, vy, vz + dz), out var vi) &&
                        vi.hits >= wallColumnMinFrames) any = true;
                if (any) { occupied++; highest = vy; }
            }
            if (highest < reachLayer) return false;

            float fill = occupied / (float)(highest - lo + 1);
            return fill >= wallColumnFillFraction;
        }

        /// <summary>Consecutive rebuilds a wall matching a→b has survived, the side it was
        /// last built on (0 if this line is new), and which `_wallSeen` entry matched (-1 if
        /// none) so the caller can mark it seen this pass.</summary>
        (int hits, int side, int matchedIdx) WallSeenLookup(Vector2 a, Vector2 b)
        {
            for (int k = 0; k < _wallSeen.Count; k++)
                if (SameWallLine(_wallSeen[k].a, _wallSeen[k].b, a, b))
                    return (_wallSeen[k].hits, _wallSeen[k].side, k);
            return (0, 0, -1);
        }

        /// <summary>
        /// True if two floor segments describe the same wall: near-parallel, near-collinear
        /// (perpendicular offset within <see cref="autoAnchorTolerance"/>), and overlapping
        /// by at least <see cref="pinnedOverlapMin"/> along their shared line.
        /// </summary>
        bool SameWallLine(Vector2 a0, Vector2 b0, Vector2 a1, Vector2 b1)
        {
            var d0 = b0 - a0; var d1 = b1 - a1;
            if (d0.sqrMagnitude < 1e-8f || d1.sqrMagnitude < 1e-8f) return false;

            float ang0 = Mathf.Repeat(Mathf.Atan2(d0.y, d0.x) * Mathf.Rad2Deg, 180f);
            float ang1 = Mathf.Repeat(Mathf.Atan2(d1.y, d1.x) * Mathf.Rad2Deg, 180f);
            if (AngleDelta(ang0, ang1) > mergeMaxAngle) return false;

            var nrm = new Vector2(-d0.y, d0.x).normalized;
            float off = Vector2.Dot(nrm, a0);
            if (Mathf.Abs(Vector2.Dot(nrm, a1) - off) > autoAnchorTolerance) return false;
            if (Mathf.Abs(Vector2.Dot(nrm, b1) - off) > autoAnchorTolerance) return false;

            var dir = d0.normalized;
            float t0 = 0f, t1 = Vector2.Dot(b0 - a0, dir);
            float u0 = Vector2.Dot(a1 - a0, dir), u1 = Vector2.Dot(b1 - a0, dir);
            float overlap = Mathf.Min(Mathf.Max(t0, t1), Mathf.Max(u0, u1))
                          - Mathf.Max(Mathf.Min(t0, t1), Mathf.Min(u0, u1));
            return overlap >= pinnedOverlapMin;
        }

        /// <summary>Removes the shorter of every pair of segments that <see cref="SameWallLine"/>
        /// says are the same wall, keeping <paramref name="gaps"/> index-parallel. Returns
        /// how many were removed.</summary>
        int DedupeOverlappingWalls(List<(Vector2 a, Vector2 b)> segs,
                                   List<List<(Vector2 a, Vector2 b)>> gaps)
        {
            int removed = 0;
            for (int i = 0; i < segs.Count; i++)
                for (int j = i + 1; j < segs.Count; )
                {
                    if (!SameWallLine(segs[i].a, segs[i].b, segs[j].a, segs[j].b)) { j++; continue; }
                    bool keepI = (segs[i].b - segs[i].a).sqrMagnitude >= (segs[j].b - segs[j].a).sqrMagnitude;
                    int drop = keepI ? j : i;
                    segs.RemoveAt(drop);
                    if (drop < gaps.Count) gaps.RemoveAt(drop);
                    removed++;
                    if (!keepI) { i--; break; }
                }
            return removed;
        }

        /// <summary>
        /// Confirms ARCore vertical planes against the occupancy grid. Each seed supplies a
        /// line; cells lying along it decide whether that line is real and how far it runs.
        /// Cells consumed by an accepted wall are removed from <paramref name="discoveryPts"/>
        /// so the blind pass does not re-fit the same wall. Returns walls accepted.
        /// </summary>
        int SeedWalls(Transform mapOrigin, List<Vector2> evidence, List<Vector2> discoveryPts,
                      List<(Vector2 a, Vector2 b)> segments)
        {
            if (planeCollector == null) return 0;

            var seeds = planeCollector.VerticalWallSeeds(mapOrigin, seedMinArea);
            if (seeds.Count == 0) return 0;

            int built = 0;
            var accepted = new List<(Vector2 a, Vector2 b)>();

            foreach (var seed in seeds)
            {
                if (segments.Count >= maxWalls) break;

                // Cells lying on this seed's infinite line.
                var nrm = new Vector2(-seed.dir.y, seed.dir.x);
                var inliers = new List<int>();
                for (int i = 0; i < evidence.Count; i++)
                    if (Mathf.Abs(Vector2.Dot(nrm, evidence[i] - seed.point)) < lineInlierDist)
                        inliers.Add(i);

                if (wallSource == WallSourceMode.Hybrid)
                {
                    // Confirm, or discard as a phantom: enough cells overall, and enough of
                    // the plane's own extent actually backed by depth.
                    if (inliers.Count < seedMinCells) continue;
                    float spanCells = seed.halfLength * 2f / cellSize + 1f;
                    if (inliers.Count < spanCells * seedMinDensityFraction) continue;
                }

                int before = segments.Count;
                if (inliers.Count > 0)
                    EmitRuns(evidence, inliers, seed.point, seed.dir,
                             seedMinCells, seedMinDensityFraction, segments);

                // PlanesOnly deliberately trusts a plane with no depth support at all —
                // that is the mode that reproduces the original phantom-wall behaviour.
                if (segments.Count == before && wallSource == WallSourceMode.PlanesOnly)
                    segments.Add((seed.point - seed.dir * seed.halfLength,
                                  seed.point + seed.dir * seed.halfLength));

                for (int i = before; i < segments.Count; i++) accepted.Add(segments[i]);
                built += segments.Count - before;
            }

            if (accepted.Count > 0)
                discoveryPts.RemoveAll(p => NearAnySegment(p, accepted, lineInlierDist));

            return built;
        }

        /// <summary>
        /// Confirms floor-boundary edges against occupancy — the <see cref="WallSourceMode.FloorConfirmed"/>
        /// analogue of <see cref="SeedWalls"/>, with each floor-plane boundary edge as the
        /// seed geometry instead of an ARCore vertical plane. Its own, separately tunable
        /// acceptance bar (<see cref="floorSeedMinCells"/> / <see cref="floorSeedMinDensityFraction"/>)
        /// — deliberately looser than the vertical-plane one, since the floor boundary is
        /// already trusted geometry — so tuning "how much to trust the floor" here never
        /// changes Hybrid's confirmation of vertical planes. A seam between two floor
        /// fragments, or a stairstep in the floor mesh, has no wall-height evidence nearby
        /// and is rejected by this gate regardless of how loose it is set.
        /// </summary>
        int SeedFloorBoundary(List<(Vector2 a, Vector2 b)> edges, List<Vector2> evidence,
                              List<(Vector2 a, Vector2 b)> segments)
        {
            int built = 0;
            foreach (var edge in edges)
            {
                if (segments.Count >= maxWalls) break;

                var d = edge.b - edge.a;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                var dir = d / len;
                var nrm = new Vector2(-dir.y, dir.x);
                var point = (edge.a + edge.b) * 0.5f;

                var inliers = new List<int>();
                for (int i = 0; i < evidence.Count; i++)
                    if (Mathf.Abs(Vector2.Dot(nrm, evidence[i] - point)) < lineInlierDist)
                        inliers.Add(i);

                if (inliers.Count < floorSeedMinCells) continue;
                float spanCells = len / cellSize + 1f;
                if (inliers.Count < spanCells * floorSeedMinDensityFraction) continue;

                built += EmitRuns(evidence, inliers, point, dir,
                                  floorSeedMinCells, floorSeedMinDensityFraction, segments);
            }
            return built;
        }

        /// <summary>Blind multi-line RANSAC: peels dominant lines out of the remaining cells.</summary>
        int DiscoverWalls(List<Vector2> pts, List<(Vector2 a, Vector2 b)> segments)
        {
            int built = 0;
            var remaining = new List<Vector2>(pts);

            while (remaining.Count >= minSegmentCells && segments.Count < maxWalls)
            {
                if (!RansacLine(remaining, out var p, out var dir, out var inliers)) break;
                if (inliers.Count < minSegmentCells) break;

                built += EmitRuns(remaining, inliers, p, dir,
                                  minSegmentCells, minDensityFraction, segments);

                var set = new HashSet<int>(inliers);
                var next = new List<Vector2>(remaining.Count - inliers.Count);
                for (int i = 0; i < remaining.Count; i++) if (!set.Contains(i)) next.Add(remaining[i]);
                remaining = next;
            }

            return built;
        }

        /// <summary>
        /// Projects inliers onto a line and splits them into continuous runs: a gap larger
        /// than <see cref="maxRunGap"/> means the line crosses empty space (a doorway, or a
        /// phantom diagonal), so it is cut there. Each sufficiently long and dense run
        /// becomes a segment. Shared by the seeded and discovery passes, which differ only
        /// in how strict <paramref name="minCells"/> / <paramref name="minDensity"/> are.
        /// </summary>
        int EmitRuns(List<Vector2> pts, List<int> inliers, Vector2 p, Vector2 dir,
                     int minCells, float minDensity, List<(Vector2 a, Vector2 b)> segments)
        {
            if (inliers.Count == 0) return 0;

            var ts = new List<float>(inliers.Count);
            foreach (var idx in inliers) ts.Add(Vector2.Dot(pts[idx] - p, dir));
            ts.Sort();

            int emitted = 0;
            float runStart = ts[0], prev = ts[0];
            int runCount = 1;

            for (int k = 1; k <= ts.Count; k++)
            {
                bool end = k == ts.Count;
                if (!end && ts[k] - prev <= maxRunGap)
                {
                    prev = ts[k]; runCount++;
                    continue;
                }

                // Close the current run.
                float length = prev - runStart;
                float lengthCells = length / cellSize + 1f;
                if (length >= cellSize * minCells * 0.5f && runCount >= lengthCells * minDensity)
                {
                    segments.Add((p + dir * runStart, p + dir * prev));
                    emitted++;
                }

                if (!end) { runStart = ts[k]; prev = ts[k]; runCount = 1; }
            }

            return emitted;
        }

        static bool NearAnySegment(Vector2 p, List<(Vector2 a, Vector2 b)> segs, float radius)
        {
            var p3 = new Vector3(p.x, 0f, p.y);
            foreach (var s in segs)
                if (DistanceToSegmentXZ(p3, new Vector3(s.a.x, 0f, s.a.y),
                                            new Vector3(s.b.x, 0f, s.b.y)) <= radius)
                    return true;
            return false;
        }

        /// <summary>
        /// Detects the horizontal surfaces above the floor — table tops, seats, shelves —
        /// as coarse axis-aligned rectangles a game can spawn objects onto. This replaces
        /// furniture-box reconstruction, which tried to guess a whole bounding volume and
        /// could not do it reliably. Run a wall pass first so wall areas are excluded.
        /// </summary>
        public int ReconstructSurfaces(Transform mapOrigin, float roomHeight = 0f)
        {
            if (roomBuilder == null) return 0;

            if (roomHeight > 0.1f) _roomHeightHint = roomHeight;
            float h = _roomHeightHint > 0.1f ? _roomHeightHint : 2.4f;
            var walls = roomBuilder.Room.walls;
            var frozen = roomBuilder.Room.surfaces.FindAll(s => s.pinned);
            float loY = furnitureBandBottom + surfaceMinHeight;
            float hiY = h * surfaceMaxHeightFraction;

            // Horizontal-dominant voxels between floor and ceiling, clear of walls and of
            // any surface already anchored.
            var surf = new HashSet<(int, int, int)>();
            foreach (var kv in _voxels)
            {
                var vi = kv.Value;
                if (vi.hits < minVoxelHits) continue;
                if (vi.horiz < minSurfaceHits || vi.horiz < vi.vert) continue;
                var c = VoxelCenter(kv.Key);
                if (c.y < loY || c.y > hiY) continue;
                if (NearAnyWall(c, walls)) continue;
                if (OnWallColumn(c, 0f)) continue;
                if (InFrozenSurface(c, frozen)) continue;
                surf.Add(kv.Key);
            }

            roomBuilder.ClearSurfaces(keepPinned: true);
            var seenNext = new List<(Vector3 center, Vector2 size, int hits)>();
            int built = 0;

            var visited = new HashSet<(int, int, int)>();
            var queue = new Queue<(int, int, int)>();
            var patch = new List<(int, int, int)>();
            foreach (var seed in surf)
            {
                if (!visited.Add(seed)) continue;

                patch.Clear();
                int minX = seed.Item1, maxX = seed.Item1, minZ = seed.Item3, maxZ = seed.Item3;
                long sumY = 0;
                queue.Clear(); queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    patch.Add(c);
                    minX = Mathf.Min(minX, c.Item1); maxX = Mathf.Max(maxX, c.Item1);
                    minZ = Mathf.Min(minZ, c.Item3); maxZ = Mathf.Max(maxZ, c.Item3);
                    sumY += c.Item2;
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        var nb = (c.Item1 + dx, c.Item2 + dy, c.Item3 + dz);
                        if (surf.Contains(nb) && visited.Add(nb)) queue.Enqueue(nb);
                    }
                }
                if (patch.Count < minSurfaceVoxels) continue;

                float sizeX = (maxX - minX + 1) * voxelSize;
                float sizeZ = (maxZ - minZ + 1) * voxelSize;
                if (sizeX > maxBoxDimension || sizeZ > maxBoxDimension) continue; // the floor, or merged surfaces
                if (Mathf.Min(sizeX, sizeZ) < minBoxDimension) continue;          // sliver

                // Density: a real surface is a contiguous sheet that fills most of its own
                // footprint. Depth-edge noise can flood-fill into a patch that spans a
                // plausible-looking area while only sparsely touching it — this rejects that
                // without penalising a surface that legitimately spans a couple of voxel
                // layers in height (which only raises patch.Count, never fails the ratio).
                float footprintVoxels = (maxX - minX + 1f) * (maxZ - minZ + 1f);
                if (patch.Count < footprintVoxels * surfaceFillFraction) continue;

                var center = new Vector3(
                    (minX + maxX + 1) * 0.5f * voxelSize,
                    ((float)sumY / patch.Count + 0.5f) * voxelSize,
                    (minZ + maxZ + 1) * 0.5f * voxelSize);
                var size = new Vector2(sizeX, sizeZ);

                int hits = SurfSeenHits(center, size) + 1;
                bool anchor = autoAnchorHits > 0 && hits >= autoAnchorHits;
                if (!anchor) seenNext.Add((center, size, hits));
                if (roomBuilder.BuildSurface(center, size, anchor) >= 0) built++;
            }

            _surfSeen.Clear();
            _surfSeen.AddRange(seenNext);

            Debug.Log($"[OccupancyMapper] Reconstructed {built} horizontal surface(s) " +
                      $"from {surf.Count} horizontal voxels.");
            return built;
        }

        /// <summary>Consecutive rebuilds a surface matching this centre/size has survived.</summary>
        int SurfSeenHits(Vector3 center, Vector2 size)
        {
            foreach (var s in _surfSeen)
                if ((s.center - center).sqrMagnitude <= autoAnchorTolerance * autoAnchorTolerance &&
                    (s.size - size).sqrMagnitude <= autoAnchorTolerance * autoAnchorTolerance)
                    return s.hits;
            return 0;
        }

        static bool InFrozenSurface(Vector3 p, List<HorizontalSurface> frozen)
        {
            foreach (var f in frozen)
                if (Mathf.Abs(p.x - f.center.x) <= f.size.x * 0.5f + 0.1f &&
                    Mathf.Abs(p.z - f.center.z) <= f.size.y * 0.5f + 0.1f &&
                    Mathf.Abs(p.y - f.center.y) <= 0.2f)
                    return true;
            return false;
        }

        /// <summary>
        /// True if the 2D wall grid has a solid column under <paramref name="pLocal"/>:
        /// a cell seen across enough frames, spanning <paramref name="requiredSpan"/>
        /// vertically, whose column also reaches the ceiling band. The reach test is what
        /// keeps a chair back or a cabinet — a tall vertical face that stops well short of
        /// the ceiling — from masking its own voxels out of furniture as if it were a wall.
        /// </summary>
        bool OnWallColumn(Vector3 pLocal, float requiredSpan)
        {
            if (!_hits.TryGetValue(Cell(pLocal), out var info)) return false;
            if (info.frames < minCellHits) return false;
            if ((info.maxY - info.minY) < requiredSpan) return false;
            return ColumnLooksLikeWall(pLocal.x, pLocal.z, _roomHeightHint);
        }

        /// <summary>
        /// Turns the gaps bridged into one merged wall into doorway openings in that wall's
        /// own frame: <c>u</c> along the floor line from <paramref name="a"/>, <c>v</c> up
        /// from the floor.
        ///
        /// Gaps arrive as origin-local XZ endpoints rather than as <c>u</c> offsets, so they
        /// stay meaningful no matter where the merged wall's start point ended up — the
        /// projection is done here, once, against the wall that is actually being built.
        ///
        /// Only doors come out of this. The signal is a gap in a <em>floor-level</em>
        /// occupancy line, and a window has wall beneath it, so a window's column is
        /// occupied and leaves no gap to find.
        /// </summary>
        List<WallOpening> DoorwaysFor(Vector2 a, Vector2 b,
                                      List<(Vector2 a, Vector2 b)> gaps,
                                      float wallHeight, out int rejected)
        {
            rejected = 0;
            if (!detectDoorways || gaps == null || gaps.Count == 0) return null;

            var d = b - a;
            float len = d.magnitude;
            if (len < AutoWallMeshBuilder.MinDimension) return null;
            var dir = d / len;

            // Leave a lintel: a doorway reaching the ceiling would split the wall in two
            // and the mesh would lose its top edge.
            float vMax = Mathf.Min(doorHeight, wallHeight - 0.05f);
            if (vMax < AutoWallMeshBuilder.MinDimension) return null;

            List<WallOpening> openings = null;
            foreach (var gap in gaps)
            {
                float u0 = Vector2.Dot(gap.a - a, dir);
                float u1 = Vector2.Dot(gap.b - a, dir);
                if (u1 < u0) { var t = u0; u0 = u1; u1 = t; }

                // Width is judged on the gap as measured, before clamping, so a doorway at
                // the very end of a wall is not shrunk into rejection.
                float width = u1 - u0;
                if (width < minDoorWidth || width > maxDoorWidth) { rejected++; continue; }

                u0 = Mathf.Max(0f, u0);
                u1 = Mathf.Min(len, u1);
                if (u1 - u0 < AutoWallMeshBuilder.MinDimension) { rejected++; continue; }

                if (openings == null) openings = new List<WallOpening>();
                openings.Add(new WallOpening
                {
                    uMin = u0, uMax = u1,
                    vMin = 0f, vMax = vMax,
                    kind = "Door",
                });
            }
            return openings;
        }

        /// <summary>
        /// Puts every near-aligned wall on one shared right-angle frame: a single room
        /// orientation, then near-equal perpendicular offsets pulled onto one line. This is
        /// what makes two scan fragments of one wall come out identical so
        /// <see cref="MergeCollinear"/> fuses them instead of leaving a doubled wall.
        /// </summary>
        void RegularizeToRoomFrame(List<(Vector2 a, Vector2 b)> segments)
        {
            if (segments.Count == 0) return;

            // Frame angle: length-weighted circular mean over 4·angle (the mod-90 space
            // becomes a full circle). Pinned walls, when present, define it alone so a hand
            // correction is never overruled by noisy fresh fragments.
            var pinned = roomBuilder.Room.walls.FindAll(w => w.pinned);
            float sx = 0f, sy = 0f;
            System.Action<Vector2> acc = d =>
            {
                float l = d.magnitude;
                if (l < 1e-4f) return;
                float a4 = Mathf.Atan2(d.y, d.x) * 4f;
                sx += Mathf.Cos(a4) * l; sy += Mathf.Sin(a4) * l;
            };
            if (pinned.Count > 0)
                foreach (var w in pinned) acc(new Vector2(w.end.x - w.start.x, w.end.z - w.start.z));
            else
                foreach (var s in segments) acc(s.b - s.a);
            if (Mathf.Abs(sx) < 1e-6f && Mathf.Abs(sy) < 1e-6f) return;

            float frame = Mathf.Repeat(Mathf.Atan2(sy, sx) * Mathf.Rad2Deg / 4f, 90f);

            // Low-pass across rebuilds unless it is the first, or the room really re-oriented.
            if (_roomFrameAngle > -900f)
            {
                float delta = Mathf.DeltaAngle(_roomFrameAngle * 4f, frame * 4f) / 4f;
                if (Mathf.Abs(delta) < 15f) frame = _roomFrameAngle + delta * 0.5f;
            }
            _roomFrameAngle = Mathf.Repeat(frame, 90f);
            frame = _roomFrameAngle;

            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                var d = s.b - s.a;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                float theta = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float rel = Mathf.DeltaAngle(frame, theta);
                float snappedRel = Mathf.Round(rel / 90f) * 90f;
                if (Mathf.Abs(rel - snappedRel) > snapAngleTolerance) continue; // true diagonal
                float snapped = (frame + snappedRel) * Mathf.Deg2Rad;
                var nd = new Vector2(Mathf.Cos(snapped), Mathf.Sin(snapped));
                var m = (s.a + s.b) * 0.5f;
                segments[i] = (m - nd * (len * 0.5f), m + nd * (len * 0.5f));
            }

            ClusterOffsets(segments, frame, 0f);
            ClusterOffsets(segments, frame, 90f);
        }

        /// <summary>Pulls the segments running along <paramref name="frame"/>+<paramref name="axisDeg"/>
        /// whose perpendicular offsets are within <see cref="wallOffsetCluster"/> onto a
        /// single shared line (length-weighted mean offset).</summary>
        void ClusterOffsets(List<(Vector2 a, Vector2 b)> segments, float frame, float axisDeg)
        {
            float rad = (frame + axisDeg) * Mathf.Deg2Rad;
            var nrm = new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));

            var items = new List<(int i, float off, float len)>();
            for (int i = 0; i < segments.Count; i++)
            {
                var d = segments[i].b - segments[i].a;
                if (d.sqrMagnitude < 1e-8f) continue;
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(frame + axisDeg, ang)) > 1f &&
                    Mathf.Abs(Mathf.DeltaAngle(frame + axisDeg, ang + 180f)) > 1f) continue;
                var mid = (segments[i].a + segments[i].b) * 0.5f;
                items.Add((i, Vector2.Dot(nrm, mid), d.magnitude));
            }
            if (items.Count < 2) return;
            items.Sort((x, y) => x.off.CompareTo(y.off));

            int start = 0;
            while (start < items.Count)
            {
                int end = start;
                float sum = 0f, wsum = 0f;
                while (end < items.Count && items[end].off - items[start].off <= wallOffsetCluster)
                {
                    sum += items[end].off * items[end].len; wsum += items[end].len; end++;
                }
                float mean = wsum > 0f ? sum / wsum : items[start].off;
                for (int k = start; k < end; k++)
                {
                    var s = segments[items[k].i];
                    var shift = nrm * (mean - Vector2.Dot(nrm, (s.a + s.b) * 0.5f));
                    segments[items[k].i] = (s.a + shift, s.b + shift);
                }
                start = end;
            }
        }

        /// <summary>A wall line in normal form, plus the fragments found lying on it.</summary>
        struct LineGroup
        {
            public float angle;      // direction, degrees, wrapped to [0,180)
            public float offset;     // signed perpendicular distance of the line from the origin
            public float weight;     // total fragment length, for weighted refitting
            public List<(Vector2 a, Vector2 b)> parts;
        }

        /// <summary>
        /// Fuses fragments of one wall by grouping them onto shared lines.
        ///
        /// Each segment is reduced to normal form — direction angle plus perpendicular
        /// offset <em>measured from the origin</em> — and segments matching on both belong
        /// to the same wall. Measuring from the origin rather than from a neighbouring
        /// segment is the point: the previous pairwise test compared each candidate against
        /// another segment's anchor point, so its error grew with distance along the wall
        /// and long walls failed to merge precisely because they were long. Grouping is
        /// also transitive by construction, so a wall broken into five pieces collapses in
        /// one pass instead of depending on which pair happened to be compared first.
        ///
        /// Within a group the fragments are projected onto the refitted line and gaps up to
        /// <see cref="maxBridgeGap"/> are bridged; anything larger stays a separate wall, so
        /// a doorway or open passage is not sealed over.
        /// </summary>
        /// <summary>
        /// True if a freshly fitted segment says the same thing as a wall the user already
        /// corrected by hand. Uses the same normal form as <see cref="MergeCollinear"/>
        /// (angle + perpendicular offset from the origin), then additionally requires the
        /// two to overlap along that line — collinear-but-separate runs, such as the wall
        /// either side of a doorway, are different walls and must both survive.
        /// </summary>
        bool DuplicatesPinnedWall(Vector2 a, Vector2 b)
        {
            if (roomBuilder == null) return false;

            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return false;

            var dir = d / len;
            float angle = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 180f);
            float offset = OffsetFromOrigin(angle, a);

            foreach (var w in roomBuilder.Walls)
            {
                if (!w.pinned) continue;

                var pa = new Vector2(w.start.x, w.start.z);
                var pb = new Vector2(w.end.x, w.end.z);
                var pd = pb - pa;
                if (pd.sqrMagnitude < 1e-8f) continue;

                float pAngle = Mathf.Repeat(Mathf.Atan2(pd.y, pd.x) * Mathf.Rad2Deg, 180f);
                if (AngleDelta(pAngle, angle) > mergeMaxAngle) continue;

                // Measured in the pinned wall's frame, for the reason spelled out in
                // MergeCollinear: two normal-form offsets taken under different angles are
                // not comparable, and the error grows with distance from the origin. Here
                // that made a rebuild re-emit an automatic wall on top of a hand-corrected
                // one — the exact thing pinning exists to prevent — whenever the wall was
                // far enough from where the scan started.
                // A parallel line anywhere within the pinned wall's box (plus a margin) is
                // the same wall — this is what stops the camera-side half of a fat occupancy
                // ridge from being emitted as a second wall next to the anchored one.
                float pw = w.width > 0f ? w.width : wallThickness;
                float perpTol = Mathf.Max(mergeOffsetTolerance, pw + wallClaimMargin);
                var pNrm = new Vector2(-pd.y, pd.x).normalized;
                float pOffset = Vector2.Dot(pNrm, pa);
                if (Mathf.Abs(Vector2.Dot(pNrm, a) - pOffset) > perpTol) continue;
                if (Mathf.Abs(Vector2.Dot(pNrm, b) - pOffset) > perpTol) continue;

                // Same line. Project both onto it and measure the shared span.
                float t0 = Vector2.Dot(a - pa, dir),  t1 = Vector2.Dot(b - pa, dir);
                float p0 = 0f,                        p1 = Vector2.Dot(pb - pa, dir);
                float overlap = Mathf.Min(Mathf.Max(t0, t1), Mathf.Max(p0, p1))
                              - Mathf.Max(Mathf.Min(t0, t1), Mathf.Min(p0, p1));
                if (overlap >= pinnedOverlapMin) return true;
            }
            return false;
        }

        /// <param name="gapsPerSeg">When given, filled index-parallel with <paramref name="segs"/>:
        /// the bridged gaps each merged wall swallowed, as origin-local XZ endpoint pairs.
        /// These are the doorway candidates.</param>
        void MergeCollinear(List<(Vector2 a, Vector2 b)> segs,
                            List<List<(Vector2 a, Vector2 b)>> gapsPerSeg,
                            float bridgeGap)
        {
            if (gapsPerSeg != null) gapsPerSeg.Clear();

            if (segs.Count < 2)
            {
                // Still keep the two lists the same length, so callers can index blindly.
                if (gapsPerSeg != null)
                    for (int i = 0; i < segs.Count; i++) gapsPerSeg.Add(null);
                return;
            }

            // Longest first: the most reliable fragments define each line.
            segs.Sort((x, y) => (y.b - y.a).sqrMagnitude.CompareTo((x.b - x.a).sqrMagnitude));

            var groups = new List<LineGroup>();

            foreach (var s in segs)
            {
                var d = s.b - s.a;
                float len = d.magnitude;
                if (len < 1e-4f) continue;

                float angle = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 180f);
                float offset = OffsetFromOrigin(angle, s.a);

                int hit = -1;
                for (int g = 0; g < groups.Count; g++)
                {
                    if (AngleDelta(groups[g].angle, angle) > mergeMaxAngle) continue;

                    // Both endpoints must lie on the group's line, measured IN THE GROUP'S
                    // OWN FRAME. Comparing the fragment's offset against the group's — each
                    // computed with its own angle — is the same number only when the two
                    // angles agree, and the angle gate deliberately allows them not to.
                    // The discrepancy is (distance from the origin) x sin(angle error), so
                    // it grew with how far the wall was from the origin: a wall 4 m out,
                    // 12 deg apart, reads as 0.83 m of offset difference against a 0.20 m
                    // tolerance, and fragments of one wall refused to fuse for no reason
                    // beyond where the user happened to start the scan. Point-to-line
                    // distance has no such term.
                    if (DistanceToLine(groups[g], s.a) > mergeOffsetTolerance) continue;
                    if (DistanceToLine(groups[g], s.b) > mergeOffsetTolerance) continue;
                    hit = g;
                    break;
                }

                if (hit < 0)
                {
                    groups.Add(new LineGroup
                    {
                        angle  = angle,
                        offset = offset,
                        weight = len,
                        parts  = new List<(Vector2 a, Vector2 b)> { s },
                    });
                    continue;
                }

                // Length-weighted refit, so a long fragment pulls the line more than a stub.
                var grp = groups[hit];
                grp.parts.Add(s);
                RefitGroup(ref grp);
                groups[hit] = grp;
            }

            segs.Clear();
            foreach (var g in groups) EmitGroup(g, segs, bridgeGap, gapsPerSeg);
        }

        /// <summary>Projects a group's fragments onto its refitted line and emits spans,
        /// bridging gaps up to <paramref name="bridgeGap"/>.</summary>
        void EmitGroup(LineGroup g, List<(Vector2 a, Vector2 b)> outSegs, float bridgeGap,
                       List<List<(Vector2 a, Vector2 b)>> gapsPerSeg = null)
        {
            float rad = g.angle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            var nrm = new Vector2(-dir.y, dir.x);
            var basePoint = nrm * g.offset;   // closest point of the line to the origin

            // Each fragment becomes a [min,max] interval along the line.
            var spans = new List<(float lo, float hi)>(g.parts.Count);
            foreach (var p in g.parts)
            {
                float ta = Vector2.Dot(p.a - basePoint, dir);
                float tb = Vector2.Dot(p.b - basePoint, dir);
                spans.Add((Mathf.Min(ta, tb), Mathf.Max(ta, tb)));
            }
            spans.Sort((x, y) => x.lo.CompareTo(y.lo));

            // Gaps bridged into the wall currently being accumulated. A bridged gap is a
            // hole in an otherwise continuous wall — which is to say, a doorway — so it is
            // recorded here rather than thrown away when the fragments either side are
            // fused. Kept in origin-local XZ so it survives whatever the merged wall's
            // start point turns out to be.
            var pending = new List<(Vector2 a, Vector2 b)>();

            float lo = spans[0].lo, hi = spans[0].hi;
            for (int i = 1; i < spans.Count; i++)
            {
                if (spans[i].lo - hi <= bridgeGap)             // same wall, gap bridged
                {
                    // Overlapping fragments give lo <= hi, which is not a gap at all.
                    if (spans[i].lo > hi)
                        pending.Add((basePoint + dir * hi, basePoint + dir * spans[i].lo));
                    hi = Mathf.Max(hi, spans[i].hi);
                    continue;
                }
                Emit(outSegs, gapsPerSeg, basePoint + dir * lo, basePoint + dir * hi, pending);
                lo = spans[i].lo; hi = spans[i].hi;
            }
            Emit(outSegs, gapsPerSeg, basePoint + dir * lo, basePoint + dir * hi, pending);
        }

        /// <summary>Appends one merged wall and the gaps it swallowed, keeping the two
        /// lists index-parallel, and empties <paramref name="pending"/> for the next wall.</summary>
        static void Emit(List<(Vector2 a, Vector2 b)> outSegs,
                         List<List<(Vector2 a, Vector2 b)>> gapsPerSeg,
                         Vector2 a, Vector2 b,
                         List<(Vector2 a, Vector2 b)> pending)
        {
            outSegs.Add((a, b));
            if (gapsPerSeg != null)
                gapsPerSeg.Add(pending.Count > 0 ? new List<(Vector2 a, Vector2 b)>(pending) : null);
            pending.Clear();
        }

        /// <summary>Signed perpendicular offset of the line through <paramref name="point"/>
        /// with direction <paramref name="angleDeg"/>, measured from the origin.</summary>
        /// <summary>
        /// Perpendicular distance from a point to a group's line, in the group's own frame.
        /// This is the coplanarity test: unlike comparing two normal-form offsets computed
        /// under different angles, it carries no term in the distance from the origin.
        /// </summary>
        static float DistanceToLine(in LineGroup g, Vector2 p)
        {
            float rad = g.angle * Mathf.Deg2Rad;
            var nrm = new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));
            return Mathf.Abs(Vector2.Dot(nrm, p) - g.offset);
        }

        /// <summary>
        /// Refits a group's line to all of its fragments, length-weighted.
        ///
        /// Angle and offset are recomputed together, from the parts, rather than the offset
        /// being rolled forward as a running average. An offset only means anything
        /// relative to the angle it was measured with, so averaging an old offset (old
        /// angle) with a new one (new angle) yields a line that need not pass through
        /// either fragment — and every subsequent member is then tested against that drift.
        /// </summary>
        static void RefitGroup(ref LineGroup g)
        {
            // Angle: circular mean over doubled angles, so the 0/180 wrap has no seam.
            float x = 0f, y = 0f, w = 0f;
            foreach (var p in g.parts)
            {
                var d = p.b - p.a;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                float doubled = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 180f) * 2f * Mathf.Deg2Rad;
                x += Mathf.Cos(doubled) * len;
                y += Mathf.Sin(doubled) * len;
                w += len;
            }
            if (w <= 0f) return;

            if (Mathf.Abs(x) > 1e-6f || Mathf.Abs(y) > 1e-6f)
                g.angle = Mathf.Repeat(Mathf.Atan2(y, x) * Mathf.Rad2Deg * 0.5f, 180f);
            g.weight = w;

            // Offset: where that line sits, from the endpoints, in the new frame.
            float rad = g.angle * Mathf.Deg2Rad;
            var nrm = new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));
            float sum = 0f;
            foreach (var p in g.parts)
            {
                float len = (p.b - p.a).magnitude;
                sum += len * 0.5f * (Vector2.Dot(nrm, p.a) + Vector2.Dot(nrm, p.b));
            }
            g.offset = sum / w;
        }

        static float OffsetFromOrigin(float angleDeg, Vector2 point)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return Vector2.Dot(new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad)), point);
        }

        /// <summary>Smallest separation between two undirected angles (mod 180°).</summary>
        static float AngleDelta(float a, float b)
        {
            float d = Mathf.Abs(Mathf.Repeat(a - b, 180f));
            return Mathf.Min(d, 180f - d);
        }

        /// <summary>Weighted mean of two undirected angles, handling the 0/180 wrap.</summary>
        static float WeightedAngle(float a, float wa, float b, float wb)
        {
            // Double the angles so the mod-180 space becomes a full circle, average as
            // vectors (no wrap discontinuity), then halve back.
            float ra = a * 2f * Mathf.Deg2Rad, rb = b * 2f * Mathf.Deg2Rad;
            float x = Mathf.Cos(ra) * wa + Mathf.Cos(rb) * wb;
            float y = Mathf.Sin(ra) * wa + Mathf.Sin(rb) * wb;
            if (Mathf.Abs(x) < 1e-6f && Mathf.Abs(y) < 1e-6f) return a;
            return Mathf.Repeat(Mathf.Atan2(y, x) * Mathf.Rad2Deg * 0.5f, 180f);
        }

        (int, int) Cell(Vector3 p)
            => (Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize));

        (int, int, int) Voxel(Vector3 p)
            => (Mathf.FloorToInt(p.x / voxelSize), Mathf.FloorToInt(p.y / voxelSize), Mathf.FloorToInt(p.z / voxelSize));

        Vector3 VoxelCenter((int, int, int) v)
            => new Vector3((v.Item1 + 0.5f) * voxelSize, (v.Item2 + 0.5f) * voxelSize, (v.Item3 + 0.5f) * voxelSize);

        bool NearAnyWall(Vector3 pLocal, List<WallSegment> walls, float radius = -1f)
        {
            float r = radius > 0f ? radius : wallThickness * 0.5f + wallExcludeMargin;
            foreach (var w in walls)
                if (DistanceToSegmentXZ(pLocal, w.start, w.end) <= r) return true;
            return false;
        }

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

        /// <summary>
        /// True when XZ point <paramref name="c"/> is inside a wall's box footprint: along
        /// the a→b floor line within <paramref name="pad"/> of its ends, and perpendicular
        /// within one full <paramref name="width"/> plus <paramref name="pad"/> of the near
        /// face on <em>either</em> side. The camera side is claimed too, because that is
        /// where the fat, biased occupancy ridge that produced doubled walls sits.
        /// <paramref name="side"/> is currently unused but kept so a future one-sided claim
        /// is a signature-compatible change.
        /// </summary>
        static bool InWallSlabXZ(Vector2 c, Vector3 aLocal, Vector3 bLocal,
                                 float width, int side, float pad)
        {
            var a = new Vector2(aLocal.x, aLocal.z);
            var d = new Vector2(bLocal.x - aLocal.x, bLocal.z - aLocal.z);
            float len = d.magnitude;
            if (len < 1e-4f) return false;
            var tHat = d / len;
            var nHat = new Vector2(-tHat.y, tHat.x);

            var rel = c - a;
            float u = Vector2.Dot(rel, tHat);
            float w = Mathf.Abs(Vector2.Dot(rel, nHat));
            return u >= -pad && u <= len + pad && w <= width + pad;
        }

        /// <summary>
        /// Removes every occupancy cell inside a just-anchored wall's box from the grid, so
        /// that region of the room is never re-fitted as a wall. Vertical extent is honoured
        /// so a half-height wall does not claim the column above it.
        /// </summary>
        void ClaimWallVolume(Vector3 aLocal, Vector3 bLocal, float width, int side,
                             float baseY, float height)
        {
            float loY = baseY - wallClaimMargin, hiY = baseY + height + wallClaimMargin;
            var drop = new List<(int, int)>();
            foreach (var kv in _hits)
            {
                if (kv.Value.maxY < loY || kv.Value.minY > hiY) continue;
                var c = new Vector2((kv.Key.Item1 + 0.5f) * cellSize, (kv.Key.Item2 + 0.5f) * cellSize);
                if (InWallSlabXZ(c, aLocal, bLocal, width, side, wallClaimMargin)) drop.Add(kv.Key);
            }
            foreach (var k in drop) _hits.Remove(k);
        }

        /// <summary>Origin-local point <paramref name="lp"/> falls inside a resolved wall's box.</summary>
        bool InAnyClaimedWall(Vector3 lp, List<WallSegment> claimed)
        {
            var c = new Vector2(lp.x, lp.z);
            foreach (var w in claimed)
            {
                if (lp.y < w.baseY - wallClaimMargin || lp.y > w.baseY + w.height + wallClaimMargin) continue;
                if (InWallSlabXZ(c, w.start, w.end,
                                 w.width > 0f ? w.width : wallThickness,
                                 w.side != 0 ? w.side : 1, wallClaimMargin))
                    return true;
            }
            return false;
        }

        bool RansacLine(List<Vector2> pts, out Vector2 point, out Vector2 dir, out List<int> inliers)
        {
            point = Vector2.zero; dir = Vector2.right; inliers = null;
            if (pts.Count < 2) return false;

            int best = 0;
            Vector2 bestP = pts[0], bestD = Vector2.right;

            for (int it = 0; it < lineIterations; it++)
            {
                int a = Random.Range(0, pts.Count);
                int b = Random.Range(0, pts.Count);
                if (a == b) continue;

                var d = pts[b] - pts[a];
                if (d.sqrMagnitude < 1e-6f) continue;
                d.Normalize();
                var nrm = new Vector2(-d.y, d.x);

                int cnt = 0;
                for (int k = 0; k < pts.Count; k++)
                    if (Mathf.Abs(Vector2.Dot(nrm, pts[k] - pts[a])) < lineInlierDist) cnt++;

                if (cnt > best) { best = cnt; bestP = pts[a]; bestD = d; }
            }

            if (best < 2) return false;

            var bn = new Vector2(-bestD.y, bestD.x);
            inliers = new List<int>(best);
            for (int k = 0; k < pts.Count; k++)
                if (Mathf.Abs(Vector2.Dot(bn, pts[k] - bestP)) < lineInlierDist) inliers.Add(k);

            point = bestP; dir = bestD;
            return true;
        }
    }
}
