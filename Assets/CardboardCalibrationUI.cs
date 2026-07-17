using UnityEngine;

// Estado de calibración del modo Cardboard. Antes dibujaba su propio panel IMGUI con un
// botón "Config" en pantalla; ahora es HEADLESS: solo mantiene/aplica/persiste los valores
// (zoom del feed, offset de cada ojo, IPD) y los expone como propiedades para que el menú
// de pausa (Opciones → Cardboard) los edite. El botón de pantalla se eliminó.
//
// Los valores se persisten con PlayerPrefs. Se sigue requiriendo el MRCardboardController
// en el mismo GameObject (dueño de las cámaras estéreo y del IPD).
[RequireComponent(typeof(MRCardboardController))]
public class CardboardCalibrationUI : MonoBehaviour
{
    // Claves de persistencia
    const string K_OFFSET_L = "cardboard_offsetL";
    const string K_OFFSET_R = "cardboard_offsetR";
    const string K_SCALE    = "cardboard_scale";
    const string K_IPD      = "cardboard_ipd";

    [SerializeField] private Material _cropLeft;
    [SerializeField] private Material _cropRight;

    private MRCardboardController _controller;

    private float _offsetL;
    private float _offsetR;
    private float _scale;
    private float _ipd;

    // ── API pública para el menú de pausa (Opciones → Cardboard) ──────────────────
    // Rangos y clamps centralizados acá; los setters aplican al toque (materiales / IPD)
    // y guardan en memoria (el flush a disco se hace con Save(), al cerrar el menú).
    public const float ScaleMin = 0.3f, ScaleMax = 1f;
    public const float IpdMin   = 0f,   IpdMax   = 0.15f;

    public float Scale
    {
        get => _scale;
        set { _scale = Mathf.Clamp(value, ScaleMin, ScaleMax); ClampOffsets(); ApplyAll(); Store(); }
    }
    public float OffsetL
    {
        get => _offsetL;
        set { _offsetL = Mathf.Clamp(value, 0f, MaxOffset); ApplyAll(); Store(); }
    }
    public float OffsetR
    {
        get => _offsetR;
        set { _offsetR = Mathf.Clamp(value, 0f, MaxOffset); ApplyAll(); Store(); }
    }
    public float Ipd
    {
        get => _ipd;
        set { _ipd = Mathf.Clamp(value, IpdMin, IpdMax); if (_controller != null) _controller.SetIPD(_ipd); Store(); }
    }

    // Offset máximo de cada ojo dado el zoom actual (no puede pasarse del borde del feed).
    public float MaxOffset => 1f - _scale;

    private void Awake()
    {
        _controller = GetComponent<MRCardboardController>();

        _scale   = PlayerPrefs.GetFloat(K_SCALE,    0.77f);
        _offsetL = PlayerPrefs.GetFloat(K_OFFSET_L, 0.064f);
        _offsetR = PlayerPrefs.GetFloat(K_OFFSET_R, 0.183f);
        // Default 0 = objetos virtuales en la MISMA posición en ambos ojos (sin parallax).
        // Subirlo agrega efecto de profundidad, a costa de que el passthrough (monoscópico)
        // no acompañe. Ver menú Opciones → Cardboard.
        _ipd     = PlayerPrefs.GetFloat(K_IPD,      0f);

        ClampOffsets();
        ApplyAll();
        if (_controller != null) _controller.SetIPD(_ipd);
    }

    private void Update()
    {
        // Mientras el estéreo está activo re-aplicamos por si el ARCameraBackground
        // recreó/reseteó su material (igual que antes).
        if (IsCardboardActive()) ApplyAll();
    }

    private bool IsCardboardActive()
    {
        return _controller != null && _controller.CardboardActive;
    }

    private void ClampOffsets()
    {
        _offsetL = Mathf.Clamp(_offsetL, 0f, MaxOffset);
        _offsetR = Mathf.Clamp(_offsetR, 0f, MaxOffset);
    }

    private void ApplyAll()
    {
        // Letterbox vertical: preserva el aspecto NATIVO del feed (barras negras arriba/abajo)
        // en vez de estirarlo. Cada ojo usa medio ancho de pantalla → el horizontal queda
        // comprimido 2×; para que no se vea estirado, comprimimos el vertical en la misma
        // proporción. La relación isotrópica exacta (se acopla sola al zoom X):
        //   _CropScaleY = 2·scale ,  _CropOffsetY = 0.5 − scale
        // (scale>0.5 → barras negras; scale<0.5 → recorta el feed; scale=0.5 → sin barras).
        float cropScaleY  = 2f * _scale;
        float cropOffsetY = 0.5f - _scale;

        if (_cropLeft != null)
        {
            _cropLeft.SetFloat("_CropOffsetX", _offsetL);
            _cropLeft.SetFloat("_CropScaleX",  _scale);
            _cropLeft.SetFloat("_CropScaleY",  cropScaleY);
            _cropLeft.SetFloat("_CropOffsetY", cropOffsetY);
        }
        if (_cropRight != null)
        {
            _cropRight.SetFloat("_CropOffsetX", _offsetR);
            _cropRight.SetFloat("_CropScaleX",  _scale);
            _cropRight.SetFloat("_CropScaleY",  cropScaleY);
            _cropRight.SetFloat("_CropOffsetY", cropOffsetY);
        }
    }

    // Guarda los valores en PlayerPrefs (sin flush). Store lo hacen los setters.
    private void Store()
    {
        PlayerPrefs.SetFloat(K_OFFSET_L, _offsetL);
        PlayerPrefs.SetFloat(K_OFFSET_R, _offsetR);
        PlayerPrefs.SetFloat(K_SCALE,    _scale);
        PlayerPrefs.SetFloat(K_IPD,      _ipd);
    }

    // Flush a disco. El menú de pausa lo llama al salir del submenú / cerrarse.
    public void Save()
    {
        Store();
        PlayerPrefs.Save();
    }
}
