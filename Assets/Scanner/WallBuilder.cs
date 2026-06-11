using UnityEngine;

namespace Scanner
{
    // Maneja el flujo de colocacion de paredes con polilinea + height-anchor.
    //
    // Flujo nuevo:
    //   1) Wall_V1     -> Place: marca primer punto de piso (V1) y guarda como ultimo.
    //   2) Wall_Height -> Place: marca un punto "arriba" para definir H = max(0.1, Vy - V1y).
    //   3) Wall_Vn     -> Place: marca proximo punto de piso (Vn). Crea pared
    //                    de Vlast -> Vn con altura H. Vlast = Vn.
    //
    // "Terminar polilinea" corta la cadena (vuelve a Idle).
    public class WallBuilder : MonoBehaviour
    {
        [Header("Defaults")]
        [Tooltip("Altura inicial sugerida (m). Se sobreescribe con el segundo vertice del polilinea.")]
        [SerializeField] private float _defaultHeight = 2.5f;
        [Tooltip("Grosor inicial de la pared-caja (m). Ajustable con el slider de ancho.")]
        [SerializeField] private float _defaultWidth = 0.15f;

        [Header("Materiales (opcional, asignar desde el Inspector)")]
        [Tooltip("Material aplicado a las paredes en estado normal. Si esta vacio se crea uno semitransparente en runtime.")]
        [SerializeField] private Material _wallMaterial;
        [Tooltip("Material aplicado cuando la pared esta seleccionada. Opcional.")]
        [SerializeField] private Material _wallSelectedMaterial;

        // Statics que WallObject lee al crear/cargar paredes.
        public static Material ConfiguredNormalMat   { get; private set; }
        public static Material ConfiguredSelectedMat { get; private set; }

        [Header("Preview de polilinea")]
        [Tooltip("Radio (m) de las esferas que se muestran en V1 (piso) y V2 (techo) durante la polilinea.")]
        [SerializeField] private float _previewSphereRadius = 0.05f;
        [Tooltip("Color de la esfera de V1 (piso).")]
        [SerializeField] private Color _previewFloorColor   = new Color(0.2f, 1f, 0.4f, 1f);
        [Tooltip("Color de la esfera de V2 (techo).")]
        [SerializeField] private Color _previewCeilingColor = new Color(1f, 0.6f, 0.2f, 1f);

        private Vector3? _lastFloorLocal;
        private Vector3? _firstFloorLocal;
        private WallObject _lastWall;   // ultima pared creada en la polilinea actual
        private float    _polylineHeight;
        private float    _polylineWidth;
        private int?     _polylineSide;  // null hasta crear el primer segmento
        private string   _currentPolylineId;
        private ScanStateMachine _fsm;
        private Camera   _camera;
        private readonly System.Collections.Generic.List<GameObject> _previewSpheres = new();

        public float CurrentPolylineHeight => _polylineHeight;
        public float CurrentPolylineWidth  => _polylineWidth;

        // Primer punto de piso de la polilinea (para el preview de altura).
        public Vector3? FirstFloor => _firstFloorLocal;

        // Devuelve los datos de la pared que se crearia AHORA si se colocara el
        // proximo vertice en endLocal (mismo calculo que PlaceVertex/Wall_Vn). Lo
        // usa PlacementPreview para el fantasma en vivo del segmento. Devuelve false
        // si no estamos en Wall_Vn o no hay vertice de arranque.
        public bool TryGetWallPreview(Vector3 endLocal, out Vector3 aLocal, out Vector3 bLocal,
                                      out float height, out float width, out Vector3 normal)
        {
            aLocal = bLocal = Vector3.zero;
            height = width = 0f;
            normal = Vector3.forward;
            if (_fsm.Current != ScannerMode.Wall_Vn || !_lastFloorLocal.HasValue) return false;

            aLocal = _lastWall != null ? _lastWall.BLocal : _lastFloorLocal.Value;
            bLocal = endLocal;
            var baseVec = bLocal - aLocal;
            if (baseVec.sqrMagnitude < 1e-6f) return false;

            height = _polylineHeight;
            width  = _polylineWidth;
            int side = _polylineSide ?? DecideSide(aLocal, bLocal);
            var baseHat = baseVec.normalized;
            var n = Vector3.Cross(Vector3.up, baseHat);
            if (n.sqrMagnitude < 1e-6f) n = Vector3.right;
            normal = (side >= 0 ? 1 : -1) * n.normalized;
            return true;
        }

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            _camera = Camera.main;
            ConfiguredNormalMat   = _wallMaterial;
            ConfiguredSelectedMat = _wallSelectedMaterial;
        }

