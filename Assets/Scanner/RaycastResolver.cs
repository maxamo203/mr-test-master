using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Scanner
{
    public enum RaycastSource { None, LidarMesh, ArPlane, ArDepth, ArFeaturePoint, Fallback }

    public struct ResolvedHit
    {
        public bool Hit;
        public Vector3 Position;
        public Vector3 Normal;
        public RaycastSource Source;
        public static ResolvedHit Miss => new ResolvedHit { Hit = false, Source = RaycastSource.None };
    }

    // Cascada de raycast cross-platform. Devuelve el primer hit valido.
    // Orden:
    //   1) Physics contra layer LiDARMesh (iOS Pro con LiDAR — MeshColliders en chunks).
    //   2) ARRaycastManager con PlaneWithinPolygon | Depth | FeaturePoint
    //      (ARCore Depth API + ARKit planos).
    //   3) Fallback: punto sobre el rayo de la camara a una distancia configurable.
    public class RaycastResolver : MonoBehaviour
    {
        public static RaycastResolver Instance { get; private set; }

        [SerializeField] private Camera _arCamera;
        [SerializeField] private ARRaycastManager _arRaycast;
        [Tooltip("Distancia en metros para el fallback cuando no hay otro hit.")]
        [Range(0.3f, 5f)]
        [SerializeField] private float _fallbackDistance = 2f;
        [Tooltip("Distancia maxima del raycast contra MeshColliders LiDAR.")]
        [SerializeField] private float _lidarMaxDistance = 10f;

        public float FallbackDistance
        {
            get => _fallbackDistance;
            set => _fallbackDistance = Mathf.Clamp(value, 0.3f, 5f);
        }

        private int _lidarLayerMask;
        private static readonly List<ARRaycastHit> _arHits = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            if (_arCamera == null) _arCamera = Camera.main;
            if (_arRaycast == null) _arRaycast = FindFirstObjectByType<ARRaycastManager>();

            int layer = LayerMask.NameToLayer(LiDARScanner.LiDARMeshLayerName);
            _lidarLayerMask = layer >= 0 ? (1 << layer) : 0;
        }

        // Resuelve el punto del centro de la pantalla.
        public ResolvedHit ResolveFromScreenCenter()
        {
            var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            return ResolveFromScreenPoint(screenCenter);
        }

        public ResolvedHit ResolveFromScreenPoint(Vector2 screenPoint)
        {
            if (_arCamera == null) return ResolvedHit.Miss;
            var ray = _arCamera.ScreenPointToRay(screenPoint);

            // 1) Physics contra mesh LiDAR.
            if (_lidarLayerMask != 0 && Physics.Raycast(ray, out var phHit, _lidarMaxDistance, _lidarLayerMask))
            {
                return new ResolvedHit
                {
                    Hit      = true,
                    Position = phHit.point,
                    Normal   = phHit.normal,
                    Source   = RaycastSource.LidarMesh,
                };
            }

            // 2) ARRaycastManager — planes + depth + feature points.
            if (_arRaycast != null)
            {
                _arHits.Clear();
                var flags = TrackableType.PlaneWithinPolygon | TrackableType.Depth | TrackableType.FeaturePoint;
                if (_arRaycast.Raycast(screenPoint, _arHits, flags) && _arHits.Count > 0)
                {
                    // Tomamos el hit mas cercano que ya viene ordenado por ARFoundation.
                    var h = _arHits[0];
                    var src = RaycastSource.ArPlane;
                    if ((h.hitType & TrackableType.Depth)        != 0) src = RaycastSource.ArDepth;
                    else if ((h.hitType & TrackableType.FeaturePoint) != 0) src = RaycastSource.ArFeaturePoint;

                    var normal = h.pose.up; // aprox; PlaneWithinPolygon da pose.up correcto, depth no
                    return new ResolvedHit
                    {
                        Hit      = true,
                        Position = h.pose.position,
                        Normal   = normal,
                        Source   = src,
                    };
                }
            }

            // 3) Fallback: sobre el rayo a la distancia configurada.
            return new ResolvedHit
            {
                Hit      = true,
                Position = ray.origin + ray.direction * _fallbackDistance,
                Normal   = -ray.direction,
                Source   = RaycastSource.Fallback,
            };
        }
    }
}
