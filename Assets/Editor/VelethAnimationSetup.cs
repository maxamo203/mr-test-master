using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class VelethAnimationSetup
{
    private const string PrefabPath = "Assets/Prefabs/Veleth.prefab";
    private const string EmergePath = "Assets/Entities/Veleth/Animations/Veleth_EmergeFromBook_HeadFixed.fbx";
    private const string CrawlPath = "Assets/Entities/Veleth/Animations/Veleth_CrawlLoop.fbx";
    private const string ControllerPath = "Assets/Entities/Veleth/Animations/Veleth.controller";
    private const string MaterialPath = "Assets/Entities/Veleth/Animations/Veleth.mat";
    private const string AlbedoPath = "Assets/Entities/Veleth/Animations/Veleth_Albedo.png";
    private const string NormalPath = "Assets/Entities/Veleth/Animations/Veleth_Normal.png";
    private const string MetallicPath = "Assets/Entities/Veleth/Animations/Veleth_Metallic.png";

    [InitializeOnLoadMethod]
    private static void ConfigureAfterImport() => EditorApplication.delayCall += ConfigureIfNeeded;

    [MenuItem("Mortuorium/Configurar animaciones de Veleth")]
    public static void ConfigureIfNeeded()
    {
        var emergeModel = AssetDatabase.LoadAssetAtPath<GameObject>(EmergePath);
        var emerge = GetClip(EmergePath);
        var crawl = GetClip(CrawlPath);
        if (emergeModel == null || emerge == null || crawl == null) return;

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            CreateController(emerge, crawl);
            AssetDatabase.SaveAssets();
            EditorApplication.delayCall += ConfigureIfNeeded;
            return;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            CreateMaterial();
            AssetDatabase.SaveAssets();
            EditorApplication.delayCall += ConfigureIfNeeded;
            return;
        }

        ApplyToPrefab(emergeModel, emerge, controller, material);
    }

    private static AnimationClip GetClip(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", System.StringComparison.Ordinal));

    private static void CreateController(AnimationClip emerge, AnimationClip crawl)
    {
        crawl.wrapMode = WrapMode.Loop;
        emerge.wrapMode = WrapMode.Once;
        EditorUtility.SetDirty(crawl);
        EditorUtility.SetDirty(emerge);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("BookConsumed", AnimatorControllerParameterType.Trigger);

        var layer = controller.layers[0];
        var crawlState = layer.stateMachine.AddState("Reptar");
        crawlState.motion = crawl;
        layer.stateMachine.defaultState = crawlState;

        var emergeState = layer.stateMachine.AddState("Emergencia desde el libro");
        emergeState.motion = emerge;

        var enter = layer.stateMachine.AddAnyStateTransition(emergeState);
        enter.AddCondition(AnimatorConditionMode.If, 0f, "BookConsumed");
        enter.hasExitTime = false;
        enter.hasFixedDuration = true;
        enter.duration = 0f;
        enter.canTransitionToSelf = false;

        var finish = emergeState.AddTransition(crawlState);
        finish.hasExitTime = true;
        finish.exitTime = 1f;
        finish.hasFixedDuration = true;
        finish.duration = 0f;
        EditorUtility.SetDirty(controller);
    }

    private static void CreateMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) return;

        var material = new Material(shader) { name = "Veleth" };
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        var metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(MetallicPath);

        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", albedo);
        if (material.HasProperty("_BumpMap"))
        {
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
        }
        if (material.HasProperty("_MetallicGlossMap"))
        {
            material.SetTexture("_MetallicGlossMap", metallic);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.25f);
        AssetDatabase.CreateAsset(material, MaterialPath);
    }

    private static void ApplyToPrefab(GameObject emergeModel, AnimationClip emerge, AnimatorController controller, Material material)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (var animator in root.GetComponents<Animator>())
                Object.DestroyImmediate(animator);

            var legacyVisual = root.transform.Find("Modelo Sorker - variante Veleth");
            if (legacyVisual != null) Object.DestroyImmediate(legacyVisual.gameObject);

            var visual = root.transform.Find("Modelo Veleth animado");
            if (visual == null)
            {
                var instance = PrefabUtility.InstantiatePrefab(emergeModel, root.transform) as GameObject;
                visual = instance.transform;
                visual.name = "Modelo Veleth animado";
            }

            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;

            var animatorOnSkeleton = visual.GetComponent<Animator>();
            // Unity can return a destroyed component reference from an imported prefab.
            // Use Unity''s null comparison so it is replaced with a real Animator.
            if (animatorOnSkeleton == null)
                animatorOnSkeleton = visual.gameObject.AddComponent<Animator>();
            animatorOnSkeleton.runtimeAnimatorController = controller;
            animatorOnSkeleton.applyRootMotion = false;

            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }

            var entity = root.GetComponent<VelethEntity>();
            if (entity != null)
            {
                var serialized = new SerializedObject(entity);
                serialized.FindProperty("_animator").objectReferenceValue = animatorOnSkeleton;
                serialized.FindProperty("_emergenceDuration").floatValue = emerge.length;
                serialized.FindProperty("_crawlImpulsesPerLoop").intValue = 4;
                serialized.FindProperty("_plantedSpeedRatio").floatValue = 0.2f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[Veleth] Configurada: libro consumido -> emerge -> reptar -> persecucion.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}



