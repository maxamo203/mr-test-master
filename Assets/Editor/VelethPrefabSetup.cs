#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Genera Veleth usando el modelo actual del Sorker con una paleta violeta propia.
// Reutilizar el modelo permite probar la entidad y su navegacion sin esperar arte final.
public static class VelethPrefabSetup
{
    private const string PrefabPath = "Assets/Prefabs/Veleth.prefab";
    private const string RegistryPath = "Assets/PrefabRegistry.asset";
    private const string SorkerPrefabPath =
        "Assets/Prefabs/Meshy_AI_El_Desollado_Abisal_biped_Character_output.prefab";
    private const string SorkerMaterialPath =
        "Assets/Meshy_AI_El_Desollado_Abisal_biped/sorken.mat";
    private const string VelethMaterialPath = "Assets/Materials/VelethSorkerTest.mat";

    [MenuItem("Mortuorium/Crear prefab de Veleth")]
    public static void Create()
    {
        var root = new GameObject("Veleth");
        try
        {
            root.AddComponent<VelethEntity>();
            root.AddComponent<VelethNetwork>();
            AddSorkerVisual(root.transform);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null)
                throw new System.InvalidOperationException("PrefabUtility no pudo guardar Veleth.");

            Register(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Veleth] Prefab creado y registrado en {PrefabPath} (typeId={EntityTypeIds.Veleth}).");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void AddSorkerVisual(Transform parent)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SorkerPrefabPath);
        if (source == null)
            throw new System.IO.FileNotFoundException("No se encontro el modelo del Sorker.", SorkerPrefabPath);

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction);
        visual.name = "Modelo Sorker - variante Veleth";
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        foreach (var component in visual.GetComponentsInChildren<SorkerAI>(true))
            Object.DestroyImmediate(component);
        foreach (var component in visual.GetComponentsInChildren<SorkerNetwork>(true))
            Object.DestroyImmediate(component);
        foreach (var component in visual.GetComponentsInChildren<Sorker>(true))
            Object.DestroyImmediate(component);
        foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);

        Material material = GetVelethMaterial();
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
        }
    }

    private static Material GetVelethMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(VelethMaterialPath);
        if (material == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(SorkerMaterialPath);
            if (source == null)
                throw new System.IO.FileNotFoundException("No se encontro el material del Sorker.", SorkerMaterialPath);
            material = new Material(source) { name = "Veleth - Sorker violeta" };
            AssetDatabase.CreateAsset(material, VelethMaterialPath);
        }

        var tint = new Color(0.42f, 0.08f, 0.62f, 1f);
        if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(0.16f, 0.01f, 0.24f));
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void Register(GameObject prefab)
    {
        var registry = AssetDatabase.LoadAssetAtPath<NetworkedPrefabRegistry>(RegistryPath);
        if (registry == null)
            throw new System.IO.FileNotFoundException("No se encontro PrefabRegistry.", RegistryPath);

        var serialized = new SerializedObject(registry);
        var entries = serialized.FindProperty("entries");
        int index = -1;
        for (int i = 0; i < entries.arraySize; i++)
        {
            if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("TypeId").intValue ==
                EntityTypeIds.Veleth)
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
        entry.FindPropertyRelative("TypeId").intValue = EntityTypeIds.Veleth;
        entry.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(registry);
    }
}
#endif
