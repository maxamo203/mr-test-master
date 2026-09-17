using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ArbmosPreviewSceneBuilder
{
    private const string Root = "Assets/ArbmosPreview";
    private const string IdleModel = Root + "/Models/Arbmos_Idle_Variants.fbx";
    private const string ChaseModel = Root + "/Models/Arbmos_Chase_Inevitable_Stalk.fbx";
    private const string ControllerPath = Root + "/Animation/ArbmosPreview.controller";
    private const string ScenePath = Root + "/Scenes/ArbmosAnimationLab.unity";

    private static readonly string[] StateNames =
    {
        "Idle 01 - Predatory Droop",
        "Idle 03 - Broken Marionette",
        "Idle 05 - Cadaveric Stillness",
        "Chase - Inevitable Stalk"
    };

    [MenuItem("Tools/Arbmos/Build Animation Lab")]
    public static string Build()
    {
        EnsureFolder(Root + "/Animation");
        EnsureFolder(Root + "/Scenes");
        EnsureFolder(Root + "/Materials");

        ConfigureModel(IdleModel);
        ConfigureModel(ChaseModel);
        var clips = FindClips();
        if (clips.Count != StateNames.Length)
            return $"Expected {StateNames.Length} clips but found {clips.Count}.";

        var controller = CreateController(clips);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var arbmos = CreateArbmos(controller);
        CreateStage();
        CreateCamera();
        CreateLighting();

        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = arbmos;
        SceneView.lastActiveSceneView?.FrameSelected();
        AssetDatabase.SaveAssets();
        return $"Created {ScenePath} with {clips.Count} looping animation states.";
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var split = path.LastIndexOf('/');
        var parent = path.Substring(0, split);
        var child = path.Substring(split + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, child);
    }

    private static void ConfigureModel(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return;
        importer.animationType = ModelImporterAnimationType.Generic;
        var clips = importer.defaultClipAnimations;
        for (var i = 0; i < clips.Length; i++)
        {
            clips[i].loopTime = true;
            clips[i].loopPose = true;
            clips[i].lockRootPositionXZ = true;
            clips[i].lockRootHeightY = true;
            clips[i].lockRootRotation = true;
            clips[i].keepOriginalPositionXZ = true;
            clips[i].keepOriginalPositionY = true;
            clips[i].keepOriginalOrientation = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    private static Dictionary<string, AnimationClip> FindClips()
    {
        var result = new Dictionary<string, AnimationClip>();
        CollectClips(IdleModel, result);
        CollectClips(ChaseModel, result);
        return result;
    }

    private static void CollectClips(string path, Dictionary<string, AnimationClip> result)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
            if (clip.name.Contains("Predatory_Droop")) result[StateNames[0]] = clip;
            else if (clip.name.Contains("Broken_Marionette")) result[StateNames[1]] = clip;
            else if (clip.name.Contains("Cadaveric_Stillness")) result[StateNames[2]] = clip;
            else if (clip.name.Contains("Inevitable_Stalk")) result[StateNames[3]] = clip;
        }
    }

    private static AnimatorController CreateController(IReadOnlyDictionary<string, AnimationClip> clips)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        var stateMachine = controller.layers[0].stateMachine;
        foreach (var child in stateMachine.states)
            stateMachine.RemoveState(child.state);

        AnimatorState first = null;
        foreach (var name in StateNames)
        {
            var state = stateMachine.AddState(name);
            state.motion = clips[name];
            state.speed = 1f;
            if (first == null) first = state;
        }
        stateMachine.defaultState = first;
        EditorUtility.SetDirty(controller);
        return controller;
    }

private static GameObject CreateArbmos(RuntimeAnimatorController controller)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(IdleModel);
        if (model == null) throw new InvalidOperationException("Arbmos idle FBX was not found.");
        var instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
        instance.name = "Arbmos_AnimationPreview";
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));

        var animator = instance.GetComponent<Animator>();
        if (!animator)
            animator = instance.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var bodyMaterial = GetMaterial(Root + "/Materials/ArbmosPreviewBody.mat", "ArbmosPreviewBody", Color.white);
        bodyMaterial.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Arbmos_BaseColor.png");
        if (bodyMaterial.HasProperty("_Metallic")) bodyMaterial.SetFloat("_Metallic", 0.08f);
        if (bodyMaterial.HasProperty("_Glossiness")) bodyMaterial.SetFloat("_Glossiness", 0.28f);
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            renderer.sharedMaterial = bodyMaterial;
        EditorUtility.SetDirty(bodyMaterial);

        var preview = instance.AddComponent<ArbmosAnimationPreview>();
        preview.Configure(animator);
        return instance;
    }

private static void CreateStage()
    {
        var floorMat = GetMaterial(Root + "/Materials/ArbmosLabFloor.mat", "ArbmosLabFloor", new Color(0.025f, 0.03f, 0.035f));
        var backdropMat = GetMaterial(Root + "/Materials/ArbmosLabBackdrop.mat", "ArbmosLabBackdrop", new Color(0.008f, 0.01f, 0.014f));
        var platformMat = GetMaterial(Root + "/Materials/ArbmosLabPlatform.mat", "ArbmosLabPlatform", new Color(0.055f, 0.012f, 0.016f));

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0f, -0.03f, 0f);
        floor.transform.localScale = new Vector3(1.2f, 1f, 1.2f);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;

        var platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        platform.name = "Arbmos_Platform";
        platform.transform.position = new Vector3(0f, 0.03f, 0f);
        platform.transform.localScale = new Vector3(1.15f, 0.06f, 1.15f);
        platform.GetComponent<Renderer>().sharedMaterial = platformMat;

        var backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backdrop.name = "Backdrop";
        backdrop.transform.position = new Vector3(0f, 1.7f, 1.85f);
        backdrop.transform.localScale = new Vector3(8f, 3.5f, 0.15f);
        backdrop.GetComponent<Renderer>().sharedMaterial = backdropMat;
    }

    private static Material GetMaterial(string path, string name, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

private static void CreateCamera()
    {
        var go = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 34f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 50f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.004f, 0.005f, 0.008f);
        go.transform.position = new Vector3(0f, 1.0f, -3.05f);
        go.transform.LookAt(new Vector3(-0.03f, 0.92f, 0.12f));
    }

private static void CreateLighting()
    {
        AddSpot("Key Light", new Vector3(-2.2f, 3.2f, -2.7f), new Color(0.72f, 0.82f, 1f), 1.25f, 48f);
        var fill = new GameObject("Fill Light").AddComponent<Light>();
        fill.type = LightType.Point;
        fill.color = new Color(0.35f, 0.45f, 0.62f);
        fill.intensity = 0.22f;
        fill.range = 8f;
        fill.transform.position = new Vector3(2.2f, 1.5f, -1.2f);
        AddSpot("Red Rim Light", new Vector3(1.6f, 2.4f, 2.1f), new Color(0.75f, 0.035f, 0.025f), 0.75f, 52f);

        var directional = new GameObject("Soft Directional Light").AddComponent<Light>();
        directional.type = LightType.Directional;
        directional.color = new Color(0.38f, 0.43f, 0.5f);
        directional.intensity = 0.12f;
        directional.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
    }

    private static void AddSpot(string name, Vector3 position, Color color, float intensity, float angle)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = color;
        light.intensity = intensity;
        light.range = 12f;
        light.spotAngle = angle;
        light.transform.position = position;
        light.transform.LookAt(new Vector3(0f, 0.85f, 0f));
    }
}
