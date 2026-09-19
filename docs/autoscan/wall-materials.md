# Wall and furniture materials

Why scanned geometry here renders the same as scanned geometry in the reference game
(`mr-test-master`), and what deliberately still differs.

Files: `Assets/Shaders/EdgeGrid.shader`, `Assets/Materials/AR/WallEdgeGrid.mat`,
`Assets/Materials/AR/CubeEdgeGrid.mat`, `Assets/Scripts/RoomScanning/ScanMaterials.cs`.

## The problem this solves

A wall scanned here and a wall drawn in the game are *the same object seen in two apps*:
`ScanDataExporter` writes the game's format, and the game's `ScanLoader` calls
`WallObject.FromData` to rebuild it as a real `Scanner.WallObject`. The geometry already
matched — both `WallMeshBuilder`s are the same algorithm in the same space — but the two
looked nothing alike, which makes judging a scan against the game guesswork:

| | Before | The game |
|---|---|---|
| Shader | `Mortuorium/ARPlaneGrid` | `Custom/EdgeGrid` |
| Wall fill | green, tinted by state | (0.80, 0.85, 0.95) at **α 0.12** |
| Lines | grid from world/object XZ | black 25 cm grid + black edges |
| Grid source | object XZ | baked `(u,v,w)` from UV1 |

## What was done

The game's `EdgeGrid.shader` and both material assets were brought across. The materials
are **byte-identical copies**, including their GUIDs, so they resolve against the same
shader and carry the same authored values.

The shader is **not** a copy. The original is written for the Built-in pipeline (CGPROGRAM,
`UnityCG.cginc`, no `RenderPipeline` tag); this project is URP, where such a pass renders
only through the untagged-pass fallback — no SRP batching, and on behaviour URP does not
promise to keep. `Assets/Shaders/EdgeGrid.shader` here is the same shader expressed as a
first-class URP pass: `UniversalPipeline` / `UniversalForward` tags, HLSL against
`ShaderLibrary/Core.hlsl`, `TransformObjectToHClip` / `TransformObjectToWorldNormal`, and
every material property inside one `CBUFFER_START(UnityPerMaterial)`.

**The property names, defaults and maths are identical to the original on purpose.** That
is what lets the game's `.mat` files load against it untouched. Renaming a property here
silently breaks two material assets that no compiler checks.

## How the shader reads a surface

Two switches decide what it draws, and `ScanMaterials.ConfigureEdgeGrid` asserts them per
object rather than trusting the material asset — a wall wired to the cube material still
renders as a wall:

| | `_GridFromUV` | `_BoxEdges` |
|---|---|---|
| Wall | **1** — grid follows the `(u,v,w)` baked into UV1, so a sloped or lifted base carries its lines with it | 0 |
| Cube | 0 — grid is metric object space, so it rotates with the box | 0 (**differs from the game**) |

### The one real deviation: `_BoxEdges`

The game's `CubeEdgeGrid.mat` sets `_BoxEdges = 1`, which finds a box's twelve edges
geometrically: an edge is where two of the three object-space axes reach the box limit. The
shader derives that limit from the transform's lossy scale — correct in the game, where a
cube is a unit mesh scaled by its transform.

Mortuorium's `FurnitureObject` bakes its size into the mesh in metres and leaves
`localScale` at 1 (deliberately — see the class comment there). The shader would therefore
place the twelve edges on a fixed 1 m box whatever the real furniture size. So box edges
are off here and both surfaces take their edges from **normal discontinuity**, which is
correct on both because every face carries its own four vertices and its own normal.

## Tinting, and the two shaders' opposite conventions

`ScanMaterials.Tint(material, color, fillAlpha)` is the only way anything tints scanned
geometry, because the same two numbers mean opposite things on the two shaders:

- **EdgeGrid** — `color` is the fill, `fillAlpha` its opacity; `_GridColor` is the *line*
  colour and is left black, since the lines are what make the surface readable.
- **ARPlaneGrid** — `_GridColor` *is* the tint and `_FillAlpha` is a separate fill opacity.

`_FillAlpha` is the discriminator: only ARPlaneGrid has it. Testing `_GridColor` first
would take the wrong branch, since both shaders have one — and the wall would come back
with black lines on one and a coloured body on the other.

## The state colours

An untouched, automatically detected wall is drawn in `ScanMaterials.GameWallFill` — the
exact authored `_Color` of the game's `WallEdgeGrid.mat`. That is the parity case, and it is
what "the walls look the same" means.

Everything else is a deliberate departure, marking a fact the game has no concept of:

| State | Colour | α |
|---|---|---|
| Automatic wall | the game's fill (0.80, 0.85, 0.95) | 0.12 |
| Pinned (hand-corrected) | blue | 0.26 |
| Selected | the game's selection cyan | 0.45 |
| Restored (a saved room overlaid) | purple | 0.16 |
| Furniture | amber | 0.30 |

The game's own selected material is α 0.85. It is dialled back here because a scanner is
looked *through*: at 0.85 the selected wall hides the real room behind it in the camera
feed, which is exactly what you are trying to line the wall up against.

## Wiring

`RoomBuilder` takes two templates, both instanced per object so tinting one cannot bleed
into the others:

- `wallMaterial` → `WallEdgeGrid.mat`
- `furnitureMaterial` → `CubeEdgeGrid.mat` (falls back to `wallMaterial` when empty, so a
  scene wired before this field existed keeps working)

Left empty, `ScanMaterials.CreateRuntime` builds an equivalent from `Shader.Find`, falling
back to `ARPlaneGrid` and then URP Unlit. `Custom/EdgeGrid` is in **Always Included
Shaders** for that path: a shader reached only through `Shader.Find` is otherwise stripped
from the player, and `WallObject.IsShaderUsable` would then report a magenta wall.

## What is still not the same object

Materials and geometry now match. The C# objects do not, and some of it is load-bearing:

| | Mortuorium | The game |
|---|---|---|
| Identity | `int Id` | 8-char GUID string + `PolylineId` |
| Selection | analytic, screen-space | `ISelectable` + `Physics.Raycast` |
| Registry | none | `SceneRegistry` |
| Corner handles | 4 cubes, no colliders | 2 spheres with `SphereCollider`s |

The physics-based pieces cannot be ported as they stand: this player is built with
engine-code stripping and the Physics module goes with it, so a `SphereCollider` primitive
throws under IL2CPP. See [wall corner editing](wall-corner-editing.md) for what replaced
them here.
