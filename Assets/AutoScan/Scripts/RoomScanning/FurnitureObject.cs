using UnityEngine;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// A piece of detected furniture as a real Unity object rather than an anonymous box
    /// mesh — the counterpart of <see cref="AutoWallObject"/>, and modelled on the same part of
    /// the reference game (<c>Scanner/CubeObject</c>) so the two projects agree on what a
    /// furniture box *is*: a centre, a size, and a rotation, carrying a stable id.
    ///
    /// Position and rotation live on the transform, as the game's cube does, so a rotated
    /// box costs nothing to represent once oriented-box fitting exists. The *size*, however,
    /// is baked into the mesh in metres instead of going on <c>localScale</c>. That is a
    /// deliberate divergence: <c>Mortuorium/ARPlaneGrid</c> derives its grid from
    /// object-space XZ, so a unit cube stretched by the transform would draw the same number
    /// of grid cells on a wardrobe as on a shoebox. Baking the size keeps the grid metric and
    /// consistent with every other surface in the scan. Nothing is lost across the export:
    /// <see cref="SizeLocal"/> is what <c>ScanCubeData.scaleLocal</c> is written from, and the
    /// game rebuilds it on its own transform.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class FurnitureObject : MonoBehaviour
    {
        /// <summary>Smallest edge we will build; below this the mesh is not worth cooking.</summary>
        public const float MinSize = 0.02f;

        public int Id { get; private set; }

        /// <summary>Box centre in map-origin-local space (the transform's local position).</summary>
        public Vector3 CenterLocal => transform.localPosition;

        /// <summary>Full extents in metres, baked into the mesh.</summary>
        public Vector3 SizeLocal { get; private set; } = Vector3.one * 0.3f;

        /// <summary>Identity until oriented-box fitting exists; carried so export stays honest.</summary>
        public Quaternion RotLocal => transform.localRotation;

        MeshFilter _mf;
        MeshRenderer _mr;
        MeshCollider _mc;
        Mesh _mesh;
        Material _material;

        void Awake()
        {
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mr == null) _mr = GetComponent<MeshRenderer>();
            if (_mc == null) _mc = GetComponent<MeshCollider>();
        }

        /// <summary>
        /// Creates a furniture box under <paramref name="parent"/> (the map origin).
        /// <paramref name="template"/> is instanced per box so tinting one cannot bleed
        /// into the others.
        /// </summary>
        public static FurnitureObject Create(Transform parent, int id,
                                             Vector3 centerLocal, Vector3 sizeLocal,
                                             Material template)
        {
            var go = new GameObject("Furniture_" + id);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.SetLocalPositionAndRotation(centerLocal, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            int layer = LayerMask.NameToLayer(AutoWallObject.PlacedLayerName);
            if (layer >= 0) go.layer = layer;

            var f = go.AddComponent<FurnitureObject>();
            f._mf = go.GetComponent<MeshFilter>();
            f._mr = go.GetComponent<MeshRenderer>();
            f._mc = go.GetComponent<MeshCollider>();

            // Same reasoning as AutoWallObject: mesh cleaning off, so PhysX does not warn on the
            // briefly-degenerate shapes a box passes through while it is being refitted.
            f._mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                                 | MeshColliderCookingOptions.WeldColocatedVertices;

            f.Id = id;
            f.SizeLocal = Clamp(sizeLocal);
            f.EnsureMaterial(template);
            f.Rebuild();
            return f;
        }

        /// <summary>Moves and resizes in place — no GameObject churn.</summary>
        public void SetBox(Vector3 centerLocal, Vector3 sizeLocal)
        {
            transform.localPosition = centerLocal;
            SizeLocal = Clamp(sizeLocal);
            Rebuild();
        }

        /// <summary>
        /// Applies a state tint to this box's own material instance. What the two arguments
        /// mean depends on the shader — see <see cref="ScanMaterials.Tint"/>.
        /// </summary>
        public void SetTint(Color color, float fillAlpha)
            => ScanMaterials.Tint(_material, color, fillAlpha);

        static Vector3 Clamp(Vector3 size) => new Vector3(
            Mathf.Max(MinSize, Mathf.Abs(size.x)),
            Mathf.Max(MinSize, Mathf.Abs(size.y)),
            Mathf.Max(MinSize, Mathf.Abs(size.z)));

        void Rebuild()
        {
            transform.localScale = Vector3.one;   // size lives in the mesh, never here

            DestroyMesh();
            _mesh = BuildBox(SizeLocal);
            _mf.sharedMesh = _mesh;
            // Null first so PhysX re-cooks the new shape instead of reusing the old cook.
            _mc.sharedMesh = null;
            _mc.sharedMesh = _mesh;
        }

        /// <summary>
        /// A box centred on the origin with <paramref name="size"/> full extents, in metres.
        ///
        /// Every face gets its own four vertices — 24, not 8. Sharing the eight corners
        /// makes <c>RecalculateNormals</c> average three mutually perpendicular faces into a
        /// single diagonal normal at each corner, which the old <c>CubeVerts</c>/<c>BoxTris</c>
        /// path got away with only because the grid shader ignores normals. The same trap is
        /// documented in <see cref="AutoWallMeshBuilder"/>; do not reintroduce it here.
        /// </summary>
        static Mesh BuildBox(Vector3 size)
        {
            var h = size * 0.5f;

            // Per face: outward normal, then the two in-plane axes spanning it.
            var faces = new[]
            {
                (n: Vector3.right,   u: Vector3.up,      v: Vector3.forward),
                (n: Vector3.left,    u: Vector3.forward, v: Vector3.up),
                (n: Vector3.up,      u: Vector3.forward, v: Vector3.right),
                (n: Vector3.down,    u: Vector3.right,   v: Vector3.forward),
                (n: Vector3.forward, u: Vector3.right,   v: Vector3.up),
                (n: Vector3.back,    u: Vector3.up,      v: Vector3.right),
            };

            var verts = new Vector3[24];
            var norms = new Vector3[24];
            var uvs   = new Vector2[24];
            var tris  = new int[36];

            for (int f = 0; f < 6; f++)
            {
                var face = faces[f];
                var c  = Vector3.Scale(face.n, h);   // face centre
                var eu = Vector3.Scale(face.u, h);   // half-edge along u
                var ev = Vector3.Scale(face.v, h);   // half-edge along v

                int b = f * 4;
                verts[b + 0] = c - eu - ev;
                verts[b + 1] = c + eu - ev;
                verts[b + 2] = c + eu + ev;
                verts[b + 3] = c - eu + ev;

                for (int k = 0; k < 4; k++) norms[b + k] = face.n;
                uvs[b + 0] = new Vector2(0f, 0f);
                uvs[b + 1] = new Vector2(1f, 0f);
                uvs[b + 2] = new Vector2(1f, 1f);
                uvs[b + 3] = new Vector2(0f, 1f);

                int t = f * 6;
                tris[t + 0] = b + 0; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                tris[t + 3] = b + 0; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
            }

            var mesh = new Mesh { name = "FurnitureBox" };
            mesh.vertices  = verts;
            mesh.normals   = norms;    // set directly; no RecalculateNormals to get wrong
            mesh.uv        = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        void EnsureMaterial(Material template)
        {
            if (template != null && AutoWallObject.IsShaderUsable(template.shader, template.name))
            {
                _material = new Material(template);
            }
            else
            {
                _material = ScanMaterials.CreateRuntime(ScanSurface.Cube);
                if (_material != null) _material.name = "FurnitureMat (runtime)";
            }

            // A box's grid is metric object space, so it rotates with the box — asserted
            // here so a box handed the wall material still renders as a box.
            ScanMaterials.ConfigureEdgeGrid(_material, ScanSurface.Cube);
            _mr.sharedMaterial = _material;
        }

        void DestroyMesh()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
            _mesh = null;
        }

        void OnDestroy()
        {
            DestroyMesh();
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material);
                _material = null;
            }
        }
    }
}
