using System.IO;
using System.Text;
using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// Serialises a finished <see cref="RoomModel"/> to device storage. JSON is the
    /// canonical reload format; an OBJ companion is emitted for external viewers.
    ///
    /// Replaces: <c>MappingExporter</c> (WriteJSON / WriteOBJ / GetExportDir),
    /// retargeted from raw baked meshes to the structured RoomModel.
    /// </summary>
    public class JsonExporter : MonoBehaviour
    {
        const string ExportFolder = "MortuoriumScans";

        /// <summary>Stable filename for "the" room, overwritten as the scan improves.
        /// The timestamped exports are an archive; this is what a later session loads.</summary>
        const string CurrentRoomFile = "current_room.json";

        public string LastExportPath { get; private set; }

        static string GetExportDir()
        {
            var path = Path.Combine(Application.persistentDataPath, ExportFolder);
            Directory.CreateDirectory(path);
            return path;
        }

        static string CurrentRoomPath => Path.Combine(GetExportDir(), CurrentRoomFile);

        // ── Autosave / reload ─────────────────────────────────────────────────

        /// <summary>
        /// Overwrites the current-room save. Called whenever a build produces geometry, so
        /// the newest good reconstruction survives the app being killed — the timestamped
        /// <see cref="Export"/> files remain the archive.
        /// </summary>
        public bool SaveCurrent(RoomModel room)
        {
            if (room == null || room.walls.Count == 0) return false;

            try
            {
                room.scanTime = System.DateTime.Now.ToString("o");
                File.WriteAllText(CurrentRoomPath, JsonUtility.ToJson(room, prettyPrint: true));
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[JsonExporter] Autosave failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Reads back the current-room save, if one exists and parses.</summary>
        public bool TryLoadCurrent(out RoomModel room)
        {
            room = null;
            try
            {
                if (!File.Exists(CurrentRoomPath)) return false;

                room = JsonUtility.FromJson<RoomModel>(File.ReadAllText(CurrentRoomPath));
                if (room == null || room.walls == null || room.walls.Count == 0)
                {
                    room = null;
                    return false;
                }

                Debug.Log($"[JsonExporter] Loaded saved room: {room.walls.Count} walls, " +
                          $"{room.furniture.Count} furniture, scanned {room.scanTime}.");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[JsonExporter] Load failed: {e.Message}");
                room = null;
                return false;
            }
        }

        public bool HasSavedRoom => File.Exists(CurrentRoomPath);

        /// <summary>Writes the room model to timestamped JSON + OBJ files; returns the JSON path.</summary>
        public string Export(RoomModel room)
        {
            if (room == null) return null;

            var dir = GetExportDir();
            var timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var jsonPath = Path.Combine(dir, $"room_{timestamp}.json");
            var objPath  = Path.Combine(dir, $"room_{timestamp}.obj");

            File.WriteAllText(jsonPath, JsonUtility.ToJson(room, prettyPrint: true));
            File.WriteAllText(objPath, BuildObj(room));

            LastExportPath = jsonPath;
            Debug.Log($"[JsonExporter] JSON: {jsonPath}");
            Debug.Log($"[JsonExporter] OBJ:  {objPath}");
            Debug.Log($"[JsonExporter] Pull (PowerShell):\n" +
                      $"  $pkg = \"{Application.identifier}\"\n" +
                      $"  adb shell \"run-as $pkg cp -r files/{ExportFolder} /sdcard/Download/\"\n" +
                      $"  adb pull /sdcard/Download/{ExportFolder} .");
            return jsonPath;
        }

        /// <summary>One quad per wall, extruded floor→ceiling. Z flipped for OBJ's right-handed space.</summary>
        static string BuildObj(RoomModel room)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Mortuorium room scan");
            sb.AppendLine($"# Exported {System.DateTime.Now}");
            sb.AppendLine($"# {room.walls.Count} walls, {room.doors.Count} doors");
            sb.AppendLine();

            int vertOffset = 1; // OBJ indices are 1-based
            for (int i = 0; i < room.walls.Count; i++)
            {
                var w = room.walls[i];
                var up = Vector3.up * w.height;
                var quad = new[] { w.start, w.end, w.end + up, w.start + up };

                sb.AppendLine($"o wall_{i:000}");
                foreach (var v in quad)
                    sb.AppendLine($"v {v.x:F4} {v.y:F4} {-v.z:F4}");
                sb.AppendLine($"f {vertOffset} {vertOffset + 1} {vertOffset + 2}");
                sb.AppendLine($"f {vertOffset} {vertOffset + 2} {vertOffset + 3}");
                sb.AppendLine();
                vertOffset += 4;
            }

            return sb.ToString();
        }
    }
}
