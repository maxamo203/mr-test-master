using UnityEngine;

// Agregar este componente a cualquier GameObject en la escena.
// Muestra info de red y posiciones para debugear sincronización.
public class NetworkDebugUI : MonoBehaviour
{
    private void OnGUI()
    {
        var net = NetworkManager.Instance;
        var wo  = WorldOrigin.Instance;
        var reg = EntityRegistry.Instance;

        if (net == null) return;

        // Panel derecho para no tapar la lobby UI
        GUILayout.BeginArea(new Rect(Screen.width - 340, 10, 330, Screen.height - 20));

        GUILayout.Label("=== DEBUG RED ===");
        GUILayout.Label($"Rol:        {(net.IsServer ? "HOST/SERVER" : "CLIENTE")}");
        GUILayout.Label($"ClientId:   {net.LocalClientId}");
        GUILayout.Label($"Tick:       {net.CurrentTick}");
        GUILayout.Label($"GameStart:  {net.GameStarted}");

        GUILayout.Space(6);
        GUILayout.Label("=== WORLD ORIGIN ===");

        if (wo != null)
        {
            GUILayout.Label($"IsReady:  {wo.IsReady}");
            GUILayout.Label($"Pos: {V(wo.transform.position)}");
        }
        else
        {
            GUILayout.Label("WorldOrigin: NULL");
        }

        if (reg == null || !net.GameStarted) { GUILayout.EndArea(); return; }

        GUILayout.Space(6);
        GUILayout.Label($"=== ENTIDADES ({reg.Count}) ===");

        foreach (var entity in reg.All)
        {
            if (entity == null) continue;

            string type   = entity.EntityTypeId == EntityTypeIds.Player  ? "PLY"
                          : entity.EntityTypeId == EntityTypeIds.Sorker  ? "SRK"
                          : $"T{entity.EntityTypeId}";
            string owned  = entity.IsOwned ? " [OWNED]" : "";
            var    wPos   = entity.transform.position;
            var    relPos = wo != null ? wo.ToRelative(wPos) : wPos;

            GUILayout.Label($"[{type}] id={entity.NetworkId}{owned}");
            GUILayout.Label($"  world: {V(wPos)}");
            GUILayout.Label($"  relat: {V(relPos)}");
        }

        GUILayout.EndArea();
    }

    private static string V(Vector3 v) =>
        $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
}
