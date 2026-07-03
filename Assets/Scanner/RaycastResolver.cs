using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Scanner
{
    public enum RaycastSource { None, Lidar, ArPlane, ArDepth, ArFeaturePoint, Fallback }

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
    //   1) LiDAR nativo (ARKit directo via NativeLidar, iPhone/iPad Pro):
    //      raycast depth-aware contra la geometria fisica real.
    //   2) ARRaycastManager con PlaneWithinPolygon | Depth | FeaturePoint
    //      (ARCore Depth API + planos; tambien iPhone sin LiDAR).
    //   3) Fallback: punto sobre el rayo de la camara a una distancia configurable.
    public class RaycastResolver : MonoBehaviour
    {
        public static RaycastResolver Instance { get; private set; }

        [SerializeField] private Camera _arCamera;
        [SerializeField] private ARRaycastManager _arRaycast;
        [Tooltip("Distancia en metros para el fallback cuando no hay otro hit.")]
        [Range(0.3f, 5f)]
        [SerializeField] private float _fallbackDistance = 2f;

        public float FallbackDistance
        {
            get => _fallbackDistance;
            set => _fallbackDistance = Mathf.Clamp(value, 0.3f, 5f);
        }

        private static readonly List<ARRaycastHit> _arHits = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            if (_arCamera == null) _arCamera = Camera.main;
            if (_arRaycast == null) _arRaycast = FindFirstObjectByType<ARRaycastManager>();
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

            // 1) LiDAR nativo (ARKit directo). Snapea a la superficie fisica real.
            if (NativeLidar.TryRaycast(screenPoint, out var lidarPos, out var lidarNormal))
            {
                return new ResolvedHit
                {
                    Hit      = true,
                    Position = lidarPos,
                    Normal   = lidarNormal,
                    Source   = RaycastSource.Lidar,
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
