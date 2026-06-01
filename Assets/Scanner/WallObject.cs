using System.Collections.Generic;
using UnityEngine;

namespace Scanner
{
    // Representa una pared en escena. Es hijo de WorldOrigin: los campos
    // aLocal / bLocal estan en coordenadas anchor-relativas, asi sobreviven
    // a recalibraciones del anchor (que reparentea WorldOrigin).
    //
    // El mesh es 2D en plano local XY; el GameObject se orienta para que
    // ese plano coincida con la pared real en el mundo:
    //   - origen del transform = aLocal (en anchor space)
    //   - eje X local = direccion horizontal de aLocal -> bLocal proyectada al plano XZ del anchor
    //   - eje Y local = +Y del anchor (vertical)
    //   - eje Z local = normal de la pared
    public class WallObject : MonoBehaviour, ISelectable
    {
        public string Id { get; private set; }
        public Vector3 ALocal { get; private set; }
        public Vector3 BLocal { get; private set; }
        public float Height { get; private set; } = 2.5f;
        public IReadOnlyList<DoorData> Doors => _doors;

        private readonly List<DoorData> _doors = new();
        private MeshFilter   _mf;
        private MeshRenderer _mr;
        private MeshCollider _mc;
        private Material     _matNormal;
        private Material     _matSelected;

        // Solo handles para piso: 0 = A (BL), 1 = B (BR). Los topes se computan
        // desde aLocal + up*H y bLocal + up*H — no son manipulables.
        private readonly WallVertexHandle[] _handles = new WallVertexHandle[2];
        private const float VertexHandleRadius = 0.05f;

        // ID compartido por todas las paredes generadas en una misma polilinea.
        // Usado para propagar cambios de altura del panel a toda la polilinea.
        public string PolylineId { get; set; }

        // Si el shader del material configurado fue stripeado en el build (tipico
        // en iOS) o si el material no tiene shader, lo detectamos y avisamos.
        // En ese caso devolvemos al fallback runtime para evitar render blanco/magenta.
        private void ValidateShader(Material mat, string label)
        {
            if (mat == null) return;
            if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
            {
                Debug.LogError(
                    $"[WallObject] Material '{mat.name}' ({label}) tiene shader invalido " +
                    $"('{(mat.shader != null ? mat.shader.name : "null")}'). " +
                    $"Probablemente fue stripeado en el build. " +
                    $"Agregalo a Edit > Project Settings > Graphics > Always Included Shaders. " +
                    $"Usando fallback runtime.");
                // Forzamos a usar el runtime: limpiar la referencia para que
                // el codigo de abajo construya uno nuevo.
                if (label == "normal")   _matNormal   = null;
                if (label == "selected") _matSelected = null;
            }
        }

        public SelectableKind Kind => SelectableKind.Wall;
        public Transform Transform => transform;

        // Factory: crea un GO con los componentes minimos, parentea a WorldOrigin,
        // y arranca con esos valores.
        public static WallObject Create(Vector3 aLocal, Vector3 bLocal, float height, string id = null)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            int placedLayer = LayerMask.NameToLayer("Placed");
            if (placedLayer >= 0) go.layer = placedLayer;

            var w = go.AddComponent<WallObject>();
            w._mf = go.AddComponent<MeshFilter>();
            w._mr = go.AddComponent<MeshRenderer>();
            w._mc = go.AddComponent<MeshCollider>();
            // Deshabilitar EnableMeshCleaning para que PhysX no tire warnings
            // cuando el mesh pasa por estados temporariamente degenerados
            // durante el drag de vertices.
            w._mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                                 | MeshColliderCookingOptions.WeldColocatedVertices;
            w.EnsureMaterials();
            w._mr.sharedMaterial = w._matNormal;

            w.Id     = id ?? System.Guid.NewGuid().ToString("N").Substring(0, 8);
            w.ALocal = aLocal;
            w.BLocal = bLocal;
            w.Height = Mathf.Max(0.1f, height);

            w.Rebuild();
            w.SpawnVertexHandles();
            SceneRegistry.Instance?.Register(w);
            return w;
        }

        private void SpawnVertexHandles()
        {
            for (int i = 0; i < 2; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"WallVertex_{i}";
                // El SphereCollider primitive viene activo; lo dejamos para hits de tap.
                var h = go.AddComponent<WallVertexHandle>();
                h.Init(this, i, VertexHandleRadius);
                _handles[i] = h;
            }
        }

        private void RefreshHandlePositions()
        {
            for (int i = 0; i < _handles.Length; i++)
                if (_handles[i] != null) _handles[i].SnapToCorner();
        }

        // Devuelve el corner de piso en world space. 0=A (BL), 1=B (BR).
        public Vector3 GetCornerWorld(int cornerIdx)
        {
            var wo = WorldOrigin.Instance;
            Vector3 local = cornerIdx == 0 ? ALocal : BLocal;
            return wo != null ? wo.ToWorld(local) : local;
        }

        public Vector3 GetCornerLocal(int cornerIdx) => cornerIdx == 0 ? ALocal : BLocal;

        // Setea el corner de piso directamente en anchor-local (sin tocar otros walls).
        // El WallVertexHandle hace la propagacion a paredes compartidas.
        public void SetCornerLocal(int cornerIdx, Vector3 local)
        {
            if (cornerIdx == 0) ALocal = local;
            else                BLocal = local;
            Rebuild();
            // Solo refrescamos el handle del OTRO corner (el del corner arrastrado
            // ya tiene transform.position = posicion deseada, no queremos pisarlo).
            int other = 1 - cornerIdx;
            if (_handles[other] != null) _handles[other].SnapToCorner();
        }

