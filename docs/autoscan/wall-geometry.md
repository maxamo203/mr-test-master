# Wall geometry

What a wall is, in each of the four places it exists, and the conventions that keep them
agreeing with each other.

## The four representations

| Layer | Type | File |
|---|---|---|
| Data | `WallSegment` | `Assets/Scripts/RoomScanning/RoomModel.cs` |
| Scene object | `WallObject` | `Assets/Scripts/RoomScanning/WallObject.cs` |
| Triangles | `WallMeshBuilder` | `Assets/Scripts/RoomScanning/WallMeshBuilder.cs` |
| Interchange | `ScanWallData` | `Assets/Scripts/RoomScanning/ScanData.cs` |

`RoomBuilder` is the only class allowed to mutate the model and the scene objects together.
Everything else — the occupancy mapper, the door detector, the wall editor — asks it.

## The box

A wall is an axis-*un*aligned box, defined by a floor line and three scalars:

```
point(u, v, w) = a + u·baseHat + v·up + w·normal

  a        the start endpoint, origin-local
  baseHat  unit horizontal direction, a → b
  normal   cross(up, baseHat) · side, unit
  u ∈ [0, length]   along the wall
  v ∈ [0, height]   up from the wall's own base
  w ∈ [0, width]    through the thickness
```

Four conventions follow from that, and all four are load-bearing:

1. **The floor line is the *near* face, not the centre line.** The wall grows from
   `a → b` in the `+normal` direction only. A wall drawn on a measured surface therefore
   sits on that surface, rather than straddling it by half a thickness. This is the game's
   convention, adopted so a scan can round-trip; it is also why walls moved about 6 cm when
   the old centred-box helper was deleted.
2. **`side` (±1) picks which way the wall grows** — away from the camera at the moment the
   wall was created (`WallObject.DecideSide`), so the face you were looking at is the face
   that was measured.
3. **`baseY` is where the box starts vertically**, and both endpoints carry it in their
   `y`. Normally 0. A corner edit can lift it (see
   [wall corner editing](wall-corner-editing.md)), which is how a half wall, a counter back
   or the solid part above a pass-through gets modelled. The box spans
   `[baseY, baseY + height]`.
4. **Geometry is baked in map-origin-local coordinates and the transform stays at
   identity.** A wall never carries its pose in its `Transform`. The map origin is the only
   moving part, so relocalizing the room is one transform move, not a walk over every wall.

## Openings

Doors and windows are rectangular through-holes in the wall's own `(u, v)` frame —
`uMin/uMax` along the base, `vMin/vMax` up from the wall's base, `kind` of `"Door"` or
`"Window"`. A door is an opening with `vMin == 0`.

`WallMeshBuilder` resolves them with a U/V cell grid: it cuts U at every opening edge, cuts
V likewise, skips cells that fall inside an opening, and for each solid cell whose
neighbour is empty emits a quad joining the near face to the far face. That single rule
produces the threshold, the lintel, the jambs and the end caps with no special cases.

Two mesh details worth not "optimising" away:

- **Every face gets its own four vertices.** Welded corners would make
  `RecalculateNormals` average perpendicular faces into a diagonal, and the wall would
  render unlit.
- **Winding is chosen from the geometric normal**, not assumed, so faces always end up
  facing outward whatever `side` says.

`v` is measured from the wall's base, not from the floor. On a lifted wall an opening at
`vMin = 0` starts at `baseY`, which is what "a hole at the bottom of this wall" should
mean.

## Vertical extent and the mesh

`WallObject` keeps `BaseY`, `Height` (and `TopY = BaseY + Height`), and forces both
endpoints' `y` onto `BaseY` on every write, so no caller can leave the two disagreeing
about where the bottom edge is. Callers that still pass floor-level points — every
automatic path does — are unaffected.

`SetGeometry(a, b, baseY, height, width)` sets the whole box in **one** rebuild. Corner
dragging changes several of these per frame, and the individual setters would remesh and
re-cook the `MeshCollider` two or three times a frame.

Degenerate boxes are refused rather than rendered: below `WallMeshBuilder.MinDimension`
(2 cm) in any dimension the builder returns `null` and the wall renders and collides as
nothing. `MeshCollider` cooking options are set to skip mesh cleaning, because a wall
passes through briefly degenerate states while it is being dragged and PhysX warns on
every one of them.

## Export

`ScanDataExporter` writes the game's format to `persistentDataPath/scans/<name>.json`:

| Model | Scan format |
|---|---|
| `WallSegment.start` / `.end` | `ScanWallData.aLocal` / `.bLocal` |
| `.height`, `.width`, `.side` | same names |
| `.baseY` | *implicit* — it is the `y` of `aLocal`/`bLocal` |
| `.openings` | `ScanWallData.doors` (u/v ranges) + one `ScanMarkerData` each |
| `FurnitureBox` | `ScanCubeData` |

Two constraints the game imposes: a scan must have a floor point (exported as origin-local
`(0,0,0)`, which is the floor by construction), and at least one marker must resolve to a
wall id or the game refuses to start a run — markers are the enemy spawn points, and they
come from openings. A scan with no detected doorway exports zero markers, and the exporter
logs a warning saying so.

`baseY` needs no schema change because the game builds its walls from `aLocal`/`bLocal`
the same way `WallMeshBuilder` does, so a lifted wall survives the round trip.

## Appearance

Walls and furniture render through the reference game's own `Custom/EdgeGrid` shader and
material assets, so the same wall looks the same in both apps — see
[wall and furniture materials](wall-materials.md). The `(u,v,w)` metrics `WallMeshBuilder`
bakes into UV1/UV2 exist for that shader: they are what makes the grid follow a wall's own
frame instead of the world axes.

## No physics, anywhere

The Android player is built with engine-code stripping and the Physics module goes with it.
Consequences that show up all over this subsystem:

- No `GameObject.CreatePrimitive` — its automatic collider throws under IL2CPP. Cube meshes
  are built by hand (`RoomScanningManager.UnitCube`, `WallCornerHandles.HandleMesh`).
- No `Physics.Raycast` for picking. Selection and dragging are analytic: screen-space
  distance to a projected line, camera-ray/plane intersection, closest point between a ray
  and a line.

The `MeshCollider` on a `WallObject` exists for the eventual game-side consumer, not for
anything in this project.
