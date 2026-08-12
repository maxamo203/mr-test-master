#if UNITY_EDITOR
using System;
using Scanner;
using Scanner.AutoScan;
using UnityEditor;
using UnityEngine;

// Escenario de regresión no destructivo para ejecutar sobre ScannerScene en Play Mode.
// Sólo corre si el registro está vacío y limpia todo lo creado antes de terminar.
public static class AutoScanQaScenario
{
    public static string LastResult { get; private set; } = "NOT_RUN";

    [MenuItem("Mortuorium/QA/AutoScan/Ejecutar regresion completa")]
    public static void Run()
    {
        LastResult = "RUNNING";
        if (!Application.isPlaying)
        {
            LastResult = "BLOCKED: Play Mode requerido";
            Debug.LogWarning("[QA AutoScan] Abrí ScannerScene y entrá en Play Mode.");
            return;
        }

        var registry = SceneRegistry.Instance;
        var fsm = ScanStateMachine.Instance;
        var scan = AutoScanController.Instance ?? AutoScanController.Ensure();
        if (registry == null || fsm == null || WorldOrigin.Instance == null || scan == null)
        {
            LastResult = "BLOCKED: faltan singletons";
            Debug.LogError("[QA AutoScan] Faltan singletons de ScannerScene.");
            return;
        }
        if (registry.Walls.Count != 0 || registry.Cubes.Count != 0 ||
            registry.Markers.Count != 0 || FloorPoint.Instance != null)
        {
            LastResult = "BLOCKED: registro no vacio";
            Debug.LogError("[QA AutoScan] El escenario requiere un registro vacío para no tocar contenido del usuario.");
            return;
        }
        bool simulatedCalibration = fsm.Current == ScannerMode.Calibrating;
        if (simulatedCalibration)
        {
            // ScannerScene espera una captura fisica antes de salir de Calibrating.
            // El escenario sintetico satisface esa precondicion solo durante esta prueba.
            fsm.SetMode(ScannerMode.Idle);
        }
        else if (fsm.Current != ScannerMode.Idle)
        {
            LastResult = $"BLOCKED: modo {fsm.Current}";
            Debug.LogError($"[QA AutoScan] El scanner debe estar Idle; estado actual: {fsm.Current}.");
            return;
        }

        try
        {
            ValidateCancel(scan, registry);
            ValidateMaterializationPersistenceAndUndo(scan, registry);
            ValidateExistingManualFloor(scan, registry);
            LastResult = "PASS";
            Debug.Log("[QA AutoScan] PASS - cancelación, materialización, persistencia, " +
                      "idempotencia, undo y piso manual verificados.");
        }
        catch (Exception exception)
        {
            scan.CancelCapture();
            scan.UndoLastMaterialization();
            registry.ClearAll();
            LastResult = $"FAIL: {exception.Message}";
            Debug.LogError($"[QA AutoScan] FAIL - {exception.Message}\n{exception.StackTrace}");
        }
        finally
        {
            if (simulatedCalibration && fsm.Current == ScannerMode.Idle)
                fsm.SetMode(ScannerMode.Calibrating);
        }
    }

    private static void ValidateCancel(AutoScanController scan, SceneRegistry registry)
    {
        Require(scan.StartCapture(), "no pudo iniciar captura para probar cancelación");
        Require(!scan.CanFinish, "permitió finalizar sin superficies estables");
        scan.AddSyntheticRoomForEditor();
        Require(scan.CanFinish && scan.MaterializableObjectCount == 5,
                $"esperaba 5 objetos listos; obtuvo {scan.MaterializableObjectCount}");
        scan.CancelCapture();
        Require(registry.Walls.Count == 0 && FloorPoint.Instance == null,
                "cancelar modificó la escena editable");
    }

    private static void ValidateMaterializationPersistenceAndUndo(
        AutoScanController scan, SceneRegistry registry)
    {
        Require(scan.StartCapture(), "no pudo iniciar captura para materializar");
        scan.AddSyntheticRoomForEditor();
        int created = scan.FinishCapture();
        Require(created == 5, $"materializó {created} objetos en vez de 5");
        Require(registry.Walls.Count == 4 && FloorPoint.Instance != null,
                "la escena no contiene cuatro paredes y un piso");

        ScanData captured = registry.Capture("qa-autoscan");
        string json = JsonUtility.ToJson(captured);
        ScanData restored = JsonUtility.FromJson<ScanData>(json);
        Require(restored != null && restored.walls.Count == 4 && restored.hasFloor,
                "ScanData no conserva paredes y piso al serializar");

        Require(scan.StartCapture(), "no pudo iniciar la comprobación de duplicados");
        scan.AddSyntheticRoomForEditor();
        Require(!scan.CanFinish && scan.MaterializableObjectCount == 0,
                "un segundo escaneo idéntico generaría duplicados");
        scan.CancelCapture();

        scan.UndoLastMaterialization();
        Require(registry.Walls.Count == 0 && FloorPoint.Instance == null,
                "undo no restauró el registro vacío");
    }

    private static void ValidateExistingManualFloor(
        AutoScanController scan, SceneRegistry registry)
    {
        const float manualFloorY = 0.35f;
        FloorPoint manualFloor = FloorPoint.Create(new Vector3(0f, manualFloorY, 0f));

        Require(scan.StartCapture(), "no pudo iniciar prueba con piso manual");
        scan.AddSyntheticRoomForEditor();
        Require(scan.MaterializableObjectCount == 4,
                "intentó reemplazar el piso manual existente");
        int created = scan.FinishCapture();
        Require(created == 4 && FloorPoint.Instance == manualFloor,
                "reemplazó el piso manual durante la materialización");
        foreach (var wall in registry.Walls)
            Require(Mathf.Abs(wall.ALocal.y - manualFloorY) < 0.001f &&
                    Mathf.Abs(wall.BLocal.y - manualFloorY) < 0.001f,
                    "una pared no respetó la altura del piso manual");

        scan.UndoLastMaterialization();
        Require(registry.Walls.Count == 0 && FloorPoint.Instance == manualFloor,
                "undo eliminó un piso que existía antes de AutoScan");
        manualFloor.Delete();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
