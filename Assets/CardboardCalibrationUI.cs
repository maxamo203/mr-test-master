using UnityEngine;
using UnityEngine.XR.ARFoundation;

// Panel de calibración en vivo para el modo Cardboard.
// Muestra sliders sobre la imagen (IMGUI) para ajustar la separación de ojos
// y el zoom del feed de cámara sin necesidad de recompilar.
// Los valores se persisten con PlayerPrefs.
//
// Activar/desactivar: triple tap en la pantalla.
[RequireComponent(typeof(MRCardboardController))]
public class CardboardCalibrationUI : MonoBehaviour
{
    // Claves de persistencia
    const string K_OFFSET_L = "cardboard_offsetL";
    const string K_OFFSET_R = "cardboard_offsetR";
    const string K_SCALE    = "cardboard_scale";

    [SerializeField] private Material _cropLeft;
    [SerializeField] private Material _cropRight;

    private MRCardboardController _controller;

    private float _offsetL;
    private float _offsetR;
    private float _scale;

    private bool  _visible = false;

    private void Awake()
    {
        _controller = GetComponent<MRCardboardController>();

        _scale   = PlayerPrefs.GetFloat(K_SCALE,    0.70f);
        _offsetL = PlayerPrefs.GetFloat(K_OFFSET_L, 0.122f);
        _offsetR = PlayerPrefs.GetFloat(K_OFFSET_R, 0.268f);

        ApplyAll();
    }

    private bool IsCardboardActive()
    {
        foreach (var bg in FindObjectsByType<ARCameraBackground>(FindObjectsSortMode.None))
        {
            var cam = bg.GetComponent<Camera>();
            if (cam != null && Mathf.Abs(cam.rect.width - 0.5f) < 0.1f) return true;
        }
        return false;
    }

    private void Update()
    {
        if (IsCardboardActive()) ApplyAll();
    }

    private void OnGUI()
    {
        if (!IsCardboardActive()) return;

        var btnRect = new Rect(10, 10, 80, 50);
        if (GUI.Button(btnRect, _visible ? "Cerrar" : "Config"))
            _visible = !_visible;

        if (!_visible) return;

        float w = Screen.width  * 0.45f;
        float h = Screen.height * 0.72f;
        float x = (Screen.width  - w) * 0.5f;
        float y = (Screen.height - h) * 0.5f;

        GUI.Box(new Rect(x - 8, y - 8, w + 16, h + 16), "");

        GUILayout.BeginArea(new Rect(x, y, w, h));
        GUILayout.Label("Calibración Cardboard");
        GUILayout.Space(10);

        _scale   = Slider("Zoom feed (subporción)", _scale,   0.3f, 1f,        "F2");
        float maxOffset = 1f - _scale;
        _offsetL = Slider("Offset ojo izq",         _offsetL, 0f,   maxOffset, "F3");
        _offsetR = Slider("Offset ojo der",         _offsetR, 0f,   maxOffset, "F3");
        _offsetL = Mathf.Clamp(_offsetL, 0f, maxOffset);
        _offsetR = Mathf.Clamp(_offsetR, 0f, maxOffset);

        GUILayout.Space(10);

        if (GUILayout.Button("Resetear defaults"))
        {
            _scale = 0.70f; _offsetL = 0.122f; _offsetR = 0.268f;
        }

        GUILayout.Space(6);
        if (GUILayout.Button("Guardar y cerrar"))
        {
            Save();
            _visible = false;
        }

        GUILayout.EndArea();

        ApplyAll();
    }

    private float Slider(string label, float value, float min, float max, string fmt)
    {
        GUILayout.Label($"{label}: {value.ToString(fmt)}");
        float v = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.Space(4);
        return v;
    }

    private void ApplyAll()
    {
        if (_cropLeft != null)
        {
            _cropLeft.SetFloat("_CropOffsetX", _offsetL);
            _cropLeft.SetFloat("_CropScaleX",  _scale);
        }
        if (_cropRight != null)
        {
            _cropRight.SetFloat("_CropOffsetX", _offsetR);
            _cropRight.SetFloat("_CropScaleX",  _scale);
        }

        // Solo toca cámaras en modo split (rect.width ≈ 0.5 = modo Cardboard).
        // Ignora cámaras full-screen y cámaras sin ARCameraBackground.
        foreach (var bg in FindObjectsByType<ARCameraBackground>(FindObjectsSortMode.None))
        {
            var cam = bg.GetComponent<Camera>();
            if (cam == null) continue;
            if (Mathf.Abs(cam.rect.width - 0.5f) > 0.1f) continue; // no es split

            if (cam.rect.x < 0.1f && _cropLeft != null)
            {
                bg.useCustomMaterial = true;
                bg.customMaterial    = _cropLeft;
            }
            else if (cam.rect.x >= 0.4f && _cropRight != null)
            {
                bg.useCustomMaterial = true;
                bg.customMaterial    = _cropRight;
            }
        }

    }

    private void Save()
    {
        PlayerPrefs.SetFloat(K_OFFSET_L, _offsetL);
        PlayerPrefs.SetFloat(K_OFFSET_R, _offsetR);
        PlayerPrefs.SetFloat(K_SCALE,    _scale);
        PlayerPrefs.Save();
    }
}
