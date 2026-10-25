using UnityEngine;

namespace Scanner
{
    // Coloca puertas (huecos rectangulares) sobre una pared. Sigue la logica de los
    // marcadores (MarkerBuilder): no hay que elegir la pared antes, la pared es la
    // que esta bajo la reticula y el punto se proyecta a ella (no al hit AR crudo).
    //
    // Flujo: StartDoor -> Door_V1 -> COLOCAR -> Door_V2 -> COLOCAR -> Idle.
    //   - Door_V1: la esquina de piso. Corre siempre por la ARISTA DE ABAJO de la pared
    //     apuntada (v = 0): se toma la pared que golpea el raycast fisico (layer
    //     "Placed") o, si el rayo no toca ninguna (apuntando al piso, a un hueco ya
    //     hecho), la arista de piso mas cercana al rayo.
    //   - Door_V2: la esquina opuesta. Libre dentro de la pared de V1: interseccion del
    //     rayo con el plano de esa pared, clampeada a sus limites. No se pierde aunque
    //     la reticula se salga de la pared.
    public class DoorBuilder : MonoBehaviour
    {
        private const float MinGap = 0.05f;   // tamano minimo de puerta en u/v (m), igual que DoorHandle
        // Cono (tangente del angulo) dentro del cual una arista de piso cuenta como
        // "apuntada" cuando el rayo no toca ninguna pared. ~20°.
        private const float MaxAristaTan = 0.36f;

        private WallObject _target;
        private float _u1;
        private bool _hayV1;
        // Ultima esquina libre valida: si el rayo queda paralelo a la pared (o le da la
        // espalda) el preview y COLOCAR usan la ultima en vez de desaparecer.
        private Vector2? _ultimaV2;
        private ScanStateMachine _fsm;
        private Camera _camera;
        private int _placedLayerMask;

        // Estado en-progreso, leido por PlacementPreview para el fantasma en vivo.
        public WallObject Target => _target;
        public float      U1     => _u1;

        private void Awake()
        {
            _fsm = ScanStateMachine.Instance;
            _camera = Camera.main;
            int placedLayer = LayerMask.NameToLayer("Placed");
            _placedLayerMask = placedLayer >= 0 ? (1 << placedLayer) : ~0;
        }

        public void StartDoor()
        {
            Limpiar();
            _fsm.SetMode(ScannerMode.Door_V1);
        }

        private void Limpiar()
        {
            _target = null;
            _hayV1 = false;
            _ultimaV2 = null;
        }

        private bool RayoCentro(out Ray ray)
        {
            ray = default;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || WorldOrigin.Instance == null) return false;
            ray = _camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            return true;
        }

