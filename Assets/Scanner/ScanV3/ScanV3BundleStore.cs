using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Scanner.ScanV3
{
    // Persistencia temporal y atomica. El manifest se reemplaza solo despues de
    // escribir el archivo nuevo; una interrupcion conserva la ultima version valida.
    public sealed class ScanV3BundleStore : IDisposable
    {
        [Serializable]
        private sealed class ObservationSidecar
        {
            public List<ScanV3CameraObservation> observations = new();
        }
        private const string ManifestName = "manifest.json";
        private readonly string _root;
        private readonly ScanV3BundleManifest _manifest;
        public string RootPath => _root;
        public ScanV3BundleManifest Manifest => _manifest;

        public ScanV3BundleStore(string captureId = null, string basePath = null)
        {
            captureId ??= Guid.NewGuid().ToString("N");
            basePath ??= Path.Combine(Application.persistentDataPath, "scan-v3-captures");
            _root = Path.Combine(basePath, captureId);
            Directory.CreateDirectory(_root);
            _manifest = new ScanV3BundleManifest
            {
                captureId = captureId,
                createdUtc = DateTime.UtcNow.ToString("O"),
            };
            Flush();
        }

        private ScanV3BundleStore(string root, ScanV3BundleManifest manifest)
        {
            _root = root;
            _manifest = manifest;
        }

        public static bool TryOpenLatestIncomplete(out ScanV3BundleStore store,
                                                   string basePath = null)
        {
            store = null;
            basePath ??= Path.Combine(Application.persistentDataPath, "scan-v3-captures");
            if (!Directory.Exists(basePath)) return false;
            string selectedRoot = null;
            ScanV3BundleManifest selected = null;
            DateTime selectedTime = DateTime.MinValue;
            foreach (string directory in Directory.GetDirectories(basePath))
            {
                string manifestPath = Path.Combine(directory, ManifestName);
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var candidate = JsonUtility.FromJson<ScanV3BundleManifest>(
                        File.ReadAllText(manifestPath));
                    if (candidate == null || candidate.version != ScanV3BundleManifest.CurrentVersion ||
                        candidate.completed) continue;
                    DateTime time = File.GetLastWriteTimeUtc(manifestPath);
                    if (time <= selectedTime) continue;
                    selectedRoot = directory;
                    selected = candidate;
                    selectedTime = time;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[ScanV3] Bundle incompleto invalido en {directory}: {exception.Message}");
                }
            }
            if (selected == null) return false;
                    selected.keyframes ??= new System.Collections.Generic.List<ScanV3Keyframe>();
            foreach (var frame in selected.keyframes)
            {
                frame.observations = new List<ScanV3CameraObservation>();
                if (string.IsNullOrEmpty(frame.observationFile)) continue;
                string sidecarPath = Path.Combine(selectedRoot, frame.observationFile);
                if (!File.Exists(sidecarPath)) continue;
                try
                {
                    var sidecar = JsonUtility.FromJson<ObservationSidecar>(File.ReadAllText(sidecarPath));
                    if (sidecar?.observations != null) frame.observations = sidecar.observations;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[ScanV3] Observaciones invalidas de frame {frame.id}: {exception.Message}");
                }
            }
            store = new ScanV3BundleStore(selectedRoot, selected);
            return true;
        }

        public void AddKeyframe(ScanV3Keyframe keyframe, byte[] jpeg = null)
        {
            if (keyframe == null) throw new ArgumentNullException(nameof(keyframe));
            if (jpeg != null && jpeg.Length > 0)
            {
                string filename = $"frame-{keyframe.id:D5}.jpg";
                WriteAtomic(Path.Combine(_root, filename), jpeg);
                keyframe.imageFile = filename;
            }
            string observationFilename = $"frame-{keyframe.id:D5}.observations.json";
            var sidecar = new ObservationSidecar
            {
                observations = keyframe.observations ?? new List<ScanV3CameraObservation>(),
            };
            WriteAtomic(Path.Combine(_root, observationFilename),
                        Encoding.UTF8.GetBytes(JsonUtility.ToJson(sidecar)));
            keyframe.observationFile = observationFilename;
            _manifest.keyframes.Add(keyframe);
            Flush();
        }

        public void Complete()
        {
            _manifest.completed = true;
            Flush();
        }

        public void Flush()
        {
            string json = JsonUtility.ToJson(_manifest, true);
            WriteAtomic(Path.Combine(_root, ManifestName), Encoding.UTF8.GetBytes(json));
        }

        public void Delete()
        {
            if (!Directory.Exists(_root)) return;
            Directory.Delete(_root, true);
        }

        public void Dispose() { }

        private static void WriteAtomic(string target, byte[] content)
        {
            string temporary = target + ".tmp";
            File.WriteAllBytes(temporary, content);
            if (File.Exists(target))
                File.Replace(temporary, target, null);
            else
                File.Move(temporary, target);
        }
    }
}
