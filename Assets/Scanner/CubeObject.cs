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

        // Handles de los dos vertices de la diagonal: 0 = corner (-,-,-), 1 = (+,+,+).
        // Permiten arrastrar cada esquina para reformar el cubo (el corner opuesto
        // queda fijo). Son hijos de WorldOrigin, no del cubo, igual que los de pared.
        private readonly CubeVertexHandle[] _handles = new CubeVertexHandle[2];
        private const float VertexHandleRadius = 0.04f;

        // Signo (±1 por eje) de la esquina marcada como "A" de la diagonal. La
        // esquina "B" es el opuesto (-A). Guardar el par exacto que el usuario
        // marco hace que las esferas caigan en esas esquinas (y no en la diagonal
        // (-,-,-)/(+,+,+) por defecto), incluso si la diagonal va "cruzada" en XZ.
        private Vector3 _cornerSignA = Vector3.one;

        public const float DefaultSize = 0.3f;
        public const float MinSize     = 0.02f;

        public static CubeObject Create(Vector3 posLocal, Quaternion rotLocal, Vector3 scaleLocal,
                                        string id = null, Vector3? cornerSignA = null)
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
            c._cornerSignA = cornerSignA ?? Vector3.one;
            c.EnsureMaterials();
            c._mr.sharedMaterial = c._matNormal;

            c.SpawnVertexHandles();
            SceneRegistry.Instance?.Register(c);
            return c;
        }

        // Crea un cubo a partir de los dos vertices de su diagonal (anchor-local).
        // El cubo resultante queda axis-aligned en anchor space (rotacion identidad);
        // luego se puede rotar/escalar/mover con el gizmo o arrastrar sus esquinas.
        public static CubeObject CreateFromDiagonal(Vector3 cornerALocal, Vector3 cornerBLocal, string id = null)
        {
            var center = (cornerALocal + cornerBLocal) * 0.5f;
            var diff   = cornerBLocal - cornerALocal;
            var size = new Vector3(
                Mathf.Max(MinSize, Mathf.Abs(diff.x)),
                Mathf.Max(MinSize, Mathf.Abs(diff.y)),
                Mathf.Max(MinSize, Mathf.Abs(diff.z)));
            // Signo de la esquina A respecto al centro: asi las esferas quedan
            // exactamente sobre los dos puntos marcados (la diagonal real, no la
            // (-,-,-)/(+,+,+) por defecto).
            var signA = new Vector3(
                cornerALocal.x >= center.x ? 1f : -1f,
                cornerALocal.y >= center.y ? 1f : -1f,
                cornerALocal.z >= center.z ? 1f : -1f);
            return Create(center, Quaternion.identity, size, id, signA);
        }

        public static CubeObject FromData(CubeData d)
        {
            // Escaneos viejos no tienen cornerSignA (null o (0,0,0)) => default (1,1,1).
            Vector3? sign = null;
            if (d.cornerSignA != null)
            {
                var v = d.cornerSignA.ToVector3();
                if (v.sqrMagnitude > 0.01f) sign = v;
            }
            return Create(d.posLocal.ToVector3(), d.rotLocal.ToQuaternion(), d.scaleLocal.ToVector3(), d.id, sign);
        }

        private void SpawnVertexHandles()
        {
            var signs = new[] { _cornerSignA, -_cornerSignA };
            for (int i = 0; i < 2; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"CubeVertex_{i}";
                var h = go.AddComponent<CubeVertexHandle>();
                h.Init(this, signs[i], VertexHandleRadius);
                _handles[i] = h;
            }
        }

        // Posicion world de una esquina, dado su signo (±1 por eje).
        public Vector3 GetDiagonalCornerWorld(Vector3 cornerSign)
        {
            return transform.TransformPoint(0.5f * cornerSign);
        }

        // Reforma el cubo cuando se arrastra un vertice: el corner opuesto queda
        // fijo en el mundo y el arrastrado va a newWorldPos. Mantiene la rotacion.
        public void SetDiagonalCornerFromHandle(Vector3 draggedCornerSign, Vector3 newWorldPos)
        {
            // Corner opuesto (fijo) ANTES de tocar el transform.
            Vector3 oppWorld = transform.TransformPoint(-0.5f * draggedCornerSign);
            Quaternion q = transform.rotation;

            // Diagonal en el frame rotado del cubo => componentes = tamano por eje.
            Vector3 diagLocal = Quaternion.Inverse(q) * (newWorldPos - oppWorld);
            var size = new Vector3(
                Mathf.Max(MinSize, Mathf.Abs(diagLocal.x)),
                Mathf.Max(MinSize, Mathf.Abs(diagLocal.y)),
                Mathf.Max(MinSize, Mathf.Abs(diagLocal.z)));

            transform.position   = (oppWorld + newWorldPos) * 0.5f;
            transform.localScale = size;
            // rotacion sin cambios

            // Resincronizar SOLO el handle opuesto (el arrastrado lo maneja el gizmo).
            foreach (var h in _handles)
                if (h != null && h.CornerSign != draggedCornerSign) h.SnapToCorner();
        }

        public CubeData ToData()
        {
            return new CubeData
            {
                id          = Id,
                posLocal    = new Vec3(transform.localPosition),
                rotLocal    = new Quat(transform.localRotation),
                scaleLocal  = new Vec3(transform.localScale),
                cornerSignA = new Vec3(_cornerSignA),
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
                // Preferimos el shader de aristas+grid; si no esta, caemos a Unlit/Color.
                var sh = Shader.Find("Custom/EdgeGrid") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                bool edgeGrid = sh != null && sh.name == "Custom/EdgeGrid";
                _matNormal = new Material(sh) { name = "CubeMat (runtime)" };
                var col = edgeGrid ? new Color(0.6f, 0.8f, 1f, 0.13f) : new Color(0.6f, 0.8f, 1f, 0.8f);
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
            DestroyHandles();
            Destroy(gameObject);
        }

        private void DestroyHandles()
        {
            for (int i = 0; i < _handles.Length; i++)
                if (_handles[i] != null) Destroy(_handles[i].gameObject);
        }

        private void OnDestroy()
        {
            // Solo destruir materiales runtime, no los asignados del Inspector.
            if (_matNormal   != null && _matNormal.name.Contains("(runtime)"))   Destroy(_matNormal);
            if (_matSelected != null && _matSelected.name.Contains("(runtime)")) Destroy(_matSelected);
            DestroyHandles();
        }
    }
}
