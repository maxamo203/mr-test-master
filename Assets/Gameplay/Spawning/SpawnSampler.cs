using System.Collections.Generic;
using Scanner;
using UnityEngine;

namespace Gameplay.Spawning
{
    /// <summary>
    /// Sorteo de posiciones AL AZAR para los objetos del mapa (pilas, reliquias), uniforme
    /// por area: dentro de los recintos de <see cref="InteriorTopology"/> (todos, no solo el
    /// del PISO) o, si el escaneo no tiene ninguno, en un disco de <c>radioRespaldo</c>
    /// alrededor del PISO (o del 0,0 del mapa si no hay PISO). Si el punto cae sobre la
    /// huella de un cubo, el objeto va ARRIBA del cubo. Sin semilla: cada noche sale distinto.
    ///
    /// Todo anchor-relativo. <see cref="Preparar"/> resuelve lo caro una vez por noche
    /// (altura del piso, regiones); <see cref="TrySample"/> es barato y se puede llamar en
    /// el momento de cada spawn. El resultado igual hay que pasarlo por
    /// InteriorSpawnValidator.TryValidate (margen a paredes, acceso, etc.).
    /// </summary>
    public sealed class SpawnSampler
    {
        private sealed class Region
        {
            public IReadOnlyList<Vector2> polygon; // null = disco
            public Vector2 center;
            public float radius;
            public Vector2 min, max;
            public float area;
        }

        private readonly float _radioRespaldo;
        private readonly bool  _usarMuebles;
        private readonly bool  _usarPiso;
        private readonly float _minAreaMueble;
        private readonly float _offsetSuperficie;

        private readonly List<Region> _regiones = new();
        private float _areaTotal;
        private float _pisoY;

        public SpawnSampler(float radioRespaldo, bool usarMuebles, bool usarPiso,
                            float minAreaMueble, float offsetSuperficie)
        {
            _radioRespaldo    = Mathf.Max(0.5f, radioRespaldo);
            _usarMuebles      = usarMuebles;
            _usarPiso         = usarPiso;
            _minAreaMueble    = minAreaMueble;
            _offsetSuperficie = offsetSuperficie;
        }

        public float AreaTotal => _areaTotal;
        public bool  ConRecintos { get; private set; }
        public string Detalle { get; private set; } = "sin preparar";

        public bool Preparar()
        {
            _regiones.Clear();
            _areaTotal = 0f;
            ConRecintos = false;

            var wo = WorldOrigin.Instance;
            if (wo == null || !wo.IsReady) { Detalle = "WorldOrigin no esta listo"; return false; }
            if (!InteriorSpawnValidator.TryEnsureReady(out var motivo)) { Detalle = motivo; return false; }

            string fuentePiso = AlturaPisoRel(out _pisoY);
            var topo = InteriorSpawnValidator.Topology;
            if (topo.HasRooms)
            {
                ConRecintos = true;
                for (int i = 0; i < topo.Rooms.Count; i++)
                    AgregarPoligono(topo.Rooms[i], topo.RoomArea(i));
                Detalle = $"{topo.Detalle}, {_areaTotal:0.0} m²";
            }
            else
            {
                Vector2 centro = FloorPoint.Instance != null ? Xz(FloorPoint.Instance.LocalPosition) : Vector2.zero;
                AgregarDisco(centro, _radioRespaldo);
                Detalle = $"sin recintos ({topo.Detalle}): radio {_radioRespaldo:0.0} m alrededor del " +
                          (FloorPoint.Instance != null ? "PISO" : "0,0 del mapa");
            }

            // Dev (OPCIONES → DESARROLLO): también alrededor del jugador. Con recintos, lo que
            // caiga afuera lo descarta el validador.
            if (SpawnDev.PisoAutomatico && SpawnDev.TryCentroPisoAutomatico(out var centroAuto, out _))
            {
                AgregarDisco(Xz(centroAuto), _radioRespaldo);
                Detalle += ", + alrededor del jugador";
            }
            Detalle += $"; piso: {fuentePiso}";
            return _areaTotal > 0f;
        }

