using System.Collections.Generic;
using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// Plain serializable description of a scanned room, expressed in the map-origin
    /// local space (the bottom corner the user confirmed). This is the single shared
    /// model that <see cref="RoomBuilder"/> populates and <see cref="JsonExporter"/>
    /// serialises — no Unity scene state, so it can be saved, reloaded or sent off-device.
    /// </summary>
    [System.Serializable]
    public class RoomModel
    {
        /// <summary>ISO-8601 timestamp of when the scan was built.</summary>
        public string scanTime;

        /// <summary>Floor height in origin-local space (usually ~0).</summary>
        public float floorY;

        /// <summary>Measured floor-to-ceiling height (m).</summary>
        public float roomHeight;

        public List<Corner> corners = new();
        public List<WallSegment> walls = new();
        public List<DoorOpening> doors = new();
        public List<FurnitureBox> furniture = new();

        /// <summary>
        /// Ordered room-boundary loop, origin-local at floor level (each point's y is
        /// <see cref="floorY"/>). Computed from the walls by <see cref="RoomGraph"/> every
        /// reconstruction — never pinned, never hand-edited. Empty when no boundary could
        /// be traced. Absent from older JSON, which reads as empty.
        /// </summary>
        public List<Vector3> outline = new();

        /// <summary>True when <see cref="outline"/> is a closed loop (the walls enclose the
        /// room). False for an open chain or no outline.</summary>
        public bool outlineClosed;

        /// <summary>Endpoint pairs of the openings the outline had to bridge to close, for a
        /// distinct debug render. Runtime-only render hint — not serialised.</summary>
        [System.NonSerialized] public List<(Vector3 a, Vector3 b)> outlineBridges = new();

        /// <summary>
        /// Horizontal surfaces above the floor — table tops, seats, shelves — the places a
        /// game can spawn objects onto. This is what furniture reconstruction produces now,
        /// instead of guessing a full bounding box. Absent from older JSON (reads as empty).
        /// </summary>
        public List<HorizontalSurface> surfaces = new();
    }

    /// <summary>
    /// A horizontal placeable surface, origin-local: an axis-aligned XZ rectangle at a
    /// fixed height. The rectangle is deliberately coarse — enough to place something on,
    /// not a faithful outline of the object.
    /// </summary>
    [System.Serializable]
    public struct HorizontalSurface
    {
        public Vector3 center;   // origin-local; center.y is the surface height
        public Vector2 size;     // XZ extent (m)

        /// <summary>Stable identity, assigned by <see cref="RoomBuilder"/>. 0 in older JSON.</summary>
        public int id;

        /// <summary>True once anchored: it survived enough rebuilds in place to be frozen.</summary>
        public bool pinned;
    }

    /// <summary>An axis-aligned furniture box (table, couch, cabinet…), origin-local.</summary>
    [System.Serializable]
    public struct FurnitureBox
    {
        public Vector3 center; // origin-local
        public Vector3 size;   // full extents (m)

        /// <summary>Stable identity, assigned by <see cref="RoomBuilder"/>. The rendered
        /// <see cref="FurnitureObject"/> carries the same value, which is the way back
        /// from a scene object to its box. Absent (0) in older saved JSON.</summary>
        public int id;

        /// <summary>
        /// True once this box has been anchored: it survived enough automatic rebuilds
        /// in the same place to be frozen. <see cref="RoomBuilder.ClearFurniture"/> keeps
        /// it and <see cref="DepthOccupancyMapper"/> excludes its voxels from further
        /// clustering. Absent from older JSON, which loads as all-unanchored.
        /// </summary>
        public bool pinned;
    }

    /// <summary>
    /// A vertical room corner (where two or more walls meet), origin-local. Produced by
    /// <see cref="RoomGraph"/> from the fitted walls each reconstruction.
    /// </summary>
    [System.Serializable]
    public struct Corner
    {
        public Vector3 position;   // origin-local, floor level
        public Vector3 wallA;      // unit XZ direction of the first incident wall
        public Vector3 wallB;      // unit XZ direction of the second incident wall

        /// <summary><see cref="WallSegment.id"/> of the two incident walls, or 0 when the
        /// wall was suppressed / is unknown. Absent (0) in older JSON.</summary>
        public int wallAId;
        public int wallBId;
    }

    /// <summary>A wall as a vertical quad: a floor-level segment extruded to roomHeight.</summary>
    [System.Serializable]
    public struct WallSegment
    {
        public Vector3 start;  // origin-local, floor level
        public Vector3 end;    // origin-local, floor level
        public float height;   // extrusion height (m)

        /// <summary>
        /// Height of the wall's bottom edge above the origin floor (m). Normally 0 — a
        /// wall starts at the floor — but corner editing can lift it, which is how a
        /// half-wall, a counter back or the solid part above a pass-through gets
        /// modelled without inventing a second geometry type. The box spans
        /// <c>[baseY, baseY + height]</c>. Absent from older JSON, which reads as 0 and
        /// therefore as the previous floor-anchored behaviour.
        /// </summary>
        public float baseY;

        /// <summary>
        /// Stable identity, assigned by <see cref="RoomBuilder"/>. The rendered
        /// <see cref="AutoWallObject"/> carries the same value, which is the only reliable
        /// way back from a scene object to its segment — the wall object list is not
        /// index-parallel with <c>walls</c> (it also holds corner quads).
        /// </summary>
        public int id;

        /// <summary>
        /// True once the user has corrected this wall by hand. Pinned walls survive
        /// reconstruction: <see cref="RoomBuilder.ClearWalls"/> keeps them, and
        /// <see cref="DepthOccupancyMapper"/> neither refits them nor emits an
        /// automatic wall that duplicates them. Absent from older saved JSON, which
        /// therefore loads as all-automatic — the intended behaviour.
        /// </summary>
        public bool pinned;

        /// <summary>Extrusion thickness (m). 0 means "use the builder's default".</summary>
        public float width;

        /// <summary>
        /// Extrusion direction, +1 or -1 along <c>cross(up, baseHat)</c>. The floor line
        /// start→end is the *near* face; the wall grows to <see cref="width"/> on this
        /// side. Matches the convention the game's scan format uses, so a wall can be
        /// exported and rebuilt without moving. 0 (older JSON) is read as +1.
        /// </summary>
        public int side;

        /// <summary>
        /// Doors and windows cut through this wall, in its own (u,v) frame. Held on the
        /// model rather than only on the scene object so an opening survives save and
        /// restore — a doorway is part of what was measured, not a rendering detail.
        /// Null in older JSON and in walls nothing was detected through, which both read
        /// as "solid".
        /// </summary>
        public List<WallOpening> openings;
    }

    /// <summary>
    /// A rectangular hole through a wall — a door or a window — expressed in the wall's
    /// own frame: <c>u</c> along the floor line from its start, <c>v</c> upward from the
    /// floor. A door has <c>vMin == 0</c>; a window sits above the floor. This is the
    /// same parameterisation the game uses, and it is what lets
    /// <see cref="AutoWallMeshBuilder"/> cut either kind with one code path.
    /// </summary>
    [System.Serializable]
    public struct WallOpening
    {
        public float uMin, uMax;   // along the wall (m from start)
        public float vMin, vMax;   // up from the floor (m)

        /// <summary>"Door" or "Window" — matches the game's marker type ids.</summary>
        public string kind;
    }

    /// <summary>A door opening detected in a wall, origin-local.</summary>
    [System.Serializable]
    public struct DoorOpening
    {
        public Vector3 center; // origin-local, at floor level
        public float width;    // along the wall (m)
        public float height;   // (m)
        public Vector3 wallDir; // unit XZ direction of the host wall
    }

    /// <summary>
    /// Result of fitting a single wall line to the depth point cloud at a corner:
    /// a direction in XZ and the signed inlier extents relative to the corner.
    /// Produced by <see cref="CornerDetector"/>, consumed by <see cref="RoomBuilder"/>.
    /// </summary>
    public struct WallFit
    {
        public Vector3 direction; // unit XZ
        public float min;         // signed extent along direction from the corner
        public float max;
    }

    /// <summary>
    /// A transient wall candidate found during continuous mapping: a cluster of
    /// origin-local depth points along a floor-boundary edge, with the outward
    /// normal. Produced by <see cref="CornerDetector"/>, turned into a mesh by
    /// <see cref="RoomBuilder"/>. Not serialised.
    /// </summary>
    public struct DetectedWall
    {
        public Vector3 normal;          // outward, origin-local XZ
        public Vector3 centre;          // origin-local
        public List<Vector3> points;    // origin-local inlier points
    }

    /// <summary>
    /// A candidate wall line proposed by an ARCore vertical plane, expressed in the
    /// origin-local XZ floor plane. A seed is only a hypothesis: the occupancy grid
    /// must confirm it before a wall is built (see <see cref="DepthOccupancyMapper"/>).
    /// Produced by <see cref="PlaneCollector.VerticalWallSeeds"/>. Not serialised.
    /// </summary>
    public struct WallSeed
    {
        public Vector2 point;      // midpoint of the plane's extent, origin-local XZ
        public Vector2 dir;        // unit horizontal direction along the wall
        public float halfLength;   // half the plane's extent along dir (m)
        public float area;         // plane area (m²), largest-first ordering
    }
}
