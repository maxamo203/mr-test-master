using System.Collections.Generic;
using UnityEngine;

namespace Scanner
{
    // Nube de puntos del mapeo LiDAR. Guarda los puntos en coordenadas
    // anchor-relativas (los GameObjects de render son hijos de WorldOrigin, asi
    // la nube sobrevive a recalibraciones igual que paredes/cubos).
    //
    // El filtro de distancia minima usa un hash espacial: un punto nuevo se
    // acepta solo si no hay otro ya aceptado a menos de MinDistance. El slider
    // de la UI controla esa distancia; aplica a los puntos NUEVOS (no re-filtra
    // lo ya capturado).
    //
    // Render: meshes con MeshTopology.Points en chunks, shader Custom/PointCloud
    // (vive en Resources para que no lo stripee el build).
    [DefaultExecutionOrder(-30)]
    public class LidarPointCloud : MonoBehaviour
    {
        public static LidarPointCloud Instance { get; private set; }

        private const int ChunkSize = 30000;   // puntos por mesh de render
        public const float MinDistanceFloor   = 0.02f;
        public const float MinDistanceCeiling = 0.5f;

        private readonly List<Vector3> _points = new();
        // Hash espacial para el filtro de distancia minima. La celda es fija
        // (el techo del slider) para no tener que re-hashear al mover el slider;
        // el chequeo fino de distancia se hace contra los vecinos 3x3x3.
        private readonly Dictionary<long, List<int>> _grid = new();
        private const float CellSize = MinDistanceCeiling;

        private float _minDistance = 0.05f;
        public float MinDistance
        {
            get => _minDistance;
            set => _minDistance = Mathf.Clamp(value, MinDistanceFloor, MinDistanceCeiling);
        }

        public int Count => _points.Count;
        public IReadOnlyList<Vector3> Points => _points;

        // Chunks de render (hijos de WorldOrigin). El ultimo se reconstruye al
        // agregar puntos; los anteriores quedan congelados.
        private readonly List<MeshFilter> _chunks = new();
        private Transform _renderRoot;
        private Material  _pointMat;
        private bool _dirty;
        // true => reconstruir TODOS los chunks (Clear/SetPoints invalidan los
        // ya congelados); false => solo crece el ultimo.
        private bool _rebuildAll;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        // ── Alta de puntos ────────────────────────────────────────────────────

        // Agrega un punto (anchor-local) respetando la distancia minima.
        // Devuelve true si el punto fue aceptado.
        public bool AddFiltered(Vector3 local)
        {
            if (HasNeighborCloserThan(local, _minDistance)) return false;
            Accept(local);
            _dirty = true;
            return true;
        }

        // Reemplaza toda la nube (carga de un scan guardado). Los puntos ya
        // vienen filtrados de origen, se insertan directo al hash.
        public void SetPoints(List<Vector3> points, float minDistance)
        {
            Clear();
            if (minDistance > 0f) MinDistance = minDistance;
            if (points != null)
                foreach (var p in points) Accept(p);
            _dirty = true;
        }

        public void Clear()
        {
            _points.Clear();
            _grid.Clear();
            _dirty = true;
            _rebuildAll = true;
        }

        private void Accept(Vector3 p)
        {
            _points.Add(p);
            var key = CellKey(p);
            if (!_grid.TryGetValue(key, out var list))
            {
                list = new List<int>();
                _grid[key] = list;
            }
            list.Add(_points.Count - 1);
        }