        // Altura del piso (anchor-relativa) para los puntos que no caen sobre un cubo: el
        // PISO del escaner; si no hay, la base de las paredes (se apoyan en el piso); si
        // tampoco, la que estima SpawnDev (piso del AR o altura de la camara). Devuelve de
        // donde salio. Tambien la usa el Arbmos (que sin PISO aparecia a la altura de los ojos).
        public static string AlturaPisoRel(out float pisoY)
        {
            if (FloorPoint.Instance != null)
            {
                pisoY = FloorPoint.Instance.LocalY;
                return "PISO del escáner";
            }

            var registry = SceneRegistry.Instance;
            bool hay = false;
            float y = 0f;
            if (registry != null)
            {
                foreach (var wall in registry.Walls)
                {
                    if (wall == null) continue;
                    float baseY = Mathf.Min(wall.ALocal.y, wall.BLocal.y);
                    if (!hay || baseY < y) { y = baseY; hay = true; }
                }
            }
            if (hay)
            {
                pisoY = y;
                return "base de las paredes";
            }

            if (SpawnDev.TryCentroPisoAutomatico(out var centro, out var fuente))
            {
                pisoY = centro.y;
                return fuente;
            }
            pisoY = 0f;
            return "0 del mapa";
        }

        private void AgregarPoligono(IReadOnlyList<Vector2> polygon, float area)
        {
            if (area <= 0f || polygon.Count < 3) return;
            Vector2 min = polygon[0], max = polygon[0];
            for (int i = 1; i < polygon.Count; i++)
            {
                min = Vector2.Min(min, polygon[i]);
                max = Vector2.Max(max, polygon[i]);
            }
            _regiones.Add(new Region { polygon = polygon, min = min, max = max, area = area });
            _areaTotal += area;
        }

        private void AgregarDisco(Vector2 center, float radius)
        {
            float area = Mathf.PI * radius * radius;
            _regiones.Add(new Region { center = center, radius = radius, area = area });
            _areaTotal += area;
        }

        /// <summary>
        /// Un candidato al azar. <paramref name="huella"/> = radio del objeto (para que,
        /// sobre un cubo, entre completo sin colgar del borde). False si en
        /// <paramref name="intentos"/> sorteos no salio nada usable (p. ej. todo cubos chicos).
        /// </summary>
        public bool TrySample(float huella, out Vector3 rel, out SpawnSurfaceKind surface,
                              out CubeObject support, int intentos = 8)
        {
            rel = default;
            surface = SpawnSurfaceKind.Floor;
            support = null;
            var wo = WorldOrigin.Instance;
            if (_areaTotal <= 0f || wo == null || !wo.IsReady) return false;

            // Solo muebles: sortear por area de muebles directamente (por area del cuarto
            // casi nunca caeria sobre uno).
            if (!_usarPiso)
                return _usarMuebles && TrySampleSoloMuebles(huella, out rel, out surface, out support);

            for (int t = 0; t < intentos; t++)
            {
                Vector2 p = SortearXZ();
                CubeObject cube = CuboMasAltoEn(p);
                if (cube != null)
                {
                    if (!_usarMuebles || TopArea(cube) < _minAreaMueble) continue; // obstaculo
                    rel = PuntoSobreCubo(cube, p, huella);
                    surface = SpawnSurfaceKind.FurnitureTop;
                    support = cube;
                    return true;
                }
                rel = new Vector3(p.x, _pisoY + _offsetSuperficie, p.y);
                surface = SpawnSurfaceKind.Floor;
                support = null;
                return true;
            }
            return false;
        }