        public void StartPolyline()
        {
            _firstFloorLocal   = null;
            _lastFloorLocal    = null;
            _lastWall          = null;
            _polylineHeight    = _defaultHeight;
            _polylineWidth     = _defaultWidth;
            _polylineSide      = null;
            _currentPolylineId = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            ClearPreviewSpheres();
            _fsm.SetMode(ScannerMode.Wall_V1);
        }

        // Cambia el grosor de la polilinea en curso y lo propaga a las paredes ya
        // creadas (mismo patron que la altura).
        public void SetPolylineWidth(float width)
        {
            _polylineWidth = Mathf.Max(0.02f, width);
            var registry = SceneRegistry.Instance;
            if (registry == null) return;
            foreach (var w in registry.Walls)
                if (w != null && w.PolylineId == _currentPolylineId)
                    w.SetWidth(_polylineWidth);
        }

        public void EndPolyline()
        {
            _firstFloorLocal = null;
            _lastFloorLocal  = null;
            _lastWall        = null;
            ClearPreviewSpheres();
            if (_fsm.Current == ScannerMode.Wall_V1
             || _fsm.Current == ScannerMode.Wall_Height
             || _fsm.Current == ScannerMode.Wall_Vn)
                _fsm.SetMode(ScannerMode.Idle);
        }

