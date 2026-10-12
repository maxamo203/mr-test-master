#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Integra automaticamente las dos acciones exportadas desde Blender. La configuracion
// tambien corre en un clon limpio: Unity importa los FBX, deja los clips como one-shot y
// los conecta al prefab de gameplay sin depender de una asignacion manual en Inspector.
public static class SorkenWindowAnimationSetup
{
    private const string EntryPath =
        "Assets/Entities/Sorken/Animations/Sorken_WindowEntry_v02.fbx";
    private const string LandingPath =
        "Assets/Entities/Sorken/Animations/Sorken_WindowLanding_v02.fbx";
    private const string GameplayEntryPath =
        "Assets/Entities/Sorken/Animations/Sorken_WindowEntry_Gameplay.anim";
    private const string GameplayLandingPath =
        "Assets/Entities/Sorken/Animations/Sorken_WindowLanding_Gameplay.anim";
    private const string PrefabPath = "Assets/Entities/Prefabs/SorkenGameplay.prefab";

    [InitializeOnLoadMethod]
    private static void ConfigureAfterImport() => EditorApplication.delayCall += ConfigureIfReady;

    [MenuItem("Mortuorium/Sorken/Integrar entrada por ventana")]
    public static void ConfigureIfReady()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(EntryPath) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(LandingPath) == null)
            return;

        bool reimported = false;
        reimported |= ConfigureClipImport(EntryPath, "Sorken_WindowEntry_v02");
        reimported |= ConfigureClipImport(LandingPath, "Sorken_WindowLanding_v02");
        if (reimported)
        {
            EditorApplication.delayCall += ConfigureIfReady;
            return;
        }

        // Los FBX conservan sus acciones fuente, pero el prefab debe usar los clips
        // normalizados para el rig real. Asignar los FBX directamente vuelve a dejar
        // al Sorken en V-pose y, ademas, intercambia visualmente puerta y ventana.
        AnimationClip entry = AssetDatabase.LoadAssetAtPath<AnimationClip>(GameplayEntryPath);
        AnimationClip landing = AssetDatabase.LoadAssetAtPath<AnimationClip>(GameplayLandingPath);
        if (entry == null || landing == null) return;

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            SorkenAnimator animator = root.GetComponent<SorkenAnimator>();
            if (animator == null)
                throw new InvalidOperationException(
                    $"El prefab {PrefabPath} no contiene SorkenAnimator en la raiz.");

            var serialized = new SerializedObject(animator);
            SerializedProperty entryProperty = serialized.FindProperty("emergeWindowClip");
            SerializedProperty landingProperty = serialized.FindProperty("windowLandingClip");
            bool changed = entryProperty.objectReferenceValue != entry ||
                           landingProperty.objectReferenceValue != landing;
            if (!changed) return;

            entryProperty.objectReferenceValue = entry;
            landingProperty.objectReferenceValue = landing;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Sorken] Ventana integrada: {entry.name} ({entry.length:0.00}s) -> " +
                      $"{landing.name} ({landing.length:0.00}s) -> persecucion.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool ConfigureClipImport(string path, string clipName)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return false;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return false;

        bool changed = importer.animationType != ModelImporterAnimationType.Generic ||
                       !importer.importAnimation || clips.Length != 1 ||
                       clips[0].name != clipName || clips[0].loopTime || clips[0].loopPose ||
                       !clips[0].keepOriginalOrientation || !clips[0].keepOriginalPositionY ||
                       !clips[0].keepOriginalPositionXZ;
        if (!changed) return false;

        ModelImporterClipAnimation clip = clips[0];
        clip.name = clipName;
        clip.loopTime = false;
        clip.loopPose = false;
        clip.keepOriginalOrientation = true;
        clip.keepOriginalPositionY = true;
        clip.keepOriginalPositionXZ = true;

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.clipAnimations = new[] { clip };
        importer.SaveAndReimport();
        return true;
    }

    private static AnimationClip GetClip(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(clip =>
                !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
}
#endif
