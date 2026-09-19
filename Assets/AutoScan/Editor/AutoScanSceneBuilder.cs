using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using Mortuorium.RoomScanning;

/// <summary>
/// Builds <c>Assets/Scenes/AutoScanScene.unity</c> and registers it in Build Settings, so the
/// scene does not have to be assembled by hand. Menu: Mortuorium ▸ AutoScan ▸ Build AutoScanScene.
/// Safe to re-run: it rebuilds the scene from the XR Origin Escaneo prefab every time.
/// </summary>
public static class AutoScanSceneBuilder
{
    const string ScenePath      = "Assets/Scenes/AutoScanScene.unity";
    const string XrPrefabPath   = "Assets/Prefabs/XR Origin Escaneo.prefab";
    const string PlanePrefabDir = "Assets/AutoScan/Prefabs";
    const string PlanePrefabPath = PlanePrefabDir + "/AutoScan Plane.prefab";
    const string PlaneMatPath   = PlanePrefabDir + "/AutoScan Plane.mat";
    const string WallMatPath    = "Assets/Shaders/WallEdgeGrid.mat";

    // Tuned DepthOccupancyMapper values from the standalone project (docs/autoscan/scene-values.md).
    static readonly (string name, float value)[] MapperValues =
    {
        ("cellSize", 0.08f), ("bandBottom", 0.3f), ("bandTopMargin", 0.2f), ("wallNormalMax", 0.35f),
        ("minCellHits", 4), ("minVerticalSpanFraction", 0.45f), ("lineInlierDist", 0.08f),
        ("lineIterations", 300), ("minSegmentCells", 7), ("minDensityFraction", 0.35f), ("maxRunGap", 0.4f),
        ("snapToRightAngles", 1), ("snapAngleTolerance", 20), ("collinearMergeAngle", 10),
        ("dropUnattachedWalls", 1), ("claimBuiltWallFootprint", 1), ("wallRidgeClaimMargin", 0.3f),
        ("wallRidgeCoreKeep", 0.08f), ("wallOffsetCluster", 0.15f), ("coplanarMaxGap", 5),
        ("wallThickness", 0.12f), ("maxWalls", 32), ("wallTopReachFraction", 0.6f),
        ("wallColumnFillFraction", 0.45f), ("wallColumnMinFrames", 2), ("wallClaimMargin", 0.12f),
        ("autoAnchorHits", 3), ("autoAnchorTolerance", 0.2f), ("autoAnchorMissGrace", 1),
        ("wallSource", 2), ("seedMinArea", 0.2f), ("seedMinCells", 4), ("seedMinDensityFraction", 0.25f),
        ("seedMinSpanFraction", 0.25f), ("floorEdgeMinLength", 0.3f), ("floorSeedMinCells", 2),
        ("floorSeedMinDensityFraction", 0.12f), ("ceilingNormalMin", 0.8f), ("heightBinSize", 0.05f),
        ("minCeilingHits", 40), ("minRoomHeight", 1.8f), ("maxRoomHeight", 5), ("mergeMaxAngle", 12),
        ("mergeOffsetTolerance", 0.2f), ("maxBridgeGap", 2.5f), ("voxelSize", 0.1f),
        ("furnitureBandBottom", 0.05f), ("minVoxelHits", 3), ("minSurfaceHits", 1),
        ("minSurfaceVoxels", 6), ("surfaceMinHeight", 0.15f),
    };

    // Components of the XR Origin Escaneo prefab that belong to the manual scanner. LiDARScanner
    // would disable the plane manager; the others depend on a reference-image library.
    static readonly string[] PrefabComponentsToRemove =
        { "ARImageAnchor", "ARTrackingBenchmark", "LiDARScanner", "ARTrackedImageManager" };

