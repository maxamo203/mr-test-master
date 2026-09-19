using System.Collections.Generic;
using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// Assembles the geometric <see cref="RoomModel"/> from detected corners/walls
    /// and renders the corresponding wall quads in the scene under the map origin.
    /// This is the only component that mutates the shared <see cref="Room"/> model.
    ///
    /// Replaces: the wall-building half of <c>DepthWallAnalyzer</c>
    /// (BuildCornerWalls, EmitWallQuad, CreateWallMesh, duplicate tracking).
    /// </summary>
    public class RoomBuilder : MonoBehaviour
    {
        [Tooltip("Template for wall surfaces. Wire WallEdgeGrid.mat — the reference game's " +
                 "own wall material — so a scanned wall looks the same in both apps. " +
                 "Left empty, AutoWallObject builds an equivalent at runtime.")]
        [SerializeField] Material wallMaterial;

        [Tooltip("Template for furniture boxes. Wire CubeEdgeGrid.mat. Left empty, the " +
                 "wall material is used, and failing that a runtime equivalent.")]
        [SerializeField] Material furnitureMaterial;

        [SerializeField] float duplicateRadius = 0.50f;

        /// <summary>
        /// Template for furniture, falling back to the wall material so a scene wired
        /// before this field existed keeps working rather than losing its material.
        /// </summary>
        Material FurnitureTemplate => furnitureMaterial != null ? furnitureMaterial : wallMaterial;

        Transform _wallRoot;
        Transform _restoredRoot;
        Transform _outlineRoot;
        Transform _mapOrigin;

        /// <summary>Draw the traced room outline + corner posts. Set by the mapper.</summary>
        bool _drawOutline = true;

        [Tooltip("Thickness (m) used when rebuilding an edited wall or adding one by hand. " +
                 "Match DepthOccupancyMapper.wallThickness so manual and automatic walls agree.")]
        [SerializeField] float manualWallThickness = 0.12f;

        readonly List<GameObject> _wallObjects = new();
        readonly List<GameObject> _furnitureObjects = new();
        readonly List<GameObject> _restoredObjects = new();
        readonly List<Vector3> _wallCentres = new();

        /// <summary>Rendered object for each wall segment, keyed by <see cref="WallSegment.id"/>.</summary>
        readonly Dictionary<int, GameObject> _wallById = new();
        int _nextWallId;

        /// <summary>Rendered object for each furniture box, keyed by <see cref="FurnitureBox.id"/>.</summary>
        readonly Dictionary<int, GameObject> _furnitureById = new();
        int _nextFurnitureId;

        /// <summary>Rendered quad for each horizontal surface, keyed by <see cref="HorizontalSurface.id"/>.</summary>
        readonly Dictionary<int, GameObject> _surfaceById = new();
        int _nextSurfaceId;

        /// <summary>The model accumulated so far. Populated as walls/doors are added.</summary>
        public RoomModel Room { get; } = new RoomModel { scanTime = System.DateTime.Now.ToString("o") };

        /// <summary>True while a previously saved room is being displayed alongside the live scan.</summary>
        public bool HasRestoredRoom => _restoredObjects.Count > 0;

        void Awake()
        {
            _wallRoot = new GameObject("EstimatedWalls").transform;
            _restoredRoot = new GameObject("RestoredRoom").transform;
            _outlineRoot = new GameObject("RoomOutline").transform;
        }

        /// <summary>Bind the builder to the confirmed map origin anchor.</summary>
        public void SetOrigin(Transform mapOrigin, float floorY)
        {
            _mapOrigin = mapOrigin;
            _wallRoot.SetParent(_mapOrigin, worldPositionStays: false);
            _wallRoot.localScale = Vector3.one;
            _restoredRoot.SetParent(_mapOrigin, worldPositionStays: false);
            _restoredRoot.localScale = Vector3.one;
            _outlineRoot.SetParent(_mapOrigin, worldPositionStays: false);
            _outlineRoot.localScale = Vector3.one;
            Room.floorY = floorY;
        }

        /// <summary>Enable/disable the room-outline debug overlay (mapper-controlled).</summary>
        public void SetDrawOutline(bool on)
        {
            _drawOutline = on;
            if (!on) ClearOutlineView();
        }

        // ── Corner walls (the two walls fitted at the origin corner) ──────────

        /// <summary>
        /// Records the room height and renders the two walls fitted at the origin
        /// corner as quads spanning floor→ceiling.
        /// </summary>
        public void BuildCornerWalls(Vector3 cornerWorld, WallFit wallA, WallFit wallB, float roomHeight)
        {
            if (_mapOrigin == null) return;
            Room.roomHeight = roomHeight;

            Room.corners.Add(new Corner
            {
                position = _mapOrigin.InverseTransformPoint(cornerWorld),
                wallA = wallA.direction,
                wallB = wallB.direction
            });

            EmitWallQuad(cornerWorld, wallA, roomHeight);
            EmitWallQuad(cornerWorld, wallB, roomHeight);
        }

        void EmitWallQuad(Vector3 cornerWorld, WallFit wall, float height)
        {
            float min = wall.min, max = wall.max;
            if (max - min < 0.3f) { min -= 0.3f; max += 0.3f; } // guarantee a visible span

            var up = Vector3.up;
            var wMin = cornerWorld + wall.direction * min;
            var wMax = cornerWorld + wall.direction * max;

            var lMin = _mapOrigin.InverseTransformPoint(wMin);
            var lMax = _mapOrigin.InverseTransformPoint(wMax);

            var v = new[]
            {
                lMin,
                lMax,
                _mapOrigin.InverseTransformPoint(wMax + up * height),
                _mapOrigin.InverseTransformPoint(wMin + up * height),
            };

            int id = _nextWallId++;
            var go = SpawnWallMesh("CornerWall_" + id, v, 0.06f);
            Register(id, go);
            Room.walls.Add(new WallSegment { start = lMin, end = lMax, height = height, id = id });
            Debug.Log($"[RoomBuilder] Corner wall built: span {max - min:F2}m × {height:F2}m high.");
        }

        // ── Mapped walls (floor-boundary clusters) ────────────────────────────

        /// <summary>Confirm and render a wall found along a floor boundary edge.</summary>
        public void AddWall(DetectedWall wall)
        {
            if (_mapOrigin == null) return;
            if (IsDuplicate(wall.centre)) return;

            var normal = wall.normal;
            var centre = wall.centre;

            var right = Vector3.Cross(normal, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(normal, Vector3.forward).normalized;
            var up = Vector3.Cross(right, normal).normalized;

            float minR = float.MaxValue, maxR = float.MinValue;
            float minU = float.MaxValue, maxU = float.MinValue;
            foreach (var p in wall.points)
            {
                var d = p - centre;
                float r = Vector3.Dot(d, right), u = Vector3.Dot(d, up);
                minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
            }

            float padR = Mathf.Max((maxR - minR) * 0.05f, 0.05f);
            float padU = Mathf.Max((maxU - minU) * 0.05f, 0.05f);
            minR -= padR; maxR += padR; minU -= padU; maxU += padU;

            var verts = new[]
            {
                centre + right * minR + up * minU,
                centre + right * maxR + up * minU,
                centre + right * maxR + up * maxU,
                centre + right * minR + up * maxU,
            };

            int id = _nextWallId++;
            Register(id, SpawnWallMesh("DepthWall_" + id, verts, 0.04f));
            _wallCentres.Add(centre);
            Room.walls.Add(new WallSegment
            {
                start = centre + right * minR,
                end = centre + right * maxR,
                height = maxU - minU,
                id = id
            });
        }

        /// <summary>Confirm and render a batch of detected walls.</summary>
        public void AddWalls(IEnumerable<DetectedWall> walls)
        {
            foreach (var w in walls) AddWall(w);
        }

        // ── Auto-reconstructed solid wall boxes (DepthOccupancyMapper) ─────────

        /// <summary>
        /// Builds a wall from floor segment a→b extruded to <paramref name="height"/> with
        /// <paramref name="thickness"/>. The segment is the wall's *near* face and it grows
        /// away from the camera, matching the convention the game's scan format uses so a
        /// wall survives an export/import round trip without moving.
        /// </summary>
        /// <param name="sideHint">Non-zero forces the extrusion side (so a wall's box does
        /// not flip as the camera moves around it once the side is known); 0 decides it
        /// from the current camera position.</param>
        public int BuildWallBox(Vector3 aLocal, Vector3 bLocal, float height, float thickness,
                                IReadOnlyList<WallOpening> openings = null, bool anchored = false,
                                int sideHint = 0)
        {
            if (_mapOrigin == null) return -1;

            int id = _nextWallId++;
            int side = sideHint != 0 ? sideHint : DecideSideForViewer(aLocal, bLocal);
            // Openings go in at construction so a wall with doorways is meshed once.
            var wall = AutoWallObject.Create(_wallRoot, id, aLocal, bLocal, height, thickness, side,
                                         wallMaterial, openings);
            if (wall == null) return -1;

            // An anchored automatic wall is frozen, but it still reads as an automatic wall:
            // blue is reserved for walls the user corrected by hand, so the two are not
            // confused. The freeze is a background optimisation, not a state to display.
            wall.SetTint(WallResting, WallResting.a);
            Register(id, wall.gameObject);
            Room.walls.Add(new WallSegment
            {
                start = aLocal, end = bLocal, height = height,
                id = id, width = thickness, side = side, pinned = anchored,
                openings = openings != null && openings.Count > 0
                    ? new List<WallOpening>(openings) : null
            });
            return id;
        }

        /// <summary>
        /// Extrusion side that puts the wall's near face toward the camera, so the surface
        /// you are looking at is the one that was measured. Falls back to +1 with no camera.
        /// </summary>
        int DecideSideForViewer(Vector3 aLocal, Vector3 bLocal)
        {
            var cam = Camera.main;
            if (cam == null || _mapOrigin == null) return 1;
            return AutoWallObject.DecideSide(aLocal, bLocal, _mapOrigin.InverseTransformPoint(cam.transform.position));
        }

        // Wall geometry now comes from AutoWallMeshBuilder via AutoWallObject. The old centred-box
        // vertex helper is gone deliberately: two wall-geometry paths with different
        // extrusion conventions would disagree by half a wall thickness.

        // Box geometry now comes from FurnitureObject, walls from AutoWallObject. The old
        // shared SpawnBox/CubeVerts/BoxTris helper is gone deliberately: it welded eight
        // corners, so RecalculateNormals averaged three perpendicular faces into a single
        // diagonal normal, and keeping a second box path around is how the two
        // conventions drift apart again.

        // An untouched, automatically detected wall is drawn in the reference game's own
        // resting colour, so "a wall here" and "a wall there" are the same picture — that
        // is the whole point of sharing the game's shader and material. The state colours
        // below are Mortuorium's own: they mark facts (pinned, selected, restored) that the
        // game has no concept of, and each one is a departure from parity on purpose.
        static readonly Color WallResting   = ScanMaterials.GameWallFill;
        static readonly Color FurnitureAmber = new Color(1f, 0.75f, 0.15f, 0.90f);
        // Restored geometry is deliberately a different hue from the live scan: overlapping
        // the two is how you judge whether relocalization actually landed.
        static readonly Color RestoredWall  = new Color(0.70f, 0.35f, 1f, 0.90f);
        static readonly Color RestoredProp  = new Color(0.25f, 0.85f, 1f, 0.90f);
        // Hand-corrected walls read as blue against the automatic green, so it is obvious
        // at a glance which walls a rebuild will leave alone; the selected one goes yellow.
        static readonly Color PinnedWall    = new Color(0.25f, 0.55f, 1f, 0.90f);
        // The game's own selection cyan, so "this one is selected" reads the same in both.
        static readonly Color SelectedWall  = ScanMaterials.GameSelected;

        /// <summary>
        /// Tears down built wall meshes (used before re-reconstructing). With
        /// <paramref name="keepPinned"/> the user's hand-corrected walls are left
        /// standing — both the mesh and the <see cref="RoomModel"/> entry — so an
        /// automatic rebuild refits only the walls nobody has touched.
        /// </summary>
        public void ClearWalls(bool keepPinned = false)
        {
            // Corners and the outline are recomputed from scratch every reconstruction and
            // are never pinned, so they always go — this is also where the old stale-corner
            // accumulation bug is stopped.
            Room.corners.Clear();
            Room.outline.Clear();
            Room.outlineClosed = false;
            ClearOutlineView();

            for (int i = Room.walls.Count - 1; i >= 0; i--)
            {
                if (keepPinned && Room.walls[i].pinned) continue;
                DestroyWallObject(Room.walls[i].id);
                Room.walls.RemoveAt(i);
            }

            // Anything still registered has no surviving segment (legacy paths that
            // spawned a mesh without one) — drop it so meshes cannot outlive the model.
            if (!keepPinned)
            {
                foreach (var kv in _wallById) if (kv.Value) Destroy(kv.Value);
                _wallById.Clear();
                _wallObjects.Clear();
                _wallCentres.Clear();
            }
        }

        // ── Manual wall editing ───────────────────────────────────────────────
        //
        // Automatic reconstruction is a full teardown, so a hand-placed correction
        // only survives by being marked pinned. Every mutator here pins the wall it
        // touches; DepthOccupancyMapper then leaves those walls alone and suppresses
        // automatic walls that would duplicate them.

        /// <summary>The walls built so far. Read-only: mutate through this class.</summary>
        public IReadOnlyList<WallSegment> Walls => Room.walls;

        public bool TryGetWall(int wallId, out WallSegment wall)
        {
            int i = IndexOf(wallId);
            if (i < 0) { wall = default; return false; }
            wall = Room.walls[i];
            return true;
        }

        /// <summary>
        /// Moves a wall to a new floor segment and pins it.
        ///
        /// Called every frame of a drag, so the wall rebuilds its mesh in place rather
        /// than respawning — respawning would allocate a GameObject, Mesh and Material
        /// per frame. Older geometry with no <see cref="AutoWallObject"/> (a corner quad) is
        /// replaced by a real wall, which is also what makes it survive the next rebuild.
        /// </summary>
        public bool TryUpdateWall(int wallId, Vector3 startLocal, Vector3 endLocal)
        {
            if (!TryGetWall(wallId, out var wall)) return false;
            return TryUpdateWallGeometry(wallId, startLocal, endLocal,
                                         wall.baseY, wall.height, wall.width);
        }

        /// <summary>
        /// Moves a wall's whole box — footprint, vertical extent and thickness — and pins it.
        ///
        /// This is the one mutator corner editing goes through, so every hand edit reaches
        /// the model and the mesh by the same route: <see cref="TryUpdateWall"/> is a thin
        /// wrapper that holds the vertical extent still. Like it, this is called every frame
        /// of a drag, so an existing <see cref="AutoWallObject"/> is rebuilt in place instead of
        /// respawned.
        /// </summary>
        /// <param name="baseY">Bottom edge above the origin floor (m); 0 is floor-standing.</param>
        /// <param name="height">Box height (m), measured up from <paramref name="baseY"/>.</param>
        /// <param name="width">Thickness (m); ≤0 falls back to the configured manual thickness.</param>
        public bool TryUpdateWallGeometry(int wallId, Vector3 startLocal, Vector3 endLocal,
                                          float baseY, float height, float width)
        {
            int i = IndexOf(wallId);
            if (i < 0 || _mapOrigin == null) return false;

            var wall = Room.walls[i];

            // Clamp first, then place the endpoints: the model and the mesh must agree on
            // the box, and the same limits AutoWallObject applies are applied here so the
            // stored WallSegment is never a box the renderer would refuse to build.
            // Height and thickness are clamped rather than refused — a drag naturally
            // passes through "too short", and stopping it dead there is worse than pinning
            // it at the minimum and letting the finger carry on.
            baseY = Mathf.Max(0f, baseY);
            height = Mathf.Max(AutoWallObject.MinHeight, height);
            width = width > 0f ? Mathf.Max(AutoWallMeshBuilder.MinDimension, width) : manualWallThickness;

            // Endpoints define the bottom edge, so they sit on the base plane by definition.
            startLocal.y = baseY; endLocal.y = baseY;

            // A degenerate footprint is refused instead, rather than losing the wall to a
            // null mesh: there is no sensible minimum to snap a length to.
            if ((endLocal - startLocal).magnitude < AutoWallMeshBuilder.MinDimension) return false;

            int side = wall.side != 0 ? wall.side : DecideSideForViewer(startLocal, endLocal);

            var existing = WallComponent(wallId);
            if (existing != null)
            {
                existing.SetGeometry(startLocal, endLocal, baseY, height, width);
            }
            else
            {
                // Older geometry with no AutoWallObject (a corner quad) is replaced by a real
                // wall, which is also what makes it survive the next rebuild.
                DestroyWallObject(wallId);
                var created = AutoWallObject.Create(_wallRoot, wallId, startLocal, endLocal,
                                                height, width, side, wallMaterial,
                                                wall.openings, baseY);
                if (created == null) return false;
                created.SetTint(PinnedWall, 0.26f);
                Register(wallId, created.gameObject);
            }

            wall.start = startLocal;
            wall.end = endLocal;
            wall.baseY = baseY;
            wall.height = height;
            wall.width = width;
            wall.side = side;
            wall.pinned = true;
            Room.walls[i] = wall;   // value type: write the struct back
            return true;
        }

        /// <summary>Adds a hand-drawn wall, already pinned. Returns its id, or -1.</summary>
        public int AddManualWall(Vector3 startLocal, Vector3 endLocal, float height)
        {
            if (_mapOrigin == null) return -1;
            startLocal.y = 0f; endLocal.y = 0f;

            if ((endLocal - startLocal).magnitude < AutoWallMeshBuilder.MinDimension) return -1;

            int id = _nextWallId++;
            int side = DecideSideForViewer(startLocal, endLocal);
            var wall = AutoWallObject.Create(_wallRoot, id, startLocal, endLocal,
                                         height, manualWallThickness, side, wallMaterial);
            if (wall == null) return -1;

            wall.SetTint(PinnedWall, 0.26f);
            Register(id, wall.gameObject);
            Room.walls.Add(new WallSegment
            {
                start = startLocal, end = endLocal, height = height, id = id,
                width = manualWallThickness, side = side, pinned = true
            });
            return id;
        }

        /// <summary>
        /// Fuses two walls the auto pass left as separate fragments into one pinned wall,
        /// spanning both along the longer wall's line. The shorter fragment's perpendicular
        /// offset is discarded on purpose — the assumption is that both are pieces of one
        /// real wall. Openings are re-projected onto the new frame. Returns the new id, or -1.
        /// </summary>
        public int CombineWalls(int idA, int idB)
        {
            int i = IndexOf(idA), j = IndexOf(idB);
            if (i < 0 || j < 0 || i == j || _mapOrigin == null) return -1;

            var a = Room.walls[i];
            var b = Room.walls[j];
            var longer = (a.end - a.start).sqrMagnitude >= (b.end - b.start).sqrMagnitude ? a : b;

            var p = longer.start; p.y = 0f;
            var d = longer.end - longer.start; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return -1;
            d.Normalize();

            float T(Vector3 q) { q.y = 0f; return Vector3.Dot(q - p, d); }
            float tMin = Mathf.Min(Mathf.Min(T(a.start), T(a.end)), Mathf.Min(T(b.start), T(b.end)));
            float tMax = Mathf.Max(Mathf.Max(T(a.start), T(a.end)), Mathf.Max(T(b.start), T(b.end)));
            if (tMax - tMin < AutoWallMeshBuilder.MinDimension) return -1;

            float baseY = Mathf.Min(a.baseY, b.baseY);
            var newStart = p + d * tMin; newStart.y = baseY;
            var newEnd = p + d * tMax; newEnd.y = baseY;
            float height = Mathf.Max(a.height, b.height);
            float width = Mathf.Max(a.width, b.width);
            if (width <= 0f) width = manualWallThickness;
            int side = longer.side != 0 ? longer.side : DecideSideForViewer(newStart, newEnd);

            var openings = MergeOpenings(a, b, newStart, d, tMax - tMin);

            int id = _nextWallId++;
            var created = AutoWallObject.Create(_wallRoot, id, newStart, newEnd,
                                            height, width, side, wallMaterial, openings, baseY);
            if (created == null) { _nextWallId--; return -1; }

            created.SetTint(PinnedWall, 0.26f);
            Register(id, created.gameObject);
            Room.walls.Add(new WallSegment
            {
                start = newStart, end = newEnd, height = height, baseY = baseY,
                id = id, width = width, side = side, pinned = true,
                openings = openings.Count > 0 ? openings : null,
            });

            RemoveWall(idA);
            RemoveWall(idB);
            return id;
        }

        /// <summary>Re-expresses both walls' openings in the combined wall's frame (u = m
        /// from <paramref name="newStart"/> along <paramref name="dir"/>), dropping any that
        /// fall outside the new span.</summary>
        static System.Collections.Generic.List<WallOpening> MergeOpenings(
            in WallSegment a, in WallSegment b, Vector3 newStart, Vector3 dir, float newLen)
        {
            var result = new System.Collections.Generic.List<WallOpening>();
            AddReprojected(a, newStart, dir, newLen, result);
            AddReprojected(b, newStart, dir, newLen, result);
            return result;
        }

        static void AddReprojected(in WallSegment src, Vector3 newStart, Vector3 dir,
                                   float newLen, System.Collections.Generic.List<WallOpening> into)
        {
            if (src.openings == null) return;
            var s = src.start; s.y = 0f;
            var sdir = src.end - src.start; sdir.y = 0f;
            if (sdir.sqrMagnitude < 1e-6f) return;
            sdir.Normalize();
            var ns = newStart; ns.y = 0f;

            foreach (var o in src.openings)
            {
                float u0 = Vector3.Dot((s + sdir * o.uMin) - ns, dir);
                float u1 = Vector3.Dot((s + sdir * o.uMax) - ns, dir);
                float lo = Mathf.Clamp(Mathf.Min(u0, u1), 0f, newLen);
                float hi = Mathf.Clamp(Mathf.Max(u0, u1), 0f, newLen);
                if (hi - lo < 0.05f) continue;
                into.Add(new WallOpening { uMin = lo, uMax = hi, vMin = o.vMin, vMax = o.vMax, kind = o.kind });
            }
        }

        /// <summary>Deletes a wall outright — used to remove phantoms the scan invented.</summary>
        public bool RemoveWall(int wallId)
        {
            int i = IndexOf(wallId);
            if (i < 0) return false;
            DestroyWallObject(wallId);
            Room.walls.RemoveAt(i);
            return true;
        }

        /// <summary>Tints a wall to show selection; pass false to restore its resting colour.</summary>
        public void SetWallHighlight(int wallId, bool selected)
        {
            if (!TryGetWall(wallId, out var wall)) return;

            var tint = selected ? SelectedWall : (wall.pinned ? PinnedWall : WallResting);
            // Resting alpha is the game's own, so deselecting returns the wall to exactly
            // the material's authored look rather than to a Mortuorium-only approximation.
            float alpha = selected ? 0.45f : (wall.pinned ? 0.26f : WallResting.a);

            var component = WallComponent(wallId);
            if (component != null) { component.SetTint(tint, alpha); return; }

            // Older geometry with no AutoWallObject (a corner quad) still tints directly, via
            // the same shader-aware helper — it may be on either shader too.
            if (!_wallById.TryGetValue(wallId, out var go) || go == null) return;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            ScanMaterials.Tint(renderer.material, tint, alpha);
        }

        /// <summary>The <see cref="AutoWallObject"/> for a wall, or null for legacy quad geometry.</summary>
        public AutoWallObject WallComponent(int wallId)
        {
            if (!_wallById.TryGetValue(wallId, out var go) || go == null) return null;
            return go.GetComponent<AutoWallObject>();
        }

        /// <summary>The <see cref="FurnitureObject"/> for a box, or null if it is gone.</summary>
        public FurnitureObject FurnitureComponent(int furnitureId)
        {
            if (!_furnitureById.TryGetValue(furnitureId, out var go) || go == null) return null;
            return go.GetComponent<FurnitureObject>();
        }

        int IndexOf(int wallId)
        {
            for (int i = 0; i < Room.walls.Count; i++)
                if (Room.walls[i].id == wallId) return i;
            return -1;
        }

        void Register(int wallId, GameObject go)
        {
            if (go == null) return;
            _wallById[wallId] = go;
        }

        void DestroyWallObject(int wallId)
        {
            if (!_wallById.TryGetValue(wallId, out var go)) return;
            _wallObjects.Remove(go);
            if (go) Destroy(go);
            _wallById.Remove(wallId);
        }

        // ── Auto-reconstructed furniture cubes (DepthOccupancyMapper) ──────────

        /// <summary>Builds an axis-aligned furniture box centred at <paramref name="centerLocal"/>.
        /// With <paramref name="anchored"/> the box is frozen: <see cref="ClearFurniture"/>
        /// keeps it and the mapper stops re-clustering its voxels.</summary>
        public void BuildFurnitureCube(Vector3 centerLocal, Vector3 sizeLocal, bool anchored = false)
        {
            if (_mapOrigin == null) return;

            int id = _nextFurnitureId++;
            var box = FurnitureObject.Create(_wallRoot, id, centerLocal, sizeLocal, FurnitureTemplate);
            if (box == null) return;

            box.SetTint(FurnitureAmber, anchored ? 0.45f : 0.30f);
            _furnitureObjects.Add(box.gameObject);
            _furnitureById[id] = box.gameObject;
            Room.furniture.Add(new FurnitureBox
            {
                center = centerLocal, size = sizeLocal, id = id, pinned = anchored
            });
        }

        // ── Horizontal surfaces (DepthOccupancyMapper) ────────────────────────

        static readonly Color SurfaceCyan = new Color(0.25f, 0.85f, 1f, 0.35f);

        /// <summary>Adds one horizontal placeable surface as a flat quad at
        /// <paramref name="centerLocal"/>. Returns its id, or -1.</summary>
        public int BuildSurface(Vector3 centerLocal, Vector2 sizeLocal, bool anchored = false)
        {
            if (_mapOrigin == null) return -1;

            int id = _nextSurfaceId++;
            var go = new GameObject("Surface_" + id);
            go.transform.SetParent(_wallRoot, worldPositionStays: false);
            go.transform.SetLocalPositionAndRotation(centerLocal, Quaternion.identity);

            float hx = sizeLocal.x * 0.5f, hz = sizeLocal.y * 0.5f;
            var verts = new[]
            {
                new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz),
                new Vector3(hx, 0f, hz),   new Vector3(-hx, 0f, hz),
            };
            var mesh = new Mesh { vertices = verts, triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 } };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().mesh = mesh;
            go.AddComponent<MeshRenderer>().material = OutlineMaterial(
                anchored ? new Color(SurfaceCyan.r, SurfaceCyan.g, SurfaceCyan.b, 0.5f) : SurfaceCyan);

            _surfaceById[id] = go;
            Room.surfaces.Add(new HorizontalSurface
            {
                center = centerLocal, size = sizeLocal, id = id, pinned = anchored
            });
            return id;
        }

        /// <summary>Tears down surface quads. With <paramref name="keepPinned"/> the anchored
        /// ones stay (mesh + model entry).</summary>
        public void ClearSurfaces(bool keepPinned = false)
        {
            for (int i = Room.surfaces.Count - 1; i >= 0; i--)
            {
                if (keepPinned && Room.surfaces[i].pinned) continue;
                int id = Room.surfaces[i].id;
                if (_surfaceById.TryGetValue(id, out var go)) { if (go) Destroy(go); _surfaceById.Remove(id); }
                Room.surfaces.RemoveAt(i);
            }
            if (!keepPinned)
            {
                foreach (var kv in _surfaceById) if (kv.Value) Destroy(kv.Value);
                _surfaceById.Clear();
                Room.surfaces.Clear();
            }
        }

        // ── Restored (previously saved) room ──────────────────────────────────

        /// <summary>
        /// Rebuilds a saved room's geometry into its own root, tinted differently from the
        /// live scan. Deliberately does <em>not</em> touch <see cref="Room"/>: the restored
        /// room is a display layer for judging alignment, and merging it into the live model
        /// would pollute the next autosave. Call after relocalizing, so the saved
        /// coordinates are valid in the current origin frame. Returns objects built.
        /// </summary>
        public int RestoreRoom(RoomModel saved)
        {
            ClearRestored();
            if (_mapOrigin == null || saved == null) return 0;

            float h = saved.roomHeight > 0.1f ? saved.roomHeight : 2.4f;
            int built = 0;

            if (saved.walls != null)
            {
                foreach (var w in saved.walls)
                {
                    // Built the same way as live walls, on purpose: judging alignment means
                    // overlaying these on the green ones, and a different extrusion
                    // convention would offset every wall by half its thickness and make a
                    // good relocalization look bad.
                    var restored = AutoWallObject.Create(_restoredRoot, w.id, w.start, w.end,
                                                     w.height > 0.1f ? w.height : h,
                                                     w.width > 0f ? w.width : manualWallThickness,
                                                     w.side != 0 ? w.side : 1,
                                                     wallMaterial, w.openings, w.baseY);
                    if (restored == null) continue;
                    restored.name = "RestoredWall_" + built;
                    restored.SetTint(RestoredWall, 0.16f);
                    _restoredObjects.Add(restored.gameObject);
                    built++;
                }
            }

            if (saved.furniture != null)
            {
                foreach (var f in saved.furniture)
                {
                    // Same construction as a live box, for the same reason the restored
                    // walls use AutoWallObject: the overlay is only useful for judging
                    // alignment if it sits exactly where a live box would.
                    var prop = FurnitureObject.Create(_restoredRoot, f.id, f.center, f.size, FurnitureTemplate);
                    if (prop == null) continue;
                    prop.name = "RestoredProp_" + built;
                    prop.SetTint(RestoredProp, 0.22f);
                    _restoredObjects.Add(prop.gameObject);
                    built++;
                }
            }

            Debug.Log($"[RoomBuilder] Restored {built} saved object(s) " +
                      $"({saved.walls?.Count ?? 0} walls, {saved.furniture?.Count ?? 0} furniture).");
            return built;
        }

        /// <summary>Removes the restored room display.</summary>
        public void ClearRestored()
        {
            foreach (var go in _restoredObjects) if (go) Destroy(go);
            _restoredObjects.Clear();
        }

        /// <summary>
        /// Tears down built furniture meshes (used before re-reconstructing). With
        /// <paramref name="keepPinned"/> the anchored boxes are left standing — mesh and
        /// model entry — so an automatic rebuild re-clusters only the loose voxels.
        /// </summary>
        public void ClearFurniture(bool keepPinned = false)
        {
            for (int i = Room.furniture.Count - 1; i >= 0; i--)
            {
                if (keepPinned && Room.furniture[i].pinned) continue;
                int id = Room.furniture[i].id;
                if (_furnitureById.TryGetValue(id, out var go))
                {
                    _furnitureObjects.Remove(go);
                    if (go) Destroy(go);
                    _furnitureById.Remove(id);
                }
                Room.furniture.RemoveAt(i);
            }

            if (!keepPinned)
            {
                foreach (var go in _furnitureObjects) if (go) Destroy(go);
                _furnitureObjects.Clear();
                _furnitureById.Clear();
                Room.furniture.Clear();
            }
        }

        // ── Doors ─────────────────────────────────────────────────────────────

        /// <summary>Merge detected door openings into the model.</summary>
        public void AddDoors(IEnumerable<DoorOpening> doors)
        {
            Room.doors.AddRange(doors);
            // TODO: optionally subtract door openings from their host wall meshes.
        }

        // ── Corners + room outline (RoomGraph output) ─────────────────────────

        /// <summary>Replace the model's corner list with the graph's junctions.</summary>
        public void SetCorners(IEnumerable<Corner> corners)
        {
            Room.corners.Clear();
            if (corners != null) Room.corners.AddRange(corners);
        }

        /// <summary>
        /// Replace the model's room outline and redraw the debug overlay. Points are
        /// origin-local at floor level; <paramref name="closed"/> means they enclose a loop.
        /// </summary>
        public void SetRoomOutline(IReadOnlyList<Vector3> pts, bool closed,
                                   IReadOnlyList<(Vector3 a, Vector3 b)> bridges = null)
        {
            Room.outline.Clear();
            if (pts != null) Room.outline.AddRange(pts);
            Room.outlineClosed = closed;
            Room.outlineBridges.Clear();
            if (bridges != null) Room.outlineBridges.AddRange(bridges);
            RebuildOutlineView();
        }

        void ClearOutlineView()
        {
            if (_outlineRoot == null) return;
            for (int i = _outlineRoot.childCount - 1; i >= 0; i--)
                Destroy(_outlineRoot.GetChild(i).gameObject);
        }

        void RebuildOutlineView()
        {
            ClearOutlineView();
            if (!_drawOutline || _outlineRoot == null || Room.outline.Count < 2) return;

            var lineGO = new GameObject("OutlineLine");
            lineGO.transform.SetParent(_outlineRoot, worldPositionStays: false);
            var lr = lineGO.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = Room.outlineClosed;
            lr.widthMultiplier = 0.025f;
            lr.material = OutlineMaterial(Room.outlineClosed ? OutlineClosedColor : OutlineOpenColor);
            lr.positionCount = Room.outline.Count;
            for (int i = 0; i < Room.outline.Count; i++)
            {
                var p = Room.outline[i];
                lr.SetPosition(i, new Vector3(p.x, p.y + 0.01f, p.z));
            }

            // Openings the trace bridged to close the loop — thin dim-cyan, so an inferred
            // span is never mistaken for a real wall edge.
            foreach (var span in Room.outlineBridges)
            {
                var bridgeGO = new GameObject("OutlineBridge");
                bridgeGO.transform.SetParent(_outlineRoot, worldPositionStays: false);
                var blr = bridgeGO.AddComponent<LineRenderer>();
                blr.useWorldSpace = false;
                blr.widthMultiplier = 0.012f;
                blr.material = OutlineMaterial(OutlineBridgeColor);
                blr.positionCount = 2;
                blr.SetPosition(0, span.a + Vector3.up * 0.01f);
                blr.SetPosition(1, span.b + Vector3.up * 0.01f);
            }

            // A short post at each corner, so a junction reads as one point both walls own.
            foreach (var c in Room.corners)
                AddCornerPost(c.position);
        }

        void AddCornerPost(Vector3 baseLocal)
        {
            var go = new GameObject("CornerPost");
            go.transform.SetParent(_outlineRoot, worldPositionStays: false);
            go.transform.localPosition = baseLocal + Vector3.up * 0.15f;
            go.transform.localScale = new Vector3(0.05f, 0.30f, 0.05f);
            go.AddComponent<MeshFilter>().sharedMesh = PostCube();
            go.AddComponent<MeshRenderer>().material = OutlineMaterial(CornerPostColor);
        }

        static readonly Color OutlineClosedColor = new Color(0.35f, 1f, 0.55f, 0.95f);
        static readonly Color OutlineOpenColor   = new Color(1f, 0.7f, 0.2f, 0.95f);
        static readonly Color OutlineBridgeColor = new Color(0.30f, 0.85f, 1f, 0.6f);
        static readonly Color CornerPostColor    = new Color(0.35f, 1f, 0.55f, 0.95f);

        static Material OutlineMaterial(Color c)
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

        static Mesh _postCube;
        static Mesh PostCube()
        {
            if (_postCube != null) return _postCube;
            var v = new[]
            {
                new Vector3(-0.5f,-0.5f,-0.5f), new Vector3(0.5f,-0.5f,-0.5f),
                new Vector3(0.5f, 0.5f,-0.5f),  new Vector3(-0.5f,0.5f,-0.5f),
                new Vector3(-0.5f,-0.5f, 0.5f), new Vector3(0.5f,-0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),  new Vector3(-0.5f,0.5f, 0.5f),
            };
            var t = new[]
            {
                0,2,1, 0,3,2,  4,5,6, 4,6,7,  0,1,5, 0,5,4,
                3,7,6, 3,6,2,  0,4,7, 0,7,3,  1,2,6, 1,6,5,
            };
            _postCube = new Mesh { vertices = v, triangles = t };
            _postCube.RecalculateNormals();
            _postCube.RecalculateBounds();
            return _postCube;
        }

        // ── Mesh + bookkeeping helpers ────────────────────────────────────────

        GameObject SpawnWallMesh(string name, Vector3[] localVerts, float fillAlpha)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_wallRoot, worldPositionStays: false);
            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            var mesh = new Mesh { vertices = localVerts, triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 } };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().mesh = mesh;

            // A flat quad, not a box: it carries none of the baked (u,v,w), so EdgeGrid is
            // told to take its grid from object space. These are the legacy corner quads —
            // the first thing a hand edit replaces with a real AutoWallObject.
            var mat = wallMaterial != null
                ? new Material(wallMaterial)
                : ScanMaterials.CreateRuntime(ScanSurface.Cube);
            // No usable shader at all: take the half-built object down rather than leaving
            // an invisible, unregistered GameObject in the scene.
            if (mat == null) { Destroy(go); return null; }
            ScanMaterials.ConfigureEdgeGrid(mat, ScanSurface.Cube);
            ScanMaterials.Tint(mat, WallResting, fillAlpha);
            if (mat.HasProperty("_LineWidth")) mat.SetFloat("_LineWidth", 0.022f);
            go.AddComponent<MeshRenderer>().material = mat;

            _wallObjects.Add(go);
            return go;
        }

        bool IsDuplicate(Vector3 centre)
        {
            foreach (var c in _wallCentres)
                if (Vector3.Distance(c, centre) < duplicateRadius) return true;
            return false;
        }

        public void Clear()
        {
            foreach (var go in _wallObjects) if (go) Destroy(go);
            foreach (var kv in _wallById) if (kv.Value) Destroy(kv.Value);
            foreach (var go in _furnitureObjects) if (go) Destroy(go);
            _wallObjects.Clear();
            _wallById.Clear();
            _furnitureObjects.Clear();
            _furnitureById.Clear();
            _wallCentres.Clear();
            ClearOutlineView();
            ClearSurfaces();
            Room.walls.Clear();
            Room.corners.Clear();
            Room.outline.Clear();
            Room.outlineClosed = false;
            Room.doors.Clear();
            Room.furniture.Clear();
        }
    }
}
