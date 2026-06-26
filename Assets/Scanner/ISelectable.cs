using UnityEngine;

namespace Scanner
{
    public enum SelectableKind { Wall, Cube, WallVertex, CubeVertex, DoorVertex, Floor }

    public interface ISelectable
    {
        SelectableKind Kind { get; }
        Transform Transform { get; }
        void OnSelect();
        void OnDeselect();
    }
}
