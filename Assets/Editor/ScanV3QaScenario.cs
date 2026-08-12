#if UNITY_EDITOR
using System;
using System.IO;
using Scanner;
using Scanner.ScanV3;
using UnityEditor;
using UnityEngine;

public static class ScanV3QaScenario
{
    public static string LastResult { get; private set; } = "NOT_RUN";

    [MenuItem("Mortuorium/QA/Scan V3 Atlas/Ejecutar regresion completa")]
    public static void Run()
    {
        LastResult = "RUNNING";
        if (!Application.isPlaying) { LastResult = "BLOCKED: Play Mode"; return; }
        var registry = SceneRegistry.Instance;
        var fsm = ScanStateMachine.Instance;
        var atlas = ScanV3Controller.Instance ?? ScanV3Controller.Ensure();
        if (registry == null || fsm == null || WorldOrigin.Instance == null || atlas == null)
        { LastResult = "BLOCKED: singletons"; return; }
        if (registry.Walls.Count > 0 || registry.Cubes.Count > 0 ||
            registry.Markers.Count > 0 || FloorPoint.Instance != null)
        { LastResult = "BLOCKED: registro no vacio"; return; }

        bool simulatedCalibration = fsm.Current == ScannerMode.Calibrating;
        if (simulatedCalibration) fsm.SetMode(ScannerMode.Idle);
        try
        {
            ValidateCancelAndBundle(atlas, registry);
            ValidateMaterialization(atlas, registry);
            ValidateRollback(atlas, registry);
            ValidateManualContent(atlas, registry);
            LastResult = "PASS";
            Debug.Log("[QA ScanV3] PASS - bundle, grafo, reintegracion, persistencia, " +
                      "deduplicacion, rollback y contenido manual verificados.");
        }
        catch (Exception exception)
        {
            atlas.CancelCapture();
            atlas.UndoLastMaterialization();
            registry.ClearAll();
            LastResult = "FAIL: " + exception.Message;
            Debug.LogError($"[QA ScanV3] {LastResult}\n{exception.StackTrace}");
        }
        finally
        {
            if (simulatedCalibration && fsm.Current == ScannerMode.Idle)
                fsm.SetMode(ScannerMode.Calibrating);
        }
    }

    private static void ValidateCancelAndBundle(ScanV3Controller atlas, SceneRegistry registry)
    {
        Require(atlas.StartCapture(), "no inicio Atlas");
        Require(!atlas.StartCapture(), "permitio captura simultanea");
        string path = atlas.CapturePath;
        Require(Directory.Exists(path) && File.Exists(Path.Combine(path, "manifest.json")),
                "bundle incremental ausente");
        Require(atlas.FinishCapture() == 0 && atlas.IsCapturing,
                "finalizar vacio cerro captura");
        atlas.AddSyntheticRoomForEditor();
        Require(atlas.CanFinish && atlas.AcceptedKeyframes == 3,
                "bundle sintetico no quedo listo");
        atlas.CancelCapture();
        Require(!Directory.Exists(path), "cancelar no borro evidencia local");
        Require(registry.Walls.Count == 0 && FloorPoint.Instance == null,
                "cancelar modifico escena");
    }

    private static void ValidateMaterialization(ScanV3Controller atlas, SceneRegistry registry)
    {
        Require(atlas.StartCapture(), "no reinicio Atlas");
        atlas.AddSyntheticRoomForEditor();
        string path = atlas.CapturePath;
        int created = atlas.FinishCapture();
        Require(created == 5, $"creo {created} objetos en vez de 5");
        Require(!Directory.Exists(path), "exito no limpio bundle temporal");
        Require(registry.Walls.Count == 4 && FloorPoint.Instance != null,
                "registro Atlas incompleto");
        foreach (var wall in registry.Walls)
            Require(wall.GetComponent<MeshCollider>()?.sharedMesh != null,
                    "pared Atlas sin collider editable");
        ScanData data = registry.Capture("qa-atlas");
        ScanData restored = JsonUtility.FromJson<ScanData>(JsonUtility.ToJson(data));
        Require(restored != null && restored.walls.Count == 4 && restored.hasFloor,
                "persistencia Atlas incompatible");
        Require(atlas.StartCapture(), "no inicio captura posterior");
        atlas.CancelCapture();
        atlas.UndoLastMaterialization();
        Require(registry.Walls.Count == 0 && FloorPoint.Instance == null,
                "captura posterior cancelada rompio undo Atlas");
    }

    private static void ValidateRollback(ScanV3Controller atlas, SceneRegistry registry)
    {
        Require(atlas.StartCapture(), "no inicio prueba rollback");
        atlas.AddSyntheticRoomForEditor();
        atlas.ForceMaterializationFailureForEditor = true;
        Require(atlas.FinishCapture() == 0, "fallo inyectado no activo rollback");
        Require(registry.Walls.Count == 0 && FloorPoint.Instance == null,
                "rollback dejo objetos parciales");
        Require(atlas.IsCapturing && Directory.Exists(atlas.CapturePath),
                "fallo destruyo evidencia recuperable");
        atlas.CancelCapture();
    }

    private static void ValidateManualContent(ScanV3Controller atlas, SceneRegistry registry)
    {
        var manualWall = WallObject.Create(new Vector3(-2f, 0f, 0f),
                                           new Vector3(2f, 0f, 0f), 2.5f, 0.15f, 1);
        var manualFloor = FloorPoint.Create(new Vector3(0f, 0.25f, 0f));
        Require(atlas.StartCapture(), "no inicio con contenido manual");
        atlas.AddSyntheticRoomForEditor();
        int created = atlas.FinishCapture();
        Require(created == 3, $"esperaba 3 paredes nuevas; creo {created}");
        Require(registry.Walls.Count == 4 && FloorPoint.Instance == manualFloor,
                "materializacion altero contenido manual");
        atlas.UndoLastMaterialization();
        Require(registry.Walls.Count == 1 && registry.Walls[0] == manualWall &&
                FloorPoint.Instance == manualFloor,
                "undo Atlas elimino contenido manual");
        manualWall.Delete();
        manualFloor.Delete();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