        private bool HasNeighborCloserThan(Vector3 p, float dist)
        {
            float sq = dist * dist;
            int cx = Mathf.FloorToInt(p.x / CellSize);
            int cy = Mathf.FloorToInt(p.y / CellSize);
            int cz = Mathf.FloorToInt(p.z / CellSize);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!_grid.TryGetValue(Pack(cx + dx, cy + dy, cz + dz), out var list)) continue;
                foreach (var idx in list)
                    if ((_points[idx] - p).sqrMagnitude < sq) return true;
            }
            return false;
        }

        private static long CellKey(Vector3 p) => Pack(
            Mathf.FloorToInt(p.x / CellSize),
            Mathf.FloorToInt(p.y / CellSize),
            Mathf.FloorToInt(p.z / CellSize));

        // 21 bits por eje con offset — sobra para cualquier cuarto real.
        private static long Pack(int x, int y, int z) =>
            ((long)(x + 1048576) << 42) | ((long)(y + 1048576) << 21) | (long)(z + 1048576);

        // ── Render ────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (!_dirty) return;
            var wo = WorldOrigin.Instance;
            if (wo == null) return;
            _dirty = false;
            RebuildChunks(wo);
        }

        private void RebuildChunks(WorldOrigin wo)
        {
            if (_renderRoot == null)
            {
                var go = new GameObject("LidarPointCloudRender");
                go.transform.SetParent(wo.transform, worldPositionStays: false);
                _renderRoot = go.transform;
            }

            int neededChunks = (_points.Count + ChunkSize - 1) / ChunkSize;

            // Sobran chunks (Clear / carga con menos puntos): destruir extras.
            for (int i = _chunks.Count - 1; i >= neededChunks; i--)
            {
                if (_chunks[i] != null)
                {
                    if (_chunks[i].sharedMesh != null) Destroy(_chunks[i].sharedMesh);
                    Destroy(_chunks[i].gameObject);
                }
                _chunks.RemoveAt(i);
            }

            if (neededChunks == 0) { _rebuildAll = false; return; }

            // Normalmente solo el ultimo chunk crece; tras Clear/SetPoints se
            // reconstruye todo (los chunks viejos tienen datos invalidos).
            int first = _rebuildAll ? 0 : Mathf.Max(0, _chunks.Count - 1);
            for (int c = first; c < neededChunks; c++)
            {
                if (c >= _chunks.Count) _chunks.Add(CreateChunk(c));
                var mf = _chunks[c];
                if (mf == null) continue;

                int start = c * ChunkSize;
                int count = Mathf.Min(ChunkSize, _points.Count - start);
                // Si el chunk ya esta completo y lleno, no lo tocamos.
                if (!_rebuildAll && mf.sharedMesh != null && mf.sharedMesh.vertexCount == count) continue;

                var verts = new Vector3[count];
                _points.CopyTo(start, verts, 0, count);
                var indices = new int[count];
                for (int i = 0; i < count; i++) indices[i] = i;

                if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
                var mesh = new Mesh { name = $"LidarPoints_{c}" };
                if (count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(verts);
                mesh.SetIndices(indices, MeshTopology.Points, 0);
                mesh.RecalculateBounds();
                mf.sharedMesh = mesh;
            }
            _rebuildAll = false;
        }

        private MeshFilter CreateChunk(int index)
        {
            var go = new GameObject($"LidarPointChunk_{index}");
            go.transform.SetParent(_renderRoot, worldPositionStays: false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial       = PointMaterial();
            mr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows       = false;
            mr.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return mf;
        }

        private Material PointMaterial()
        {
            if (_pointMat != null) return _pointMat;
            var sh = Resources.Load<Shader>("PointCloud")
                  ?? Shader.Find("Custom/PointCloud")
                  ?? Shader.Find("Unlit/Color");
            _pointMat = new Material(sh) { name = "LidarPointCloudMat (runtime)" };
            var col = new Color(0.2f, 1f, 0.6f, 1f);
            if (_pointMat.HasProperty("_Color"))     _pointMat.color = col;
            if (_pointMat.HasProperty("_BaseColor")) _pointMat.SetColor("_BaseColor", col);
            return _pointMat;
        }

        private void OnDestroy()
        {
            foreach (var mf in _chunks)
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            if (_pointMat != null) Destroy(_pointMat);
            if (Instance == this) Instance = null;
        }
    }
}
