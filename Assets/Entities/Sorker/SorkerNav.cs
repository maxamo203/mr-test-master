using System.Collections.Generic;
using UnityEngine;
using Scanner;

// Navegacion 2D propia (A* sobre grid) para el Sorker, SIN NavMesh ni paquetes.
//
// Por que custom y no el Agent de Unity: la geometria se escanea en runtime y todo
// es anchor-relativo (hijo de WorldOrigin). Este grid se construye en espacio
// WorldOrigin a partir de los segmentos de pared (+ rangos de puerta) y los cubos
// del SceneRegistry, asi que sobrevive a las recalibraciones del anchor sin
// re-hornear nada: las posiciones de inicio/destino se convierten world->anchor
// con el WorldOrigin actual en cada consulta.
//
// Solo se usa en el servidor (la IA es autoritativa). Singleton; se auto-crea.
public class SorkerNav : MonoBehaviour
{
    public static SorkerNav Instance { get; private set; }

    [Header("Grid")]
    [Tooltip("Lado de celda en metros. Mas chico = mas preciso, mas costoso.")]
    [SerializeField] private float _cellSize = 0.12f;
    [Tooltip("Radio del agente (hitbox de navegacion). Mas chico que el modelo real " +
             "para que pase por huecos justos. Infla los obstaculos.")]
    [SerializeField] private float _agentRadius = 0.2f;
    [Tooltip("Alto del agente (m). Un cubo que quede entero por encima no bloquea (pasa por debajo).")]
    [SerializeField] private float _agentHeight = 1.6f;
    [Tooltip("Tope de celdas por lado para no explotar la memoria; si el mapa es mas grande, se agranda la celda.")]
    [SerializeField] private int _maxCells = 300;

    // Una puerta cuenta como hueco caminable si su base esta a <= esto del piso.
    private const float DoorFloorThresh = 0.2f;

    // ── Grid ────────────────────────────────────────────────────────────────
    private bool[,] _blocked;
    private int     _cols, _rows;
    private float   _minX, _minZ;     // origen del grid en anchor-XZ
    private float   _cell;            // tamano efectivo de celda
    private float   _floorY;          // plano de navegacion (anchor Y)
    private bool    _built;
    private int     _lastSig;         // firma (cant. paredes/cubos) para auto-rebuild

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public static SorkerNav Ensure()
    {
        if (Instance == null)
        {
            var go = new GameObject("SorkerNav");
            DontDestroyOnLoad(go);
            go.AddComponent<SorkerNav>();
        }
        return Instance;
    }

    private static int SceneSignature()
    {
        var reg = SceneRegistry.Instance;
        if (reg == null) return 0;
        return reg.Walls.Count * 73856093 ^ reg.Cubes.Count * 19349663;
    }