        private bool TrySampleSoloMuebles(float huella, out Vector3 rel, out SpawnSurfaceKind surface,
                                          out CubeObject support)
        {
            rel = default;
            surface = SpawnSurfaceKind.FurnitureTop;
            support = null;
            var registry = SceneRegistry.Instance;
            if (registry == null) return false;

            float total = 0f;
            foreach (var cube in registry.Cubes)
                if (cube != null && TopArea(cube) >= _minAreaMueble) total += TopArea(cube);
            if (total <= 0f) return false;

            float r = Random.value * total;
            foreach (var cube in registry.Cubes)
            {
                if (cube == null) continue;
                float a = TopArea(cube);
                if (a < _minAreaMueble) continue;
                r -= a;
                if (r > 0f) continue;
                var local = new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f));
                Vector3 world = cube.transform.TransformPoint(local);
                rel = PuntoSobreCubo(cube, Xz(WorldOrigin.Instance.ToRelative(world)), huella);
                support = cube;
                return true;
            }
            return false;
        }

        private Vector2 SortearXZ()
        {
            float r = Random.value * _areaTotal;
            Region region = _regiones[_regiones.Count - 1];
            foreach (var candidate in _regiones)
            {
                r -= candidate.area;
                if (r <= 0f) { region = candidate; break; }
            }

            if (region.polygon == null)
            {
                Vector2 dir = Random.insideUnitCircle;
                return region.center + dir * region.radius;
            }

            // Rechazo dentro de la caja del poligono. Un recinto razonable llena >30% de su
            // caja, asi que 24 intentos fallan con probabilidad despreciable; si igual pasa,
            // el validador descarta el punto y el llamador vuelve a sortear.
            Vector2 p = region.min;
            for (int i = 0; i < 24; i++)
            {
                p = new Vector2(Random.Range(region.min.x, region.max.x),
                                Random.Range(region.min.y, region.max.y));
                if (InteriorTopology.PointInPolygon(p, region.polygon)) return p;
            }
            return p;
        }

        // El cubo cuya huella XZ contiene p (si hay varios apilados, el de techo mas alto).
        private CubeObject CuboMasAltoEn(Vector2 p)
        {
            var registry = SceneRegistry.Instance;
            if (registry == null) return null;
            Vector3 world = WorldOrigin.Instance.ToWorld(new Vector3(p.x, _pisoY, p.y));
            CubeObject best = null;
            float bestTop = float.MinValue;
            foreach (var cube in registry.Cubes)
            {
                if (cube == null) continue;
                Vector3 local = cube.transform.InverseTransformPoint(world);
                // El cubo primitivo mide 1 unidad => media-extension 0.5 en cada eje local.
                if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.z) > 0.5f) continue;
                float top = cube.transform.TransformPoint(new Vector3(0f, 0.5f, 0f)).y;
                if (top <= bestTop) continue;
                best = cube;
                bestTop = top;
            }
            return best;
        }

        // Punto sobre la cara superior del cubo, metido hacia adentro lo justo para que la
        // huella del objeto entre completa (ver InteriorSpawnValidator.FurnitureSupports).
        private Vector3 PuntoSobreCubo(CubeObject cube, Vector2 p, float huella)
        {
            var t = cube.transform;
            Vector3 world = WorldOrigin.Instance.ToWorld(new Vector3(p.x, _pisoY, p.y));
            Vector3 local = t.InverseTransformPoint(world);
            Vector3 scale = t.lossyScale;
            float limX = Mathf.Max(0f, 0.5f - (huella + 0.01f) / Mathf.Max(0.001f, Mathf.Abs(scale.x)));
            float limZ = Mathf.Max(0f, 0.5f - (huella + 0.01f) / Mathf.Max(0.001f, Mathf.Abs(scale.z)));
            local.x = Mathf.Clamp(local.x, -limX, limX);
            local.z = Mathf.Clamp(local.z, -limZ, limZ);
            local.y = 0.5f;
            Vector3 top = t.TransformPoint(local) + t.up * _offsetSuperficie;
            return WorldOrigin.Instance.ToRelative(top);
        }

        private static float TopArea(CubeObject cube)
        {
            var s = cube.transform.lossyScale;
            return Mathf.Abs(s.x * s.z);
        }

        private static Vector2 Xz(Vector3 v) => new(v.x, v.z);
    }
}
