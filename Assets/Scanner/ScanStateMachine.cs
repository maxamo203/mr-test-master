using System;
using UnityEngine;

namespace Scanner
{
    // Modos de operacion. Cada uno define que hace el boton "Colocar" de la
    // reticula y que UI esta visible.
    public enum ScannerMode
    {
        Idle,             // sin modo activo: solo se pueden seleccionar objetos
        Calibrating,      // buscando imagen de referencia
        Wall_V1,          // esperando primer vertice de pared (piso)
        Wall_Height,      // esperando vertice de altura (segundo punto para definir H)
        Wall_Vn,          // esperando proximo vertice de piso (encadenado)
        DoorPickWall,     // esperando tap sobre pared para asociar puerta
        Door_V1,          // esperando esquina inferior de puerta
        Door_V2,          // esperando esquina superior opuesta
        Cube_V1,          // esperando primer vertice de la diagonal del cubo
        Cube_V2,          // esperando segundo vertice de la diagonal del cubo (esquina opuesta)
        Cube_V3,          // esperando tercer punto de referencia para la rotacion (yaw) del cubo
        Floor_Place,      // esperando colocar (o reubicar) el punto de piso
        Selected,         // hay un objeto seleccionado (panel de edicion visible)
        EditMoveTarget,   // moviendo un objeto/vertice ya colocado
    }

    [DefaultExecutionOrder(-50)]
    public class ScanStateMachine : MonoBehaviour
    {
        public static ScanStateMachine Instance { get; private set; }

        public ScannerMode Current { get; private set; } = ScannerMode.Calibrating;
        public ISelectable CurrentSelection { get; private set; }

        public event Action<ScannerMode, ScannerMode> OnModeChanged;
        public event Action<ISelectable> OnSelectionChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public void SetMode(ScannerMode next)
        {
            if (Current == next) return;
            var prev = Current;
            Current  = next;
            Debug.Log($"[FSM] {prev} -> {next}");
            OnModeChanged?.Invoke(prev, next);
        }

        public void SetSelection(ISelectable sel)
        {
            if (CurrentSelection == sel) return;
            CurrentSelection?.OnDeselect();
            CurrentSelection = sel;
            sel?.OnSelect();
            OnSelectionChanged?.Invoke(sel);

            if (sel != null && Current == ScannerMode.Idle)
                SetMode(ScannerMode.Selected);
            else if (sel == null && Current == ScannerMode.Selected)
                SetMode(ScannerMode.Idle);
        }

        public void ClearSelection() => SetSelection(null);
    }
}
