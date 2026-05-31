using UnityEngine;

namespace Scanner
{
    public enum SelectableKind { Wall, Cube, WallVertex }

    public interface ISelectable
    {
        SelectableKind Kind { get; }
        Transform Transform { get; }
        void OnSelect();
        void OnDeselect();
    }
}
