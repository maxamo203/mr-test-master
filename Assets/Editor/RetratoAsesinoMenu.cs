using Gameplay;
using UnityEditor;
using UnityEngine;

// Menú Mortuorium > Retrato del asesino: tunear sin buildear el retrato que aparece al morir
// (LocalDeath + RetratoAsesinoConfig).
//   - Previsualizador: ventana con una pestaña por entidad para editar su ajuste y verlo en
//     Play (RetratoAsesinoWindow).
//   - Crear / seleccionar config: crea Assets/Resources/RetratoAsesinoConfig.asset (o lo
//     selecciona) para editarlo en el Inspector. Es lo que lee el juego, también en el build.
//   - Cerrar prueba: saca el retrato de prueba.
public static class RetratoAsesinoMenu
{
    private const string Menu = "Mortuorium/Retrato del asesino/";
    private const string RutaAsset = "Assets/Resources/" + RetratoAsesinoConfig.RutaResources + ".asset";

    [MenuItem(Menu + "Crear o seleccionar config")]
    public static void SeleccionarConfig()
    {
        var cfg = AssetDatabase.LoadAssetAtPath<RetratoAsesinoConfig>(RutaAsset);
        if (cfg == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            cfg = ScriptableObject.CreateInstance<RetratoAsesinoConfig>();
            AssetDatabase.CreateAsset(cfg, RutaAsset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Retrato] Config creada en {RutaAsset}.");
        }
        Selection.activeObject = cfg;
        EditorGUIUtility.PingObject(cfg);
    }

    [MenuItem(Menu + "Cerrar prueba")]
    private static void Cerrar() => LocalDeath.Instance?.CerrarPrueba();

    [MenuItem(Menu + "Cerrar prueba", true)]
    private static bool CerrarValido() =>
        Application.isPlaying && LocalDeath.Instance != null && LocalDeath.Instance.EnPrueba;
}
