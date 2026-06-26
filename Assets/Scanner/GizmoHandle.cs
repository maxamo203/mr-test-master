using System.Collections.Generic;
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

        // Sentido del eje: +1 / -1. Permite flechas de Move en ambos sentidos
        // (positivo y negativo) sobre el mismo eje. Scale/Rotate usan +1.
        public int Dir = 1;

        // Feedback de drag: mientras se arrastra un handle lo pintamos de amarillo
        // (opaco) para indicar visualmente cual se esta manipulando.
        private static readonly Color PressedColor = Color.yellow;
        private readonly List<Material> _mats      = new();
        private readonly List<Color>    _baseColor = new();
        private bool _pressed;

        public Vector3 LocalAxis()
        {
            Vector3 a;
            switch (Axis)
            {
                case GizmoAxis.X: a = Vector3.right;   break;
                case GizmoAxis.Y: a = Vector3.up;      break;
                default:          a = Vector3.forward; break;
            }
            return a * Dir;
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

        // El controller registra cada material del handle al construirlo, para
        // poder atenuarlo despues sin tener que buscar renderers en runtime.
        public void RegisterMaterial(Material m)
        {
            if (m == null) return;
            _mats.Add(m);
            _baseColor.Add(m.HasProperty("_Color") ? m.color : Color.white);
        }

        public void SetPressed(bool pressed)
        {
            if (_pressed == pressed) return;
            _pressed = pressed;
            for (int i = 0; i < _mats.Count; i++)
            {
                var m = _mats[i];
                if (m == null) continue;
                m.color = pressed ? PressedColor : _baseColor[i];
            }
        }
    }
}