        // Esquina de piso bajo la reticula: pared + posicion u sobre su arista de abajo
        // (clampeada a [0, Length]). Compartido por la colocacion y el preview en vivo.
        public bool TryResolveFloorCorner(out WallObject wall, out float u)
        {
            wall = null; u = 0f;
            if (!RayoCentro(out var ray)) return false;

            // 1) La pared que golpea el rayo. RaycastAll + la mas cercana que sea pared:
            //    los handles (vertices, puertas) y los cubos tambien estan en "Placed" y
            //    no deben tapar a la pared de atras.
            var hits = Physics.RaycastAll(ray, 50f, _placedLayerMask);
            float mejor = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.distance >= mejor) continue;
                var w = h.collider.GetComponentInParent<WallObject>();
                if (w == null || w.SoloReferencia) continue;
                if (!w.WorldPointToWallUV(h.point, out var hu, out _)) continue;
                mejor = h.distance;
                wall = w;
                u = Mathf.Clamp(hu, 0f, w.Length);
            }
            if (wall != null) return true;

            // 2) Sin pared bajo la reticula: la arista de piso mas cercana al rayo (en
            //    angulo), asi apuntar al piso junto a la pared o a traves de un hueco ya
            //    hecho sigue deslizando la esquina por la base.
            var registry = SceneRegistry.Instance;
            if (registry == null) return false;
            var wo = WorldOrigin.Instance;
            Vector3 o = wo.ToRelative(ray.origin);
            Vector3 d = wo.ToRelativeDir(ray.direction).normalized;
            float mejorTan = MaxAristaTan;
            foreach (var w in registry.Walls)
            {
                if (w == null || w.SoloReferencia) continue;
                float len = w.Length;
                if (len < 1e-4f) continue;
                CercanoRayoSegmento(o, d, w.ALocal, w.BLocal, out float t, out float s);
                if (t < 0.1f) continue; // detras (o encima) de la camara
                var pRayo = o + d * t;
                var pSeg  = Vector3.Lerp(w.ALocal, w.BLocal, s);
                float tan = Vector3.Distance(pRayo, pSeg) / t;
                if (tan >= mejorTan) continue;
                mejorTan = tan;
                wall = w;
                u = s * len;
            }
            return wall != null;
        }

        // Esquina libre sobre la pared de V1: (u,v) donde el rayo corta el plano de la
        // cara que mira a la camara, clampeado a la pared. Si el rayo no la corta,
        // devuelve la ultima esquina valida.
        public bool TryResolveFreeCorner(out float u, out float v)
        {
            u = 0f; v = 0f;
            if (_target == null || !RayoCentro(out var ray)) return false;

            var wo = WorldOrigin.Instance;
            Vector3 o = wo.ToRelative(ray.origin);
            Vector3 d = wo.ToRelativeDir(ray.direction).normalized;
            var n = _target.Normal;

            // Cara cercana (w=0) o lejana (w=Width): la que esta del lado de la camara.
            float wCam = Vector3.Dot(o - _target.ALocal, n);
            var p0 = _target.ALocal + (wCam > _target.Width * 0.5f ? _target.Width : 0f) * n;

            float den = Vector3.Dot(d, n);
            float t = Mathf.Abs(den) > 1e-4f ? Vector3.Dot(p0 - o, n) / den : -1f;
            if (t > 0f)
            {
                var rel = o + d * t - _target.ALocal;
                float hu = Vector3.Dot(rel, _target.BaseHat);
                float hv = rel.y;
                // Ancho minimo hacia el lado al que apunta, sin salirse de la pared.
                hu = hu >= _u1 ? Mathf.Max(hu, _u1 + MinGap) : Mathf.Min(hu, _u1 - MinGap);
                _ultimaV2 = new Vector2(Mathf.Clamp(hu, 0f, _target.Length),
                                        Mathf.Clamp(hv, MinGap, _target.Height));
            }

            if (!_ultimaV2.HasValue) return false;
            u = _ultimaV2.Value.x;
            v = _ultimaV2.Value.y;
            return true;
        }

        // Llamado por el boton "Colocar" cuando el FSM esta en Door_V1/Door_V2.
        public void PlaceCornerAtCurrentReticle()
        {
            if (_fsm.Current == ScannerMode.Door_V1)
            {
                if (!TryResolveFloorCorner(out var wall, out var u))
                {
                    Debug.Log("[DoorBuilder] La reticula no apunta a una pared; no se coloca la esquina.");
                    return;
                }
                _target = wall;
                _u1 = u;
                _hayV1 = true;
                _ultimaV2 = null;
                _fsm.SetMode(ScannerMode.Door_V2);
                return;
            }

            if (_fsm.Current != ScannerMode.Door_V2) return;
            if (_target == null || !_hayV1)
            {
                // La pared se borro en el medio (o estado roto): volver a la 1ra esquina.
                Limpiar();
                _fsm.SetMode(ScannerMode.Door_V1);
                return;
            }
            if (!TryResolveFreeCorner(out var u2, out var v2)) return;

            float uMin = Mathf.Min(_u1, u2);
            float uMax = Mathf.Max(_u1, u2);
            if (uMax - uMin >= MinGap * 0.99f && v2 >= MinGap * 0.99f)
            {
                _target.AddDoor(new DoorData
                {
                    id   = System.Guid.NewGuid().ToString("N").Substring(0, 8),
                    uMin = uMin, uMax = uMax,
                    // Las puertas arrancan siempre del piso (base de la pared).
                    vMin = 0f,   vMax = v2,
                });
            }

            Limpiar();
            _fsm.SetMode(ScannerMode.Idle);
        }

        // Puntos mas cercanos entre el rayo o + t*d (d unitario, t >= 0) y el segmento
        // a..b (s en [0,1]).
        private static void CercanoRayoSegmento(Vector3 o, Vector3 d, Vector3 a, Vector3 b,
                                                out float t, out float s)
        {
            var e = b - a;
            var r = o - a;
            float ee = Vector3.Dot(e, e);
            float de = Vector3.Dot(d, e);
            float dr = Vector3.Dot(d, r);
            float er = Vector3.Dot(e, r);
            float den = ee - de * de; // |d|=1

            s = den > 1e-6f ? Mathf.Clamp01((er - de * dr) / den) : 0f;
            t = Vector3.Dot(a + e * s - o, d);
            if (t < 0f)
            {
                // El punto del rayo quedaria detras: fijar t=0 y reproyectar al segmento.
                t = 0f;
                s = Mathf.Clamp01(er / ee);
                return;
            }
            // Con t fijado, re-optimizar s (el clamp de s pudo moverlo).
            s = Mathf.Clamp01(Vector3.Dot(o + d * t - a, e) / ee);
        }
    }
}