    [MenuItem("Mortuorium/AutoScan/Build AutoScanScene")]
    public static void Build()
    {
        var xrPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrPrefabPath);
        if (xrPrefab == null)
        {
            EditorUtility.DisplayDialog("AutoScan", $"No se encontró {XrPrefabPath}.", "OK");
            return;
        }

        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("AutoScan", $"{ScenePath} ya existe. ¿Reconstruirla?", "Reconstruir", "Cancelar"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var planePrefab = EnsurePlanePrefab();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var session = new GameObject("AR Session");
        session.AddComponent<ARSession>();
        session.AddComponent<ARInputManager>();

        var root = (GameObject)PrefabUtility.InstantiatePrefab(xrPrefab, scene);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = "XR Origin AutoScan";
        RemovePrefabComponents(root);

        var planeManager = root.GetComponent<ARPlaneManager>();
        SetObject(planeManager, "m_PlanePrefab", planePrefab);

        root.AddComponent<ARSessionSetup>();
        root.AddComponent<PlaneCollector>();
        root.AddComponent<CornerDetector>();
        var mapper = root.AddComponent<DepthOccupancyMapper>();
        var builder = root.AddComponent<RoomBuilder>();
        root.AddComponent<DoorDetector>();
        root.AddComponent<JsonExporter>();
        root.AddComponent<RoomRelocalizer>();
        root.AddComponent<RoomScanningManager>();

        SetObject(builder, "wallMaterial", AssetDatabase.LoadAssetAtPath<Material>(WallMatPath));
        ApplyMapperValues(mapper);
        ApplySourceSceneValues(planeManager, root.GetComponent<CornerDetector>(), builder);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings();
        Debug.Log($"[AutoScan] Escena creada en {ScenePath} y agregada a Build Settings.");
    }

    static void RemovePrefabComponents(GameObject root)
    {
        foreach (var name in PrefabComponentsToRemove)
            foreach (var c in root.GetComponents<Component>())
                if (c != null && c.GetType().Name == name) { Object.DestroyImmediate(c); break; }
    }

    // A plane prefab whose material is the AutoScan grid shader, with the classification
    // colouring and the boundary LineRenderer the source project's plane prefab has;
    // ARPlaneManager instantiates one of these per detected plane.
    static GameObject EnsurePlanePrefab()
    {
        Directory.CreateDirectory(PlanePrefabDir);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(PlaneMatPath);
        if (mat == null)
        {
            var shader = Shader.Find("Mortuorium/ARPlaneGrid");
            mat = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(mat, PlaneMatPath);
        }

        var go = new GameObject("AutoScan Plane");
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshCollider>();
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var line = go.AddComponent<LineRenderer>();
        line.enabled = false;
        line.useWorldSpace = false;
        line.loop = true;
        line.widthMultiplier = 0.01f;
        line.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Line.mat");
        go.AddComponent<ARPlane>();
        go.AddComponent<ARPlaneMeshVisualizer>();
        go.AddComponent<PlaneClassificationVisualizer>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PlanePrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static void ApplyMapperValues(Object target)
    {
        var so = new SerializedObject(target);
        var missing = new List<string>();
        foreach (var (name, value) in MapperValues)
        {
            var p = so.FindProperty(name);
            if (p == null) { missing.Add(name); continue; }
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float:   p.floatValue = value; break;
                case SerializedPropertyType.Integer: p.intValue = (int)value; break;
                case SerializedPropertyType.Boolean: p.boolValue = value != 0f; break;
                case SerializedPropertyType.Enum:    p.enumValueIndex = (int)value; break;
                default: missing.Add(name); break;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        if (missing.Count > 0)
            Debug.LogWarning("[AutoScan] Campos del mapper sin aplicar: " + string.Join(", ", missing));
    }

    // Values the standalone project's scene carries that the code defaults do not.
    static void ApplySourceSceneValues(ARPlaneManager planes, CornerDetector corners, RoomBuilder builder)
    {
        SetNumber(planes, "m_DetectionMode", 3);
        SetNumber(corners, "minWallAngle", 35);
        SetNumber(corners, "minLinePoints", 25);
        SetNumber(corners, "maxDepth", 5);
        SetNumber(builder, "duplicateRadius", 0.8f);
    }

    static void SetNumber(Object target, string field, float value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"[AutoScan] {target.GetType().Name}.{field} no existe."); return; }
        if (p.propertyType == SerializedPropertyType.Float) p.floatValue = value;
        else p.intValue = (int)value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetObject(Object target, string field, Object value)
    {
        if (target == null || value == null) return;
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"[AutoScan] {target.GetType().Name}.{field} no existe."); return; }
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void AddToBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var s in scenes)
            if (s.path == ScenePath) { s.enabled = true; EditorBuildSettings.scenes = scenes.ToArray(); return; }
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
