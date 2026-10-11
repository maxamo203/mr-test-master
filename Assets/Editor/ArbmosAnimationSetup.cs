#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

// Mantiene el clip de persecucion nuevo enlazado al prefab de gameplay. Se puede volver
// a ejecutar despues de cada reexportacion desde Blender sin tocar el prefab a mano.
public static class ArbmosAnimationSetup
{
    private const string ClipPath =
        "Assets/ArbmosPreview/Models/Arbmos_Video_Stalking_Advance.fbx";
    private const string PrefabPath = "Assets/Prefabs/Arbmos.prefab";

    [MenuItem("Mortuorium/Arbmos/Integrar persecucion sincronizada")]
    public static void Integrate()
    {
        ConfigureClipImport();

        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(ClipPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(candidate => !candidate.name.StartsWith("__preview__"));
        if (clip == null)
            throw new System.InvalidOperationException(
                $"No se encontro un AnimationClip importado en {ClipPath}.");

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ArbmosAnimator arbmosAnimator = root.GetComponent<ArbmosAnimator>();
            if (arbmosAnimator == null)
                throw new System.InvalidOperationException(
                    $"El prefab {PrefabPath} no contiene ArbmosAnimator en la raiz.");

            var serialized = new SerializedObject(arbmosAnimator);
            serialized.FindProperty("chaseClip").objectReferenceValue = clip;
            serialized.FindProperty("_stepsPerLoop").intValue = 6;
            serialized.FindProperty("_plantedSpeedRatio").floatValue = 0.15f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Arbmos] Persecucion integrada: {clip.name}, " +
                  $"{clip.length:0.00}s, seis pasos con desplazamiento sincronizado.");
    }

    private static void ConfigureClipImport()
    {
        var importer = AssetImporter.GetAtPath(ClipPath) as ModelImporter;
        if (importer == null)
            throw new System.InvalidOperationException(
                $"No se encontro el ModelImporter de {ClipPath}.");

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
            throw new System.InvalidOperationException(
                $"El FBX {ClipPath} no contiene animaciones.");

        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].name = "Arbmos_Video_Stalking_Advance";
            clips[i].loopTime = true;
            clips[i].loopPose = true;
            clips[i].keepOriginalOrientation = true;
            clips[i].keepOriginalPositionY = true;
            clips[i].keepOriginalPositionXZ = true;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }
}
#endif
