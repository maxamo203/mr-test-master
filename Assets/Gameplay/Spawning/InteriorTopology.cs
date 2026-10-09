using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Spawning
{
    /// <summary>
    /// Geometria 2D pura del interior escaneado. Solo acepta ciclos cerrados: ante un
    /// contorno abierto o ambiguo no inventa un area jugable.
    /// </summary>
    public sealed class InteriorTopology
    {
        public readonly struct Segment
        {
            public readonly Vector2 A;
            public readonly Vector2 B;

            public Segment(Vector2 a, Vector2 b)
            {
                A = a;
                B = b;
            }
        }

        private sealed class Node
        {
            public Vector2 position;
            public readonly List<int> edges = new();
        }

        private readonly List<List<Vector2>> _rooms;
        private readonly List<Segment> _boundaries;

        private InteriorTopology(List<List<Vector2>> rooms, List<Segment> boundaries)
        {
            _rooms = rooms;
            _boundaries = boundaries;
        }

        public IReadOnlyList<List<Vector2>> Rooms => _rooms;
        public IReadOnlyList<Segment> Boundaries => _boundaries;

        public static bool TryBuild(IReadOnlyList<Segment> segments, float snapTolerance,
                                    out InteriorTopology topology, out string reason)
        {
            topology = null;
            reason = null;
            if (segments == null || segments.Count < 3)
            {
                reason = "se necesitan al menos tres paredes";
                return false;
            }

            float tolerance = Mathf.Max(0.001f, snapTolerance);
            float tolerance2 = tolerance * tolerance;
            var nodes = new List<Node>();
            var edgeNodes = new List<(int a, int b)>();

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
                for (int e = 0; e < edgeNodes.Count; e++)
                {
                    var existing = edgeNodes[e];
                    if ((existing.a == a && existing.b == b) ||
                        (existing.a == b && existing.b == a))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate) continue;

                int edgeIndex = edgeNodes.Count;
                edgeNodes.Add((a, b));
                nodes[a].edges.Add(edgeIndex);
                nodes[b].edges.Add(edgeIndex);
            }

            if (edgeNodes.Count < 3)
            {
                reason = "no hay suficientes paredes validas";
                return false;
            }

            // En esta primera version conservadora cada componente debe ser un ciclo.
            // Un extremo abierto o una bifurcacion no permite afirmar que lado es interior.
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].edges.Count != 2)
                {
                    reason = $"contorno abierto o ambiguo en ({nodes[i].position.x:0.00}, {nodes[i].position.y:0.00})";
                    return false;
                }
            }

            // Cruces sin vertice explicito son ambiguos y no se corrigen automaticamente.
            for (int i = 0; i < edgeNodes.Count; i++)
            for (int j = i + 1; j < edgeNodes.Count; j++)
            {
                var e1 = edgeNodes[i];
                var e2 = edgeNodes[j];
                if (e1.a == e2.a || e1.a == e2.b || e1.b == e2.a || e1.b == e2.b) continue;
                if (SegmentsIntersect(nodes[e1.a].position, nodes[e1.b].position,
                                      nodes[e2.a].position, nodes[e2.b].position))
                {
                    reason = "hay paredes cruzadas sin una esquina comun";
                    return false;
                }
            }

            var visitedEdges = new bool[edgeNodes.Count];
            var rooms = new List<List<Vector2>>();
            for (int startEdge = 0; startEdge < edgeNodes.Count; startEdge++)
            {
                if (visitedEdges[startEdge]) continue;
                var first = edgeNodes[startEdge];
                int startNode = first.a;
                int currentNode = first.b;
                int currentEdge = startEdge;
                var polygon = new List<Vector2> { nodes[startNode].position };

                int guard = edgeNodes.Count + 1;
                while (guard-- > 0)
                {
                    visitedEdges[currentEdge] = true;
                    polygon.Add(nodes[currentNode].position);
                    if (currentNode == startNode) break;

                    var incident = nodes[currentNode].edges;
                    int nextEdge = incident[0] == currentEdge ? incident[1] : incident[0];
                    var next = edgeNodes[nextEdge];
                    currentNode = next.a == currentNode ? next.b : next.a;
                    currentEdge = nextEdge;
                }

                if (polygon.Count < 4 || polygon[polygon.Count - 1] != polygon[0])
                {
                    reason = "no se pudo cerrar un contorno";
                    return false;
                }
                polygon.RemoveAt(polygon.Count - 1);
                if (Mathf.Abs(SignedArea(polygon)) < 0.05f)
                {
                    reason = "un recinto tiene area insuficiente";
                    return false;
                }
                rooms.Add(polygon);
            }

            var boundaries = new List<Segment>(edgeNodes.Count);
            foreach (var edge in edgeNodes)
                boundaries.Add(new Segment(nodes[edge.a].position, nodes[edge.b].position));

            topology = new InteriorTopology(rooms, boundaries);
            return true;
        }

        public bool ContainsDisc(Vector2 center, float clearance)
        {
            if (!TryGetRoomIndex(center, out int roomIndex)) return false;
            return ContainsDiscInRoom(roomIndex, center, clearance);
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
            if (roomIndex < 0 || roomIndex >= _rooms.Count ||
                !PointInPolygon(center, _rooms[roomIndex])) return false;

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
