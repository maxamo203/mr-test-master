# Automatic reconstruction and anchoring

How walls and horizontal surfaces get built during a scan, without the user pressing
anything, and how a piece of geometry stops being refitted once it is trusted.

Files: `RoomScanningManager` (the loop), `DepthOccupancyMapper` (the fitting),
`RoomBuilder` (the scene objects), `RoomModel` (the `pinned` flags).

## The build loop

While `ScanState.Mapping`, `RoomScanningManager.Update` does two things on timers:

- **Accumulate** every `mappingInterval` (0.3 s) — folds one depth cycle into the 2D wall
  occupancy grid and the 3D voxel grid.
- **Auto-build** every `autoBuildInterval` (5 s), when `autoBuild` is on — `AutoBuild()`
  refits walls, and detects surfaces too only if `autoBuildSurfaces` is on (**off by
  default**). The `BUILD ROOM` / `BUILD WALLS` / `BUILD SURFACES` buttons stay as a manual
  force and always do the full job.

Both pause during `ADJUST ORIGIN` and `EDIT WALLS`: the grid must not shift under a
correction in progress. **PAUSE SCAN** (Mapping HUD, top of the left column) freezes both
without entering an edit mode — the prompt shows `PAUSED` — while the manual `BUILD` buttons
still work. `RESUME SCAN` unfreezes; a fresh scan always starts unpaused.

Auto-build is also off entirely for the `FloorRaw`/`FloorConfirmed` wall sources — those
build once, on a `CONFIRM FLOOR MAPPED` press, never on the timer. See
[wall-sources.md](wall-sources.md).

## BuildRoom: three passes

Walls and surfaces each want the other first — walls exclude surface footprints, surfaces
exclude wall areas. `BuildRoom()` breaks the cycle:

1. `Reconstruct` — walls from raw occupancy.
2. `ReconstructSurfaces` — horizontal patches with wall areas excluded.
3. `Reconstruct` again — walls refitted with the fresh surface footprints excluded.

Then `AutoSave` writes the reloadable room. Each `Reconstruct` also runs the
[room graph](room-graph.md): intersecting walls snap to shared corners and a room outline
is traced.

## Where a candidate wall line comes from (`wallSource`)

Five interchangeable sources feed the same candidate `segments` list before regularisation
— see [wall-sources.md](wall-sources.md) for all of them, including the two experimental
floor-boundary modes. Every later stage (merge, diagonal drop, dedupe, the room graph and
its collinear fusion, anchoring) is source-blind.

## Wall regularisation (`snapToRightAngles`)

Before merging, `RegularizeToRoomFrame` puts every wall on **one** frame: a single room
orientation (length-weighted circular mean over the mod-90 angle space, from the pinned
walls alone if any exist, low-passed across rebuilds), each near-aligned wall rotated to
`frame + k·90`, then near-equal perpendicular offsets (`wallOffsetCluster`, 0.15 m) pulled
onto one line. Two scan fragments of the same wall then come out identical, so
`MergeCollinear` fuses them — and because a regularised collinear group is genuinely one
wall, it bridges gaps up to `coplanarMaxGap` (5 m): door-sized gaps become openings, the
rest stays solid. An alcove whose two walls share a line is the price — turn the toggle off
if that bites.

## Cleaning up automatic walls (`dropUnattachedWalls`)

Right after `MergeCollinear` and before the room graph, two cleanups run (both gated by
this one toggle; turn it off for a genuinely non-orthogonal room):

- **Diagonal drop** — a segment still more than `snapAngleTolerance` (20°) off the nearest
  `k·90°` of the room frame is removed, the same test `RegularizeToRoomFrame` uses to leave
  a segment un-snapped. In a rectilinear room these are ridge phantoms, not walls. Needs
  `snapToRightAngles` on and a room frame established. Logged as `N loose skipped`.
