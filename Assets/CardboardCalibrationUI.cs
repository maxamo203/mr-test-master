using UnityEngine;

// Calibración del modo Cardboard: SOLO estado (zoom del feed + offset horizontal por ojo),
// persistido en PlayerPrefs y expuesto como propiedades. El menú de pausa (Opciones→Cardboard)
// las edita y `MRCardboardController` las lee cada frame para armar el uvRect/letterbox de cada
// ojo. Ya NO toca materiales ni IPD: el rewrite a RenderTexture compone passthrough+virtuales
// juntos, así que el recorte por ojo se hace en la RawImage, no en un shader de feed.
[RequireComponent(typeof(MRCardboardController))]
public class CardboardCalibrationUI : MonoBehaviour
{
    const string K_OFFSET_L = "cardboard_offsetL";
    const string K_OFFSET_R = "cardboard_offsetR";
    const string K_SCALE    = "cardboard_scale";

    private float _offsetL;
    private float _offsetR;
    private float _scale;

    // Rangos/clamps centralizados (los usa el menú para dibujar los sliders).
    public const float ScaleMin = 0.3f, ScaleMax = 1f;

    // Offset máximo de cada ojo dado el zoom actual (no puede pasarse del borde de la RT).
    public float MaxOffset => 1f - _scale;

    public float Scale
    {
        get => _scale;
        set { _scale = Mathf.Clamp(value, ScaleMin, ScaleMax); ClampOffsets(); Store(); }
    }
    public float OffsetL
    {
        get => _offsetL;
        set { _offsetL = Mathf.Clamp(value, 0f, MaxOffset); Store(); }
    }
    public float OffsetR
    {
        get => _offsetR;
        set { _offsetR = Mathf.Clamp(value, 0f, MaxOffset); Store(); }
    }

    private void Awake()
    {
        _scale   = PlayerPrefs.GetFloat(K_SCALE,    0.77f);
        _offsetL = PlayerPrefs.GetFloat(K_OFFSET_L, 0.064f);
        _offsetR = PlayerPrefs.GetFloat(K_OFFSET_R, 0.183f);
        ClampOffsets();
    }

    private void ClampOffsets()
    {
        _offsetL = Mathf.Clamp(_offsetL, 0f, MaxOffset);
        _offsetR = Mathf.Clamp(_offsetR, 0f, MaxOffset);
    }

    // Guarda en PlayerPrefs (sin flush; el flush lo hace Save()).
    private void Store()
    {
        PlayerPrefs.SetFloat(K_OFFSET_L, _offsetL);
        PlayerPrefs.SetFloat(K_OFFSET_R, _offsetR);
        PlayerPrefs.SetFloat(K_SCALE,    _scale);
    }

    // Flush a disco. El menú de pausa lo llama al salir del submenú / cerrarse.
    public void Save()
    {
        Store();
        PlayerPrefs.Save();
    }
}
