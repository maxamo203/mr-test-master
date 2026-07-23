using Scanner;
using UnityEngine;

// Panel de debug del ESCÁNER (hijo de DebugHud): modo actual de la FSM y
// posición del jugador relativa al anchor. Antes vivía en ReticleController.
public class DebugEscanerUI : MonoBehaviour
{
    private Camera _camera;

    private void OnGUI()
    {
        var fsm = ScanStateMachine.Instance;
        if (fsm == null) return;   // solo tiene sentido en la ScannerScene

        UIScale.Begin();

        var style = DebugHudEstilos.Label(Color.white, 16);
        GUI.Label(new Rect(10f, 120f, 240f, 34f), $"Modo: {fsm.Current}", style);

        if (_camera == null) _camera = Camera.main;
        var wo = WorldOrigin.Instance;
        string jugador = (_camera == null || wo == null || !wo.IsReady)
            ? "Jugador: (sin calibrar)"
            : Fmt(wo.ToRelative(_camera.transform.position));
        GUI.Label(new Rect(10f, 158f, 240f, 50f), jugador, style);
    }

    private static string Fmt(Vector3 p) =>
        $"Jugador (rel. anchor):\nX {p.x:F2}  Y {p.y:F2}  Z {p.z:F2} m";
}
