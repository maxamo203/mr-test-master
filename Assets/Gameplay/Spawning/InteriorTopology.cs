using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Spawning
{
    /// <summary>
    /// Geometria 2D pura del interior escaneado. Cada conjunto de paredes conectadas es un
    /// recinto: si ya cierra (la ultima toca la primera) se usa tal cual; si es una cadena
    /// abierta se cierra INTERNAMENTE uniendo sus dos extremos (esa pared de cierre no
    /// existe: cuenta para el borde del recinto pero no tapa la linea de vision). Una pared
    /// suelta no encierra nada y no forma recinto, aunque sigue siendo pared.
    /// </summary>
    public sealed class InteriorTopology
    {
        public readonly struct Segment
        {
            public readonly Vector2 A;
            public readonly Vector2 B;
            // Polilinea del escaner a la que pertenece (WallObject.PolylineId); null = suelta.
            // Solo se usa para separar recintos que se tocan en una esquina.
            public readonly string Group;

            public Segment(Vector2 a, Vector2 b, string group = null)
            {
                A = a;
                B = b;
                Group = group;
            }
        }

        private sealed class Node
        {
            public Vector2 position;
        }

        private readonly struct Edge
        {
            public readonly int a;
            public readonly int b;
            public readonly string group;

            public Edge(int a, int b, string group)
            {
                this.a = a;
                this.b = b;
                this.group = group;
            }
        }

        private const float MinRoomArea = 0.05f;

        private readonly List<List<Vector2>> _rooms;
        private readonly List<float> _areas;
        private readonly List<Segment> _boundaries;

        private InteriorTopology(List<List<Vector2>> rooms, List<float> areas,
                                 List<Segment> boundaries, string detalle)
        {
            _rooms = rooms;
            _areas = areas;
            _boundaries = boundaries;
            Detalle = detalle;
        }

        public IReadOnlyList<List<Vector2>> Rooms => _rooms;
        // Solo paredes REALES (las de cierre interno no estan aca).
        public IReadOnlyList<Segment> Boundaries => _boundaries;
        public bool HasRooms => _rooms.Count > 0;
        // Resumen legible para el HUD de debug: cuantos recintos y que se descarto.
        public string Detalle { get; }

        public float RoomArea(int roomIndex) => _areas[roomIndex];

        public static InteriorTopology Build(IReadOnlyList<Segment> segments, float snapTolerance)
        {
            var rooms = new List<List<Vector2>>();
            var areas = new List<float>();
            var boundaries = new List<Segment>();
            if (segments == null || segments.Count == 0)
                return new InteriorTopology(rooms, areas, boundaries, "no hay paredes");

            float tolerance = Mathf.Max(0.001f, snapTolerance);
            float tolerance2 = tolerance * tolerance;
            var nodes = new List<Node>();
            var edges = new List<Edge>();

            int FindOrAdd(Vector2 point)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    if ((nodes[i].position - point).sqrMagnitude <= tolerance2)
                    {
                        // Promediar suavemente absorbe el error normal del escaneo sin
                        // cerrar huecos mayores que la tolerancia declarada.
                        nodes[i].position = (nodes[i].position + point) * 0.5f;
                        return i;
                    }
                }

                nodes.Add(new Node { position = point });
                return nodes.Count - 1;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                if ((segments[i].A - segments[i].B).sqrMagnitude < 0.0001f) continue;
                int a = FindOrAdd(segments[i].A);
                int b = FindOrAdd(segments[i].B);
                if (a == b) continue;

                bool duplicate = false;
                for (int e = 0; e < edges.Count; e++)
                {
                    if ((edges[e].a == a && edges[e].b == b) ||
                        (edges[e].a == b && edges[e].b == a))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) edges.Add(new Edge(a, b, segments[i].Group));
            }

            foreach (var edge in edges)
                boundaries.Add(new Segment(nodes[edge.a].position, nodes[edge.b].position, edge.group));

            int sueltas = 0, bifurcadas = 0, sinArea = 0;

            void Agregar(List<int> cadena)
            {
                switch (TryChainPolygon(cadena, edges, nodes, out var polygon))
                {
                    case ChainResult.Ok:
                        float area = Mathf.Abs(SignedArea(polygon));
                        if (area < MinRoomArea) { sinArea++; return; }
                        rooms.Add(polygon);
                        areas.Add(area);
                        break;
                    case ChainResult.SingleWall: sueltas++; break;
                    case ChainResult.Branched:   bifurcadas++; break;
                }
            }

            var todas = new List<int>(edges.Count);
            for (int i = 0; i < edges.Count; i++) todas.Add(i);

            foreach (var component in Components(todas, edges))
            {
                if (!IsBranched(component, edges))
                {
                    Agregar(component);
                    continue;
                }

                // Dos recintos que se tocan en una esquina quedan en una misma componente
                // con nodos de 3+ paredes: se separan por la polilinea en que se dibujaron.
                var porGrupo = new Dictionary<string, List<int>>();
                var sinGrupo = new List<int>();
                foreach (int e in component)
                {
                    string g = edges[e].group;
                    if (string.IsNullOrEmpty(g)) { sinGrupo.Add(e); continue; }
                    if (!porGrupo.TryGetValue(g, out var lista)) porGrupo[g] = lista = new List<int>();
                    lista.Add(e);
                }
                if (sinGrupo.Count > 0) porGrupo[string.Empty] = sinGrupo;
                foreach (var grupo in porGrupo.Values)
                foreach (var sub in Components(grupo, edges))
                    Agregar(sub);
            }

            string detalle = $"{rooms.Count} recinto(s)";
            if (sueltas > 0)    detalle += $", {sueltas} pared(es) suelta(s) ignorada(s)";
            if (bifurcadas > 0) detalle += $", {bifurcadas} conjunto(s) ambiguo(s) ignorado(s)";
            if (sinArea > 0)    detalle += $", {sinArea} sin area";
            return new InteriorTopology(rooms, areas, boundaries, detalle);
        }

        private enum ChainResult { Ok, SingleWall, Branched }

        // Una cadena conexa de paredes → poligono. Ciclo: se recorre tal cual. Camino
        // abierto: se recorre de un extremo al otro y el poligono queda cerrado
        // implicitamente (ultimo vertice → primero).
        private static ChainResult TryChainPolygon(List<int> chain, List<Edge> edges, List<Node> nodes,
                                                   out List<Vector2> polygon)
        {
            polygon = null;
            if (chain.Count < 2) return ChainResult.SingleWall;

            var incident = new Dictionary<int, List<int>>();
            void Link(int node, int edge)
            {
                if (!incident.TryGetValue(node, out var list)) incident[node] = list = new List<int>(2);
                list.Add(edge);
            }
            foreach (int e in chain)
            {
                Link(edges[e].a, e);
                Link(edges[e].b, e);
            }

            int start = edges[chain[0]].a;
            foreach (var kv in incident)
            {
                if (kv.Value.Count > 2) return ChainResult.Branched;
                if (kv.Value.Count == 1) start = kv.Key; // extremo de una cadena abierta
            }

            polygon = new List<Vector2> { nodes[start].position };
            int previous = -1, current = start;
            for (int step = 0; step < chain.Count; step++)
            {
                var list = incident[current];
                int next = list[0] != previous ? list[0] : (list.Count > 1 ? list[1] : -1);
                if (next < 0) break;
                current = edges[next].a == current ? edges[next].b : edges[next].a;
                previous = next;
                if (current == start) break;
                polygon.Add(nodes[current].position);
            }
            return polygon.Count >= 3 ? ChainResult.Ok : ChainResult.SingleWall;
        }

        private static bool IsBranched(List<int> component, List<Edge> edges)
        {
            var degree = new Dictionary<int, int>();
            foreach (int e in component)
            {
                degree.TryGetValue(edges[e].a, out int da);
                degree.TryGetValue(edges[e].b, out int db);
                degree[edges[e].a] = da + 1;
                degree[edges[e].b] = db + 1;
            }
            foreach (int d in degree.Values)
                if (d > 2) return true;
            return false;
        }

        // Componentes conexas del subconjunto de paredes `subset`.
        private static List<List<int>> Components(List<int> subset, List<Edge> edges)
        {
            var result = new List<List<int>>();
            var pending = new HashSet<int>(subset);
            var byNode = new Dictionary<int, List<int>>();
            foreach (int e in subset)
            {
                foreach (int n in new[] { edges[e].a, edges[e].b })
                {
                    if (!byNode.TryGetValue(n, out var list)) byNode[n] = list = new List<int>();
                    list.Add(e);
                }
            }

            var stack = new Stack<int>();
            foreach (int seed in subset)
            {
                if (!pending.Remove(seed)) continue;
                var component = new List<int>();
                stack.Push(seed);
                while (stack.Count > 0)
                {
                    int e = stack.Pop();
                    component.Add(e);
                    foreach (int n in new[] { edges[e].a, edges[e].b })
                    foreach (int other in byNode[n])
                        if (pending.Remove(other)) stack.Push(other);
                }
                result.Add(component);
            }
            return result;
        }

        // ¿Hay un disco de radio `clearance` dentro de ALGUN recinto, sin tocar paredes?
        // Con recintos superpuestos alcanza con que entre en uno.
        public bool ContainsDisc(Vector2 center, float clearance)
        {
            for (int i = 0; i < _rooms.Count; i++)
                if (ContainsDiscInRoom(i, center, clearance)) return true;
            return false;
        }

        public bool TryGetRoomIndex(Vector2 point, out int roomIndex)
        {
            for (int i = 0; i < _rooms.Count; i++)
            {
                if (!PointInPolygon(point, _rooms[i])) continue;
                roomIndex = i;
                return true;
            }
            roomIndex = -1;
            return false;
        }

        public bool ContainsDiscInRoom(int roomIndex, Vector2 center, float clearance)
        {
            if (roomIndex < 0 || roomIndex >= _rooms.Count) return false;
            var room = _rooms[roomIndex];
            if (!PointInPolygon(center, room)) return false;

            // Borde propio del recinto (incluye la pared de cierre interno) + todas las
            // paredes reales (de otros recintos o sueltas).
            float required = Mathf.Max(0f, clearance);
            float required2 = required * required;
            for (int i = 0, j = room.Count - 1; i < room.Count; j = i++)
                if (DistanceToSegmentSquared(center, room[j], room[i]) < required2)
                    return false;
            return ClearOfWalls(center, clearance);
        }

        // ¿Esta el disco libre de toda pared real? (sin importar recintos)
        public bool ClearOfWalls(Vector2 center, float clearance)
        {
            float required = Mathf.Max(0f, clearance);
            float required2 = required * required;
            for (int i = 0; i < _boundaries.Count; i++)
                if (DistanceToSegmentSquared(center, _boundaries[i].A, _boundaries[i].B) < required2)
                    return false;
            return true;
        }

        public bool HasClearSegment(Vector2 from, Vector2 to, float endpointTolerance = 0.03f)
        {
            for (int i = 0; i < _boundaries.Count; i++)
            {
                var wall = _boundaries[i];
                if (!SegmentsIntersect(from, to, wall.A, wall.B)) continue;
                if (DistanceToSegmentSquared(to, wall.A, wall.B) <= endpointTolerance * endpointTolerance)
                    continue;
                return false;
            }
            return true;
        }

        internal static bool PointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                bool crosses = (a.y > point.y) != (b.y > point.y) &&
                               point.x < (b.x - a.x) * (point.y - a.y) /
                                         (b.y - a.y + Mathf.Epsilon) + a.x;
                if (crosses) inside = !inside;
            }
            return inside;
        }

        private static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        private static float DistanceToSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denominator = ab.sqrMagnitude;
            if (denominator < 1e-8f) return (point - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
            return (point - (a + ab * t)).sqrMagnitude;
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float o1 = Cross(b - a, c - a);
            float o2 = Cross(b - a, d - a);
            float o3 = Cross(d - c, a - c);
            float o4 = Cross(d - c, b - c);
            const float epsilon = 1e-5f;
            if (Mathf.Abs(o1) < epsilon && OnSegment(a, b, c)) return true;
            if (Mathf.Abs(o2) < epsilon && OnSegment(a, b, d)) return true;
            if (Mathf.Abs(o3) < epsilon && OnSegment(c, d, a)) return true;
            if (Mathf.Abs(o4) < epsilon && OnSegment(c, d, b)) return true;
            return (o1 > 0f) != (o2 > 0f) && (o3 > 0f) != (o4 > 0f);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p) =>
            p.x >= Mathf.Min(a.x, b.x) - 1e-5f && p.x <= Mathf.Max(a.x, b.x) + 1e-5f &&
            p.y >= Mathf.Min(a.y, b.y) - 1e-5f && p.y <= Mathf.Max(a.y, b.y) + 1e-5f;
    }
}
