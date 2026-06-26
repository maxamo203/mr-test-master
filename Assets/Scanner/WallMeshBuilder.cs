using System.Collections.Generic;
using UnityEngine;

namespace Scanner
{
    // Genera la malla de una pared como CAJA 3D en espacio anchor-relativo.
    //
    // La caja se define por:
    //   - cara cercana: el plano que pasa por la linea de piso aLocal -> bLocal,
    //     extendido hacia arriba (eje up del anchor) por 'height'.
    //   - grosor: la caja se extruye 'width' a lo largo de 'normal' (horizontal,
    //     perpendicular a la base). La cara cercana queda en w=0; la lejana en w=width.
    //
    // Parametrizacion local: punto(u,v,w) = aLocal + u*baseHat + v*up + w*normal,
    //   con u in [0,length], v in [0,height], w in [0,width].
    //
    // Las puertas son agujeros rectangulares PASANTES (atraviesan el grosor). Se
    // resuelven con el mismo enfoque de grilla U/V que la version plana: se cortan
    // celdas en U (a lo largo de la base) y V (a lo alto), se omiten las celdas
    // dentro de un hueco, y para cada celda solida cuyo vecino es vacio se emite un
    // quad lateral que conecta la cara cercana con la lejana. Eso genera de forma
    // uniforme: tapa inferior (umbral, con gaps en las puertas), tapa superior,
    // tapas de los extremos y las jambas/dintel de cada puerta.
    //
    // El mesh se genera con vertices en coordenadas anchor-relativas; el WallObject
    // pone el transform en Vector3.zero / identity, asi los vertices "ya estan" en su
    // lugar relativo al anchor.
    public static class WallMeshBuilder
    {
        // Umbral minimo de dimension para generar geometria. Por debajo devolvemos
        // un mesh nulo (evita warnings de PhysX al cookear meshes degenerados
        // durante el drag de vertices).
        private const float MinDimension = 0.02f;

