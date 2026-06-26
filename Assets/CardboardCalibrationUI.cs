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

        _scale   = PlayerPrefs.GetFloat(K_SCALE,    0.77f);
        _offsetL = PlayerPrefs.GetFloat(K_OFFSET_L, 0.043f);
        _offsetR = PlayerPrefs.GetFloat(K_OFFSET_R, 0.159f);

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

        // Tamaños proporcionales a la resolución real (sin GUI.matrix).
        // En 1080p landscape: fs≈34, bh≈86, slh≈65.
        int   fs  = Mathf.RoundToInt(Screen.height * 0.032f);
        float bh  = Screen.height * 0.08f;
        float slh = Screen.height * 0.06f;

        var lblStyle = new GUIStyle(GUI.skin.label)  { fontSize = fs };
        var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = fs };

        // Debajo de "Unirse a partida" del GameBootstrapper (~y=155px)
        var btnRect = new Rect(10, 170, 220, bh);
        if (GUI.Button(btnRect, _visible ? "Cerrar" : "Config", btnStyle))
            _visible = !_visible;

        if (!_visible) return;

        float pw = Screen.width  * 0.75f;
        float ph = Screen.height * 0.85f;
        float px = (Screen.width  - pw) * 0.5f;
        float py = (Screen.height - ph) * 0.5f;

        GUI.Box(new Rect(px - 8, py - 8, pw + 16, ph + 16), "");
        GUILayout.BeginArea(new Rect(px, py, pw, ph));

        GUILayout.Label("Calibración Cardboard", lblStyle);
        GUILayout.Space(10);

        _scale   = Slider("Zoom feed",      _scale,   0.3f, 1f,        "F2", fs, slh);
        float maxOffset = 1f - _scale;
        _offsetL = Slider("Offset ojo izq", _offsetL, 0f,   maxOffset, "F3", fs, slh);
        _offsetR = Slider("Offset ojo der", _offsetR, 0f,   maxOffset, "F3", fs, slh);
        _offsetL = Mathf.Clamp(_offsetL, 0f, maxOffset);
        _offsetR = Mathf.Clamp(_offsetR, 0f, maxOffset);

        GUILayout.Space(14);
        if (GUILayout.Button("Resetear defaults", btnStyle, GUILayout.Height(bh)))
            { _scale = 0.77f; _offsetL = 0.043f; _offsetR = 0.159f; }

        GUILayout.Space(8);
        if (GUILayout.Button("Guardar y cerrar", btnStyle, GUILayout.Height(bh)))
            { Save(); _visible = false; }

        GUILayout.EndArea();
        ApplyAll();
    }

    private float Slider(string label, float value, float min, float max, string fmt, int fs, float slh)
    {
        GUILayout.Label($"{label}: {value.ToString(fmt)}", new GUIStyle(GUI.skin.label) { fontSize = fs });
        float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Height(slh));
        GUILayout.Space(6);
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
