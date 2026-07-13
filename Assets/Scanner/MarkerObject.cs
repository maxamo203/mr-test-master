using UnityEngine;

namespace Scanner
{
    // Marcador (punto de interes) colocado SOBRE una pared: identifica que en ese
    // punto hay una puerta / ventana / etc, SIN modificar la geometria de la pared.
    //
    // Es hijo de WorldOrigin (coords anchor-relativas). Su posicion NO se guarda
    // libre: es siempre relativa a una WallObject (wallId + u,v + side, sobre la cara
    // de la pared que estabas mirando). La pared, en su Rebuild(), re-sincroniza sus
    // marcadores, asi el punto sigue a la pared si se edita/mueve y nunca se ve
    // despegado de ella. La rotacion (flecha) apunta hacia afuera de esa cara.
    // Cada cara es un marcador distinto (podes tener uno de cada lado).
    public class MarkerObject : MonoBehaviour, ISelectable
    {
        public string Id { get; private set; }
        // Tipo del marcador (asset MarkerType, definido desde el editor).
        public MarkerType Type { get; private set; }
        public WallObject Wall => _wall;
        public float U => _u;
        public float V => _v;
        public int FaceSign => _faceSign;

        public SelectableKind Kind => SelectableKind.Marker;
        public Transform Transform => transform;

        private WallObject _wall;
        private float _u, _v;
        // +1 = cara +Normal, -1 = cara -Normal (el lado desde el que lo mirabas).
        private int _faceSign = -1;

        // Cuanto se separa la esfera de la cara (para que no quede semi-enterrada).
        private const float SurfaceOffset = 0.03f;

        private MeshRenderer _sphereMr;
        private MeshRenderer _arrowMr;
        private Material _matNormal;
        private Material _matSelected;

        private const float SphereDiameter = 0.10f;
        private const float ArrowLength     = 0.18f;
        private const float ArrowThickness  = 0.02f;

        // Crea el marcador asociado a una pared, en (u,v) de la cara indicada por
        // faceSign (+1 = lado +Normal, -1 = lado -Normal).
        public static MarkerObject Create(MarkerType type, WallObject wall, float u, float v,
                                          int faceSign, string id = null)
        {
            if (wall == null || WorldOrigin.Instance == null) return null;
            // Fuera de DisplayOnly (scanner) el tipo es obligatorio (define color/visual).
            // En DisplayOnly (multijugador) el marcador es invisible y el tipo es
            // indistinto (solo punto de spawn), asi que se admite type == null.
            if (type == null && !ScanLoader.DisplayOnly)
            {
                Debug.LogWarning("[MarkerObject] Sin MarkerType (catalogo no asignado?). Se descarta.");
                return null;
            }

            var root = new GameObject("Marker");
            root.transform.SetParent(WorldOrigin.Instance.transform, worldPositionStays: false);
            int placedLayer = LayerMask.NameToLayer("Placed");
            if (placedLayer >= 0) root.layer = placedLayer;

            var m = root.AddComponent<MarkerObject>();
            m.Id    = id ?? System.Guid.NewGuid().ToString("N").Substring(0, 8);
            m.Type  = type;
            m._wall = wall;
            m._u = u;
            m._v = v;
            m._faceSign = faceSign >= 0 ? 1 : -1;

            // En carga display-only (gameplay multijugador) el marcador es INVISIBLE:
            // no se le crea la esfera/flecha ni materiales, pero SI existe como punto
            // (transform + pared + pose) para que el server lo use como spawn del Sorken.
            bool visible = !ScanLoader.DisplayOnly;
            if (visible) m.BuildVisual();
            wall.AddMarker(m);
            m.SyncToWall();               // posiciona el transform aunque no haya visual
            if (visible) m.ApplyColor(false);
            SceneRegistry.Instance?.Register(m);
            return m;
        }

        public static MarkerObject FromData(MarkerData d)
        {
            var wall = SceneRegistry.Instance?.FindWall(d.wallId);
            if (wall == null)
            {
                Debug.LogWarning($"[MarkerObject] No se encontro la pared '{d.wallId}' para el marcador '{d.id}'. Se descarta.");
                return null;
            }
            // Resolvemos el tipo por id contra el catalogo activo. Si el id no existe
            // (catalogo cambiado), caemos al primer tipo para no perder el marcador.
            var catalog = MarkerCatalog.Active;
            var type = catalog?.GetById(d.kind) ?? catalog?.First;
            // En DisplayOnly (multijugador) NO hay MarkerBuilder que publique el catalogo,
            // asi que type puede quedar null: igual creamos el marcador (invisible) como
            // punto de spawn. En el scanner el tipo es obligatorio.
            if (type == null && !ScanLoader.DisplayOnly)
            {
                Debug.LogWarning($"[MarkerObject] Tipo '{d.kind}' no esta en el catalogo (marcador '{d.id}'). Se descarta.");
                return null;
            }
            return Create(type, wall, d.u, d.v, d.side, d.id);
        }

