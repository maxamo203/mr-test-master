using UnityEngine;

namespace Scanner
{
    // Coloca marcadores (puntos de interes) sobre una pared. NO modifica la
    // geometria: solo agrega un punto que identifica que ahi hay una puerta/ventana.
    //
    // Flujo: se elige el tipo en el submenu (StartMarker) -> modo Marker_Place ->
    // se apunta a una pared con la reticula -> COLOCAR. La pared bajo la reticula se
    // detecta por raycast al layer "Placed"; el punto se proyecta a la cara de la
    // pared (u,v), asi queda pegado a ella y la distancia cruda del AR no importa.
    public class MarkerBuilder : MonoBehaviour
    {
        [Header("Catalogo de tipos de marcador (asset MarkerCatalog)")]
        [Tooltip("Asset con la lista de tipos disponibles. Se edita desde el editor; " +
                 "para sumar un tipo nuevo se crea un MarkerType y se agrega a este catalogo.")]
        [SerializeField] private MarkerCatalog _catalog;

        public MarkerCatalog Catalog => _catalog;

        private ScanStateMachine _fsm;
        private Camera _camera;
        private int _placedLayerMask;

        // Tipo pendiente elegido en el submenu, leido por PlacementPreview para el
        // color del fantasma en vivo.
        public MarkerType PendingType { get; private set; }

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            _camera = Camera.main;
            int placedLayer = LayerMask.NameToLayer("Placed");
            _placedLayerMask = placedLayer >= 0 ? (1 << placedLayer) : ~0;

            // Publicamos el catalogo para que MarkerObject.FromData resuelva por id.
            if (_catalog != null) MarkerCatalog.Active = _catalog;
            else Debug.LogWarning("[MarkerBuilder] Sin MarkerCatalog asignado: no se podran colocar ni cargar marcadores.");

            if (PendingType == null) PendingType = _catalog != null ? _catalog.First : null;
        }

        public void StartMarker(MarkerType type)
        {
            if (type == null) return;
            PendingType = type;
            _fsm.SetMode(ScannerMode.Marker_Place);
        }

        // Detecta la pared bajo el centro de pantalla y proyecta el hit a (u,v) de
        // su cara (clampeado a los limites). faceSign indica de que lado de la pared
        // se golpeo — es decir, el lado desde el que la estas mirando (+1 = +Normal,
        // -1 = -Normal). Compartido por la colocacion y el preview en vivo. Devuelve
        // false si la reticula no apunta a una pared.
        public bool TryResolveOnWall(out WallObject wall, out float u, out float v, out int faceSign)
        {
            wall = null; u = 0f; v = 0f; faceSign = -1;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || WorldOrigin.Instance == null) return false;

            var ray = _camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            if (!Physics.Raycast(ray, out var hit, 50f, _placedLayerMask)) return false;

            wall = hit.collider.GetComponentInParent<WallObject>();
            if (wall == null) return false;
            if (!wall.WorldPointToWallUV(hit.point, out u, out v)) { wall = null; return false; }

            u = Mathf.Clamp(u, 0f, wall.Length);
            v = Mathf.Clamp(v, 0f, wall.Height);

            // Lado impactado: w = distancia sobre la normal desde la cara w=0. Si esta
            // mas cerca de Width (cara +Normal) => +1; si esta cerca de 0 => -1.
            var pLocal = WorldOrigin.Instance.ToRelative(hit.point);
            float w = Vector3.Dot(pLocal - wall.ALocal, wall.Normal);
            faceSign = (w > wall.Width * 0.5f) ? 1 : -1;
            return true;
        }

        // Resuelve (u,v) sobre UNA pared concreta (la del marcador que se mueve),
        // usando el mismo raycast fisico que la colocacion. Asi el punto cae sobre la
        // superficie real de la pared y no queda corrido (el raycast AR de
        // RaycastResolver devuelve puntos que NO estan sobre la pared). No cambia de
        // pared ni de cara: solo actualiza (u,v). Devuelve false si la reticula no
        // apunta a esa misma pared.
        public bool TryResolveOnSpecificWall(WallObject targetWall, out float u, out float v)
        {
            u = 0f; v = 0f;
            if (targetWall == null) return false;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return false;

            var ray = _camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            if (!Physics.Raycast(ray, out var hit, 50f, _placedLayerMask)) return false;
            if (hit.collider.GetComponentInParent<WallObject>() != targetWall) return false;
            if (!targetWall.WorldPointToWallUV(hit.point, out u, out v)) return false;

            u = Mathf.Clamp(u, 0f, targetWall.Length);
            v = Mathf.Clamp(v, 0f, targetWall.Height);
            return true;
        }

        // Llamado por el boton "COLOCAR" cuando el FSM esta en Marker_Place.
        public void PlaceAtCurrentReticle()
        {
            if (_fsm.Current != ScannerMode.Marker_Place) return;
            if (PendingType == null)
            {
                Debug.LogWarning("[MarkerBuilder] Sin tipo seleccionado (catalogo vacio?).");
                return;
            }
            if (!TryResolveOnWall(out var wall, out var u, out var v, out var faceSign))
            {
                Debug.Log("[MarkerBuilder] La reticula no apunta a una pared; no se coloca marcador.");
                return;
            }
            MarkerObject.Create(PendingType, wall, u, v, faceSign);
            _fsm.SetMode(ScannerMode.Idle);
        }
    }
}
