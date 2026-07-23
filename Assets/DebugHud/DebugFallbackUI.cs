using Scanner;
using UnityEngine;

// Slider de debug para la DISTANCIA DEL RAYCAST FALLBACK (el punto a lo largo
// del rayo de la cámara cuando ninguna fuente AR devuelve hit). Antes vivía en
// ReticleController; es una herramienta de diagnóstico, no UI de juego.
public class DebugFallbackUI : MonoBehaviour
{
    private static bool EsModoColocacion(ScannerMode m) =>
        m == ScannerMode.Wall_V1 || m == ScannerMode.Wall_Height || m == ScannerMode.Wall_Vn ||
        m == ScannerMode.Door_V1 || m == ScannerMode.Door_V2 ||
        m == ScannerMode.Cube_V1 || m == ScannerMode.Cube_V2 || m == ScannerMode.Cube_V3 ||
        m == ScannerMode.Floor_Place || m == ScannerMode.Marker_Place || m == ScannerMode.EditMoveTarget;

    private void OnGUI()
    {
        var fsm = ScanStateMachine.Instance;
        if (fsm == null || !EsModoColocacion(fsm.Current)) return;
        if (RaycastResolver.Instance == null) return;

        UIScale.Begin();
        float vh = UIScale.VirtualHeight;

        var area = new Rect(10f, vh - 330f, 300f, 76f);
        UIBlocker.AddVirtualRect(area);
        GUILayout.BeginArea(area, GUIContent.none);
        GUILayout.Label($"Distancia fallback: {RaycastResolver.Instance.FallbackDistance:F2}m",
                        DebugHudEstilos.Label(Color.white, 15));
        RaycastResolver.Instance.FallbackDistance =
            GUILayout.HorizontalSlider(RaycastResolver.Instance.FallbackDistance, 0.3f, 5f);
        GUILayout.EndArea();
    }
}
