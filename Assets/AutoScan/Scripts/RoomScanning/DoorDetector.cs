using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Mortuorium.RoomScanning
{
    /// <summary>
    /// Detects door openings in the scanned room. Two complementary signals:
    ///   1. ARCore <see cref="PlaneClassifications.DoorFrame"/> planes, when the
    ///      device reports them (implemented below).
    ///   2. Geometry fallback: door-sized gaps in an otherwise continuous wall run
    ///      (documented TODO).
    ///
    /// New component — there is no door logic in the current codebase, only a
    /// <c>DoorFrame</c> colour/label in <c>PlaneClassificationVisualizer</c>.
    /// Results are origin-local <see cref="DoorOpening"/>s.
    /// </summary>
    public class DoorDetector : MonoBehaviour
    {
        [Header("Plausible door dimensions (m)")]
        [SerializeField] float minWidth = 0.6f;
        [SerializeField] float maxWidth = 1.4f;
        [SerializeField] float minHeight = 1.8f;
        [SerializeField] float maxHeight = 2.4f;

        [SerializeField] PlaneCollector planeCollector;

        void Awake()
        {
            if (planeCollector == null) planeCollector = GetComponent<PlaneCollector>();
        }

        /// <summary>
        /// Finds door openings, expressed in <paramref name="mapOrigin"/>-local space.
        /// </summary>
        public List<DoorOpening> DetectDoors(IReadOnlyList<WallSegment> walls, Transform mapOrigin)
        {
            var doors = new List<DoorOpening>();
            if (mapOrigin == null || planeCollector == null) return doors;

            var worldToOrigin = mapOrigin.worldToLocalMatrix;

            foreach (var plane in planeCollector.Planes)
            {
                if (!plane.classifications.HasFlag(PlaneClassifications.DoorFrame)) continue;
                if (!TryDoorFromPlane(plane, worldToOrigin, out var door)) continue;
                doors.Add(door);
            }

            // TODO: geometry fallback — find door-sized gaps along the wall run in
            //       `walls` (within [minWidth,maxWidth] × [minHeight,maxHeight]) for
            //       devices that don't classify door frames.

            if (doors.Count > 0)
                Debug.Log($"[DoorDetector] Found {doors.Count} door(s) from ARCore DoorFrame planes.");
            return doors;
        }

        bool TryDoorFromPlane(ARPlane plane, Matrix4x4 worldToOrigin, out DoorOpening door)
        {
            door = default;

            // The plane's two in-plane axes are transform.right and transform.forward;
            // its normal is transform.up. For a wall-mounted door frame one in-plane
            // axis is horizontal (width) and the other near-vertical (height).
            var rightAxis = plane.transform.right;
            var fwdAxis   = plane.transform.forward;

            float width, height;
            Vector3 wallDirWorld;
            if (Mathf.Abs(rightAxis.y) <= Mathf.Abs(fwdAxis.y))
            {
                width = plane.size.x; height = plane.size.y; wallDirWorld = rightAxis;
            }
            else
            {
                width = plane.size.y; height = plane.size.x; wallDirWorld = fwdAxis;
            }

            if (width < minWidth || width > maxWidth) return false;
            if (height < minHeight || height > maxHeight) return false;

            // Centre at floor level in origin-local space (floor ≈ origin y = 0).
            var centre = worldToOrigin.MultiplyPoint3x4(plane.transform.position);
            centre.y = 0f;

            var wallDir = worldToOrigin.MultiplyVector(wallDirWorld);
            wallDir.y = 0f;
            wallDir = wallDir.sqrMagnitude > 1e-6f ? wallDir.normalized : Vector3.forward;

            door = new DoorOpening { center = centre, width = width, height = height, wallDir = wallDir };
            return true;
        }
    }
}
