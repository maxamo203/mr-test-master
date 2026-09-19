# Room graph — junctions and outline

Turns the flat list of fitted wall segments into a connected floor-plan: wall ends that
belong together become one corner, intersecting walls are snapped to meet exactly, and the
room boundary is traced into an ordered outline.

Files: `RoomGraph` (all the geometry), `DepthOccupancyMapper.Reconstruct` (calls it),
`RoomBuilder` (stores + draws the result), `RoomModel` (`corners`, `outline`,
`outlineClosed`).

## Where it runs

Inside `Reconstruct`, **after `MergeCollinear`** (which fuses collinear fragments of one
wall) and **before the wall-build loop**. `RoomGraph.Build` takes the merged segments plus
the pinned (hand-corrected) walls as fixed constraints, and returns:

- `Segments` + `SourceIndices` — the snapped walls to build, and which merged segment(s)
  each came from (so `gapsPerSeg` / doorways follow; more than one when fused, see below).
- `CollinearMerges` — how many fragment pairs got fused into one wall this pass.
- `Junctions` — corner position + the two incident wall directions + built wall ids.
- `Outline` / `OutlineClosed` — the ordered boundary loop (empty if none could be traced).
- `BridgedSpans` — gaps the outline had to jump (doorway-sized openings).

`buildRoomGraph = false` on `DepthOccupancyMapper` skips all of this — exactly the previous
independent-segment behaviour.

## Algorithm (`RoomGraph.Build`)

1. **Nodes.** One node per wall end; ends within `joinRadius` (0.35 m) merge. A cluster that
   contains a pinned end takes that exact coordinate — **pinned geometry is never moved**.
2. **Junction snap.** For each close pair of non-parallel walls, both near ends move onto the
   exact line-line intersection and that node locks. Reaching the intersection may only cost
   a trim/extend of `≤ junctionExtendMax` (0.6 m) per wall. A pinned wall pins the
   intersection to its own line; only the other wall moves.
3. **Near-miss extension.** A dangling wall end within `joinRadius` of another wall's line is
   pulled onto it and that wall is split there (a T-junction).
4. **Collinear fusion.** Two fragments meeting end-to-end within `collinearMergeAngle` (10°)
   of dead straight are the same physical wall, mis-fit as two pieces — fused into one edge
   so only one wall gets built. Restricted to nodes no earlier step locked (a real junction,
   T-branch or intersection), so corners, T-junctions and dividers are never touched. Each
   fused wall's doorway candidates (`gapsPerSeg`) are unioned from every fragment it absorbed
   — see `SourceIndices`, one list of contributing input segments per emitted wall (plural,
   unlike the older 1:1 `SourceIndex`).
5. **Outline trace.** Dangling ends within `outlineGapBridge` (= `maxBridgeGap`, 2.5 m) are
   joined by a synthetic bridge edge, then the outer boundary is wall-followed (tightest
   turn at each node). A closed loop is kept only if it validates: ≥ 3 corners, area ≥ 1 m²,
   simple (no self-crossing). Otherwise the leading open chain is emitted with
   `OutlineClosed = false`. The bridged spans are returned in `BridgedSpans`.

## What it does not do (yet)

- **No editable junctions.** The snap is recomputed every reconstruction, so walls stay met
  across rebuilds, but dragging a corner in EDIT WALLS still moves only that one wall.
- **The relocalizer ignores corners.** `RoomRelocalizer` still matches infinite lines only.

## Conventions respected

- Snapping moves the **floor-line endpoints**, which are the wall's *near face* — no switch
  to centre-lines (that shifts every wall ~6 cm and breaks the game export round-trip). Two
  near-face lines meeting at 90° leave a ~`width`×`width` (~6 cm) notch at the corner; this
  is accepted, the game shares the convention, and the outline is traced through the same
  near-face points so polygon and boxes agree.
- Pinned walls are fixed constraints, never moved or split-for-building.
- `RoomGraph` never deletes a wall and never shortens one below ~2·`MinDimension`.
- Corners and the outline are recomputed every reconstruction and cleared in
  `RoomBuilder.ClearWalls` — this also fixes the old bug where `Room.corners` accumulated
  stale entries forever. The single origin `Corner` that `BuildCornerWalls` adds at mapping
  start is wiped by the first rebuild; the graph rediscovers that corner from the walls.

## Debug overlay

`drawOutline` (on by default) draws the outline as a floor-level `LineRenderer` under
`RoomOutline` — green when closed, amber when open — plus a short post at each corner.
Each bridged span (an opening the trace jumped to close the loop) is drawn as a separate
thin dim-cyan `OutlineBridge` line, so an inferred edge is never read as a real wall. A
true dashed line would need a tiled dash texture; the thin dim segment is the stand-in.
Rendering only, no collider.

Drawn with `Mortuorium/OriginGizmo` (`Assets/Shaders/OriginGizmo.shader`), always included
in the build so `Shader.Find` never falls back to a plain Unlit shader. `ZTest Always` plus
pinning the vertex depth to the near plane (`ZWrite On`) means the outline and its corner
posts stay visible over AR environment occlusion — not just untested against the depth
buffer, but written as the nearest thing on screen, which also survives an occlusion
implementation that composites in a later pass a plain `ZWrite Off` draw could not.