        private GameObject SpawnPreviewSphere(Vector3 anchorLocal, Color color, PolylinePreviewHandle.PreviewKind kind)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"PolylinePreview_{kind}";
            // El SphereCollider se mantiene para ser tappeable.
            var handle = go.AddComponent<PolylinePreviewHandle>();
            handle.Init(this, kind, anchorLocal, color, _previewSphereRadius);
            _previewSpheres.Add(go);
            return go;
        }

        // Llamado por PolylinePreviewHandle cuando el usuario mueve la esfera V1.
        public void UpdateV1Floor(Vector3 newLocal)
        {
            var oldFirst = _firstFloorLocal;
            _firstFloorLocal = newLocal;
            // Si _lastFloorLocal todavia apuntaba a la posicion original de V1
            // (estamos antes de crear la primer pared), tambien la actualizamos.
            if (_lastFloorLocal.HasValue && oldFirst.HasValue
                && (_lastFloorLocal.Value - oldFirst.Value).sqrMagnitude < 1e-6f)
                _lastFloorLocal = newLocal;
        }

        // Llamado por PolylinePreviewHandle cuando el usuario mueve la esfera V2 (techo).
        public void UpdateCeiling(Vector3 newLocal)
        {
            if (!_firstFloorLocal.HasValue) return;
            float h = Mathf.Abs(newLocal.y - _firstFloorLocal.Value.y);
            _polylineHeight = Mathf.Max(0.1f, h);
            // Propagar a paredes ya creadas en esta polilinea.
            var registry = SceneRegistry.Instance;
            if (registry == null) return;
            foreach (var w in registry.Walls)
                if (w != null && w.PolylineId == _currentPolylineId)
                    w.SetHeight(_polylineHeight);
        }

        private void ClearPreviewSpheres()
        {
            // Si la seleccion actual es una preview, deseleccionar primero para
            // que el gizmo se detenga limpio.
            var fsm = ScanStateMachine.Instance;
            if (fsm != null && fsm.CurrentSelection is PolylinePreviewHandle)
                fsm.ClearSelection();

            foreach (var s in _previewSpheres)
            {
                if (s == null) continue;
                Destroy(s);
            }
            _previewSpheres.Clear();
        }

        // Destruye la preview de V1 (queda redundante con el handle A de la primera pared).
        private void RemoveV1Preview()
        {
            for (int i = _previewSpheres.Count - 1; i >= 0; i--)
            {
                var s = _previewSpheres[i];
                if (s == null) { _previewSpheres.RemoveAt(i); continue; }
                var h = s.GetComponent<PolylinePreviewHandle>();
                if (h != null && h.Type == PolylinePreviewHandle.PreviewKind.V1Floor)
                {
                    Destroy(s);
                    _previewSpheres.RemoveAt(i);
                }
            }
        }

        public void PlaceVertexAtCurrentReticle()
        {
            if (WorldOrigin.Instance == null)
            {
                Debug.LogWarning("[WallBuilder] WorldOrigin aun no esta listo. Calibrar primero.");
                return;
            }
            var hit = RaycastResolver.Instance?.ResolveFromScreenCenter() ?? ResolvedHit.Miss;
            if (!hit.Hit) return;
            PlaceVertex(WorldOrigin.Instance.ToRelative(hit.Position));
        }

        public void PlaceVertex(Vector3 anchorLocal)
        {
            switch (_fsm.Current)
            {
                case ScannerMode.Wall_V1:
                {
                    _firstFloorLocal = anchorLocal;
                    _lastFloorLocal  = anchorLocal;
                    SpawnPreviewSphere(anchorLocal, _previewFloorColor, PolylinePreviewHandle.PreviewKind.V1Floor);
                    _fsm.SetMode(ScannerMode.Wall_Height);
                    return;
                }
                case ScannerMode.Wall_Height:
                {
                    float h = Mathf.Abs(anchorLocal.y - (_firstFloorLocal?.y ?? anchorLocal.y));
                    _polylineHeight = Mathf.Max(0.1f, h);
                    SpawnPreviewSphere(anchorLocal, _previewCeilingColor, PolylinePreviewHandle.PreviewKind.Ceiling);
                    _fsm.SetMode(ScannerMode.Wall_Vn);
                    return;
                }
                case ScannerMode.Wall_Vn:
                {
                    if (_lastFloorLocal.HasValue)
                    {
                        // Arrancamos desde la posicion ACTUAL del ultimo vertice. Si el
                        // usuario arrastro manualmente el handle B de la pared anterior,
                        // _lastWall.BLocal ya refleja ese cambio (mientras que el cacheado
                        // _lastFloorLocal seguiria con la posicion original). Asi el handle
                        // A de la nueva pared queda pegado al vertice editado.
                        Vector3 startLocal = _lastWall != null ? _lastWall.BLocal : _lastFloorLocal.Value;
                        // El lado de extrusion se decide UNA vez (primer segmento) para
                        // que toda la polilinea tenga grosor del mismo lado. Se elige el
                        // signo que apunta la normal lejos de la camara (cara cercana
                        // hacia el jugador).
                        if (!_polylineSide.HasValue)
                            _polylineSide = DecideSide(startLocal, anchorLocal);
                        var w = WallObject.Create(startLocal, anchorLocal, _polylineHeight,
                                                  _polylineWidth, _polylineSide.Value);
                        w.PolylineId = _currentPolylineId;
                        // Primer wall: la preview de V1 ya esta cubierta por el handle A de este wall.
                        RemoveV1Preview();
                        _lastFloorLocal = anchorLocal;
                        _lastWall       = w;
                    }
                    return;
                }
            }
        }

        // Elige el lado (+1/-1 sobre cross(up, baseHat)) tal que la normal de grosor
        // apunte alejandose de la camara: asi la cara cercana (los puntos colocados)
        // queda del lado del jugador y la pared crece "hacia adentro".
        private int DecideSide(Vector3 aLocal, Vector3 bLocal)
        {
            var baseVec = bLocal - aLocal;
            if (baseVec.sqrMagnitude < 1e-6f) return 1;
            var baseHat = baseVec.normalized;
            var n0 = Vector3.Cross(Vector3.up, baseHat);
            if (n0.sqrMagnitude < 1e-6f) return 1;
            n0.Normalize();

            if (_camera == null) _camera = Camera.main;
            var wo = WorldOrigin.Instance;
            if (_camera == null || wo == null) return 1;

            var camLocal = wo.ToRelative(_camera.transform.position);
            var mid      = (aLocal + bLocal) * 0.5f;
            float dot    = Vector3.Dot(n0, mid - camLocal);
            // dot>0 => n0 ya apunta lejos de la camara => side=+1; si no, -1.
            return dot >= 0f ? 1 : -1;
        }

        private void OnDestroy() => ClearPreviewSpheres();
    }
}
