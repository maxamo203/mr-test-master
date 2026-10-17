using Gameplay;
using UnityEditor;
using UnityEngine;

// Menú Mortuorium > Retrato del asesino: tunear sin buildear el retrato que aparece al morir
// (LocalDeath + RetratoAsesinoConfig).
//   - Crear / seleccionar config: crea Assets/Resources/RetratoAsesinoConfig.asset (o lo
//     selecciona) para editarlo en el Inspector. Es lo que lee el juego, también en el build.
//   - Probar en Play: arma el retrato del prefab seleccionado en el Project (o del Sorken si
//     no hay ninguno) delante de la cámara y lo deja ahí; los cambios de la config se ven al
//     instante. Con los controles WASD del editor se puede mover la cámara y ver que lo sigue.
//   - Cerrar prueba: lo saca.
public static class RetratoAsesinoMenu
{
    private const string Menu = "Mortuorium/Retrato del asesino/";
    private const string RutaAsset = "Assets/Resources/" + RetratoAsesinoConfig.RutaResources + ".asset";
    private const string PrefabPorDefecto = "Assets/Entities/Prefabs/SorkenGameplay.prefab";

    [MenuItem(Menu + "Crear o seleccionar config")]
    private static void SeleccionarConfig()
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

    [MenuItem(Menu + "Probar en Play")]
    private static void Probar()
    {
        var prefab = Selection.activeObject as GameObject;
        if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPorDefecto);
        if (prefab == null) { Debug.LogWarning($"[Retrato] No encontré {PrefabPorDefecto}."); return; }

        var muerte = LocalDeath.Ensure();
        if (!muerte.ProbarRetrato(prefab))
            Debug.LogWarning("[Retrato] No se pudo armar el retrato (¿hay cámara? ¿ya estás muerto? " +
                             $"¿'{prefab.name}' tiene mallas visibles?).");
        else
            Debug.Log($"[Retrato] Probando con '{prefab.name}'. Editá la config (menú Crear o seleccionar config) y mirá el Game.");
    }

    [MenuItem(Menu + "Probar en Play", true)]
    private static bool ProbarValido() => Application.isPlaying;

    [MenuItem(Menu + "Cerrar prueba")]
    private static void Cerrar() => LocalDeath.Instance?.CerrarPrueba();

    [MenuItem(Menu + "Cerrar prueba", true)]
    private static bool CerrarValido() =>
        Application.isPlaying && LocalDeath.Instance != null && LocalDeath.Instance.EnPrueba;
}