        public MarkerData ToData() => new MarkerData
        {
            id     = Id,
            kind   = Type != null ? Type.Id : null,
            wallId = _wall != null ? _wall.Id : null,
            u      = _u,
            v      = _v,
            side   = _faceSign,
        };

        // Posicion (anchor-local) de la esfera sobre la cara faceSign de la pared:
        // el punto (u,v) sobre la cara + un pequeno offset hacia afuera para que la
        // esfera no quede semi-enterrada. Compartida con el preview en vivo.
        public static Vector3 FacePosition(WallObject wall, float u, float v, int faceSign)
        {
            float w = faceSign >= 0 ? wall.Width : 0f;      // +Normal esta a w=Width
            Vector3 outward = (faceSign >= 0 ? 1f : -1f) * wall.Normal;
            return wall.ALocal + u * wall.BaseHat + v * Vector3.up
                 + w * wall.Normal + outward * SurfaceOffset;
        }

        private void BuildVisual()
        {
            // Esfera (el marcador en si). Picking por screen-space (SelectionController),
            // asi que no necesita collider.
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "MarkerSphere";
            var sc = sphere.GetComponent<Collider>();
            if (sc != null) Destroy(sc);
            sphere.transform.SetParent(transform, worldPositionStays: false);
            sphere.transform.localScale = Vector3.one * SphereDiameter;
            _sphereMr = sphere.GetComponent<MeshRenderer>();

            // Flecha = direccion normal a la pared (a lo largo del +Z local del marcador).
            // Cilindro (eje Y por defecto) rotado para quedar sobre +Z, corrido hacia afuera.
            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arrow.name = "MarkerNormal";
            var ac = arrow.GetComponent<Collider>();
            if (ac != null) Destroy(ac);
            arrow.transform.SetParent(transform, worldPositionStays: false);
            // El cilindro primitivo mide 2 uds de alto (Y): escala Y = mitad del largo.
            arrow.transform.localScale    = new Vector3(ArrowThickness, ArrowLength * 0.5f, ArrowThickness);
            arrow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Y -> Z
            arrow.transform.localPosition = new Vector3(0f, 0f, SphereDiameter * 0.5f + ArrowLength * 0.5f);
            _arrowMr = arrow.GetComponent<MeshRenderer>();

            EnsureMaterials();
        }

        // Recoloca el marcador segun la pared: posicion sobre la cara cercana (u,v,
        // clampeados a los limites de la pared) y rotacion segun la normal. Llamado
        // por WallObject.Rebuild() en cada cambio de la pared.
        public void SyncToWall()
        {
            if (_wall == null) return;
            _u = Mathf.Clamp(_u, 0f, _wall.Length);
            _v = Mathf.Clamp(_v, 0f, _wall.Height);

            transform.localPosition = FacePosition(_wall, _u, _v, _faceSign);

            // Flecha hacia afuera de la cara donde vive el marcador.
            var n = (_faceSign >= 0 ? 1f : -1f) * _wall.Normal;
            if (n.sqrMagnitude < 1e-6f) n = Vector3.forward;
            transform.localRotation = Quaternion.LookRotation(n.normalized, Vector3.up);
        }

        // Reubica el marcador dentro de su misma pared (u,v clampeados). No puede
        // salir de la pared; para moverlo a otra hay que borrarlo y crear otro.
        public void SetUV(float u, float v)
        {
            _u = u;
            _v = v;
            SyncToWall();
        }

        // Cambia el tipo (Puerta <-> Ventana <-> ...), refrescando el color.
        public void SetType(MarkerType type)
        {
            if (type == null) return;
            Type = type;
            bool selected = ScanStateMachine.Instance != null
                         && (object)ScanStateMachine.Instance.CurrentSelection == this;
            ApplyColor(selected);
        }

        private void EnsureMaterials()
        {
            var sh = Shader.Find("Custom/LitMarker") ?? Shader.Find("Unlit/Color");
            if (_matNormal == null)
                _matNormal = new Material(sh) { name = "MarkerMat (runtime)" };
            if (_matSelected == null)
                _matSelected = new Material(sh) { name = "MarkerMatSelected (runtime)" };
        }

        private static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_Color"))     m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        private void ApplyColor(bool selected)
        {
            EnsureMaterials();
            var baseCol = Type != null ? Type.Color : Color.white;
            SetColor(_matNormal, baseCol);
            SetColor(_matSelected, new Color(1f, 1f, 0.2f, 1f)); // amarillo = seleccionado
            var mat = selected ? _matSelected : _matNormal;
            if (_sphereMr != null) _sphereMr.sharedMaterial = mat;
            if (_arrowMr  != null) _arrowMr.sharedMaterial  = _matNormal; // la flecha mantiene el color del tipo
        }

        public void OnSelect()  => ApplyColor(true);
        public void OnDeselect() => ApplyColor(false);

        public void Delete()
        {
            if (ScanStateMachine.Instance != null
                && (object)ScanStateMachine.Instance.CurrentSelection == this)
                ScanStateMachine.Instance.ClearSelection();
            _wall?.RemoveMarker(this);
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
