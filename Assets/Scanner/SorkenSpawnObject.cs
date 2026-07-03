using UnityEngine;

namespace Scanner
{
    // Punto de spawn de Sorken: una piramide aplanada cuya punta apunta segun
    // la normal de la pared/superficie donde se coloco (+Z local). Hijo de
    // WorldOrigin: pos/rot local son anchor-relativos, igual que paredes/cubos.
    //
    // Es ISelectable: tap para seleccionar y mover/rotar con el gizmo.
    public class SorkenSpawnObject : MonoBehaviour, ISelectable
    {
        public string Id { get; private set; }

        public SelectableKind Kind => SelectableKind.SorkenSpawn;
        public Transform Transform => transform;

        // Dimensiones de la piramide (m): base cuadrada en el plano XY local,
        // punta en +Z. Aplanada: mucho mas ancha que alta.
        public const float BaseHalf = 0.14f;
        public const float TipLength = 0.10f;

        private MeshRenderer _mr;
        private Material     _matNormal;
        private Material     _matSelected;

        public static SorkenSpawnObject Create(Vector3 posLocal, Quaternion rotLocal, string id = null)
        {
            var go = new GameObject("SorkenSpawn");
            go.transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            go.transform.localPosition = posLocal;
            go.transform.localRotation = rotLocal;
            go.transform.localScale    = Vector3.one;
            int placedLayer = LayerMask.NameToLayer("Placed");
            if (placedLayer >= 0) go.layer = placedLayer;

            var s = go.AddComponent<SorkenSpawnObject>();
            s.Id = id ?? System.Guid.NewGuid().ToString("N").Substring(0, 8);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = PyramidMesh();
            s._mr = go.AddComponent<MeshRenderer>();
            s.EnsureMaterials();
            s._mr.sharedMaterial = s._matNormal;

            // Collider convexo para el tap de seleccion (evitamos CreatePrimitive
            // por el stripping de Physics bajo IL2CPP — ver convenciones).
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = PyramidMesh();
            mc.convex     = true;

            SceneRegistry.Instance?.Register(s);
            return s;
        }

        public static SorkenSpawnObject FromData(SpawnPointData d) =>
            Create(d.posLocal.ToVector3(), d.rotLocal.ToQuaternion(), d.id);

        public SpawnPointData ToData()
        {
            return new SpawnPointData
            {
                id       = Id,
                posLocal = new Vec3(transform.localPosition),
                rotLocal = new Quat(transform.localRotation),
            };
        }

        // Piramide aplanada compartida: base cuadrada en z=0, apex en +Z.
        private static Mesh _pyramid;
        public static Mesh PyramidMesh()
        {
            if (_pyramid != null) return _pyramid;

            float s = BaseHalf, h = TipLength;
            var verts = new[]
            {
                new Vector3(-s, -s, 0f),  // 0 base
                new Vector3( s, -s, 0f),  // 1
                new Vector3( s,  s, 0f),  // 2
                new Vector3(-s,  s, 0f),  // 3
                new Vector3( 0f, 0f, h),  // 4 apex (punta = normal de la pared)
            };
            var tris = new[]
            {
                // caras laterales (winding hacia afuera)
                0, 1, 4,
                1, 2, 4,
                2, 3, 4,
                3, 0, 4,
                // tapa de la base (mirando -Z, hacia la pared)
                0, 2, 1,
                0, 3, 2,
            };
            _pyramid = new Mesh { name = "SorkenSpawnPyramid" };
            _pyramid.SetVertices(verts);
            _pyramid.SetTriangles(tris, 0);
            _pyramid.RecalculateNormals();
            _pyramid.RecalculateBounds();
            return _pyramid;
        }

        private void EnsureMaterials()
        {
            if (_matNormal == null)
            {
                var sh = Shader.Find("Custom/LitMarker") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                _matNormal = new Material(sh) { name = "SorkenSpawnMat (runtime)" };
                var col = new Color(0.85f, 0.15f, 0.55f, 1f); // magenta-rojo = Sorken
                if (_matNormal.HasProperty("_Color"))     _matNormal.color = col;
                if (_matNormal.HasProperty("_BaseColor")) _matNormal.SetColor("_BaseColor", col);
            }
            if (_matSelected == null)
            {
                _matSelected = new Material(_matNormal) { name = "SorkenSpawnMatSelected (runtime)" };
                var col = new Color(1f, 0.8f, 0.2f, 1f);
                if (_matSelected.HasProperty("_Color"))     _matSelected.color = col;
                if (_matSelected.HasProperty("_BaseColor")) _matSelected.SetColor("_BaseColor", col);
            }
        }

        public void OnSelect()
        {
            EnsureMaterials();
            if (_mr != null) _mr.sharedMaterial = _matSelected;
            // Gizmo completo: mover + rotar (re-orientar la punta) + escalar.
            TransformGizmoController.Instance?.Attach(transform, moveOnly: false);
        }

        public void OnDeselect()
        {
            EnsureMaterials();
            if (_mr != null) _mr.sharedMaterial = _matNormal;
            TransformGizmoController.Instance?.Detach();
        }

        public void Delete()
        {
            SceneRegistry.Instance?.Unregister(this);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_matNormal   != null && _matNormal.name.Contains("(runtime)"))   Destroy(_matNormal);
            if (_matSelected != null && _matSelected.name.Contains("(runtime)")) Destroy(_matSelected);
        }
    }
}
