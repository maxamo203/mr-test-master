using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Scanner
{
    // Guarda y carga ScanData a/desde Application.persistentDataPath/scans/<name>.json
    // En iOS esto vive en el sandbox del app (no visible al usuario directamente);
    // en Android es app-private storage.
    public static class ScanSerializer
    {
        private const string SubDir = "scans";

        private static string ScansDir
        {
            get
            {
                var path = Path.Combine(Application.persistentDataPath, SubDir);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string PathFor(string name) =>
            Path.Combine(ScansDir, SanitizeName(name) + ".json");

        // PNG hermano del json con la imagen de referencia del escaneo.
        public static string RefImagePathFor(string name) =>
            Path.Combine(ScansDir, SanitizeName(name) + ".png");

        public static bool HasRefImage(string name) => File.Exists(RefImagePathFor(name));

        // Guarda la imagen de referencia como PNG junto al json.
        public static void SaveRefImage(string name, Texture2D tex)
        {
            if (tex == null) return;
            var png = tex.EncodeToPNG();
            if (png == null)
            {
                Debug.LogWarning($"[ScanSerializer] No se pudo codificar la imagen de referencia de '{name}' (¿textura no legible?).");
                return;
            }
            File.WriteAllBytes(RefImagePathFor(name), png);
            Debug.Log($"[ScanSerializer] Imagen de referencia guardada en {RefImagePathFor(name)}");
        }

        // Carga la imagen de referencia como Texture2D legible, o null si no existe.
        public static Texture2D LoadRefImage(string name)
        {
            var path = RefImagePathFor(name);
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!tex.LoadImage(bytes, markNonReadable: false))
            {
                Debug.LogWarning($"[ScanSerializer] No se pudo decodificar '{path}'.");
                return null;
            }
            return tex;
        }

        public static void Save(string name, ScanData data)
        {
            data.name = name;
            var json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(PathFor(name), json);
            Debug.Log($"[ScanSerializer] Guardado '{name}' en {PathFor(name)}");
        }

        public static ScanData Load(string name)
        {
            var path = PathFor(name);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[ScanSerializer] No existe '{path}'");
                return null;
            }
            var json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<ScanData>(json);
            Debug.Log($"[ScanSerializer] Cargado '{name}' ({data?.walls?.Count ?? 0} walls, {data?.cubes?.Count ?? 0} cubes)");
            return data;
        }

        public static List<string> ListSaved()
        {
            var result = new List<string>();
            var files = Directory.GetFiles(ScansDir, "*.json");
            foreach (var f in files) result.Add(Path.GetFileNameWithoutExtension(f));
            result.Sort();
            return result;
        }

        public static bool Delete(string name)
        {
            var path = PathFor(name);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            var png = RefImagePathFor(name);
            if (File.Exists(png)) File.Delete(png);
            return true;
        }

        private static string SanitizeName(string n)
        {
            if (string.IsNullOrWhiteSpace(n)) return "untitled";
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Trim();
        }
    }
}
