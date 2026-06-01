using UnityEngine;

namespace Scanner
{
    // Cubo para representar muebles. Hijo de WorldOrigin: pos/rot/scale local
    // son anchor-relativos.
    public class CubeObject : MonoBehaviour, ISelectable
    {
        public string Id { get; private set; }

        public SelectableKind Kind => SelectableKind.Cube;
        public Transform Transform => transform;

        private MeshRenderer _mr;
        private Material     _matNormal;
        private Material     _matSelected;

        public const float DefaultSize = 0.3f;

        public static CubeObject Create(Vector3 posLocal, Quaternion rotLocal, Vector3 scaleLocal, string id = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PlacedCube";
            go.transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            go.transform.localPosition = posLocal;
            go.transform.localRotation = rotLocal;
            go.transform.localScale    = scaleLocal;
            int placedLayer = LayerMask.NameToLayer("Placed");
            if (placedLayer >= 0) go.layer = placedLayer;

            var c = go.AddComponent<CubeObject>();
            c.Id  = id ?? System.Guid.NewGuid().ToString("N").Substring(0, 8);
            c._mr = go.GetComponent<MeshRenderer>();
            c.EnsureMaterials();
            c._mr.sharedMaterial = c._matNormal;

            SceneRegistry.Instance?.Register(c);
            return c;
        }

        public static CubeObject FromData(CubeData d)
        {
            return Create(d.posLocal.ToVector3(), d.rotLocal.ToQuaternion(), d.scaleLocal.ToVector3(), d.id);
        }

        public CubeData ToData()
        {
            return new CubeData
            {
                id         = Id,
                posLocal   = new Vec3(transform.localPosition),
                rotLocal   = new Quat(transform.localRotation),
                scaleLocal = new Vec3(transform.localScale),
            };
        }

        private void EnsureMaterials()
        {
            // 1) Materiales del CubeBuilder Inspector si existen.
            if (_matNormal == null && CubeBuilder.ConfiguredNormalMat != null)
                _matNormal = CubeBuilder.ConfiguredNormalMat;
            if (_matSelected == null && CubeBuilder.ConfiguredSelectedMat != null)
                _matSelected = CubeBuilder.ConfiguredSelectedMat;

            // 2) Fallback runtime si no hay material configurado.
            if (_matNormal == null)
            {
                var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                _matNormal = new Material(sh) { name = "CubeMat (runtime)" };
                var col = new Color(0.6f, 0.8f, 1f, 0.8f);
                if (_matNormal.HasProperty("_Color"))     _matNormal.color = col;
                if (_matNormal.HasProperty("_BaseColor")) _matNormal.SetColor("_BaseColor", col);
                _matNormal.renderQueue = 3000;
            }
            if (_matSelected == null)
            {
                _matSelected = new Material(_matNormal) { name = "CubeMatSelected (runtime)" };
                var col = new Color(1f, 0.8f, 0.2f, 0.85f);
                if (_matSelected.HasProperty("_Color"))     _matSelected.color = col;
                if (_matSelected.HasProperty("_BaseColor")) _matSelected.SetColor("_BaseColor", col);
                _matSelected.renderQueue = 3000;
            }
        }

        public void OnSelect()
        {
            EnsureMaterials();
            if (_mr != null) _mr.sharedMaterial = _matSelected;
        }

        public void OnDeselect()
        {
            EnsureMaterials();
            if (_mr != null) _mr.sharedMaterial = _matNormal;
        }

        public void Delete()
        {
            SceneRegistry.Instance?.Unregister(this);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // Solo destruir materiales runtime, no los asignados del Inspector.
            if (_matNormal   != null && _matNormal.name.Contains("(runtime)"))   Destroy(_matNormal);
            if (_matSelected != null && _matSelected.name.Contains("(runtime)")) Destroy(_matSelected);
        }
    }
}
