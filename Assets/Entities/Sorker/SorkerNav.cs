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

    // ── Grid ────────────────────────────────────────────────────────────────
    // _blocked: obstaculos inflados por el radio del agente (por donde camina el centro).
    // _solid:   obstaculos reales, sin inflar (para "hay una pared de por medio").
    // En los dos, toda puerta es un hueco libre (ver TallarPuertas).
    private bool[,] _blocked;
    private bool[,] _solid;
    // Si no hay grid de obstaculos, los directores pueden avanzar en linea recta. Si hay
    // grid y TryGetPath falla, no deben usar ese fallback porque atravesarian paredes.
    public bool HasObstacleGrid => _blocked != null;
    private int     _cols, _rows;
    private float   _minX, _minZ;     // origen del grid en anchor-XZ
    private float   _cell;            // tamano efectivo de celda
    private float   _floorY;          // plano de navegacion (anchor Y)
    private bool    _pisoConocido;    // _floorY sale del PISO o de la base de las paredes
    private bool    _built;
    private int     _lastSig;         // firma (cant. paredes/cubos) para auto-rebuild

    // El grid cubre el mapa y, ademas, los puntos consultados FUERA de el (jugador en un
    // pasillo o fuera del perimetro escaneado). Antes el grid terminaba a ~1 m del mapa:
    // con el jugador mas afuera no habia ruta, y como con paredes los directores no avanzan
    // en linea recta, los personajes se quedaban quietos. Afuera es espacio abierto: el
    // grid crece para incluir esos puntos (con margen, para no reconstruir a cada paso) y
    // se olvida al cambiar de mapa.
    private bool    _hayExtra;
    private Vector2 _extraMin, _extraMax;
    private const float MargenExtra = 3f;     // m alrededor de un punto de afuera
    private const float BordeMinimo = 0.75f;  // un punto a menos de esto del borde agranda el grid

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

    // Firma del contenido actual del SceneRegistry. Incluye Generacion (que sube en cada
    // ClearAll, o sea en cada carga de mapa) ademas de las cantidades: este singleton es
    // DontDestroyOnLoad, asi que el grid cruza el cambio de escena, y con solo contar
    // paredes/cubos dos mapas distintos con las mismas cantidades daban la misma firma —
    // la IA terminaba navegando la geometria del mapa anterior.
    private static int SceneSignature()
    {
        var reg = SceneRegistry.Instance;
        if (reg == null) return 0;
        return reg.Walls.Count * 73856093 ^ reg.Cubes.Count * 19349663 ^ reg.Generacion * 83492791;
    }

    // ── Piso ──────────────────────────────────────────────────────────────
    // Los personajes caminan SIEMPRE a nivel del piso. Misma regla que pilas, reliquias y
    // Arbmos (SpawnSampler.AlturaPisoRel) en sus dos fuentes confiables: el PISO del
    // escaner o, si no hay, la base mas baja de las paredes. Sin ninguna de las dos no se
    // corrige la altura (la estimacion restante puede ser la de la camara).
    private void CalcularPiso(SceneRegistry reg)
    {
        _pisoConocido = false;
        _floorY = 0f;
        if (FloorPoint.Instance != null)
        {
            _floorY = FloorPoint.Instance.LocalY;
            _pisoConocido = true;
            return;
        }
        if (reg == null) return;
        foreach (var w in reg.Walls)
        {
            if (w == null) continue;
            float baseY = Mathf.Min(w.ALocal.y, w.BLocal.y);
            if (!_pisoConocido || baseY < _floorY) { _floorY = baseY; _pisoConocido = true; }
        }
    }

    // Y del piso (anchor-local). El PISO se lee siempre en vivo (se puede mover); el resto
    // queda de la ultima construccion del grid.
    private float PisoRel => FloorPoint.Instance != null ? FloorPoint.Instance.LocalY : _floorY;
    private bool PisoConocido => FloorPoint.Instance != null || _pisoConocido;

    // Apoya una posicion world en el piso. Los directores la aplican despues de cada paso
    // de persecucion. Sin piso conocido devuelve la posicion tal cual.
    public static Vector3 AlPiso(Vector3 world)
    {
        var nav = Instance;
        var wo = WorldOrigin.Instance;
        if (nav == null || wo == null) return world;
        nav.AsegurarConstruido();
        if (!nav.PisoConocido) return world;
        // WorldOrigin siempre es Y-up (ImageAnchorPose), asi que la Y del piso no depende de XZ.
        world.y = wo.ToWorld(new Vector3(0f, nav.PisoRel, 0f)).y;
        return world;
    }

    private void AsegurarConstruido()
    {
        int sig = SceneSignature();
        if (_built && sig == _lastSig) return;
        if (sig != _lastSig) _hayExtra = false;   // otro mapa: lo de afuera ya no vale
        Rebuild();
    }

    // Asegura que el grid cubra estos dos puntos (anchor-local). Si alguno cae afuera (o
    // pegado al borde), el grid se agranda para incluirlo: afuera es espacio libre.
    private void Asegurar(Vector3 aLocal, Vector3 bLocal)
    {
        AsegurarConstruido();
        if (_blocked == null) return;
        bool aDentro = DentroConMargen(aLocal), bDentro = DentroConMargen(bLocal);
        if (aDentro && bDentro) return;
        if (!aDentro) Incluir(aLocal);
        if (!bDentro) Incluir(bLocal);
        Rebuild();
    }

    private bool DentroConMargen(Vector3 local) =>
        local.x >= _minX + BordeMinimo && local.x <= _minX + _cols * _cell - BordeMinimo &&
        local.z >= _minZ + BordeMinimo && local.z <= _minZ + _rows * _cell - BordeMinimo;

    private void Incluir(Vector3 local)
    {
        var min = new Vector2(local.x - MargenExtra, local.z - MargenExtra);
        var max = new Vector2(local.x + MargenExtra, local.z + MargenExtra);
        if (_hayExtra) { _extraMin = Vector2.Min(_extraMin, min); _extraMax = Vector2.Max(_extraMax, max); }
        else { _extraMin = min; _extraMax = max; _hayExtra = true; }
    }

    // Construye (o reconstruye) el grid desde el SceneRegistry actual.
    public void Rebuild()
    {
        _built = false;
        var reg = SceneRegistry.Instance;
        if (WorldOrigin.Instance == null || reg == null) return;

        CalcularPiso(reg);

        // 1) Bounds en anchor-XZ a partir de las esquinas de paredes y cubos.
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        bool any = false;

        foreach (var w in reg.Walls)
        {
            if (w == null || w.SoloReferencia) continue;
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

        if (!any) { _blocked = null; _solid = null; _built = true; _lastSig = SceneSignature(); return; }

        // Padding para que el agente pueda rodear por afuera.
        float pad = _agentRadius + 1.0f;
        minX -= pad; minZ -= pad; maxX += pad; maxZ += pad;

        // Puntos consultados fuera del mapa (jugador afuera del perimetro).
        if (_hayExtra)
        {
            minX = Mathf.Min(minX, _extraMin.x); minZ = Mathf.Min(minZ, _extraMin.y);
            maxX = Mathf.Max(maxX, _extraMax.x); maxZ = Mathf.Max(maxZ, _extraMax.y);
        }

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

        // 3) Marcar celdas solidas, cada pared/cubo sobre su propio rectangulo de celdas
        //    (recorrer todo el grid por cada objeto era caro con el grid agrandado).
        var raw     = new bool[_cols, _rows];
        var mueble  = new bool[_cols, _rows];   // solido por un cubo (las puertas no lo abren)
        foreach (var w in reg.Walls)
        {
            if (w == null || w.SoloReferencia) continue;
            MarcarFootprint(raw, null, WallFootprintXZ(w), _cell, (x, z) => PointInWall(w, x, z, _cell * 0.5f));
        }
        foreach (var cu in reg.Cubes)
        {
            if (cu == null) continue;
            MarcarFootprint(raw, mueble, CubeFootprintXZ(cu), 0f, (x, z) => PointInCube(cu, x, z));
        }

        // 4) Inflar por el radio del agente (dilatacion en disco).
        int rad = Mathf.Max(0, Mathf.CeilToInt(_agentRadius / _cell));
        _blocked = new bool[_cols, _rows];
        if (rad == 0)
        {
            System.Array.Copy(raw, _blocked, raw.Length);
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

        // 5) Puertas: se tallan DESPUES de inflar, asi que cualquier puerta deja pasar
        //    aunque sea mas angosta que el agente.
        _solid = raw;
        foreach (var w in reg.Walls)
            if (w != null && !w.SoloReferencia) TallarPuertas(w, raw, mueble);

        _built = true;
        _lastSig = SceneSignature();
    }

    // Marca las celdas del rectangulo que envuelve 'esquinas' (+ margen) cuyo centro
    // cumple 'dentro'.
    private void MarcarFootprint(bool[,] grid, bool[,] tambien, IEnumerable<Vector2> esquinas, float margen,
                                 System.Func<float, float, bool> dentro)
    {
        if (!RangoCeldas(esquinas, margen, out int c0, out int r0, out int c1, out int r1)) return;
        for (int c = c0; c <= c1; c++)
        for (int r = r0; r <= r1; r++)
        {
            if (grid[c, r] && tambien == null) continue;
            CellCenter(c, r, out float x, out float z);
            if (!dentro(x, z)) continue;
            grid[c, r] = true;
            if (tambien != null) tambien[c, r] = true;
        }
    }

    private bool RangoCeldas(IEnumerable<Vector2> esquinas, float margen,
                             out int c0, out int r0, out int c1, out int r1)
    {
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var p in esquinas)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y);
        }
        c0 = Mathf.Max(0, Mathf.FloorToInt((minX - margen - _minX) / _cell));
        r0 = Mathf.Max(0, Mathf.FloorToInt((minZ - margen - _minZ) / _cell));
        c1 = Mathf.Min(_cols - 1, Mathf.FloorToInt((maxX + margen - _minX) / _cell));
        r1 = Mathf.Min(_rows - 1, Mathf.FloorToInt((maxZ + margen - _minZ) / _cell));
        return c0 <= c1 && r0 <= r1;
    }

    // Toda puerta puesta en el escaner es un paso, sin importar su ancho real ni su
    // altura: en _blocked se abre un pasillo a lo ancho de la puerta (minimo 3 celdas) que
    // atraviesa el grosor de la pared y el inflado de los dos lados; en _solid, el hueco de
    // la pared. No abre lo que tapa un mueble ni otra pared pegada a la puerta.
    private void TallarPuertas(WallObject w, bool[,] raw, bool[,] mueble)
    {
        if (w.Doors.Count == 0) return;
        Vector2 a = new Vector2(w.ALocal.x, w.ALocal.z);
        Vector2 b = new Vector2(w.BLocal.x, w.BLocal.z);
        float len = (b - a).magnitude;
        if (len < 1e-4f) return;
        Vector2 baseHat = (b - a) / len;
        Vector2 normal  = new Vector2(w.Normal.x, w.Normal.z).normalized;
        float m = _cell * 0.5f;                       // mismo margen que PointInWall
        float alcance = _agentRadius + _cell * 2f;    // mas alla del inflado, a cada lado

        foreach (var d in w.Doors)
        {
            if (d == null) continue;
            float uc = (d.uMin + d.uMax) * 0.5f;
            float hw = Mathf.Max((d.uMax - d.uMin) * 0.5f, _cell * 1.5f);
            float w0 = -alcance, w1 = w.Width + alcance;
            var esquinas = new[]
            {
                a + baseHat * (uc - hw) + normal * w0, a + baseHat * (uc + hw) + normal * w0,
                a + baseHat * (uc - hw) + normal * w1, a + baseHat * (uc + hw) + normal * w1,
            };
            if (!RangoCeldas(esquinas, 0f, out int c0, out int r0, out int c1, out int r1)) continue;
            for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
            {
                CellCenter(c, r, out float x, out float z);
                Vector2 rel = new Vector2(x, z) - a;
                float u  = Vector2.Dot(rel, baseHat);
                float wv = Vector2.Dot(rel, normal);
                if (u < uc - hw || u > uc + hw || wv < w0 || wv > w1) continue;
                if (mueble[c, r]) continue;
                bool enEstaPared = wv >= -m && wv <= w.Width + m;
                if (raw[c, r] && !enEstaPared) continue;   // otra pared detras/delante de la puerta
                _blocked[c, r] = false;
                if (enEstaPared) raw[c, r] = false;
            }
        }
    }

    public bool IsWalkable(Vector3 worldPosition)
    {
        if (WorldOrigin.Instance == null) return false;
        AsegurarConstruido();
        if (_blocked == null) return true;
        // Fuera del grid no hay obstaculos (el grid envuelve toda la geometria): libre. No
        // se agranda por esto, o cada candidato de spawn lejano reconstruiria el grid.
        Vector3 local = WorldOrigin.Instance.ToRelative(worldPosition);
        return !CellOf(local.x, local.z, out int col, out int row) || !_blocked[col, row];
    }

    // ¿Hay una pared u obstaculo real entre los dos puntos? Usa los obstaculos SIN inflar:
    // con los inflados, un jugador pegado a una pared quedaba "detras" de ella y nunca lo
    // podian atrapar.
    public bool HasClearLine(Vector3 startWorld, Vector3 endWorld)
    {
        if (WorldOrigin.Instance == null) return true;
        AsegurarConstruido();
        if (_solid == null) return true;

        // Fuera del grid no hay obstaculos: alcanza con revisar el tramo que lo cruza (sin
        // agrandarlo, que esto se consulta por cada candidato de spawn del Arbmos).
        Vector3 s3 = WorldOrigin.Instance.ToRelative(startWorld);
        Vector3 e3 = WorldOrigin.Instance.ToRelative(endWorld);
        Vector2 start = new Vector2(s3.x, s3.z), end = new Vector2(e3.x, e3.z);
        if (!RecortarAlGrid(ref start, ref end)) return true;

        if (!CellOf(start.x, start.y, out int startCol, out int startRow)) return true;
        if (!CellOf(end.x, end.y, out int endCol, out int endRow)) return true;
        // La celda de cada extremo no cuenta: alguien apoyado en una pared cae en la media
        // celda con que se engordan las paredes. Si la linea la atraviesa, igual choca con
        // la celda siguiente.
        return LineOfSight(_solid, new Vector2Int(startCol, startRow),
                           new Vector2Int(endCol, endRow), ignorarExtremos: true);
    }

    // Recorta el segmento p0→p1 (anchor-XZ) al rectangulo del grid (Liang-Barsky). False
    // si no lo toca.
    private bool RecortarAlGrid(ref Vector2 p0, ref Vector2 p1)
    {
        const float eps = 1e-3f;
        float x0 = _minX + eps, z0 = _minZ + eps;
        float x1 = _minX + _cols * _cell - eps, z1 = _minZ + _rows * _cell - eps;
        Vector2 d = p1 - p0;
        float t0 = 0f, t1 = 1f;
        if (!Recorte(-d.x, p0.x - x0, ref t0, ref t1) || !Recorte(d.x, x1 - p0.x, ref t0, ref t1) ||
            !Recorte(-d.y, p0.y - z0, ref t0, ref t1) || !Recorte(d.y, z1 - p0.y, ref t0, ref t1))
            return false;
        Vector2 a = p0 + d * t0, b = p0 + d * t1;
        p0 = a; p1 = b;
        return true;
    }

    private static bool Recorte(float p, float q, ref float t0, ref float t1)
    {
        if (Mathf.Abs(p) < 1e-9f) return q >= 0f;
        float t = q / p;
        if (p < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
        else        { if (t < t0) return false; if (t < t1) t1 = t; }
        return true;
    }

    // ── Test de solidez en (x,z) anchor-local ─────────────────────────────
    // 'margen' engorda la pared media celda: una pared mas fina que la celda podia caer
    // entre centros de celda y quedar con agujeros por donde se colaba la ruta.
    private bool PointInWall(WallObject w, float x, float z, float margen)
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
        return u >= -margen && u <= len + margen && wv >= -margen && wv <= w.Width + margen;
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

        Vector3 sLocal = WorldOrigin.Instance.ToRelative(startWorld);
        Vector3 gLocal = WorldOrigin.Instance.ToRelative(goalWorld);

        // Auto-build / auto-rebuild si cambio el mapa o si alguno cae fuera del grid.
        Asegurar(sLocal, gLocal);
        if (_blocked == null) return false; // sin obstaculos: el caller va directo

        if (!CellOf(sLocal.x, sLocal.z, out int sc, out int sr)) return false;
        if (!CellOf(gLocal.x, gLocal.z, out int gc, out int gr)) return false;
        // Si inicio/fin caen en celda bloqueada (pegado a pared), buscar la libre mas cercana.
        if (_blocked[sc, sr] && !NearestFree(_blocked, sc, sr, out sc, out sr)) return false;
        if (_blocked[gc, gr] && !NearestFree(_blocked, gc, gr, out gc, out gr)) return false;

        var path = AStar(sc, sr, gc, gr);
        if (path == null) return false;

        // Suavizado (string-pulling) por linea de vista sobre el grid.
        float pisoRel = PisoRel;
        var simplified = Simplify(path);
        foreach (var idx in simplified)
        {
            CellCenter(idx.x, idx.y, out float x, out float z);
            outWorld.Add(WorldOrigin.Instance.ToWorld(new Vector3(x, pisoRel, z)));
        }
        // Reemplazar el ultimo waypoint por el destino real (mas preciso que el centro de
        // celda), pero a nivel del piso: el destino suele ser la cabeza del jugador.
        if (outWorld.Count > 0)
            outWorld[outWorld.Count - 1] = new Vector3(goalWorld.x, outWorld[outWorld.Count - 1].y, goalWorld.z);
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
            if (!LineOfSight(_blocked, path[anchor], path[i]))
            {
                outp.Add(path[i - 1]);
                anchor = i - 1;
            }
        }
        outp.Add(path[path.Count - 1]);
        return outp;
    }

    private static bool LineOfSight(bool[,] grid, Vector2Int a, Vector2Int b, bool ignorarExtremos = false)
    {
        int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            bool extremo = (x0 == a.x && y0 == a.y) || (x0 == x1 && y0 == y1);
            if (grid[x0, y0] && !(ignorarExtremos && extremo)) return false;
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
    private bool NearestFree(bool[,] grid, int c, int r, out int fc, out int fr)
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
                if (!grid[nc, nr]) { fc = nc; fr = nr; return true; }
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
