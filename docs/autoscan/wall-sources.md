# Wall sources (`wallSource`)

Where a candidate wall *line* comes from, before `RegularizeToRoomFrame`/`MergeCollinear`/
the diagonal drop/dedupe/[room graph](room-graph.md) ever see it. All five modes just
populate the same `List<(Vector2 a, Vector2 b)> segments` — everything downstream is
source-blind, so switching modes never changes how a candidate becomes (or fails to
become) a built, anchored wall — only what candidates it's fed.

Files: `DepthOccupancyMapper.Reconstruct` (dispatch), `SeedWalls` / `SeedFloorBoundary` /
`DiscoverWalls` (the three fitting methods), `PlaneCollector.VerticalWallSeeds` /
`FloorBoundaryEdges` (AR-plane geometry extraction).

## Choosing and switching modes

Mode is chosen once, mandatorily, in `ScanState.ChooseMode` — right after the origin is
confirmed (`ReviewOrigin` for the auto-detected origin, or `CONFIRM TOP` for the manual
flow) and before `Mapping` begins. The `MODE:` button there cycles `DepthOnly → PlanesOnly
→ Hybrid → FloorRaw → FloorConfirmed → Hybrid`; `CONFIRM MODE` commits and starts mapping.

The same `MODE:` button is still available later, in the Mapping HUD — but pressing it now
**resets the scan**: `RoomScanningManager.ResetScan()` clears the occupancy grid and every
wall/surface (hand-corrected ones included), keeping only the origin/anchor. Each mode is
meant to be its own clean experiment, not five ways of refitting one shared pile of
accumulated depth — mixing, say, Hybrid-fitted geometry into a fresh `FloorRaw` attempt
would defeat comparing them. Switch deliberately; there's no undo.

## The five modes

- **`DepthOnly`** — blind multi-line RANSAC over the occupancy grid (`DiscoverWalls`).
  Accurate but needs enough of a sweep for a wall to have solid evidence.
- **`PlanesOnly`** — trusts ARCore vertical planes outright (`SeedWalls`), no depth
  confirmation at all. Fast, phantom-prone; kept for comparison.
- **`Hybrid`** (default) — vertical planes propose lines (`PlaneCollector.VerticalWallSeeds`:
  plane normal → direction, `plane.boundary` → extent along it), the occupancy grid must
  confirm each one (`seedMinCells`, `seedMinDensityFraction`), then blind RANSAC still runs
  over whatever's left unclaimed.
- **`FloorRaw`** — experimental. Seeds from the ARCore *floor* plane's own boundary polygon
  instead of vertical planes, trusting every edge outright (mirrors `PlanesOnly`'s
  no-confirmation philosophy) — the "the floor is already delimited well, just use it"
  mode.
- **`FloorConfirmed`** — experimental. Same floor-boundary edges, but each needs occupancy
  backing before it becomes a wall (`SeedFloorBoundary`) — the confirmed counterpart to
  `FloorRaw`. Its acceptance bar is its own, separate from Hybrid's vertical-plane gate:
  `floorSeedMinCells` (2) / `floorSeedMinDensityFraction` (0.12), deliberately looser than
  `seedMinCells` (4) / `seedMinDensityFraction` (0.25) — since the floor boundary is already
  trusted geometry, it takes less depth evidence to confirm it than a vertical plane needs.
  Lower either further to trust the floor more (some real walls only get patchy depth
  coverage and were being rejected); raise them back toward Hybrid's bar if that starts
  admitting phantoms.

`FloorRaw`/`FloorConfirmed` are deliberately **pure**: no vertical-plane seeding, no blind
RANSAC in the same pass, so each isolates the floor-boundary idea for a clean three-way
comparison against `DepthOnly`/`PlanesOnly`/`Hybrid` on the same scan.

## The floor's two-stage flow: sweep, confirm, build once

`DepthOnly`/`PlanesOnly`/`Hybrid` all rebuild on the normal 5 s auto-build timer while
mapping, refitting from whatever's accumulated so far. The floor modes don't: "the floor
is king" only means something once the floor is *finished* being scanned, so building from
it early — while ARCore's floor plane is still growing — would just build from a half-drawn
polygon. Instead, entering `Mapping` in a floor mode replaces the usual HUD with a single
prompt: **sweep the floor's full perimeter, then press `CONFIRM FLOOR MAPPED`.** No
automatic rebuild happens before or after that press (`RoomScanningManager` excludes
`DepthOccupancyMapper.IsFloorMode` walls from the auto-build timer entirely) — the confirm
button runs `Reconstruct` (walls only) exactly once; floor mode is about walls, so it
doesn't also run furniture-surface detection the way the manual `BUILD ROOM` button does.
Walking further afterward and waiting does nothing; the manual `BUILD ROOM`/`BUILD WALLS`/
`BUILD SURFACES` buttons (back in the normal HUD once confirmed) are the only way to force
another pass, or to pull in surfaces too, e.g. after sweeping more floor.

`PAUSE SCAN`, `TUNE` (to dial in `floorEdgeMinLength`/`floorSeedMinCells`/
`floorSeedMinDensityFraction` before that one build happens) and `MODE:` stay available
during the sweep; `BUILD`/`EXPORT`/`EDIT WALLS` are hidden until confirmed — there's nothing
yet for them to act on.

## How the floor boundary becomes wall lines

`PlaneCollector.FloorBoundaryEdges(mapOrigin, floorEdgeMinLength)` walks every
floor-classified `ARPlane`'s `boundary` (AR Foundation's own polygon field, plane-local 2D)
as an ordered ring of edges, transforms each to origin-local XZ via the plane's own
transform then `mapOrigin`, and drops any edge shorter than `floorEdgeMinLength` (0.3 m) —
the smallest stairstep artifacts in ARCore's floor mesh never become candidates at all.
This is the same edge-walk `CornerDetector.DetectBoundaryWalls` already implemented — dead
code from the project's very first pipeline commit, never wired to anything — reused here
as the geometry extraction, feeding the shared pipeline instead of building walls directly.

**The floor can fragment into several `ARPlane` trackables** with no merge between them —
this project has no subsumption handling anywhere (`PlaneCollector.LargestFloor` just
re-picks the largest current floor plane each call) — so every floor-classified plane's
boundary is walked independently. A seam edge where two fragments meet has no wall there:
`FloorConfirmed` rejects it because nothing backs it with depth, but `FloorRaw` has no such
check and is expected to build a phantom wall over it, or over any stairstep the min-length
filter didn't catch. That's the known, deliberate risk of trusting the floor outright —
it's what the comparison against `FloorConfirmed`/`Hybrid` is for.

## Known limits

- Neither floor-boundary mode has shipped as a default — evaluate on device before
  considering `FloorConfirmed` as a `Hybrid` replacement.
- `CornerDetector.DetectBoundaryWalls` / `RoomBuilder.AddWall` / `AddWalls` remain dead
  code; only the edge-walking idea was reused, not the methods themselves.
