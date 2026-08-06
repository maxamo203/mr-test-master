using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// "Frame Timing Stats" (Player Settings > Other Settings > Rendering) es lo que hace
// que Unity le pida al driver los tiempos reales de CPU/GPU por frame. Sin eso,
// FrameTimingManager.GetLatestTimings() devuelve 0 muestras y el panel de energía
// (DebugHud/PowerProbe) no puede separar GPU de CPU.
//
// El problema: es un setting GLOBAL, no distingue development de release, así que si
// queda prendido se va a producción con su costo (unas queries de timing por frame).
//
// Esto lo resuelve solo:
//   - build DEVELOPMENT  → lo prende, así el panel de energía siempre mide (tampoco
//                          hay que acordarse de encenderlo).
//   - build RELEASE      → lo apaga.
//   - después de buildear → restaura el valor que tenía el proyecto, para no ensuciar
//                          ProjectSettings.asset en git (está trackeado).
//
// Si una build falla, el postprocess no corre y el valor queda pisado; se corrige solo
// en la próxima build.
public class FrameTimingStatsGuard : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    private static bool _valorPrevio;
    private static bool _pisado;

    public void OnPreprocessBuild(BuildReport report)
    {
        bool development = (report.summary.options & BuildOptions.Development) != 0;

        _valorPrevio = PlayerSettings.enableFrameTimingStats;
        _pisado      = true;

        PlayerSettings.enableFrameTimingStats = development;

        UnityEngine.Debug.Log(
            $"[FrameTimingStatsGuard] Build {(development ? "development" : "release")}: " +
            $"Frame Timing Stats = {development} (el proyecto lo tenía en {_valorPrevio}).");
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (!_pisado) return;
        PlayerSettings.enableFrameTimingStats = _valorPrevio;
        _pisado = false;
    }
}
