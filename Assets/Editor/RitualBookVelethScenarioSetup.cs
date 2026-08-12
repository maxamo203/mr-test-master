using Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RitualBookVelethScenarioSetup
{
    private const string ScenePath = "Assets/Scenes/RitualBookVelethTest.unity";
    private const string BookPath = "Assets/Resources/LibroRitual.prefab";
    private const string VelethPath = "Assets/Prefabs/Veleth.prefab";

    [MenuItem("Mortuorium/Crear escenario de prueba Libro + Veleth")]
    public static void CreateScenario()
    {
        var bookPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BookPath);
        var velethPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VelethPath);
        if (bookPrefab == null || velethPrefab == null)
        {
            Debug.LogError("[EscenarioVeleth] Faltan los prefabs del libro o de Veleth.");
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("ESCENARIO PRUEBA — LIBRO Y VELETH");

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        Material floorMaterial = GetMaterial("Assets/Materials/TestRitualFloor.mat", new Color(0.055f, 0.06f, 0.075f));
        Material woodMaterial = GetMaterial("Assets/Materials/TestRitualPedestal.mat", new Color(0.22f, 0.09f, 0.035f));
        Material wallMaterial = GetMaterial("Assets/Materials/TestRitualWalls.mat", new Color(0.025f, 0.02f, 0.035f));

        CreatePrimitive("Piso", PrimitiveType.Plane, root.transform,
            Vector3.zero, new Vector3(1.5f, 1f, 1.5f), floorMaterial);
        CreatePrimitive("Pedestal", PrimitiveType.Cube, root.transform,
            new Vector3(0f, 0.36f, 0f), new Vector3(0.75f, 0.72f, 0.75f), woodMaterial);
        CreatePrimitive("Pared fondo", PrimitiveType.Cube, root.transform,
            new Vector3(0f, 1.5f, 4.4f), new Vector3(7f, 3f, 0.18f), wallMaterial);
        CreatePrimitive("Pared izquierda", PrimitiveType.Cube, root.transform,
            new Vector3(-3.4f, 1.5f, 0f), new Vector3(0.18f, 3f, 9f), wallMaterial);
        CreatePrimitive("Pared derecha", PrimitiveType.Cube, root.transform,
            new Vector3(3.4f, 1.5f, 0f), new Vector3(0.18f, 3f, 9f), wallMaterial);

        var bookGo = (GameObject)PrefabUtility.InstantiatePrefab(bookPrefab, scene);
        bookGo.name = "Libro Ritual (ancla simulada)";
        bookGo.transform.SetParent(root.transform);
        bookGo.transform.position = new Vector3(0f, 0.73f, 0f);
        bookGo.transform.localScale = Vector3.one * 2f; // ampliado solo para evaluar el shader en monitor
        var book = bookGo.GetComponent<RitualBookView>();

        var velethGo = (GameObject)PrefabUtility.InstantiatePrefab(velethPrefab, scene);
        velethGo.name = "Veleth (inactiva hasta invocacion)";
        velethGo.transform.SetParent(root.transform);
        var veleth = velethGo.GetComponent<VelethEntity>();
        velethGo.SetActive(false);

        var cameraGo = new GameObject("Jugador — Main Camera");
        cameraGo.tag = "MainCamera";
        cameraGo.transform.SetParent(root.transform);
        cameraGo.transform.position = new Vector3(0f, 1.3f, -4f);
        cameraGo.transform.LookAt(new Vector3(0f, 0.78f, 0f));
        var camera = cameraGo.AddComponent<Camera>();
        camera.fieldOfView = 62f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.008f, 0.008f, 0.014f);
        cameraGo.AddComponent<AudioListener>();

        var flashlightGo = new GameObject("Linterna simulada (mantener F)");
        flashlightGo.transform.SetParent(cameraGo.transform, false);
        var flashlight = flashlightGo.AddComponent<Light>();
        flashlight.type = LightType.Spot;
        flashlight.color = new Color(1f, 0.92f, 0.72f);
        flashlight.intensity = 4.2f;
        flashlight.range = 8f;
        flashlight.spotAngle = 38f;
        flashlight.enabled = false;

        var moonGo = new GameObject("Luz ambiente");
        moonGo.transform.SetParent(root.transform);
        moonGo.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        var moon = moonGo.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.25f, 0.31f, 0.48f);
        moon.intensity = 0.55f;

        var harness = root.AddComponent<RitualBookVelethTestScenario>();
        harness.EditorConfigurar(book, veleth, cameraGo.transform, flashlight);

        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = root;
        Debug.Log($"[EscenarioVeleth] Escena creada en {ScenePath}. Presiona Play para probarla.");
    }

    [MenuItem("Mortuorium/QA/Invocar Veleth en escenario activo")]
    public static void InvokeVelethInActiveScenario()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[EscenarioVeleth] Esta accion requiere Play Mode.");
            return;
        }

        var scenario = Object.FindAnyObjectByType<RitualBookVelethTestScenario>();
        if (scenario == null)
        {
            Debug.LogError("[EscenarioVeleth] No se encontro el harness activo.");
            return;
        }
        scenario.InvokeVelethForValidation();
        Debug.Log("[EscenarioVeleth] Veleth invocada para validacion MCP.");
    }

    private static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent,
                                              Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static Material GetMaterial(string path, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }
}
