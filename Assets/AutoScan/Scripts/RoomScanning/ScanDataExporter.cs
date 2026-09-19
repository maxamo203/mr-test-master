using System.Globalization;
using UnityEngine;
using Scanner;
using ScanData = Scanner.ScanData;
using ScanVec3 = Scanner.Vec3;
using ScanQuat = Scanner.Quat;
using ScanWallData = Scanner.WallData;
using ScanDoorData = Scanner.DoorData;
using ScanMarkerData = Scanner.MarkerData;
using ScanCubeData = Scanner.CubeData;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// Writes a scanned room in the horror game's scan format, so the game can rebuild it
    /// as its own wall objects, furniture and markers.
    ///
    /// <see cref="RoomModel"/> stays this project's native format — it holds things the
    /// game has no field for (pinned walls, corners) and this project must keep working
    /// standalone — so this is a one-way translation layer rather than a schema swap.
    ///
    /// Inside the game this uses its real <c>Scanner.ScanData</c> and <c>ScanSerializer</c>
    /// (the standalone project keeps a field-for-field mirror instead), so the scan lands in
    /// the same saved-scans list as any other and opens in its <c>ScannerScene</c>.
    /// </summary>
    public class ScanDataExporter : MonoBehaviour
    {
        [SerializeField] RoomBuilder roomBuilder;

        [Tooltip("Marker type id for a detected door. Must match a MarkerType asset in the game.")]
        [SerializeField] string doorMarkerKind = "Door";

        [Tooltip("Marker type id for a detected window.")]
        [SerializeField] string windowMarkerKind = "Window";

        public string LastExportPath { get; private set; }
        public string LastExportName { get; private set; }

        void Awake()
        {
            if (roomBuilder == null) roomBuilder = GetComponent<RoomBuilder>();
        }

        /// <summary>
        /// Exports the live room. Returns false and logs when there is nothing to write.
        /// </summary>
        public bool Export(string scanName = null)
        {
            if (roomBuilder == null) { Debug.LogWarning("[ScanDataExporter] No RoomBuilder."); return false; }

            var room = roomBuilder.Room;
            if (room == null || room.walls.Count == 0)
            {
                Debug.LogWarning("[ScanDataExporter] Nothing to export — build the walls first.");
                return false;
            }

            scanName = string.IsNullOrEmpty(scanName)
                ? "auto_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss")
                : scanName;

            var data = Build(room, scanName);

            try
            {
                ScanSerializer.Save(scanName, data);
                LastExportPath = ScanSerializer.PathFor(scanName);
                LastExportName = scanName;

                Debug.Log($"[ScanDataExporter] Guardado '{scanName}': {data.walls.Count} pared(es), " +
                          $"{data.cubes.Count} cubo(s), {data.markers.Count} marcador(es) → {LastExportPath}");

                if (data.markers.Count == 0)
                {
                    // The game picks its enemy spawn from the markers and refuses to start a
                    // run without any: the scan loads and looks right but will not play.
                    Debug.LogWarning("[ScanDataExporter] Sin marcadores: el juego spawnea al enemigo " +
                                     "en un marcador y no inicia la partida sin al menos uno. " +
                                     "Agregalos en el editor (Identificar) si no se detectó ninguna puerta/ventana.");
                }
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ScanDataExporter] Export failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Saves the room, then opens it in the game's own <c>ScannerScene</c> in edit mode —
        /// where the reference image is captured and the walls fine-tuned. The auto-scan
        /// anchors to a detected corner rather than the game's physical reference image, so
        /// this hand-off is what makes the scan playable.
        /// </summary>
        public bool ExportAndOpenInScanner(string scanName = null)
        {
            if (!Export(scanName)) return false;
            ScannerLaunchParams.EditScanName = LastExportName;
            SceneFlow.GoTo(SceneFlow.EscenaEscaner);
            return true;
        }

        /// <summary>Translates a <see cref="RoomModel"/> into the game's schema.</summary>
        public ScanData Build(RoomModel room, string scanName)
        {
            var data = new ScanData { name = scanName };

            // The map origin sits on the floor by construction and every wall is built with
            // y = 0, so the floor plane is the origin's own XZ plane.
            data.hasFloor = true;
            data.floorLocal = new ScanVec3(Vector3.zero);

            foreach (var wall in room.walls)
            {
                if ((wall.end - wall.start).magnitude < AutoWallMeshBuilder.MinDimension) continue;

                // A wall lifted off the floor by corner editing needs no extra field here:
                // WallSegment.baseY rides in the y of both endpoints (the model keeps them
                // on the base plane), and the game builds its walls from aLocal/bLocal
                // exactly as AutoWallMeshBuilder does, so the lift survives the round trip.
                var wallId = wall.id.ToString(CultureInfo.InvariantCulture);
                var outWall = new ScanWallData
                {
                    id = wallId,
                    polylineId = string.Empty,
                    aLocal = new ScanVec3(wall.start),
                    bLocal = new ScanVec3(wall.end),
                    height = wall.height > 0.1f ? wall.height : room.roomHeight,
                    width = wall.width > 0f ? wall.width : 0.12f,
                    side = wall.side != 0 ? wall.side : 1,
                };

                // Openings come from the model, which is what a saved-and-restored room
                // carries; the scene object is the fallback for a wall built before the
                // model held them.
                var openings = wall.openings;
                if (openings == null || openings.Count == 0)
                {
                    var component = roomBuilder.WallComponent(wall.id);
                    if (component != null && component.Openings.Count > 0)
                        openings = new System.Collections.Generic.List<WallOpening>(component.Openings);
                }

                if (openings != null)
                {
                    int n = 0;
                    foreach (var opening in openings)
                    {
                        outWall.doors.Add(new ScanDoorData
                        {
                            id = $"{wallId}_d{n++}",
                            uMin = opening.uMin, uMax = opening.uMax,
                            vMin = opening.vMin, vMax = opening.vMax,
                        });

                        data.markers.Add(new ScanMarkerData
                        {
                            id = $"{wallId}_m{data.markers.Count}",
                            kind = string.Equals(opening.kind, "Window", System.StringComparison.OrdinalIgnoreCase)
                                ? windowMarkerKind : doorMarkerKind,
                            wallId = wallId,   // must match ScanWallData.id exactly
                            u = (opening.uMin + opening.uMax) * 0.5f,
                            v = (opening.vMin + opening.vMax) * 0.5f,
                            side = -1,
                        });
                    }
                }

                data.walls.Add(outWall);
            }

            int cubeIndex = 0;
            foreach (var box in room.furniture)
            {
                // The scene object is the live truth for a box's pose. Furniture is still
                // fitted as an axis-aligned box, so this is identity today — but reading it
                // rather than hardcoding it means the export stays correct the day oriented
                // -box fitting lands, instead of silently flattening every rotation.
                var component = roomBuilder.FurnitureComponent(box.id);
                var rot  = component != null ? component.RotLocal  : Quaternion.identity;
                var size = component != null ? component.SizeLocal : box.size;

                data.cubes.Add(new ScanCubeData
                {
                    id = "c" + cubeIndex++,
                    posLocal = new ScanVec3(component != null ? component.CenterLocal : box.center),
                    rotLocal = new ScanQuat(rot),
                    scaleLocal = new ScanVec3(size),
                    cornerSignA = new ScanVec3(Vector3.one),
                });
            }

            // Horizontal surfaces export as a thin slab whose top sits at the surface
            // height — the game reads it as a platform to spawn objects onto.
            foreach (var s in room.surfaces)
            {
                data.cubes.Add(new ScanCubeData
                {
                    id = "s" + cubeIndex++,
                    posLocal = new ScanVec3(new Vector3(s.center.x, s.center.y - 0.02f, s.center.z)),
                    rotLocal = new ScanQuat(Quaternion.identity),
                    scaleLocal = new ScanVec3(new Vector3(s.size.x, 0.04f, s.size.y)),
                    cornerSignA = new ScanVec3(Vector3.one),
                });
            }

            return data;
        }
    }
}
