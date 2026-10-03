#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Scanner;   // UIBlocker

// Movimiento del jugador para el PLAY MODE DEL EDITOR: WASD para caminar y mouse capturado
// para mirar. En el editor no hay tracking AR (ver ARImageAnchor.EditorStub), así
// que la cámara se queda clavada en el origen y no hay forma de recorrer el cuarto
// escaneado ni de ver los efectos de pantalla completa desde otro ángulo. Mueve la cámara
// AR directamente, que es lo que el resto del juego lee como posición del jugador
// (PlayerNetwork, GameDirector, TensionSystem...), así que a efectos de la sesión el
// personaje se está moviendo de verdad.
//
// Controles:
//   WASD            caminar (horizontal, relativo a hacia dónde estás mirando)
//   Shift           correr
//   Espacio / E     subir      |   Ctrl / Q   bajar
//   clic en el mundo  capturar mouse y mirar libremente
//   Escape             liberar mouse
//
// TIER: editor-only. El archivo entero está dentro de #if UNITY_EDITOR: no se compila en
// ningún build (ni development ni release). Se prende/apaga desde el menú
// Mortuorium > Controles WASD en Play (EditorPrefs, de esta máquina).
public class EditorPlayerControls : MonoBehaviour
{
    public const string KeyActivo = "mortuorium_controles_wasd";

    public static bool Activo
    {
        get => EditorPrefs.GetBool(KeyActivo, true);
        set => EditorPrefs.SetBool(KeyActivo, value);
    }

    private const float VelCaminar   = 2.2f;    // m/s, paso humano
    private const float VelCorrer    = 6f;      // m/s con Shift
    private const float VelVertical  = 1.6f;    // m/s subiendo/bajando
    private const float GradosPorPx  = 0.15f;   // sensibilidad del arrastre
    private const float PitchMax     = 85f;

    private Transform _cam;
    private Behaviour _poseDriver;   // TrackedPoseDriver de la cámara AR, si lo hay
    private float _yaw, _pitch;
    private bool  _cursorCapturado;
    private bool  _controlActivoAnterior;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!Activo) return;
        var go = new GameObject("EditorPlayerControls");
        DontDestroyOnLoad(go);
        go.AddComponent<EditorPlayerControls>();
        Debug.Log("EditorPlayerControls: WASD para caminar, Shift correr, Espacio/E subir, " +
                  "Ctrl/Q bajar, clic para capturar el mouse y Escape para liberarlo. " +
                  "Se apaga en Mortuorium > Controles WASD en Play.");
    }

    private void Update()
    {
        bool nocheEnCurso = NetworkManager.Instance != null &&
                            NetworkManager.Instance.GameStarted;
        bool escaneando = IsScannerScene(SceneManager.GetActiveScene().name);
        bool controlActivo = nocheEnCurso || escaneando;
        if (!controlActivo)
        {
            _controlActivoAnterior = false;
            LiberarCursor();
            return;
        }

        if (!ResolverCamara()) return;

        // El menú y el lobby mantienen el cursor libre. La noche y ScannerScene usan
        // primera persona: se captura al entrar, Escape lo libera para tocar botones y
        // un clic fuera de la UI vuelve a capturarlo.
        if (!_controlActivoAnterior)
        {
            _controlActivoAnterior = true;
            SincronizarAngulosConCamara();
            CapturarCursor();
        }

        // El menú de pausa se queda con el input (y con el teclado, si estás editando un
        // valor a mano): mover la cámara desde atrás sería un accidente, no una acción.
        if (Gamepad.PauseMenuController.IsOpen)
        {
            LiberarCursor();
            return;
        }

        Mirar();
        Caminar();
    }

    public static bool IsScannerScene(string sceneName) => sceneName == "ScannerScene";

    // La cámara AR se crea con la escena y cambia al cambiar de escena; además hay que
    // callar al TrackedPoseDriver, que si el editor llegara a entregarle una pose (XR
    // Simulation, un device conectado) pisaría el transform en el mismo frame.
    private bool ResolverCamara()
    {
        if (_cam != null) return true;

        var cam = Camera.main;
        if (cam == null) return false;

        _cam   = cam.transform;
        var e  = _cam.eulerAngles;
        _yaw   = e.y;
        _pitch = NormalizarPitch(e.x);

        // Por nombre de tipo: hay dos TrackedPoseDriver distintos (Input System y el
        // viejo de SpatialTracking) y no queremos atarnos a cuál está en el prefab.
        _poseDriver = null;
        foreach (var mb in cam.GetComponents<MonoBehaviour>())
        {
            if (mb == null || mb.GetType().Name != "TrackedPoseDriver") continue;
            _poseDriver = mb;
            mb.enabled  = false;
            break;
        }
        return true;
    }

    private void Mirar()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        var keyboard = Keyboard.current;
        if (_cursorCapturado && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            LiberarCursor();
            return;
        }

        if (!_cursorCapturado)
        {
            bool click = mouse.leftButton.wasPressedThisFrame ||
                         mouse.rightButton.wasPressedThisFrame;
            if (!click || UIBlocker.IsPointerOver(mouse.position.ReadValue())) return;

            // Re-sincronizar por si otro sistema rotó la cámara mientras el cursor
            // estaba libre (menú, cambio de escena o tracking simulado).
            SincronizarAngulosConCamara();
            CapturarCursor();
        }

        var d = mouse.delta.ReadValue();
        if (d.sqrMagnitude <= 0f) return;

        _yaw   += d.x * GradosPorPx;
        _pitch  = Mathf.Clamp(_pitch - d.y * GradosPorPx, -PitchMax, PitchMax);
        _cam.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void SincronizarAngulosConCamara()
    {
        if (_cam == null) return;
        var e  = _cam.eulerAngles;
        _yaw   = e.y;
        _pitch = NormalizarPitch(e.x);
    }

    private void CapturarCursor()
    {
        _cursorCapturado = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void LiberarCursor()
    {
        if (!_cursorCapturado && Cursor.lockState == CursorLockMode.None) return;
        _cursorCapturado = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Caminar()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float x = 0f, z = 0f, y = 0f;
        if (kb.aKey.isPressed) x -= 1f;
        if (kb.dKey.isPressed) x += 1f;
        if (kb.sKey.isPressed) z -= 1f;
        if (kb.wKey.isPressed) z += 1f;
        if (kb.spaceKey.isPressed)     y += 1f;
        if (kb.leftCtrlKey.isPressed || kb.qKey.isPressed)  y -= 1f;

        if (x == 0f && z == 0f && y == 0f) return;

        // Horizontal relativo al yaw actual (no al pitch): mirar al piso no te hunde.
        var plano = Quaternion.Euler(0f, _cam.eulerAngles.y, 0f) * new Vector3(x, 0f, z);
        if (plano.sqrMagnitude > 1f) plano.Normalize();

        float vel = (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) ? VelCorrer : VelCaminar;
        _cam.position += (plano * vel + Vector3.up * (y * VelVertical)) * Time.deltaTime;
    }

    private static float NormalizarPitch(float eulerX) => eulerX > 180f ? eulerX - 360f : eulerX;

    private void OnDestroy()
    {
        // Devolver la cámara como estaba por si el objeto se destruye en pleno play.
        LiberarCursor();
        if (_poseDriver != null) _poseDriver.enabled = true;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) LiberarCursor();
    }
}
#endif
