#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Integra solamente la caminata del Sorken cubriendose de la luz. El FBX fuente se
// mantiene separado del modelo principal para no reimportar ni alterar los demas clips.
public static class SorkenCoverWalkAnimationSetup
{
    private const string SourcePath =
        "Assets/Entities/Sorken/Animations/Sorken_CoverWalk_WingsRested.fbx";
    private const string GameplayPath =
        "Assets/Entities/Sorken/Animations/Sorken_CoverWalk_WingsRested_Gameplay.anim";
    private const string PrefabPath = "Assets/Entities/Prefabs/SorkenGameplay.prefab";
    private const string ClipName = "Sorken_Cover_Walk_WingsRested";

    [InitializeOnLoadMethod]
    private static void ConfigureAfterImport() =>
        EditorApplication.delayCall += ConfigureIfReady;

    [MenuItem("Mortuorium/Sorken/Integrar caminata cubierta")]
    public static void ConfigureIfReady()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath) == null)
            return;

        if (ConfigureClipImport())
        {
            EditorApplication.delayCall += ConfigureIfReady;
            return;
        }

        AnimationClip source = AssetDatabase.LoadAllAssetsAtPath(SourcePath)
            .OfType<AnimationClip>()
            .FirstOrDefault(clip =>
                !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
        if (source == null) return;

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            SorkenAnimator animator = root.GetComponent<SorkenAnimator>();
            if (animator == null)
                throw new InvalidOperationException(
                    $"El prefab {PrefabPath} no contiene SorkenAnimator en la raiz.");

            AnimationClip gameplay = CreateOrUpdateGameplayClip(source, root.transform);
            var serialized = new SerializedObject(animator);
            SerializedProperty property = serialized.FindProperty("coverWalkClip");
            if (property.objectReferenceValue != gameplay)
            {
                property.objectReferenceValue = gameplay;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Sorken] Caminata cubierta integrada: {gameplay.name} " +
                      $"({gameplay.length:0.00}s, loop).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool ConfigureClipImport()
    {
        var importer = AssetImporter.GetAtPath(SourcePath) as ModelImporter;
        if (importer == null) return false;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return false;

        ModelImporterClipAnimation clip = clips[0];
        bool changed = importer.animationType != ModelImporterAnimationType.Generic ||
                       !importer.importAnimation || clips.Length != 1 ||
                       clip.name != ClipName || !clip.loopTime || !clip.loopPose ||
                       !clip.keepOriginalOrientation || !clip.keepOriginalPositionY ||
                       !clip.keepOriginalPositionXZ;
        if (!changed) return false;

        clip.name = ClipName;
        clip.loopTime = true;
        clip.loopPose = true;
        clip.keepOriginalOrientation = true;
        clip.keepOriginalPositionY = true;
        clip.keepOriginalPositionXZ = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.clipAnimations = new[] { clip };
        importer.SaveAndReimport();
        return true;
    }

    private static AnimationClip CreateOrUpdateGameplayClip(
        AnimationClip source, Transform prefabRoot)
    {
        AnimationClip destination = AssetDatabase.LoadAssetAtPath<AnimationClip>(GameplayPath);
        if (destination == null)
        {
            destination = new AnimationClip();
            AssetDatabase.CreateAsset(destination, GameplayPath);
        }

        destination.name = "Sorken_CoverWalk_WingsRested_Gameplay";
        destination.frameRate = source.frameRate;
        destination.ClearCurves();

        foreach (EditorCurveBinding sourceBinding in AnimationUtility.GetCurveBindings(source))
        {
            // El movimiento del GameObject raiz lo controla SorkenEntity. Copiar estas
            // curvas provocaria traslacion o escala duplicada en runtime.
            if (IsRootBinding(sourceBinding.path)) continue;
            EditorCurveBinding destinationBinding = sourceBinding;
            destinationBinding.path = ResolvePath(prefabRoot, sourceBinding.path);
            AnimationUtility.SetEditorCurve(destination, destinationBinding,
                AnimationUtility.GetEditorCurve(source, sourceBinding));
        }

        foreach (EditorCurveBinding sourceBinding in
                 AnimationUtility.GetObjectReferenceCurveBindings(source))
        {
            if (IsRootBinding(sourceBinding.path)) continue;
            EditorCurveBinding destinationBinding = sourceBinding;
            destinationBinding.path = ResolvePath(prefabRoot, sourceBinding.path);
            AnimationUtility.SetObjectReferenceCurve(destination, destinationBinding,
                AnimationUtility.GetObjectReferenceCurve(source, sourceBinding));
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = true;
        settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(destination, settings);
        AnimationUtility.SetAnimationEvents(destination,
            AnimationUtility.GetAnimationEvents(source));
        EditorUtility.SetDirty(destination);
        return destination;
    }

    private static string ResolvePath(Transform prefabRoot, string sourcePath)
    {
        if (prefabRoot.Find(sourcePath) != null)
            return sourcePath;

        string armaturePath = "Armature/" + sourcePath;
        return prefabRoot.Find(armaturePath) != null ? armaturePath : sourcePath;
    }

    private static bool IsRootBinding(string path) =>
        string.IsNullOrEmpty(path) || string.Equals(path, "Armature", StringComparison.Ordinal);
}
#endif