    // Construye (o reconstruye) el grid desde el SceneRegistry actual.
    public void Rebuild()
    {
        _built = false;
        var reg = SceneRegistry.Instance;
        if (WorldOrigin.Instance == null || reg == null) return;

        _floorY = FloorPoint.Instance != null ? FloorPoint.Instance.LocalY : 0f;

        // 1) Bounds en anchor-XZ a partir de las esquinas de paredes y cubos.
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        bool any = false;

        foreach (var w in reg.Walls)
        {
            if (w == null) continue;
            any = true;
            foreach (var p in WallFootprintXZ(w))
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y);
            }
        }
        foreach (var c in reg.Cubes)
        {
            if (c == null) continue;
            any = true;
            foreach (var p in CubeFootprintXZ(c))
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y);
            }
        }

        if (!any) { _blocked = null; _built = true; _lastSig = SceneSignature(); return; }

        // Padding para que el agente pueda rodear por afuera.
        float pad = _agentRadius + 1.0f;
        minX -= pad; minZ -= pad; maxX += pad; maxZ += pad;

        // 2) Resolucion: respetar _cellSize pero capear la cantidad de celdas.
        _cell = _cellSize;
        _cols = Mathf.CeilToInt((maxX - minX) / _cell);
        _rows = Mathf.CeilToInt((maxZ - minZ) / _cell);
        if (_cols > _maxCells || _rows > _maxCells)
        {
            float scale = Mathf.Max(_cols, _rows) / (float)_maxCells;
            _cell *= scale;
            _cols = Mathf.CeilToInt((maxX - minX) / _cell);
            _rows = Mathf.CeilToInt((maxZ - minZ) / _cell);
        }
        _minX = minX; _minZ = minZ;

        // 3) Marcar celdas bloqueadas (centro de celda dentro de pared/cubo).
        var raw = new bool[_cols, _rows];
        for (int c = 0; c < _cols; c++)
        for (int r = 0; r < _rows; r++)
        {
            float x = _minX + (c + 0.5f) * _cell;
            float z = _minZ + (r + 0.5f) * _cell;
            raw[c, r] = IsSolid(reg, x, z);
        }

        // 4) Inflar por el radio del agente (dilatacion en disco).
        int rad = Mathf.Max(0, Mathf.CeilToInt(_agentRadius / _cell));
        _blocked = new bool[_cols, _rows];
        if (rad == 0)
        {
            _blocked = raw;
        }
        else
        {
            int rad2 = rad * rad;
            for (int c = 0; c < _cols; c++)
            for (int r = 0; r < _rows; r++)
            {
                if (!raw[c, r]) continue;
                for (int dc = -rad; dc <= rad; dc++)
                for (int dr = -rad; dr <= rad; dr++)
                {
                    if (dc * dc + dr * dr > rad2) continue;
                    int nc = c + dc, nr = r + dr;
                    if (nc < 0 || nr < 0 || nc >= _cols || nr >= _rows) continue;
                    _blocked[nc, nr] = true;
                }
            }
        }

        _built = true;
        _lastSig = SceneSignature();
    }

    // ── Test de solidez en (x,z) anchor-local ─────────────────────────────
    private bool IsSolid(SceneRegistry reg, float x, float z)
    {
        foreach (var w in reg.Walls)
            if (w != null && PointInWall(w, x, z)) return true;
        foreach (var c in reg.Cubes)
            if (c != null && PointInCube(c, x, z)) return true;
        return false;
    }

    private bool PointInWall(WallObject w, float x, float z)
    {
        Vector2 a   = new Vector2(w.ALocal.x, w.ALocal.z);
        Vector2 b   = new Vector2(w.BLocal.x, w.BLocal.z);
        Vector2 dir = b - a;
        float len = dir.magnitude;
        if (len < 1e-4f) return false;
        Vector2 baseHat = dir / len;
        Vector2 normal  = new Vector2(w.Normal.x, w.Normal.z).normalized;

        Vector2 rel = new Vector2(x, z) - a;
        float u = Vector2.Dot(rel, baseHat);     // a lo largo de la pared
        float wv = Vector2.Dot(rel, normal);     // a traves del grosor
        if (u < 0f || u > len || wv < 0f || wv > w.Width) return false;

        // Hueco de puerta: si hay una puerta que llega al piso y cubre este u, no bloquea.
        foreach (var d in w.Doors)
            if (d != null && d.vMin <= DoorFloorThresh && u >= d.uMin && u <= d.uMax)
                return false;

        return true;
    }

    private bool PointInCube(CubeObject c, float x, float z)
    {
        var tr  = c.transform;
        Vector3 pos = tr.localPosition;     // anchor-local (hijo de WorldOrigin)
        Vector3 sc  = tr.localScale;

        // Solo bloquea si el cubo solapa verticalmente la franja del agente sobre el piso.
        float cubeBottom = pos.y - sc.y * 0.5f;
        float cubeTop    = pos.y + sc.y * 0.5f;
        if (cubeTop < _floorY || cubeBottom > _floorY + _agentHeight) return false;

        // Punto en rectangulo orientado (XZ) usando la rotacion local.
        Vector3 local = Quaternion.Inverse(tr.localRotation) * (new Vector3(x, pos.y, z) - pos);
        return Mathf.Abs(local.x) <= sc.x * 0.5f && Mathf.Abs(local.z) <= sc.z * 0.5f;
    }

    // Esquinas XZ (anchor-local) del footprint, para los bounds del grid.
    private static IEnumerable<Vector2> WallFootprintXZ(WallObject w)
    {
        Vector2 a = new Vector2(w.ALocal.x, w.ALocal.z);
        Vector2 b = new Vector2(w.BLocal.x, w.BLocal.z);
        Vector2 n = new Vector2(w.Normal.x, w.Normal.z).normalized * w.Width;
        yield return a; yield return b; yield return a + n; yield return b + n;
    }

    private static IEnumerable<Vector2> CubeFootprintXZ(CubeObject c)
    {
        var tr = c.transform;
        Vector3 pos = tr.localPosition; Vector3 sc = tr.localScale; Quaternion rot = tr.localRotation;
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sz = -1; sz <= 1; sz += 2)
        {
            Vector3 corner = pos + rot * new Vector3(sx * sc.x * 0.5f, 0f, sz * sc.z * 0.5f);
            yield return new Vector2(corner.x, corner.z);
        }
    }

    // ── A* ────────────────────────────────────────────────────────────────
    // Devuelve true y llena outWorld con waypoints en WORLD space (incluido el
    // destino). startWorld/goalWorld en world; se convierten a anchor adentro.
    public bool TryGetPath(Vector3 startWorld, Vector3 goalWorld, List<Vector3> outWorld)
    {
        outWorld.Clear();
        if (WorldOrigin.Instance == null) return false;

        // Auto-build / auto-rebuild si cambio el mapa.
        if (!_built || SceneSignature() != _lastSig) Rebuild();
        if (_blocked == null) return false; // sin obstaculos: el caller va directo

        Vector3 sLocal = WorldOrigin.Instance.ToRelative(startWorld);
        Vector3 gLocal = WorldOrigin.Instance.ToRelative(goalWorld);

        if (!CellOf(sLocal.x, sLocal.z, out int sc, out int sr)) return false;
        if (!CellOf(gLocal.x, gLocal.z, out int gc, out int gr)) return false;
        // Si inicio/fin caen en celda bloqueada (pegado a pared), buscar la libre mas cercana.
        if (_blocked[sc, sr] && !NearestFree(sc, sr, out sc, out sr)) return false;
        if (_blocked[gc, gr] && !NearestFree(gc, gr, out gc, out gr)) return false;

        var path = AStar(sc, sr, gc, gr);
        if (path == null) return false;

        // Suavizado (string-pulling) por linea de vista sobre el grid.
        var simplified = Simplify(path);
        foreach (var idx in simplified)
        {
            CellCenter(idx.x, idx.y, out float x, out float z);
            outWorld.Add(WorldOrigin.Instance.ToWorld(new Vector3(x, _floorY, z)));
        }
        // Reemplazar el ultimo waypoint por el destino real (mas preciso que el centro de celda).
        if (outWorld.Count > 0) outWorld[outWorld.Count - 1] = new Vector3(goalWorld.x, goalWorld.y, goalWorld.z);
        return outWorld.Count > 0;
    }

    private List<Vector2Int> AStar(int sc, int sr, int gc, int gr)
    {
        int n = _cols * _rows;
        var came  = new int[n];
        var gScore = new float[n];
        var closed = new bool[n];
        for (int i = 0; i < n; i++) { came[i] = -1; gScore[i] = float.MaxValue; }

        int Id(int c, int r) => r * _cols + c;
        int start = Id(sc, sr), goal = Id(gc, gr);
        gScore[start] = 0f;

        var open = new MinHeap(n);
        open.Push(start, Heur(sc, sr, gc, gr));

        int[] dcs = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] drs = { 0, 0, 1, -1, 1, -1, 1, -1 };

        while (open.Count > 0)
        {
            int cur = open.Pop();
            if (cur == goal) break;
            if (closed[cur]) continue;
            closed[cur] = true;

            int cc = cur % _cols, cr = cur / _cols;
            for (int k = 0; k < 8; k++)
            {
                int nc = cc + dcs[k], nr = cr + drs[k];
                if (nc < 0 || nr < 0 || nc >= _cols || nr >= _rows) continue;
                if (_blocked[nc, nr]) continue;
                bool diag = k >= 4;
                // No cortar esquinas: en diagonal, los dos ortogonales deben estar libres.
                if (diag && (_blocked[cc + dcs[k], cr] || _blocked[cc, cr + drs[k]])) continue;

                int nid = Id(nc, nr);
                if (closed[nid]) continue;
                float step = diag ? 1.41421356f : 1f;
                float tentative = gScore[cur] + step;
                if (tentative < gScore[nid])
                {
                    came[nid] = cur;
                    gScore[nid] = tentative;
                    open.Push(nid, tentative + Heur(nc, nr, gc, gr));
                }
            }
        }

        if (came[goal] == -1 && goal != start) return null;

        var path = new List<Vector2Int>();
        for (int cur = goal; cur != -1; cur = came[cur])
            path.Add(new Vector2Int(cur % _cols, cur / _cols));
        path.Reverse();
        return path;
    }

    private float Heur(int c, int r, int gc, int gr)
    {
        int dx = Mathf.Abs(c - gc), dy = Mathf.Abs(r - gr);
        // Octile distance.
        return (dx + dy) + (1.41421356f - 2f) * Mathf.Min(dx, dy);
    }

    // String-pulling: descarta waypoints intermedios con linea de vista libre.
    private List<Vector2Int> Simplify(List<Vector2Int> path)
    {
        if (path.Count <= 2) return path;
        var outp = new List<Vector2Int> { path[0] };
        int anchor = 0;
        for (int i = 2; i < path.Count; i++)
        {
            if (!LineOfSight(path[anchor], path[i]))
            {
                outp.Add(path[i - 1]);
                anchor = i - 1;
            }
        }
        outp.Add(path[path.Count - 1]);
        return outp;
    }

    private bool LineOfSight(Vector2Int a, Vector2Int b)
    {
        int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            if (_blocked[x0, y0]) return false;
            if (x0 == x1 && y0 == y1) return true;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 <  dx) { err += dx; y0 += sy; }
        }
    }

    // ── Helpers de grid ──────────────────────────────────────────────────
    private bool CellOf(float x, float z, out int c, out int r)
    {
        c = Mathf.FloorToInt((x - _minX) / _cell);
        r = Mathf.FloorToInt((z - _minZ) / _cell);
        return c >= 0 && r >= 0 && c < _cols && r < _rows;
    }

    private void CellCenter(int c, int r, out float x, out float z)
    {
        x = _minX + (c + 0.5f) * _cell;
        z = _minZ + (r + 0.5f) * _cell;
    }

    // BFS en anillos para encontrar la celda libre mas cercana a (c,r).
    private bool NearestFree(int c, int r, out int fc, out int fr)
    {
        fc = c; fr = r;
        int maxRing = Mathf.Max(_cols, _rows);
        for (int ring = 1; ring <= maxRing; ring++)
        {
            for (int dc = -ring; dc <= ring; dc++)
            for (int dr = -ring; dr <= ring; dr++)
            {
                if (Mathf.Max(Mathf.Abs(dc), Mathf.Abs(dr)) != ring) continue;
                int nc = c + dc, nr = r + dr;
                if (nc < 0 || nr < 0 || nc >= _cols || nr >= _rows) continue;
                if (!_blocked[nc, nr]) { fc = nc; fr = nr; return true; }
            }
        }
        return false;
    }

    // ── Min-heap simple (f-score) ─────────────────────────────────────────
    private class MinHeap
    {
        private int[]   _id;
        private float[] _f;
        public  int     Count;

        public MinHeap(int cap) { cap = Mathf.Max(16, cap); _id = new int[cap]; _f = new float[cap]; }

        public void Push(int id, float f)
        {
            if (Count == _id.Length) { System.Array.Resize(ref _id, Count * 2); System.Array.Resize(ref _f, Count * 2); }
            _id[Count] = id; _f[Count] = f;
            int i = Count++;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_f[p] <= _f[i]) break;
                Swap(i, p); i = p;
            }
        }

        public int Pop()
        {
            int top = _id[0];
            Count--;
            _id[0] = _id[Count]; _f[0] = _f[Count];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, rr = 2 * i + 2, s = i;
                if (l < Count && _f[l] < _f[s]) s = l;
                if (rr < Count && _f[rr] < _f[s]) s = rr;
                if (s == i) break;
                Swap(i, s); i = s;
            }
            return top;
        }

        private void Swap(int a, int b)
        {
            (_id[a], _id[b]) = (_id[b], _id[a]);
            (_f[a], _f[b]) = (_f[b], _f[a]);
        }
    }
}