        // Cambia la altura propagando a todos los walls de la misma polilinea.
        public void SetHeightForPolyline(float h)
        {
            var newH = Mathf.Max(0.1f, h);
            if (string.IsNullOrEmpty(PolylineId))
            {
                SetHeight(newH);
                return;
            }
            var registry = SceneRegistry.Instance;
            if (registry == null) { SetHeight(newH); return; }
            foreach (var w in registry.Walls)
                if (w != null && w.PolylineId == PolylineId)
                    w.SetHeight(newH);
        }

        public static WallObject FromData(WallData d)
        {
            var w = Create(d.aLocal.ToVector3(), d.bLocal.ToVector3(), d.height, d.id);
            w.PolylineId = d.polylineId;
            if (d.doors != null)
                foreach (var door in d.doors) w._doors.Add(door);
            w.Rebuild();
            return w;
        }

        public WallData ToData()
        {
            return new WallData
            {
                id         = Id,
                polylineId = PolylineId,
                aLocal     = new Vec3(ALocal),
                bLocal     = new Vec3(BLocal),
                height     = Height,
                doors      = new List<DoorData>(_doors),
            };
        }

        public void SetEndpoints(Vector3 aLocal, Vector3 bLocal)
        {
            ALocal = aLocal;
            BLocal = bLocal;
            Rebuild();
            RefreshHandlePositions();
        }

        public void SetHeight(float h)
        {
            Height = Mathf.Max(0.1f, h);
            Rebuild();
            RefreshHandlePositions();
        }

        public void AddDoor(DoorData door)
        {
            _doors.Add(door);
            Rebuild();
        }

        public void ClearDoors()
        {
            _doors.Clear();
            Rebuild();
        }

        // Convierte un punto en world space a coordenadas locales del wall:
        // u = a lo largo del eje base (0..|b-a| proyectada al plano horizontal),
        // v = altura sobre la base (anchor Y - aLocal.y).
        // Util para proyectar V1/V2 de puerta a UV.
        public bool WorldPointToWallUV(Vector3 worldPoint, out float u, out float v)
        {
            u = 0f; v = 0f;
            var wo = WorldOrigin.Instance;
            if (wo == null) return false;

            var pLocal = wo.ToRelative(worldPoint);
            var dir   = BLocal - ALocal;
            float length = dir.magnitude;
            if (length < 0.0001f) return false;
            var baseHat = dir / length;

            var rel = pLocal - ALocal;
            u = Vector3.Dot(rel, baseHat);
            v = Vector3.Dot(rel, Vector3.up); // mismo eje vertical que el mesh
            return true;
        }

        public float Length => Vector3.Distance(ALocal, BLocal);

        public void Rebuild()
        {
            // El mesh se construye en anchor-space (vertices absolutos relativos
            // al WorldOrigin), entonces el transform local queda identity. Asi
            // todas las paredes consecutivas de una polilinea con el mismo H
            // tienen sus topes alineados exactamente.
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale    = Vector3.one;

            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mr == null) _mr = GetComponent<MeshRenderer>();
            if (_mc == null) _mc = GetComponent<MeshCollider>();

            if (_mf.sharedMesh != null) Destroy(_mf.sharedMesh);
            var mesh = WallMeshBuilder.Build(ALocal, BLocal, Height, _doors);

            // MeshCollider necesita unassign + reassign para refrescar.
            _mc.sharedMesh = null;
            if (mesh != null)
            {
                _mf.sharedMesh = mesh;
                _mc.sharedMesh = mesh;
            }
            else
            {
                // Pared degenerada (vertices demasiado cerca). Dejamos los slots
                // vacios — al separar los vertices se rebuild y reaparece.
                _mf.sharedMesh = null;
            }
        }

        private void EnsureMaterials()
        {
            // 1) Si el WallBuilder tiene materiales asignados desde el Inspector, los usamos.
            if (_matNormal == null && WallBuilder.ConfiguredNormalMat != null)
            {
                _matNormal = WallBuilder.ConfiguredNormalMat;
                ValidateShader(_matNormal, "normal");
            }
            if (_matSelected == null && WallBuilder.ConfiguredSelectedMat != null)
            {
                _matSelected = WallBuilder.ConfiguredSelectedMat;
                ValidateShader(_matSelected, "selected");
            }

            // 2) Si no hay seleccionado configurado, generamos uno tintado a partir
            //    del normal (o del default runtime).
            if (_matNormal == null)
            {
                var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                _matNormal = new Material(sh) { name = "WallMat (runtime)" };
                var col = new Color(0.85f, 0.85f, 0.95f, 0.85f);
                if (_matNormal.HasProperty("_Color"))     _matNormal.color = col;
                if (_matNormal.HasProperty("_BaseColor")) _matNormal.SetColor("_BaseColor", col);
                _matNormal.renderQueue = 3000;
            }
            if (_matSelected == null)
            {
                _matSelected = new Material(_matNormal) { name = "WallMatSelected (runtime)" };
                var col = new Color(0.2f, 0.9f, 1f, 0.85f);
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
            for (int i = 0; i < _handles.Length; i++)
                if (_handles[i] != null) Destroy(_handles[i].gameObject);
            Destroy(gameObject);
        }

        private void OnDestroy_Cleanup()
        {
            for (int i = 0; i < _handles.Length; i++)
                if (_handles[i] != null) Destroy(_handles[i].gameObject);
        }

        private void OnDestroy()
        {
            if (_mf != null && _mf.sharedMesh != null) Destroy(_mf.sharedMesh);
            // Solo destruimos materiales que generamos en runtime (los del Inspector
            // son assets compartidos).
            if (_matNormal   != null && _matNormal.name.Contains("(runtime)"))   Destroy(_matNormal);
            if (_matSelected != null && _matSelected.name.Contains("(runtime)")) Destroy(_matSelected);
            OnDestroy_Cleanup();
        }
    }
}