- **Duplicate merge** — when `MergeCollinear` fails to fuse two near-collinear overlapping
  fragments of one wall (offset just over `mergeOffsetTolerance`), both would build and one
  usually snaps inward at a junction. `DedupeOverlappingWalls` keeps the longer of each
  pair `SameWallLine` calls the same wall. Logged as `N duplicate(s) merged`.

Pinned / hand-added walls are never in this set, so dividers are untouched.

A third, distinct reduction happens one stage later, inside the room graph itself: two
fragments of the same straight wall that both survive to here get **fused into one wall**
rather than built as two — see "Collinear fusion" in [room-graph.md](room-graph.md). That
one is not gated by `dropUnattachedWalls`; it has its own `collinearMergeAngle`.

## Claiming a built wall's ridge (`claimBuiltWallFootprint`)

Every non-anchored wall built in a pass is remembered in `_wallSeen`. On the next pass
`CollectCells` drops any cell within `wallRidgeClaimMargin` (0.30 m) of one of those wall
lines but outside a `wallRidgeCoreKeep` (0.08 m) core — so the wall keeps refitting from
its centreline while the wide camera-biased occupancy ridge beside it, where phantoms
spawn, is withheld. Anchored and pinned walls already claim their whole box
(`ClaimWallVolume`); this brings the same protection forward to the first pass. A wall that
is deleted or moved in `EDIT WALLS` leaves `_wallSeen` on the next rebuild, so its area
frees up again.

The built wall mesh is also floated `WallObject.RenderNudge` (5 cm) toward the camera off
its measured near face, so environment-depth occlusion does not swallow it against the real
wall — the model segment and mesh verts stay on the detection line. It has to be big enough
to beat ARCore's own depth error (a few cm), not just avoid exact z-fighting — an earlier,
much smaller nudge (3 mm) was below that noise floor and did nothing.

## Wall vs surface: the column profile

A row of chair backs, a line of cabinets or a stack of boxes is a run of near-vertical
faces in the wall band — to the 2D line fitter it looks exactly like a wall. `CollectCells`
keeps a wall cell only when the 3D voxel column at that XZ (± one voxel) has the vertical
profile of a wall, tested by `ColumnLooksLikeWall`:

- **reach** — the highest occupied voxel layer is at or above `wallTopReachFraction` (0.6)
  of the room height. A wall runs to the ceiling; furniture tops out below it.
- **continuity** — at least `wallColumnFillFraction` (0.45) of the layers from the wall
  band up to that highest layer are occupied. A wall is a continuous vertical run; a low
  furniture band plus one stray high point (depth noise, a reflection) passes reach but
  fails this, because of the empty gap between.

A voxel only counts toward either test once it has been seen across `wallColumnMinFrames`
(2) separate accumulate cycles — a single noisy frame's flying-pixel specks near the
ceiling must not let a chair or a stack of boxes pass reach. Set it to 1 for the old
single-glance behaviour (walls appear a touch faster, furniture leaks in more easily).

`OnWallColumn` runs the same test on the surface side, so a tall chair back does not mask
its own voxels out of surface detection. Lower `wallTopReachFraction` if eye-level-only
walls go missing; raise it if a wardrobe still becomes a wall. Lower `wallColumnFillFraction`
if a sparsely-scanned wall is rejected.

## Horizontal surfaces (spawn targets)

Furniture is **not** reconstructed as a bounding box any more — that was unreliable. The
aim is somewhere to spawn things, so `ReconstructSurfaces` produces the **horizontal
surfaces**: table tops, seats, shelves, each a coarse axis-aligned XZ rectangle at one
height (`RoomModel.surfaces`, drawn as a flat cyan quad).

Every point is voxelised in `Accumulate` and tagged horizontal or vertical from its normal
(`VoxInfo`). Like the wall grid, a voxel's `hits`/`horiz`/`vert` counts are folded in **once
per frame** (`_frameVoxels`, mirroring `_frameCells`) — a single noisy depth cycle can no
longer satisfy `minSurfaceHits` on its own; the threshold means distinct frames observed,
not raw samples in one glance. Because it is now a per-frame count, `1`–`2` is the useful
range for `minSurfaceHits` / `minVoxelHits`; `3`+ makes a surface need several passes
before it appears and starves the wall pass of footprints to exclude. `ReconstructSurfaces`:

