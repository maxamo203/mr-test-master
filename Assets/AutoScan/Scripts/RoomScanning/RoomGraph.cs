using System.Collections.Generic;
using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>Thresholds for <see cref="RoomGraph.Build"/>. All distances in metres.</summary>
    public struct RoomGraphSettings
    {
        /// <summary>Endpoints within this of each other are one node.</summary>
        public float joinRadius;
        /// <summary>Largest trim/extend applied to a wall end to reach an intersection.</summary>
        public float extendMax;
        /// <summary>Reject a junction between two walls whose acute angle is below this (deg).</summary>
        public float perpMinAngleDeg;
        /// <summary>Dangling wall ends this close are bridged so the outline can close.</summary>
        public float outlineGapBridge;
        /// <summary>Two wall fragments meeting end-to-end within this many degrees of
        /// dead straight are fused into one wall instead of left as two.</summary>
        public float collinearMergeAngle;

        public static RoomGraphSettings Default => new RoomGraphSettings
        {
            joinRadius = 0.35f, extendMax = 0.6f, perpMinAngleDeg = 15f, outlineGapBridge = 1.5f,
            collinearMergeAngle = 10f,
        };
    }

    /// <summary>
    /// Turns a set of fitted wall segments into a connected floor-plan: wall ends that
    /// belong together are merged into one node, intersecting walls are snapped to a shared
    /// corner ("the intersection is a unit"), and the room boundary is traced into an
    /// ordered outline. Pure geometry — no Unity objects, no model mutation. The caller
    /// feeds <see cref="Segments"/> to the wall builder and stores <see cref="Junctions"/>
    /// / <see cref="Outline"/> on the model.
    /// </summary>
    public sealed class RoomGraph
    {
        public struct Junction
        {
            public Vector2 pos;
            /// <summary>Indices into <see cref="Segments"/> of the built walls meeting here
            /// (a pinned wall contributes no index).</summary>
            public List<int> segmentIndices;
            /// <summary>Unit XZ directions of the two primary walls meeting here.</summary>
            public Vector2 dirA, dirB;
        }

        /// <summary>Snapped wall segments, 1:1 with <see cref="SourceIndex"/>.</summary>
        public List<(Vector2 a, Vector2 b)> Segments = new();
        /// <summary>For each entry in <see cref="Segments"/>, one representative input
        /// segment it came from (the first, if it is a fused wall — see
        /// <see cref="SourceIndices"/> for all of them).</summary>
        public List<int> SourceIndex = new();
        /// <summary>For each entry in <see cref="Segments"/>, every input segment fused into
        /// it — a caller propagating per-fragment data (doorway gaps) should union over all
        /// of these rather than use <see cref="SourceIndex"/> alone.</summary>
        public List<List<int>> SourceIndices = new();
        /// <summary>How many collinear pass-through pairs were fused into one wall.</summary>
        public int CollinearMerges;
        public List<Junction> Junctions = new();
        /// <summary>Ordered room-boundary points. Empty when nothing could be traced.</summary>
        public List<Vector2> Outline = new();
        public bool OutlineClosed;
        /// <summary>Endpoint pairs of gaps the outline had to bridge (for a dashed render).</summary>
        public List<(Vector2 a, Vector2 b)> BridgedSpans = new();

        // ── internal graph ───────────────────────────────────────────────────

        struct Edge
        {
            public int n0, n1;      // node slots (resolve through Find)
            public int source;      // input segment index, or -1 for a pinned wall / bridge
            public List<int> sources; // every input segment fused into this edge; null = just source
            public bool pinned;     // a hand-corrected wall: never move its ends
            public bool bridge;     // synthetic gap-closer: traced, never built
        }

        readonly List<Vector2> _pos = new();     // node slot → position
        readonly List<int> _alias = new();       // union-find over node slots
        readonly List<bool> _locked = new();     // slot is a settled junction
        readonly List<Edge> _edges = new();
        RoomGraphSettings _s;

        int Find(int i)
        {
            while (_alias[i] != i) { _alias[i] = _alias[_alias[i]]; i = _alias[i]; }
            return i;
        }

        int AddNode(Vector2 p, bool locked)
        {
            _pos.Add(p); _alias.Add(_pos.Count - 1); _locked.Add(locked);
            return _pos.Count - 1;
        }

        Vector2 P(int slot) => _pos[Find(slot)];

        /// <summary>Union two slots at <paramref name="at"/> and lock the result.</summary>
        void MergeAt(int a, int b, Vector2 at)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) _alias[rb] = ra;
            _pos[ra] = at; _locked[ra] = true;
        }

        // ── entry point ──────────────────────────────────────────────────────

        public static RoomGraph Build(
            IReadOnlyList<(Vector2 a, Vector2 b)> mergedSegments,
            IReadOnlyList<(Vector2 a, Vector2 b)> pinnedWalls,
            RoomGraphSettings settings)
        {
            var g = new RoomGraph { _s = settings };
            g.BuildNodes(mergedSegments, pinnedWalls);
            g.SnapJunctions();
            g.ExtendNearMisses();
            g.MergeCollinearPassThroughs();
            g.EmitSegments();
            g.TraceOutline();
            return g;
        }

        // 1) One node per endpoint, clustered within joinRadius. A cluster that contains a
        //    pinned endpoint takes that exact position — pinned geometry is ground truth.
        void BuildNodes(IReadOnlyList<(Vector2 a, Vector2 b)> segs,
                        IReadOnlyList<(Vector2 a, Vector2 b)> pinned)
        {
            var pts = new List<(Vector2 p, bool pinned)>();
            foreach (var s in segs) { pts.Add((s.a, false)); pts.Add((s.b, false)); }
            if (pinned != null)
                foreach (var s in pinned) { pts.Add((s.a, true)); pts.Add((s.b, true)); }

            int n = pts.Count;
            var uf = new int[n];
            for (int i = 0; i < n; i++) uf[i] = i;
            int R(int i) { while (uf[i] != i) { uf[i] = uf[uf[i]]; i = uf[i]; } return i; }

            float jr2 = _s.joinRadius * _s.joinRadius;
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if ((pts[i].p - pts[j].p).sqrMagnitude <= jr2) uf[R(i)] = R(j);

            var slotOf = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                int r = R(i);
                if (slotOf.ContainsKey(r)) continue;
                Vector2 sum = Vector2.zero, pinSum = Vector2.zero;
                int count = 0, pinCount = 0;
                for (int k = 0; k < n; k++)
                {
                    if (R(k) != r) continue;
                    sum += pts[k].p; count++;
                    if (pts[k].pinned) { pinSum += pts[k].p; pinCount++; }
                }
                var p = pinCount > 0 ? pinSum / pinCount : sum / Mathf.Max(1, count);
                slotOf[r] = AddNode(p, pinCount > 0);
            }

            for (int i = 0; i < segs.Count; i++)
            {
                int a = slotOf[R(2 * i)], b = slotOf[R(2 * i + 1)];
                if (a == b) continue; // collapsed to a point
                _edges.Add(new Edge { n0 = a, n1 = b, source = i, sources = new List<int> { i },
                                      pinned = false, bridge = false });
            }
            if (pinned != null)
                for (int i = 0; i < pinned.Count; i++)
                {
                    int baseIdx = 2 * segs.Count + 2 * i;
                    int a = slotOf[R(baseIdx)], b = slotOf[R(baseIdx + 1)];
                    if (a == b) continue;
                    _edges.Add(new Edge { n0 = a, n1 = b, source = -1, pinned = true, bridge = false });
                }
        }

        // 2) For every close pair of non-parallel edges, snap both near ends onto the exact
        //    line-line intersection and lock that node. Pinned edges pin the intersection to
        //    their own line and are never moved themselves.
        void SnapJunctions()
        {
            int count = _edges.Count;
            for (int i = 0; i < count; i++)
            for (int j = i + 1; j < count; j++)
            {
                var e0 = _edges[i]; var e1 = _edges[j];
                if (e0.pinned && e1.pinned) continue;

                Vector2 a0 = P(e0.n0), b0 = P(e0.n1), a1 = P(e1.n0), b1 = P(e1.n1);
                Vector2 d0 = (b0 - a0), d1 = (b1 - a1);
                if (d0.sqrMagnitude < 1e-6f || d1.sqrMagnitude < 1e-6f) continue;
                d0.Normalize(); d1.Normalize();

                // Only walls that already almost touch get a shared corner — otherwise a
                // shallow bay whose lines cross far outside both walls would be collapsed.
                bool shareNode = Find(e0.n0) == Find(e1.n0) || Find(e0.n0) == Find(e1.n1)
                              || Find(e0.n1) == Find(e1.n0) || Find(e0.n1) == Find(e1.n1);
                float nearest = Mathf.Min(Mathf.Min((a0 - a1).magnitude, (a0 - b1).magnitude),
                                          Mathf.Min((b0 - a1).magnitude, (b0 - b1).magnitude));
                if (!shareNode && nearest > _s.joinRadius) continue;

                float acute = Vector2.Angle(d0, d1);
                if (acute > 90f) acute = 180f - acute;
                if (acute < _s.perpMinAngleDeg) continue;

                if (!LineIntersect(a0, d0, a1, d1, out var x)) continue;
                if (e0.pinned) x = a0 + d0 * Vector2.Dot(x - a0, d0);
                else if (e1.pinned) x = a1 + d1 * Vector2.Dot(x - a1, d1);

                if (!ReachEnd(a0, b0, x, out int end0, out float over0) || over0 > _s.extendMax) continue;
                if (!ReachEnd(a1, b1, x, out int end1, out float over1) || over1 > _s.extendMax) continue;

                int s0 = end0 == 0 ? e0.n0 : e0.n1;
                int s1 = end1 == 0 ? e1.n0 : e1.n1;
                int r0 = Find(s0), r1 = Find(s1);
                if (r0 == r1) continue; // already the same node

                // A settled corner stays put — a third wall just attaches to it.
                if (_locked[r0] || _locked[r1])
                {
                    Vector2 anchor = _locked[r0] ? _pos[r0] : _pos[r1];
                    if ((_pos[r0] - _pos[r1]).magnitude <= _s.joinRadius + _s.extendMax)
                        MergeAt(s0, s1, anchor);
                    continue;
                }
                MergeAt(s0, s1, x);
            }
        }

        // 3) A wall end that just misses another wall's line is pulled onto it; the target
        //    wall is split there so the outline can turn the corner.
        void ExtendNearMisses()
        {
            int count = _edges.Count;
            for (int i = 0; i < count; i++)
            {
                var e = _edges[i];
                if (e.pinned) continue;
                for (int endSel = 0; endSel < 2; endSel++)
                {
                    int slot = endSel == 0 ? e.n0 : e.n1;
                    if (_locked[Find(slot)] || Degree(slot) != 1) continue;

                    Vector2 p = P(slot);
                    int bestEdge = -1; float bestMove = _s.extendMax; Vector2 bestFoot = p;
                    for (int t = 0; t < _edges.Count; t++)
                    {
                        if (t == i) continue;
                        var te = _edges[t];
                        if (!TryFootOnSegment(p, P(te.n0), P(te.n1), _s.extendMax, out var foot)) continue;
                        float move = (foot - p).magnitude;
                        if (move < bestMove) { bestMove = move; bestEdge = t; bestFoot = foot; }
                    }
                    if (bestEdge < 0) continue;

                    int hub = SplitEdgeAt(bestEdge, bestFoot);
                    MergeAt(slot, hub, bestFoot);
                    count = _edges.Count; // SplitEdgeAt appended
                }
            }
        }

        /// <summary>Replaces edge <paramref name="idx"/> with two halves meeting at a new
        /// locked node, and returns that node's slot.</summary>
        int SplitEdgeAt(int idx, Vector2 at)
        {
            var e = _edges[idx];
            int hub = AddNode(at, true);
            _edges[idx] = new Edge { n0 = e.n0, n1 = hub, source = e.source, sources = e.sources,
                                     pinned = e.pinned, bridge = e.bridge };
            _edges.Add(new Edge { n0 = hub, n1 = e.n1, source = e.source, sources = e.sources,
                                  pinned = e.pinned, bridge = e.bridge });
            return hub;
        }

        int Degree(int slot)
        {
            int r = Find(slot), d = 0;
            foreach (var e in _edges)
                if (!e.bridge && (Find(e.n0) == r || Find(e.n1) == r)) d++;
            return d;
        }

        // 3b) A wall end that lands exactly where another wall's end also lands, in a dead
        //     straight line, is not a corner (see the acute-angle skip in EmitSegments below)
        //     — it is one physical wall that RANSAC or MergeCollinear fit as two fragments.
        //     Fuse those pairs into a single edge so only one wall gets built. Restricted to
        //     unlocked nodes: every lock site (SnapJunctions, the ExtendNearMisses hub) means
        //     "this is a real junction", so corners, T-junctions and dividers are untouched.
        void MergeCollinearPassThroughs()
        {
            int guard = _edges.Count + 4;
            while (guard-- > 0 && FuseOnePassThrough()) { }
        }

        bool FuseOnePassThrough()
        {
            var byRoot = new Dictionary<int, List<(int edgeIdx, Vector2 dir, int farNode)>>();
            void Add(int root, int edgeIdx, Vector2 dir, int farNode)
            {
                if (!byRoot.TryGetValue(root, out var lst))
                { lst = new List<(int, Vector2, int)>(); byRoot[root] = lst; }
                lst.Add((edgeIdx, dir, farNode));
            }
            for (int i = 0; i < _edges.Count; i++)
            {
                var e = _edges[i];
                if (e.pinned || e.bridge) continue;
                int r0 = Find(e.n0), r1 = Find(e.n1);
                if (r0 == r1) continue;
                Vector2 p0 = P(e.n0), p1 = P(e.n1);
                Add(r0, i, (p1 - p0).normalized, e.n1);
                Add(r1, i, (p0 - p1).normalized, e.n0);
            }

            foreach (var kv in byRoot)
            {
                if (_locked[kv.Key] || kv.Value.Count != 2) continue;
                var x = kv.Value[0]; var y = kv.Value[1];
                if (x.edgeIdx == y.edgeIdx) continue;

                float acute = Vector2.Angle(x.dir, y.dir);
                if (acute > 90f) acute = 180f - acute;
                if (acute >= _s.collinearMergeAngle) continue; // a real corner, not straight

                if (Find(x.farNode) == Find(y.farNode)) continue; // would collapse to a point

                var ex = _edges[x.edgeIdx]; var ey = _edges[y.edgeIdx];
                var sources = new List<int>();
                sources.AddRange(ex.sources ?? new List<int> { ex.source });
                sources.AddRange(ey.sources ?? new List<int> { ey.source });

                int hi = Mathf.Max(x.edgeIdx, y.edgeIdx), lo = Mathf.Min(x.edgeIdx, y.edgeIdx);
                _edges.RemoveAt(hi); _edges.RemoveAt(lo);
                _edges.Add(new Edge { n0 = x.farNode, n1 = y.farNode, source = sources[0],
                                      sources = sources, pinned = false, bridge = false });
                CollinearMerges++;
                return true;
            }
            return false;
        }

        // 4) Final non-pinned, non-bridge edges become the built walls.
        void EmitSegments()
        {
            var outIdxByEdge = new Dictionary<int, int>();
            for (int i = 0; i < _edges.Count; i++)
            {
                var e = _edges[i];
                if (e.pinned || e.bridge) continue;
                Vector2 a = P(e.n0), b = P(e.n1);
                if ((b - a).magnitude < 0.04f) continue; // < 2 * MinDimension
                outIdxByEdge[i] = Segments.Count;
                Segments.Add((a, b));
                SourceIndex.Add(e.source);
                SourceIndices.Add(e.sources != null ? new List<int>(e.sources) : new List<int> { e.source });
            }

            // Incidence per node: every non-bridge edge (pinned walls included, as index -1),
            // with the edge's direction away from the node.
            var byRoot = new Dictionary<int, List<(int outIdx, Vector2 dir)>>();
            for (int i = 0; i < _edges.Count; i++)
            {
                var e = _edges[i];
                if (e.bridge) continue;
                outIdxByEdge.TryGetValue(i, out int oi);
                if (!outIdxByEdge.ContainsKey(i)) oi = -1;
                int ra = Find(e.n0), rb = Find(e.n1);
                Add(ra, oi, (P(e.n1) - P(e.n0)).normalized);
                Add(rb, oi, (P(e.n0) - P(e.n1)).normalized);
            }
            void Add(int root, int oi, Vector2 dir)
            {
                if (!byRoot.TryGetValue(root, out var lst)) { lst = new List<(int, Vector2)>(); byRoot[root] = lst; }
                lst.Add((oi, dir));
            }

            foreach (var kv in byRoot)
            {
                if (kv.Value.Count < 2) continue;
                // A shared node is a corner whether it came from an intersection snap or from
                // two walls that already ended at the same point — but skip a straight
                // pass-through (two near-collinear edges), which is not a corner.
                var d0 = kv.Value[0].dir; var d1 = kv.Value[1].dir;
                float acute = Vector2.Angle(d0, d1);
                if (acute > 90f) acute = 180f - acute;
                if (!_locked[kv.Key] && acute < 10f) continue;

                var built = new List<int>();
                foreach (var inc in kv.Value) if (inc.outIdx >= 0 && !built.Contains(inc.outIdx)) built.Add(inc.outIdx);
                Junctions.Add(new Junction { pos = _pos[kv.Key], segmentIndices = built, dirA = d0, dirB = d1 });
            }
        }

        // 5) Bridge dangling ends within reach, then wall-follow the outer boundary.
        void TraceOutline()
        {
            AddGapBridges();

            var adj = BuildAdjacency(out var nodes);
            if (nodes.Count < 3) { LogOutline("none (too few nodes)"); return; }

            int start = nodes[0];
            foreach (int nd in nodes)
                if (_pos[nd].y < _pos[start].y ||
                    (Mathf.Approximately(_pos[nd].y, _pos[start].y) && _pos[nd].x < _pos[start].x))
                    start = nd;

            var loop = new List<int> { start };
            int cur = start, prev = -1;
            Vector2 backDir = new Vector2(0f, -1f);
            int guard = _edges.Count * 2 + 8;

            while (guard-- > 0)
            {
                if (!NextEdge(cur, prev, backDir, adj, out _, out int nxt)) break;
                if (nxt == start && loop.Count >= 3) { OutlineClosed = true; break; }
                loop.Add(nxt);
                backDir = (_pos[cur] - _pos[nxt]).normalized;
                prev = cur; cur = nxt;
            }

            if (OutlineClosed && ValidLoop(loop))
            {
                foreach (int nd in loop) Outline.Add(_pos[nd]);
                CollectBridgedSpans(loop);
                LogOutline($"closed ({loop.Count} corners)");
                return;
            }

            // Open fallback: the boundary did not close. Emit the leading run of the walk
            // up to the first node it revisits (backtracking over a spur repeats nodes).
            OutlineClosed = false;
            var chain = new List<int>();
            var visited = new HashSet<int>();
            foreach (int nd in loop)
            {
                if (!visited.Add(nd)) break;
                chain.Add(nd);
            }
            if (chain.Count >= 2) { foreach (int nd in chain) Outline.Add(_pos[nd]); LogOutline($"open ({chain.Count} points)"); }
            else LogOutline("none (no boundary)");
        }

        void AddGapBridges()
        {
            var dangling = new List<int>();
            var seen = new HashSet<int>();
            for (int i = 0; i < _edges.Count; i++)
            foreach (int slot in new[] { _edges[i].n0, _edges[i].n1 })
            {
                int r = Find(slot);
                if (seen.Contains(r) || Degree(r) != 1) continue;
                seen.Add(r); dangling.Add(r);
            }

            var pairs = new List<(float d, int a, int b)>();
            for (int i = 0; i < dangling.Count; i++)
            for (int j = i + 1; j < dangling.Count; j++)
            {
                float d = (_pos[dangling[i]] - _pos[dangling[j]]).magnitude;
                if (d <= _s.outlineGapBridge) pairs.Add((d, dangling[i], dangling[j]));
            }
            pairs.Sort((x, y) => x.d.CompareTo(y.d));

            var used = new HashSet<int>();
            foreach (var pr in pairs)
            {
                if (used.Contains(pr.a) || used.Contains(pr.b)) continue;
                used.Add(pr.a); used.Add(pr.b);
                _edges.Add(new Edge { n0 = pr.a, n1 = pr.b, source = -1, pinned = false, bridge = true });
            }
        }

        Dictionary<int, List<(int edge, int other)>> BuildAdjacency(out List<int> nodes)
        {
            var adj = new Dictionary<int, List<(int, int)>>();
            for (int i = 0; i < _edges.Count; i++)
            {
                int r0 = Find(_edges[i].n0), r1 = Find(_edges[i].n1);
                if (r0 == r1) continue;
                if (!adj.TryGetValue(r0, out var l0)) { l0 = new List<(int, int)>(); adj[r0] = l0; }
                if (!adj.TryGetValue(r1, out var l1)) { l1 = new List<(int, int)>(); adj[r1] = l1; }
                l0.Add((i, r1)); l1.Add((i, r0));
            }
            nodes = new List<int>(adj.Keys);
            return adj;
        }

        /// <summary>Tightest turn: among edges at <paramref name="cur"/> (excluding the one
        /// we arrived on) pick the smallest CCW angle from the reversed arrival direction.</summary>
        bool NextEdge(int cur, int prev, Vector2 backDir,
                      Dictionary<int, List<(int edge, int other)>> adj, out int edge, out int next)
        {
            edge = -1; next = -1;
            if (!adj.TryGetValue(cur, out var inc) || inc.Count == 0) return false;

            float best = float.MaxValue;
            foreach (var (e, other) in inc)
            {
                if (other == prev && inc.Count > 1) continue; // don't backtrack unless forced
                Vector2 dir = (_pos[other] - _pos[cur]).normalized;
                float ang = CcwAngle(backDir, dir);
                if (ang < 1e-4f) ang = Mathf.PI * 2f; // exact reverse ⇒ last resort
                if (ang < best) { best = ang; edge = e; next = other; }
            }
            return edge >= 0;
        }

        void CollectBridgedSpans(List<int> loop)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                int a = loop[i], b = loop[(i + 1) % loop.Count];
                foreach (var e in _edges)
                {
                    if (!e.bridge) continue;
                    int r0 = Find(e.n0), r1 = Find(e.n1);
                    if ((r0 == a && r1 == b) || (r0 == b && r1 == a))
                        BridgedSpans.Add((_pos[a], _pos[b]));
                }
            }
        }

        bool ValidLoop(List<int> loop)
        {
            if (loop.Count < 3) return false;
            float area2 = 0f;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 p = _pos[loop[i]], q = _pos[loop[(i + 1) % loop.Count]];
                area2 += p.x * q.y - q.x * p.y;
            }
            if (Mathf.Abs(area2) * 0.5f < 1.0f) return false;

            for (int i = 0; i < loop.Count; i++)
            for (int j = i + 1; j < loop.Count; j++)
            {
                if (i == j) continue;
                if (j == i + 1 || (i == 0 && j == loop.Count - 1)) continue; // adjacent edges
                Vector2 p1 = _pos[loop[i]], p2 = _pos[loop[(i + 1) % loop.Count]];
                Vector2 p3 = _pos[loop[j]], p4 = _pos[loop[(j + 1) % loop.Count]];
                if (SegmentsCross(p1, p2, p3, p4)) return false;
            }
            return true;
        }

        static void LogOutline(string what) { if (Debug.isDebugBuild) Debug.Log($"[RoomGraph] Outline: {what}."); }

        // ── small geometry helpers ───────────────────────────────────────────

        static bool LineIntersect(Vector2 p0, Vector2 d0, Vector2 p1, Vector2 d1, out Vector2 x)
        {
            x = Vector2.zero;
            float denom = d0.x * d1.y - d0.y * d1.x;
            if (Mathf.Abs(denom) < 1e-6f) return false;
            float t = ((p1.x - p0.x) * d1.y - (p1.y - p0.y) * d1.x) / denom;
            x = p0 + d0 * t;
            return true;
        }

        /// <summary>Which end of a→b is nearest <paramref name="x"/>, and how far x lies
        /// beyond it (0 when x is between the ends).</summary>
        static bool ReachEnd(Vector2 a, Vector2 b, Vector2 x, out int end, out float over)
        {
            var d = b - a; float len = d.magnitude;
            end = 0; over = 0f;
            if (len < 1e-4f) return false;
            d /= len;
            float t = Vector2.Dot(x - a, d);
            end = t <= len * 0.5f ? 0 : 1;
            if (t < 0f) over = -t;
            else if (t > len) over = t - len;
            return true;
        }

        /// <summary>Foot of the perpendicular from p onto segment a→b. Succeeds only when
        /// the foot lands within <paramref name="slack"/> of the segment span AND the
        /// perpendicular offset is itself within <paramref name="slack"/> (a near miss,
        /// not a far parallel line).</summary>
        static bool TryFootOnSegment(Vector2 p, Vector2 a, Vector2 b, float slack, out Vector2 foot)
        {
            foot = p;
            var d = b - a; float len = d.magnitude;
            if (len < 1e-4f) return false;
            d /= len;
            float t = Vector2.Dot(p - a, d);
            if (t < -slack || t > len + slack) return false;
            var f = a + d * Mathf.Clamp(t, 0f, len);
            if ((f - p).magnitude > slack) return false;
            foot = f;
            return true;
        }

        static float CcwAngle(Vector2 from, Vector2 to)
        {
            float a = Mathf.Atan2(to.y, to.x) - Mathf.Atan2(from.y, from.x);
            if (a < 0f) a += Mathf.PI * 2f;
            return a;
        }

        static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float o1 = Cross(a, b, c), o2 = Cross(a, b, d);
            float o3 = Cross(c, d, a), o4 = Cross(c, d, b);
            return o1 * o2 < 0f && o3 * o4 < 0f;
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 c)
            => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }
}