        public static Mesh Build(Vector3 aLocal, Vector3 bLocal, float height, float width,
                                 Vector3 normal, IReadOnlyList<DoorData> doors)
        {
            var baseVec = bLocal - aLocal;
            float length = baseVec.magnitude;
            if (length < MinDimension || height < MinDimension || width < MinDimension) return null;
            var baseHat = baseVec / length;
            var up      = Vector3.up;
            var n       = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.Cross(up, baseHat).normalized;

            // Cortes en U (a lo largo del base) y V (a lo alto).
            var uSet = new SortedSet<float> { 0f, length };
            var vSet = new SortedSet<float> { 0f, height };
            if (doors != null)
            {
                foreach (var d in doors)
                {
                    uSet.Add(Mathf.Clamp(d.uMin, 0f, length));
                    uSet.Add(Mathf.Clamp(d.uMax, 0f, length));
                    vSet.Add(Mathf.Clamp(d.vMin, 0f, height));
                    vSet.Add(Mathf.Clamp(d.vMax, 0f, height));
                }
            }
            var us = new List<float>(uSet);
            var vs = new List<float>(vSet);

            var verts = new List<Vector3>();
            var uvs   = new List<Vector2>();
            var tris  = new List<int>();
            // Coordenada metrica (u,v,w) y normal en ese frame, por vertice. El shader
            // EdgeGrid (con _GridFromUV=1) las usa para que el grid siga la geometria
            // de la pared (u a lo largo de la base, v en altura) en vez de un grid
            // world-aligned: asi una pared inclinada refleja su inclinacion.
            var metrics = new List<Vector3>();
            var gnorms  = new List<Vector3>();

            float lInv = 1f / length;
            float hInv = height > 0.0001f ? 1f / height : 0f;

            // Punto en espacio anchor.
            Vector3 P(float u, float v, float w) => aLocal + u * baseHat + v * up + w * n;
            // Coordenada metrica del grid (misma terna u,v,w que P).
            Vector3 G(float u, float v, float w) => new Vector3(u, v, w);
            // Normales en el frame del grid: cara de grosor (w), de base (u), de altura (v).
            var gnW = new Vector3(0f, 0f, 1f);
            var gnU = new Vector3(1f, 0f, 0f);
            var gnV = new Vector3(0f, 1f, 0f);

            // Determina si la celda (i,j) es solida (dentro de rango y no dentro de
            // ningun hueco de puerta).
            bool IsSolid(int i, int j)
            {
                if (i < 0 || i >= us.Count - 1 || j < 0 || j >= vs.Count - 1) return false;
                float cu = (us[i] + us[i + 1]) * 0.5f;
                float cv = (vs[j] + vs[j + 1]) * 0.5f;
                if (doors != null)
                    foreach (var d in doors)
                        if (cu > d.uMin && cu < d.uMax && cv > d.vMin && cv < d.vMax) return false;
                return true;
            }

            // Emite un quad (4 vertices propios) cuyo frente mira hacia 'outward'.
            // Los corners p0..p3 van alrededor del perimetro; el orden de los
            // triangulos se elige para que la normal geometrica coincida con
            // 'outward' (asi RecalculateNormals da normales hacia afuera sin
            // depender de que yo acierte el winding a mano).
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

                    // IMPORTANTE: cada cara usa SUS PROPIOS 4 vertices. Si caras con
                    // normales opuestas compartieran vertices, RecalculateNormals
                    // promediaria a ~0 y el shader lit quedaria sin iluminacion
                    // (pared blanca/negra en iOS). Vertices separados => normal correcta.

                    // ── Cara cercana (w=0), mira hacia -n ──
                    Quad(P(u0, v0, 0f), P(u1, v0, 0f), P(u1, v1, 0f), P(u0, v1, 0f), -n,
                         uv00, uv10, uv11, uv01,
                         G(u0, v0, 0f), G(u1, v0, 0f), G(u1, v1, 0f), G(u0, v1, 0f), gnW);

                    // ── Cara lejana (w=width), mira hacia +n ──
                    Quad(P(u0, v0, width), P(u1, v0, width), P(u1, v1, width), P(u0, v1, width), n,
                         uv00, uv10, uv11, uv01,
                         G(u0, v0, width), G(u1, v0, width), G(u1, v1, width), G(u0, v1, width), gnW);

                    // ── Quads laterales hacia vecinos vacios (caps + jambas de puerta) ──
                    // El frente mira "hacia afuera" de la celda solida (away del vecino).
                    // Izquierda (u=u0): vecino i-1 vacio. Mira -baseHat.
                    if (!IsSolid(i - 1, j))
                        Quad(P(u0, v0, 0f), P(u0, v1, 0f), P(u0, v1, width), P(u0, v0, width), -baseHat,
                             new Vector2(v0, 0f), new Vector2(v1, 0f), new Vector2(v1, 1f), new Vector2(v0, 1f),
                             G(u0, v0, 0f), G(u0, v1, 0f), G(u0, v1, width), G(u0, v0, width), gnU);

                    // Derecha (u=u1): vecino i+1 vacio. Mira +baseHat.
                    if (!IsSolid(i + 1, j))
                        Quad(P(u1, v0, 0f), P(u1, v0, width), P(u1, v1, width), P(u1, v1, 0f), baseHat,
                             new Vector2(v0, 0f), new Vector2(v0, 1f), new Vector2(v1, 1f), new Vector2(v1, 0f),
                             G(u1, v0, 0f), G(u1, v0, width), G(u1, v1, width), G(u1, v1, 0f), gnU);

                    // Abajo (v=v0): vecino j-1 vacio. Mira -up (tapa inferior / umbral).
                    if (!IsSolid(i, j - 1))
                        Quad(P(u0, v0, 0f), P(u0, v0, width), P(u1, v0, width), P(u1, v0, 0f), -up,
                             new Vector2(u0, 0f), new Vector2(u0, 1f), new Vector2(u1, 1f), new Vector2(u1, 0f),
                             G(u0, v0, 0f), G(u0, v0, width), G(u1, v0, width), G(u1, v0, 0f), gnV);

                    // Arriba (v=v1): vecino j+1 vacio. Mira +up (tapa superior / dintel).
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
            mesh.SetUVs(1, metrics); // (u,v,w) metrico para el grid del shader
            mesh.SetUVs(2, gnorms);  // normal en el frame del grid
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