1. keeps voxels that are **horizontal-dominant** (`vi.horiz >= minSurfaceHits && horiz >=
   vert`), sit between `surfaceMinHeight` above the floor and `surfaceMaxHeightFraction` of
   the ceiling, and are clear of walls (`NearAnyWall` / `OnWallColumn`) and of anchored
   surfaces;
2. flood-fills them into patches (26-connected); a patch of `minSurfaceVoxels`+ becomes a
   candidate surface at the patch's XZ bounding box and mean height;
3. rejects a patch wider than `maxBoxDimension` (the floor, or several merged) or narrower
   than `minBoxDimension` (noise);
4. rejects a patch whose voxel count is below `surfaceFillFraction` (0.5) of its bounding
   box's footprint area — a sparse, scattered flood-fill that only *looks* big is not a real
   flat surface, just noise spread across space.

A surface that stays put for `autoAnchorHits` rebuilds (`_surfSeen` / `autoAnchorTolerance`)
is anchored: `ClearSurfaces(keepPinned)` keeps it and its voxels are excluded next time.
`CollectCells` drops wall cells under a surface footprint. The game export writes each
surface as a thin slab whose top sits at the surface height.

## Anchoring: freezing trusted geometry

A wall or surface that keeps rebuilding in the same place is **anchored** after
`autoAnchorHits` (3) consecutive rebuilds within `autoAnchorTolerance` (0.20 m). Anchoring
sets `WallSegment.pinned` / `HorizontalSurface.pinned`, which means:

- `RoomBuilder.ClearWalls(keepPinned)` / `ClearSurfaces(keepPinned)` leave it standing.
- `DuplicatesPinnedWall` suppresses any automatic wall that restates it — the parallel
  tolerance widens to `width + wallClaimMargin`, so a phantom drawn from the camera-side
  half of the wall's own occupancy ridge is caught, not built as a second wall.
- Its **whole box volume is claimed** (`InWallSlabXZ`, `wallClaimMargin` = 0.12 m on every
  face): `Accumulate` stops adding new wall cells inside it, `CollectCells` drops any that
  are there, and `ClaimWallVolume` deletes the enclosed `_hits` cells outright on anchor.
  That region of the room is resolved — no new walls at that x/y/z.
- The extrusion `side` is frozen once known (`_wallSeen` carries it), so the box does not
  flip across the floor line as the camera moves around the wall.
- `InFrozenSurface` drops an anchored surface's voxels from future patches.

Anchored automatic walls keep the normal resting colour — **blue is reserved for walls
the user corrected by hand**, so the two are never confused. The freeze is a background
optimisation, not a state worth showing. Stability counters live in `DepthOccupancyMapper`
(`_wallSeen`, `_surfSeen`) and reset with `ResetGrid`.

`SameWallLine` is the match test: near-parallel (`mergeMaxAngle`), near-collinear
(`autoAnchorTolerance`), overlapping by at least `pinnedOverlapMin`.

Raise `autoAnchorHits` to make anchoring slower/more conservative, or set it to 0 to disable.

The hit count used to reset to 0 the moment a wall failed to match on any single rebuild —
walked past, briefly occluded, one noisy cycle — undoing several good passes at once.
`autoAnchorMissGrace` (1) now lets a wall miss up to that many consecutive rebuilds without
losing progress; `_wallSeen` carries the stale entry (last known position, unchanged hit
count) forward for those extra passes instead of dropping it outright. Missing for longer
than the grace still forgets it, exactly as before. Set to 0 to reproduce the old behaviour.

## Known limits

- Surfaces are axis-aligned XZ rectangles at one height — no orientation, no per-surface
  slope, and a shelf unit is several stacked rectangles rather than one object.
- Anchoring is one-way. A wall anchored slightly wrong must be fixed in `EDIT WALLS`
  (delete or drag) — the automatic pass will not revisit it.
