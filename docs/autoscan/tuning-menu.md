# On-device detection tuning menu

`DetectionTuningMenu` is an IMGUI panel for the wall / surface detection thresholds,
so they can be dialled in against a live scan without rebuilding the APK (~10 min a
turn otherwise).

Files: `DetectionTuningMenu` (the panel), `DepthOccupancyMapper.Tuning` (the transfer
struct), `RoomScanningManager` (`TUNE` button, `RebuildRoom` / `ResetScan` hooks).

## Opening it

`RoomScanningManager` adds the component to XR Origin at `Awake` if the scene has none
(same pattern as `WallEditor` / `ScanDataExporter`), and auto-wires `mapper` / `manager`
by `GetComponent`. A `TUNE` button appears in the Mapping button column; it toggles the
panel on the right half of the screen. The panel only draws while mapping and not mid
wall-edit or origin-adjust. While it is open the Mapping HUD hides the right-column
buttons (`MODE`, `EDIT WALLS`, `ADJUST ORIGIN`, relocalize) and narrows its status line so
nothing sits under the panel — close `TUNE` to get them back.

`HELP: ON/OFF` in the panel header expands a one-line explainer under every knob: what it
does and what raising or lowering it means.

## Applying a change

- **APPLY + REBUILD** — writes the knobs into `DepthOccupancyMapper` and re-runs the full
  reconstruction. The occupancy grid is kept, so this is near-instant. Use it for every
  knob under *Wall column*, *Wall fit* and *Surfaces*.
- **APPLY + RESET GRID** — writes the knobs, then clears the occupancy grid and all
  non-pinned geometry. Needed for the *Wall band* knobs (`wallNormalMax`, `bandBottom`,
  `bandTopMargin`, `minCellHits`): they are consumed while depth is accumulated, so old
  cells were binned under the old values. You must re-walk the room afterwards.

## Keeping values

- **DEFAULTS** loads the compile-time defaults into the panel (does not apply).
- **SAVE** / **LOAD** round-trip the current knobs through `PlayerPrefs` as JSON, so a
  tuned set survives the app being killed.
- Nothing is ever written back to `ArBasicScene.unity`. Once the numbers are good, copy
  them into the `DepthOccupancyMapper` block of the scene by hand — the scene stays the
  single source of truth.

## The knobs

Grouped in the panel as *Wall band*, *Wall column*, *Wall fit*, *Anchoring*, *Surfaces*. Each maps
one-to-one to a `[SerializeField]` on `DepthOccupancyMapper`; see
[auto-reconstruction.md](auto-reconstruction.md) for what each one does. `wallColumnMinFrames`
is how many frames a voxel must be seen across before it counts toward a wall column — the
lever for "furniture is being read as a wall".

*Wall fit* also carries `bridge gap` (`maxBridgeGap` — how wide an opening the room outline
may still close over), `drop loose walls` (`dropUnattachedWalls` — a `BoolRow` toggle: drop
diagonal automatic walls *and* merge duplicate overlapping ones; off for a non-orthogonal
room), and the built-footprint claim:
`claim built footprint` (`claimBuiltWallFootprint`), `ridge claim margin`
(`wallRidgeClaimMargin`) and `ridge core keep` (`wallRidgeCoreKeep`) — the band beside a
wall built last pass that new detection is ignored in, and the core on the line that is
kept so the wall still refits. `collinear merge angle` (`collinearMergeAngle`) is separate
from the toggle above: how straight two end-to-end fragments must be, in the room graph,
to fuse into one wall instead of building as two — see
[room-graph.md](room-graph.md#algorithm-roomgraphbuild). `floor edge min length`
(`floorEdgeMinLength`), `floor confirm min cells` (`floorSeedMinCells`) and
`floor confirm density` (`floorSeedMinDensityFraction`) are specific to the two
experimental floor-boundary wall sources — see [wall-sources.md](wall-sources.md) — and
are otherwise inert; the confirm knobs only affect `FLOOR+DEPTH`, and both are already
looser than Hybrid's vertical-plane bar by default so FLOOR+DEPTH trusts the floor boundary
more readily.

*Anchoring* carries `anchor hits` (`autoAnchorHits`), `anchor tolerance`
(`autoAnchorTolerance`) and `anchor miss grace` (`autoAnchorMissGrace`) — how many
consecutive matching rebuilds freeze a wall, how close two rebuilds must land to count as
the same wall, and how many consecutive misses a wall can survive without losing its
progress. See "Anchoring" in [auto-reconstruction.md](auto-reconstruction.md).
