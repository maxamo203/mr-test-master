# Wall corner editing

Hand-fitting detected walls to the real room, on the device, by dragging the corners of the
box.

Files: `WallEditor.cs` (interaction), `WallCornerHandles.cs` (handles + corner geometry),
`RoomBuilder.TryUpdateWallGeometry` (the single mutator), `WallObject.SetGeometry`
(one rebuild per change). Geometry conventions live in [wall geometry](wall-geometry.md).

## Why it exists

Detection is automatic and stays automatic. It is also routinely a few centimetres off —
worst on featureless walls, where ARCore's depth is largely an ML prior rather than a
measurement, and no threshold tuning recovers a number the sensor never took. Beyond
offset error, a fitted wall is always a full-height rectangle standing on the floor, and
real rooms are not: half walls, counters, a stub between two doorways, a run that overshot
into the next room.

The constrained edit modes (MOVE, ENDS) fix the first problem. Corner editing fixes the
second: it treats a wall as the box it actually is and lets each corner be placed by hand.
ROTATE straightens a wall the fit left skewed; COMBINE fuses fragments the auto pass never
merged.

## What you can grab

Four handles, on the wall's **near face** — the surface the scan measured and the one you
are looking at:

| Handle | Colour | Drag does |
|---|---|---|
| `BaseStart`, `BaseEnd` | blue | Moves that endpoint across the wall's own base plane. |
| `TopStart`, `TopEnd` | orange | Moves the top edge up or down (changes `height`). |
| *(the active one)* | yellow | — |

Plus two gestures/controls that have no handle:

- **Two fingers, vertical** — lifts the bottom edge (`baseY`) while holding the top still,
  so a full-height wall becomes a half wall. This reuses the "1 finger = floor, 2 fingers =
  up/down" idiom the origin marker already established.
- **`W -` / `W +`** — thickness, in 2 cm steps, clamped to 2–60 cm.

### Why four handles and not eight

A box has eight corners, but the near and far corner of a pair are `width` apart — about
12 cm — and land within a few pixels of each other on a phone screen. Offering both would
make them indistinguishable under a fingertip. Thickness gets buttons instead, where there
is no ambiguity to resolve.

### Why the top edge is one height

Dragging either top handle moves the whole top edge. The box stays a box because
`WallMeshBuilder` — and the game's scan format behind it — parameterises a wall as *base
line + height + thickness*. A per-end height would skew the wall into a trapezoid, which
needs a different geometry type on both sides of the export. That is a much larger change
than it looks, and nothing has yet asked for it.

## Snapping (`SNAP: ON`)

Two rules, both defeatable with the button:

- **Base corner welding.** A dragged base corner that lands within 25 cm of another wall's
  base corner snaps onto it exactly, in XZ. This is the difference between a room that
  looks closed and one that is: two walls sharing a corner exactly leave no sliver for
  occlusion or pathing to leak through, and no amount of careful thumb work gets two
  independently dragged corners to the same millimetre. The wall's own other endpoint is
  never a snap target, or a short wall would collapse onto itself.
- **Top edge to room height.** A top edge within 8 cm of the measured room height snaps to
  it, so walls that do reach the ceiling end up exactly level with each other.

## Interaction rules

- **Tap to select.** Tapping away from any handle picks the wall under the finger, so
  switching walls does not need a mode change. Handles are shown only for the selected
  wall, and only in CORNERS mode.
- **Picking is against the wall's projected face**, not a line — the quad through its four
  near-face corners, with a tap inside it counting as a direct hit and the grab radius only
  widening that to a near miss. The first version measured distance to the wall's mid-line,
  which fails exactly where it is used: at the one or two metres you stand from a wall in
  AR, the wall fills the screen and a tap on its upper half is hundreds of pixels from that
  mid-line. The vertical edges are clipped to the near plane rather than the wall being
  dropped when a corner falls behind the camera — a long wall running past your shoulder
  nearly always has one end behind you while most of its surface is in front. Where a tap
  is inside two walls, the nearer one wins.
- **Handles beat geometry.** A handle under the finger wins over the wall behind it — the
  handles draw on top, so grabbing what you can see is the least surprising rule.
- **Drags are offsets from the grab, not accumulations.** The whole original box is
  captured when the finger lands and every frame recomputes from it. Accumulating frame to
  frame would let rounding walk a wall away under a stationary thumb.
- **Vertical drags carry a grab offset**, so the top edge does not jump to the finger on
  the first frame.
- **A tap that never moved is a selection, not an edit.** Only a drag that actually moved
  the wall pins it and triggers the autosave.
