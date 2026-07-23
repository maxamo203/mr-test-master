using Scanner;
using UnityEngine;

// Etiqueta de debug con la FUENTE del raycast de colocación (LidarMesh, ArPlane,
// Fallback, etc.) bajo la retícula. Antes vivía en ReticleController; la retícula
// misma sigue coloreándose por fuente (eso es feedback de juego, no debug).
public class DebugRaycastUI : MonoBehaviour
{
    private ReticleController _reticle;
    private float _proximaBusqueda;

    private static bool EsModoColocacion(ScannerMode m) =>
        m == ScannerMode.Wall_V1 || m == ScannerMode.Wall_Height || m == ScannerMode.Wall_Vn ||
        m == ScannerMode.Door_V1 || m == ScannerMode.Door_V2 ||
        m == ScannerMode.Cube_V1 || m == ScannerMode.Cube_V2 || m == ScannerMode.Cube_V3 ||
        m == ScannerMode.Floor_Place || m == ScannerMode.Marker_Place || m == ScannerMode.EditMoveTarget;

    private void OnGUI()
    {
        var fsm = ScanStateMachine.Instance;
        if (fsm == null || !EsModoColocacion(fsm.Current)) return;

        // La retícula vive en la escena; se re-busca con throttling por si cambió.
        if (_reticle == null && Time.unscaledTime >= _proximaBusqueda)
        {
            _reticle = FindFirstObjectByType<ReticleController>();
            _proximaBusqueda = Time.unscaledTime + 1f;
        }
        if (_reticle == null) return;

        UIScale.Begin();
        float vw = UIScale.VirtualWidth, vh = UIScale.VirtualHeight;

        var hit = _reticle.LastHit;
        var style = DebugHudEstilos.Label(Color.yellow, 15);
        GUI.Label(new Rect(vw * 0.5f - 110f, vh * 0.5f + 50f, 220f, 30f),
                  $"src: {hit.Source}", style);
    }
}
