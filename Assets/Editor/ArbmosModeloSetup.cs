#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

// Reemplaza el modelo del Arbmos de gameplay (Assets/Prefabs/Arbmos.prefab) por el zombie
// de Assets/Entities/Models/Arbmos, conservando los componentes y su configuracion.
// Idle -> idleVariants, Walk -> chaseClip. Se puede volver a ejecutar tras reexportar el FBX.
public static class ArbmosModeloSetup
{
    private const string FbxPath = "Assets/Entities/Models/Arbmos/Zombie_Character.fbx";
    private const string TexDir = "Assets/Entities/Models/Arbmos/Textures/";
    private const string MatPath = "Assets/Entities/Models/Arbmos/Zombie.mat";
    private const string PrefabPath = "Assets/Prefabs/Arbmos.prefab";

    [MenuItem("Mortuorium/Arbmos/Reemplazar modelo por el zombie")]
    public static void Run()
    {
        ConfigurarTexturas();
        ConfigurarClips();
        var mat = CrearMaterial();

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var idle = Clip("Zombie_Idle");
        var walk = Clip("Zombie_Walk");
        Debug.Log($"[ArbmosModelo] idle={idle?.length}s walk={walk?.length}s");

        var viejo = PrefabUtility.LoadPrefabContents(PrefabPath);
        var nuevo = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        try
        {
            PrefabUtility.UnpackPrefabInstance(nuevo, PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            nuevo.name = "Arbmos";

            foreach (var r in nuevo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();

            var anim = nuevo.GetComponent<Animator>();
            if (anim == null) anim = nuevo.AddComponent<Animator>();
            anim.applyRootMotion = false;

            Copiar<ArbmosEntity>(viejo, nuevo);
            Copiar<ArbmosNetwork>(viejo, nuevo);
            Copiar<ArbmosSmokeAura>(viejo, nuevo);
            var aa = Copiar<ArbmosAnimator>(viejo, nuevo);
            aa.idleVariants = new[] { idle, idle, idle };
            aa.chaseClip = walk;
            AjustarPasos(fbx, walk, aa);

            var bounds = new Bounds();
            bool first = true;
            foreach (var r in nuevo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
            }
            Debug.Log($"[ArbmosModelo] bounds center={bounds.center} size={bounds.size}");
            foreach (var t in nuevo.GetComponentsInChildren<Transform>(true))
                Debug.Log($"[ArbmosModelo] hueso {t.name} pos={t.position}");

            PrefabUtility.SaveAsPrefabAsset(nuevo, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(viejo);
            Object.DestroyImmediate(nuevo);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[ArbmosModelo] listo");
    }

    // El Walk del zombie tiene 2 pasos por ciclo (uno por pie). La medicion automatica
    // sobre los tobillos dio ~0,05 m/s (inservible), asi que son valores estimados:
    // ajustar _metersPerStep / _footContacts a ojo mirando al Arbmos caminar.
    private static void AjustarPasos(GameObject fbx, AnimationClip walk, ArbmosAnimator aa)
    {
        var so = new SerializedObject(aa);
        so.FindProperty("_stepsPerLoop").intValue = 2;
        so.FindProperty("_metersPerStep").floatValue = 0.7f;
        var arr = so.FindProperty("_footContacts");
        arr.arraySize = 2;
        arr.GetArrayElementAtIndex(0).floatValue = 0.05f;
        arr.GetArrayElementAtIndex(1).floatValue = 0.55f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T Copiar<T>(GameObject de, GameObject a) where T : Component
    {
        var src = de.GetComponent<T>();
        var dst = a.GetComponent<T>();
        if (dst == null) dst = a.AddComponent<T>();
        if (src != null) EditorUtility.CopySerialized(src, dst);
        return dst;
    }

    private static AnimationClip Clip(string nombre) =>
        AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == nombre);

    private static void ConfigurarTexturas()
    {
        var n = AssetImporter.GetAtPath(TexDir + "Zombie_Normal.png") as TextureImporter;
        if (n != null && n.textureType != TextureImporterType.NormalMap)
        { n.textureType = TextureImporterType.NormalMap; n.SaveAndReimport(); }
    }

    private static void ConfigurarClips()
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = c.name.Substring(c.name.LastIndexOf('|') + 1);
            c.loopTime = true;
            c.keepOriginalPositionXZ = true;
            c.keepOriginalPositionY = true;
            c.keepOriginalOrientation = true;
        }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
    }

    private static Material CrearMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "Zombie_BaseColor.png");
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "Zombie_Normal.png"));
        mat.EnableKeyword("_NORMALMAP");
        mat.SetFloat("_Glossiness", 0.2f);
        mat.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
#endif
