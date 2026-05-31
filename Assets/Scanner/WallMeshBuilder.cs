using System.Collections.Generic;
using UnityEngine;

namespace Scanner
{
    // Genera la malla de una pared en espacio anchor-relativo.
    //
    // La pared es un parallelogram con:
    //   - lado inferior: de aLocal a bLocal (horizontal pero puede tener tilt en Y)
    //   - lado vertical: siempre paralelo a Vector3.up (anchor +Y)
    //
    // Asi paredes consecutivas en una polilinea que comparten un vertice
    // (la b de una es la a de la otra) tienen los topes a la misma altura
    // exacta (aLocal.y + H == bLocal.y + H solo si floor flat, pero el vertice
    // compartido SI alinea su tope: shared_floor.y + H queda igual para ambas).
    //
    // El mesh se genera con vertices en coordenadas anchor-relativas; el
    // WallObject pone el transform en Vector3.zero / identity, asi los
    // vertices "ya estan" en su lugar relativo al anchor.
    public static class WallMeshBuilder
    {
        public static Mesh Build(Vector3 aLocal, Vector3 bLocal, float height, IReadOnlyList<DoorData> doors)
        {
            var baseVec = bLocal - aLocal;
            float length = baseVec.magnitude;
            if (length < 0.0001f)
            {
                var empty = new Mesh { name = "WallMeshEmpty" };
                return empty;
            }
            var baseHat = baseVec / length;
            var up      = Vector3.up;

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

            for (int i = 0; i < us.Count - 1; i++)
            {
                for (int j = 0; j < vs.Count - 1; j++)
                {
                    float u0 = us[i], u1 = us[i + 1];
                    float v0 = vs[j], v1 = vs[j + 1];
                    float cu = (u0 + u1) * 0.5f;
                    float cv = (v0 + v1) * 0.5f;

                    bool insideHole = false;
                    if (doors != null)
                    {
                        foreach (var d in doors)
                        {
                            if (cu > d.uMin && cu < d.uMax && cv > d.vMin && cv < d.vMax)
                            { insideHole = true; break; }
                        }
                    }
                    if (insideHole) continue;

                    Vector3 p00 = aLocal + u0 * baseHat + v0 * up;
                    Vector3 p10 = aLocal + u1 * baseHat + v0 * up;
                    Vector3 p11 = aLocal + u1 * baseHat + v1 * up;
                    Vector3 p01 = aLocal + u0 * baseHat + v1 * up;

                    int baseIdx = verts.Count;
                    verts.Add(p00); verts.Add(p10); verts.Add(p11); verts.Add(p01);

                    float lInv = 1f / length;
                    float hInv = height > 0.0001f ? 1f / height : 0f;
                    uvs.Add(new Vector2(u0 * lInv, v0 * hInv));
                    uvs.Add(new Vector2(u1 * lInv, v0 * hInv));
                    uvs.Add(new Vector2(u1 * lInv, v1 * hInv));
                    uvs.Add(new Vector2(u0 * lInv, v1 * hInv));

                    // Cara A (normal segun cross(baseHat, up))
                    tris.Add(baseIdx + 0); tris.Add(baseIdx + 2); tris.Add(baseIdx + 1);
                    tris.Add(baseIdx + 0); tris.Add(baseIdx + 3); tris.Add(baseIdx + 2);
                    // Cara B (visible desde el otro lado)
                    tris.Add(baseIdx + 0); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                    tris.Add(baseIdx + 0); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
                }
            }

            var mesh = new Mesh { name = "WallMesh" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
