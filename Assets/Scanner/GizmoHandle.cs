using UnityEngine;

namespace Scanner
{
    public enum GizmoOperation { Move, Rotate, Scale }
    public enum GizmoAxis { X, Y, Z }

    // Marker que pega el TransformGizmoController a cada handle clickeable.
    // El controller mira este componente para saber que hacer con el drag.
    public class GizmoHandle : MonoBehaviour
    {
        public GizmoOperation Operation;
        public GizmoAxis      Axis;

        public Vector3 LocalAxis()
        {
            switch (Axis)
            {
                case GizmoAxis.X: return Vector3.right;
                case GizmoAxis.Y: return Vector3.up;
                default:          return Vector3.forward;
            }
        }

        public Color AxisColor()
        {
            switch (Axis)
            {
                case GizmoAxis.X: return new Color(1f, 0.25f, 0.25f, 1f);
                case GizmoAxis.Y: return new Color(0.25f, 1f, 0.25f, 1f);
                default:          return new Color(0.3f, 0.5f, 1f, 1f);
            }
        }
    }
}
