using System.IO;
using UnityEngine;

namespace Scanner
{
    // Empaqueta/desempaqueta un escaneo completo en un archivo .MSCN para
    // compartir entre dispositivos. Contenedor binario propio (sin dependencias de
    // System.IO.Compression, que puede stripearse bajo IL2CPP):
    //
    //   [4]        magic "MSCN"
    //   [4 int32]  version (LE)
    //   [4 int32]  jsonLen
    //   [jsonLen]  ScanData en JSON (UTF-8) — ya trae name, refImageWidthMeters, etc.
    //   [4 int32]  pngLen (0 si no hay imagen de referencia)
    //   [pngLen]   bytes del PNG de referencia
    public static class ScanPackage
    {
        public const string Extension = "mscn";
        private const int Version = 1;
        private static readonly byte[] Magic = { (byte)'M', (byte)'S', (byte)'C', (byte)'N' };

        // Arma el contenedor a partir de un escaneo ya guardado en disco.
        public static byte[] Pack(string name)
        {
            var jsonPath = ScanSerializer.PathFor(name);
            if (!File.Exists(jsonPath))
            {
                Debug.LogWarning($"[ScanPackage] No existe el escaneo '{name}' para empaquetar.");
                return null;
            }
            byte[] json = File.ReadAllBytes(jsonPath);

            byte[] png = System.Array.Empty<byte>();
            var pngPath = ScanSerializer.RefImagePathFor(name);
            if (File.Exists(pngPath)) png = File.ReadAllBytes(pngPath);

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(json.Length);
                w.Write(json);
                w.Write(png.Length);
                w.Write(png);
            }
            return ms.ToArray();
        }

        // Empaqueta y escribe a un archivo temporal listo para compartir.
        // Devuelve el path del .mscn (o null si falla).
        public static string WriteTempFile(string name)
        {
            var bytes = Pack(name);
            if (bytes == null) return null;
            var path = Path.Combine(Application.temporaryCachePath, SanitizeFileName(name) + "." + Extension);
            File.WriteAllBytes(path, bytes);
            Debug.Log($"[ScanPackage] .{Extension} escrito en {path} ({bytes.Length} bytes)");
            return path;
        }

        // True si los bytes empiezan con el magic del formato.
        public static bool LooksValid(byte[] data)
        {
            if (data == null || data.Length < 4) return false;
            for (int i = 0; i < 4; i++) if (data[i] != Magic[i]) return false;
            return true;
        }

        // Desempaqueta e instala el escaneo en el directorio de scans (con dedupe de
        // nombre si ya existe). Devuelve el name instalado, o null si es inválido.
        public static string Import(byte[] data)
        {
            if (!LooksValid(data))
            {
                Debug.LogWarning("[ScanPackage] Archivo no reconocido (magic MSCN ausente).");
                return null;
            }
            try
            {
                using var ms = new MemoryStream(data);
                using var r = new BinaryReader(ms);
                r.ReadBytes(4); // magic (ya validado)
                int version = r.ReadInt32();
                if (version > Version)
                    Debug.LogWarning($"[ScanPackage] Versión {version} más nueva que la soportada ({Version}); se intenta igual.");

                int jsonLen = r.ReadInt32();
                if (jsonLen <= 0 || jsonLen > ms.Length) return null;
                byte[] jsonBytes = r.ReadBytes(jsonLen);

                int pngLen = r.ReadInt32();
                byte[] pngBytes = pngLen > 0 ? r.ReadBytes(pngLen) : System.Array.Empty<byte>();

                var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
                var scan = JsonUtility.FromJson<ScanData>(json);
                if (scan == null) { Debug.LogWarning("[ScanPackage] JSON inválido."); return null; }

                string name = UniqueName(string.IsNullOrWhiteSpace(scan.name) ? "importado" : scan.name);
                scan.name = name;
                ScanSerializer.Save(name, scan);
                if (pngBytes.Length > 0)
                    File.WriteAllBytes(ScanSerializer.RefImagePathFor(name), pngBytes);

                Debug.Log($"[ScanPackage] Importado '{name}' ({scan.walls?.Count ?? 0} walls, " +
                          $"{scan.cubes?.Count ?? 0} cubes, img={pngBytes.Length} bytes)");
                return name;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ScanPackage] Error al importar: {e.Message}");
                return null;
            }
        }

        // Devuelve un name que no choque con un escaneo existente: "x", "x (2)", …
        private static string UniqueName(string baseName)
        {
            if (!File.Exists(ScanSerializer.PathFor(baseName))) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                var candidate = $"{baseName} ({i})";
                if (!File.Exists(ScanSerializer.PathFor(candidate))) return candidate;
            }
            return baseName + " " + System.Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        private static string SanitizeFileName(string n)
        {
            if (string.IsNullOrWhiteSpace(n)) return "scan";
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Trim();
        }
    }
}
