#if UNITY_EDITOR
using System;
using Scanner;
using Scanner.ScanV2;
using UnityEditor;
using UnityEngine;

public static class ScanV2QaScenario
{
    public static string LastResult { get; private set; } = "NOT_RUN";

    [MenuItem("Mortuorium/QA/Scan V2/Ejecutar regresion completa")]
    public static void Run()
    {
        LastResult = "RUNNING";
        if (!Application.isPlaying)
        {
            LastResult = "BLOCKED: Play Mode requerido";
            Debug.LogWarning("[QA ScanV2] Abri ScannerScene y entra en Play Mode.");
            return;
        }

        var registry = SceneRegistry.Instance;
        var fsm = ScanStateMachine.Instance;
        var scan = ScanV2Controller.Instance ?? ScanV2Controller.Ensure();
        if (registry == null || fsm == null || WorldOrigin.Instance == null || scan == null)
        {
            LastResult = "BLOCKED: faltan singletons";
            Debug.LogError("[QA ScanV2] Faltan singletons de ScannerScene.");
            return;
        }
        if (registry.Walls.Count != 0 || registry.Cubes.Count != 0 ||
            registry.Markers.Count != 0 || FloorPoint.Instance != null)
        {
            LastResult = "BLOCKED: registro no vacio";
            Debug.LogError("[QA ScanV2] Se requiere registro vacio para proteger contenido del usuario.");
            return;
        }

        bool simulatedCalibration = fsm.Current == ScannerMode.Calibrating;
        if (simulatedCalibration) fsm.SetMode(ScannerMode.Idle);
        else if (fsm.Current != ScannerMode.Idle)
        {
            LastResult = $"BLOCKED: modo {fsm.Current}";
            return;
        }

        try
        {
            Require(scan.StartCapture(), "no inicio captura");
            Require(!scan.StartCapture(), "permitio iniciar una segunda captura simultanea");
            Require(!scan.CanFinish, "permitio finalizar sin evidencia");
            Require(scan.FinishCapture() == 0 && scan.IsCapturing,
                    "finalizar vacio cerro o modifico la captura");
            scan.AddSyntheticRoomForEditor();
            Require(scan.CanFinish && scan.MaterializableObjectCount == 5,
                    $"esperaba 5 objetos listos; obtuvo {scan.MaterializableObjectCount}");
            scan.CancelCapture();
            Require(registry.Walls.Count == 0 && FloorPoint.Instance == null,
                    "cancelar modifico la escena");

            Require(scan.StartCapture(), "no reinicio captura");
            scan.AddSyntheticRoomForEditor();
            Require(scan.FinishCapture() == 5, "no materializo cuatro paredes y piso");
            Require(registry.Walls.Count == 4 && FloorPoint.Instance != null,
                    "registro materializado incompleto");
            ScanData data = registry.Capture("qa-scan-v2");
            ScanData restored = JsonUtility.FromJson<ScanData>(JsonUtility.ToJson(data));
            Require(restored != null && restored.walls.Count == 4 && restored.hasFloor,
                    "persistencia JSON incompatible");

            Require(scan.StartCapture(), "no inicio prueba de duplicados");
            scan.AddSyntheticRoomForEditor();
            Require(scan.MaterializableObjectCount == 0,
                    "un segundo escaneo identico crearia duplicados");
            scan.CancelCapture();
            scan.UndoLastMaterialization();
            Require(registry.Walls.Count == 0 && FloorPoint.Instance == null,
                    "cancelar segunda captura rompio el undo anterior");
            scan.UndoLastMaterialization();
            scan.CancelCapture();
            Require(registry.Walls.Count == 0, "undo/cancel repetidos no fueron idempotentes");

            var manualWall = WallObject.Create(new Vector3(-2f, 0f, 0f),
                                               new Vector3(2f, 0f, 0f),
                                               2.5f, 0.15f, 1);
            Require(scan.StartCapture(), "no inicio prueba con pared manual");
            scan.AddSyntheticRoomForEditor();
            Require(scan.MaterializableObjectCount == 4,
                    $"no deduplico pared manual; listos={scan.MaterializableObjectCount}");
            Require(scan.FinishCapture() == 4 && registry.Walls.Count == 4,
                    "materializacion junto a pared manual incorrecta");
            foreach (var wall in registry.Walls)
                Require(wall != null && wall.GetComponent<MeshCollider>()?.sharedMesh != null,
                        "una pared creada no es editable/colisionable");
            scan.UndoLastMaterialization();
            Require(registry.Walls.Count == 1 && registry.Walls[0] == manualWall,
                    "undo elimino o reemplazo la pared manual");
            manualWall.Delete();

            var manualFloor = FloorPoint.Create(new Vector3(0f, 0.30f, 0f));
            Require(scan.StartCapture(), "no inicio prueba con piso manual");
            scan.AddSyntheticRoomForEditor();
            Require(scan.MaterializableObjectCount == 4, "intentaria reemplazar el piso manual");
            Require(scan.FinishCapture() == 4, "no creo paredes junto al piso manual");
            Require(FloorPoint.Instance == manualFloor &&
                    Mathf.Abs(FloorPoint.Instance.LocalY - 0.30f) < 0.001f,
                    "reemplazo o movio el piso manual");
            scan.UndoLastMaterialization();
            Require(registry.Walls.Count == 0 && FloorPoint.Instance == manualFloor,
                    "undo elimino contenido manual");
            manualFloor.Delete();

            LastResult = "PASS";
            Debug.Log("[QA ScanV2] PASS - fusion, cancelacion, materializacion, persistencia, " +
                      "duplicados, undo y piso manual verificados.");
        }
        catch (Exception exception)
        {
            scan.CancelCapture();
            scan.UndoLastMaterialization();
            registry.ClearAll();
            LastResult = $"FAIL: {exception.Message}";
            Debug.LogError($"[QA ScanV2] {LastResult}\n{exception.StackTrace}");
        }
        finally
        {
            if (simulatedCalibration && fsm.Current == ScannerMode.Idle)
                fsm.SetMode(ScannerMode.Calibrating);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
