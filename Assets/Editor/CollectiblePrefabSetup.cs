#if UNITY_EDITOR
using System.IO;
using System.Linq;
using Collectibles;
using UnityEditor;
using UnityEngine;

// Genera un prefab de Reliquia por cada modelo FBX que encuentre en
// Assets/Collectibles/Models/, y los registra en Assets/PrefabRegistry.asset bajo
// EntityTypeIds.CollectibleReliquia + i (i = orden alfabético del archivo). Correrlo
// de nuevo después de agregar/sacar modelos actualiza los prefabs existentes y suma
// los nuevos — no hace falta tocar código por cada modelo.
//
// El halo (glow) NO va acá: lo agrega CollectibleEntity solo, en tiempo de ejecución
// (mismo mecanismo que BatteryEntity) — este script sólo arma el prefab con el modelo
// como visual y lo registra.
public static class CollectiblePrefabSetup
{
    private const string ModelsFolder  = "Assets/Collectibles/Models";
    private const string PrefabsFolder = "Assets/Prefabs";
    private const string RegistryPath  = "Assets/PrefabRegistry.asset";

    [MenuItem("Mortuorium/Crear prefabs de Reliquias")]
    public static void Create()
    {
        if (!AssetDatabase.IsValidFolder(ModelsFolder))
        {
            Directory.CreateDirectory(ModelsFolder);
            AssetDatabase.Refresh();
            Debug.Log($"[Reliquias] Creé la carpeta {ModelsFolder}. Poné ahí los .fbx y volvé a " +
                       "correr este menú.");
            return;
        }

        var fbxPaths = AssetDatabase.FindAssets("t:Model", new[] { ModelsFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (fbxPaths.Length == 0)
        {
            Debug.Log($"[Reliquias] No encontré ningún .fbx en {ModelsFolder}. Poné ahí los modelos " +
                       "y volvé a correr Mortuorium > Crear prefabs de Reliquias.");
            return;
        }

        var registry = AssetDatabase.LoadAssetAtPath<NetworkedPrefabRegistry>(RegistryPath);
        if (registry == null)
        {
            Debug.LogError($"[Reliquias] No se encontró {RegistryPath}.");
            return;
        }

        int creados = 0;
        for (int i = 0; i < fbxPaths.Length; i++)
        {
            string modelPath = fbxPaths[i];
            string modelName = Path.GetFileNameWithoutExtension(modelPath);
            byte   typeId    = (byte)(EntityTypeIds.CollectibleReliquia + i);
            string prefabPath = $"{PrefabsFolder}/Reliquia_{modelName}.prefab";

            var root = new GameObject($"Reliquia_{modelName}");
            try
            {
                var entity = root.AddComponent<CollectibleEntity>();
                entity.variantIndex = (byte)i;

                AddVisual(root.transform, modelPath);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"[Reliquias] No se pudo guardar el prefab de '{modelName}'.");
                    continue;
                }

                Register(registry, typeId, prefab);
                creados++;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Reliquias] {creados}/{fbxPaths.Length} prefab(s) creados/actualizados y registrados " +
                   $"(TypeId {EntityTypeIds.CollectibleReliquia}..{EntityTypeIds.CollectibleReliquia + fbxPaths.Length - 1}).");
    }

    private static void AddVisual(Transform parent, string modelPath)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (source == null)
            throw new FileNotFoundException("No se pudo cargar el modelo.", modelPath);

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction);
        visual.name = "Modelo";
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        // Defensivo: un FBX no debería traer colliders, pero si los trae los sacamos
        // (mismo criterio que VelethPrefabSetup — evita colisiones no deseadas con la
        // layer de apuntado/oclusión).
        foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);
    }

    private static void Register(NetworkedPrefabRegistry registry, byte typeId, GameObject prefab)
    {
        var serialized = new SerializedObject(registry);
        var entries = serialized.FindProperty("entries");
        int index = -1;
        for (int i = 0; i < entries.arraySize; i++)
        {
            if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("TypeId").intValue == typeId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            index = entries.arraySize;
            entries.InsertArrayElementAtIndex(index);
        }

        var entry = entries.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("TypeId").intValue = typeId;
        entry.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