- **Touches that start on the sub-HUD never reach the room.** Without this, pressing
  `DELETE WALL` would also grab the wall behind it. Only the rows actually drawn are
  excluded, so the strip below them stays tappable in the modes with three rows.

## Constraints applied to every edit

| Quantity | Rule |
|---|---|
| Length | Below `WallMeshBuilder.MinDimension` (2 cm) the edit is **refused** — the wall keeps its previous footprint. |
| Height | Clamped to ≥ 10 cm, not refused: a drag naturally passes through "too short", and stopping it dead there is worse than pinning it at the minimum. |
| `baseY` | Clamped to `[0, top − minHeight]`. A wall below the floor plane is never what was meant. |
| Thickness | Clamped to 2–60 cm. |

## Pinning: how an edit survives reconstruction

Automatic reconstruction is a full teardown, so a hand-placed correction only survives by
being marked `WallSegment.pinned`. Every mutator in `RoomBuilder` pins the wall it touches,
and pinning changes what reconstruction may do:

- `RoomBuilder.ClearWalls(keepPinned: true)` spares pinned walls — mesh and model entry
  both. Both callers pass it.
- Pinned walls are never fed back into the fitting `segments` list, so `SnapToFrame` cannot
  rotate them and `MergeCollinear` cannot absorb them.
- `DepthOccupancyMapper.DuplicatesPinnedWall` drops any automatic segment that restates a
  pinned wall — same normal-form test as the merge, plus an overlap requirement along the
  shared line so two runs either side of a doorway both survive.

Walls nobody has touched keep re-fitting from the occupancy grid as the user scans more of
the room, so correcting one wall does not freeze the rest.

## Rotate and combine

Both are `EditMode`s in `WallEditor`, both pin their result, both autosave on release.

- **ROTATE** has its own button on row 3 (in MOVE/ENDS), not a MODE-cycle slot — the cycle
  is just MOVE → ENDS → CORNERS. Drag anywhere around the selected wall; it turns about its
  **centre**, length unchanged, writing through the same `TryUpdateWall` as MOVE. `ANGLE:
  15°` snaps the wall's heading to the nearest 15° on each frame, so a near-square wall
  lands exactly axis-aligned. `DONE` returns to MOVE. Rotating a wall moves both ends, so a
  corner previously welded to a neighbour comes apart — re-weld it in CORNERS.
- **COMBINE** also has its own button (like ADD, so cycling cannot drop into a two-tap
  flow). Tap one wall, then another: `RoomBuilder.CombineWalls` spans both along the
  **longer** wall's line — the shorter fragment's perpendicular offset is discarded, on the
  assumption both are pieces of one real wall — takes `max` height/width, `min` `baseY`, and
  re-projects each wall's openings onto the new frame (uniform spacing; a door that lands a
  little off can be nudged in CORNERS). The two originals are deleted; the room graph
  re-traces corners and outline on the next build.



`WallCornerHandles` is pure view: it draws four cubes and knows nothing about touches.
Picking lives in `WallEditor`. Both compute corner positions from the same `WallCorners`
helper, so what is drawn and what is grabbable cannot diverge.

- Hand-built cube mesh, no collider — the Physics module is stripped from the player.
- Unlit, always-on-top material (the origin gizmo's shader search order), because a handle
  hidden behind its own wall cannot be aimed at.
- Scaled by camera distance every `LateUpdate`, clamped to 3.5–14 cm, so a corner three
  metres away is still a thumb target and one half a metre away does not fill the view.

## Reading the HUD when a tap does nothing

The prompt carries the numbers that decide a selection, because on a phone there is no
console to log to:

```
touch 1 | walls 7 | 3 in view | tap 412px / 129px
```

- **touch** — fingers `Touch.activeTouches` reported this frame. A steady 0 while you are
  touching the screen means input never arrived, not that picking failed.
- **walls** — segments in the model. 0 means nothing was ever built.
- **in view** — how many survived near-plane clipping on the last tap. 0 with a non-zero
  wall count means the walls are behind the camera, or the map origin is not where you
  think it is.
- **tap … px / … px** — how close the tap fell to the nearest wall face, against the radius
  it had to beat. 0px is a hit inside the face. A number far above the radius when you
  tapped a wall you can plainly see means the projection is wrong, not the radius.

## Known limitation

Edits do not survive an app restart. Within a session (scan → edit → export) they are safe,
and `pinned` and `baseY` both serialise into the JSON. But `TryLoadCurrent` is reachable
only from `Relocalize()` and lands in the display-only `RestoredRoom` layer, which never
re-enters `Room`; the next `SaveCurrent` then overwrites the file with a live model that
lacks the edits. Fixing this needs an `AdoptRestoredRoom()` that copies restored walls back
in as pinned — a separate feature, deliberately not done here.
