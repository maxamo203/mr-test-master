using System.Collections.Generic;
using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// Builds a wall as a 3D box in map-origin-local space, with door and window
    /// openings cut clean through it.
    ///
    /// The box is defined by:
    ///   - near face: the plane through the floor line aLocal → bLocal, extended
    ///     upward by <c>height</c>;
    ///   - thickness: extruded <c>width</c> along <c>normal</c> (horizontal,
    ///     perpendicular to the base). Near face at w=0, far face at w=width.
    ///
    /// Local parameterisation: point(u,v,w) = aLocal + u*baseHat + v*up + w*normal,
    /// with u in [0,length], v in [0,height], w in [0,width].
    ///
    /// Openings are rectangular holes that pass through the thickness. They are
    /// resolved with a U/V cell grid: cuts are introduced in U (along the base) and
    /// V (up the height) at every opening edge, cells falling inside an opening are
    /// skipped, and for each solid cell whose neighbour is empty a side quad is
    /// emitted joining the near face to the far face. That one rule produces, without
    /// special cases: the bottom cap (threshold, with gaps at doors), the top cap, the
    /// end caps, and the jambs and lintel of every opening.
    ///
    /// Vertices are generated in map-origin-local coordinates and the wall
    /// GameObject's transform is left at identity, so they already sit in the right
    /// place relative to the origin — the same convention the rest of the builder uses.
    ///
    /// Ported from the reference game's Scanner/AutoWallMeshBuilder.cs, which already
    /// solved this; kept close to the original so fixes can be carried across.
    /// </summary>
    public static class AutoWallMeshBuilder
    {
        /// <summary>
        /// Below this any dimension yields a null mesh. Guards against PhysX warnings
        /// when cooking degenerate colliders while a wall is being dragged.
        /// </summary>
        public const float MinDimension = 0.02f;

        public static Mesh Build(Vector3 aLocal, Vector3 bLocal, float height, float width,
                                 Vector3 normal, IReadOnlyList<WallOpening> openings)
        {
            var baseVec = bLocal - aLocal;
            float length = baseVec.magnitude;
            if (length < MinDimension || height < MinDimension || width < MinDimension) return null;

            var baseHat = baseVec / length;
            var up = Vector3.up;
            var n = normal.sqrMagnitude > 1e-6f
                ? normal.normalized
                : Vector3.Cross(up, baseHat).normalized;

            // Cuts along the base (U) and up the height (V), one pair per opening edge.
            var uSet = new SortedSet<float> { 0f, length };
            var vSet = new SortedSet<float> { 0f, height };
            if (openings != null)
            {
                foreach (var o in openings)
                {
                    uSet.Add(Mathf.Clamp(o.uMin, 0f, length));
                    uSet.Add(Mathf.Clamp(o.uMax, 0f, length));
                    vSet.Add(Mathf.Clamp(o.vMin, 0f, height));
                    vSet.Add(Mathf.Clamp(o.vMax, 0f, height));
                }
            }
            var us = new List<float>(uSet);
            var vs = new List<float>(vSet);

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            // Metric (u,v,w) and the normal in that frame, per vertex. Carried in UV1/UV2
            // so a grid shader can follow the wall's own geometry rather than world axes.
            // Mortuorium's ARPlaneGrid does not read them today; they cost nothing unread
            // and keep the mesh correct if a wall-aware shader is added later.
            var metrics = new List<Vector3>();
            var gnorms = new List<Vector3>();

            float lInv = 1f / length;
            float hInv = height > 0.0001f ? 1f / height : 0f;

            Vector3 P(float u, float v, float w) => aLocal + u * baseHat + v * up + w * n;
            Vector3 G(float u, float v, float w) => new Vector3(u, v, w);

            var gnW = new Vector3(0f, 0f, 1f);
            var gnU = new Vector3(1f, 0f, 0f);
            var gnV = new Vector3(0f, 1f, 0f);

            // A cell is solid when it is in range and not inside any opening.
            bool IsSolid(int i, int j)
            {
                if (i < 0 || i >= us.Count - 1 || j < 0 || j >= vs.Count - 1) return false;
                float cu = (us[i] + us[i + 1]) * 0.5f;
                float cv = (vs[j] + vs[j + 1]) * 0.5f;
                if (openings != null)
                    foreach (var o in openings)
                        if (cu > o.uMin && cu < o.uMax && cv > o.vMin && cv < o.vMax) return false;
                return true;
            }

            // Emits a quad with its own four vertices, facing 'outward'. The winding is
            // chosen from the geometric normal rather than assumed, so RecalculateNormals
            // always ends up pointing outward.
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward,
                      Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3,
                      Vector3 g0, Vector3 g1, Vector3 g2, Vector3 g3, Vector3 gn)
            {
                int b = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
                uvs.Add(t0); uvs.Add(t1); uvs.Add(t2); uvs.Add(t3);
                metrics.Add(g0); metrics.Add(g1); metrics.Add(g2); metrics.Add(g3);
                gnorms.Add(gn); gnorms.Add(gn); gnorms.Add(gn); gnorms.Add(gn);

                var geo = Vector3.Cross(p1 - p0, p2 - p0);
                if (Vector3.Dot(geo, outward) >= 0f)
                {
                    tris.Add(b + 0); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 3);
                }
                else
                {
                    tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 0); tris.Add(b + 3); tris.Add(b + 2);
                }
            }

            for (int i = 0; i < us.Count - 1; i++)
            {
                for (int j = 0; j < vs.Count - 1; j++)
                {
                    if (!IsSolid(i, j)) continue;

                    float u0 = us[i], u1 = us[i + 1];
                    float v0 = vs[j], v1 = vs[j + 1];

                    var uv00 = new Vector2(u0 * lInv, v0 * hInv);
                    var uv10 = new Vector2(u1 * lInv, v0 * hInv);
                    var uv11 = new Vector2(u1 * lInv, v1 * hInv);
                    var uv01 = new Vector2(u0 * lInv, v1 * hInv);

                    // IMPORTANT: every face gets its OWN four vertices. If faces with
                    // opposing normals shared vertices, RecalculateNormals would average
                    // them to ~0 and a lit shader would render the wall unlit.

                    // Near face (w=0), facing -n.
                    Quad(P(u0, v0, 0f), P(u1, v0, 0f), P(u1, v1, 0f), P(u0, v1, 0f), -n,
                         uv00, uv10, uv11, uv01,
                         G(u0, v0, 0f), G(u1, v0, 0f), G(u1, v1, 0f), G(u0, v1, 0f), gnW);

                    // Far face (w=width), facing +n.
                    Quad(P(u0, v0, width), P(u1, v0, width), P(u1, v1, width), P(u0, v1, width), n,
                         uv00, uv10, uv11, uv01,
                         G(u0, v0, width), G(u1, v0, width), G(u1, v1, width), G(u0, v1, width), gnW);

                    // Side quads toward empty neighbours: end caps, threshold, lintel, jambs.
                    if (!IsSolid(i - 1, j))
                        Quad(P(u0, v0, 0f), P(u0, v1, 0f), P(u0, v1, width), P(u0, v0, width), -baseHat,
                             new Vector2(v0, 0f), new Vector2(v1, 0f), new Vector2(v1, 1f), new Vector2(v0, 1f),
                             G(u0, v0, 0f), G(u0, v1, 0f), G(u0, v1, width), G(u0, v0, width), gnU);

                    if (!IsSolid(i + 1, j))
                        Quad(P(u1, v0, 0f), P(u1, v0, width), P(u1, v1, width), P(u1, v1, 0f), baseHat,
                             new Vector2(v0, 0f), new Vector2(v0, 1f), new Vector2(v1, 1f), new Vector2(v1, 0f),
                             G(u1, v0, 0f), G(u1, v0, width), G(u1, v1, width), G(u1, v1, 0f), gnU);

                    if (!IsSolid(i, j - 1))
                        Quad(P(u0, v0, 0f), P(u0, v0, width), P(u1, v0, width), P(u1, v0, 0f), -up,
                             new Vector2(u0, 0f), new Vector2(u0, 1f), new Vector2(u1, 1f), new Vector2(u1, 0f),
                             G(u0, v0, 0f), G(u0, v0, width), G(u1, v0, width), G(u1, v0, 0f), gnV);

                    if (!IsSolid(i, j + 1))
                        Quad(P(u0, v1, 0f), P(u1, v1, 0f), P(u1, v1, width), P(u0, v1, width), up,
                             new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f),
                             G(u0, v1, 0f), G(u1, v1, 0f), G(u1, v1, width), G(u0, v1, width), gnV);
                }
            }

            if (tris.Count == 0) return null;

            var mesh = new Mesh { name = "WallMesh" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, metrics); // metric (u,v,w)
            mesh.SetUVs(2, gnorms);  // normal in the grid frame
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

