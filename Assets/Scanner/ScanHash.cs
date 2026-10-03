using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Scanner
{
    // Hash de contenido de un escaneo: SHA-256 de su JSON canónico + los bytes del PNG
    // de referencia. Excluye el nombre (renombrar o el "x (2)" del import no lo
    // cambian) y el propio contentHash.
    public static class ScanHash
    {
        public static string Compute(ScanData data, byte[] png)
        {
            var copia = JsonUtility.FromJson<ScanData>(JsonUtility.ToJson(data));
            copia.name = "";
            copia.contentHash = "";
            var json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(copia));

            using var sha = SHA256.Create();
            sha.TransformBlock(json, 0, json.Length, null, 0);
            var img = png ?? System.Array.Empty<byte>();
            sha.TransformFinalBlock(img, 0, img.Length);

            var sb = new StringBuilder(64);
            foreach (var b in sha.Hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
