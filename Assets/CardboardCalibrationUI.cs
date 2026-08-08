using UnityEngine;

// Calibración del modo Cardboard: SOLO estado, persistido en PlayerPrefs y expuesto como
// propiedades. El menú de pausa (Opciones→Cardboard) las edita y `MRCardboardController`
// las lee cada frame. Son dos grupos INDEPENDIENTES:
//
//   1) ÓPTICA DEL VISOR (Scale / OffsetL / OffsetR) — zoom del recorte y posición horizontal
//      de la ventana de cada ojo dentro de la RenderTexture. Alinea la imagen con las lentes
//      del visor físico. No tiene nada que ver con el 3D: se calibra hasta que las dos
//      mitades fusionen cómodas.
//
//   2) ESTÉREO REAL (Estereo3D / Ipd / Convergencia) — si el mundo se rendea DOS veces, una
//      por ojo, separando la cámara ±Ipd/2. Es lo único que produce sensación de profundidad.
//      `Convergencia` es la distancia a la que un objeto cae EXACTAMENTE en el mismo punto de
//      pantalla en los dos ojos (paralaje cero): más cerca "sale" hacia el jugador, más lejos
//      "se hunde". Ver el comentario de cabecera de MRCardboardController.
[RequireComponent(typeof(MRCardboardController))]
public class CardboardCalibrationUI : MonoBehaviour
{
    const string K_OFFSET_L = "cardboard_offsetL";
    const string K_OFFSET_R = "cardboard_offsetR";
    const string K_SCALE    = "cardboard_scale";
    const string K_STEREO   = "cardboard_stereo3d";
    const string K_IPD      = "cardboard_ipd";
    const string K_CONV     = "cardboard_convergencia";

    private float _offsetL;
    private float _offsetR;
    private float _scale;
    private bool  _stereo3d;
    private float _ipd;
    private float _conv;

    // Rangos/clamps centralizados (los usa el menú para dibujar los sliders).
    public const float ScaleMin = 0.3f, ScaleMax = 1f;
    // IPD en METROS. 0 = mono (los dos ojos ven lo mismo, no se paga el doble render);
    // 0.064 es la separación interpupilar humana promedio. Más que eso hace ver el mundo
    // en miniatura (hiperestéreo), menos lo aplana pero es más cómodo sobre passthrough mono.
    public const float IpdMin = 0f, IpdMax = 0.08f;
    // Distancia de paralaje cero, en metros. Para una habitación, 2–3 m es lo natural.
    public const float ConvMin = 0.5f, ConvMax = 10f;

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

    public bool Estereo3D
    {
        get => _stereo3d;
        set { _stereo3d = value; Store(); }
    }
    public float Ipd
    {
        get => _ipd;
        set { _ipd = Mathf.Clamp(value, IpdMin, IpdMax); Store(); }
    }
    public float Convergencia
    {
        get => _conv;
        set { _conv = Mathf.Clamp(value, ConvMin, ConvMax); Store(); }
    }

    // Con IPD 0 los dos pases darían la MISMA imagen: no vale pagar el doble render.
    public bool EstereoActivo => _stereo3d && _ipd > 0.001f;

    private void Awake()
    {
        _scale    = PlayerPrefs.GetFloat(K_SCALE,    0.77f);
        _offsetL  = PlayerPrefs.GetFloat(K_OFFSET_L, 0.064f);
        _offsetR  = PlayerPrefs.GetFloat(K_OFFSET_R, 0.183f);
        _stereo3d = PlayerPrefs.GetInt(K_STEREO, 1) != 0;
        _ipd      = Mathf.Clamp(PlayerPrefs.GetFloat(K_IPD,  0.055f), IpdMin,  IpdMax);
        _conv     = Mathf.Clamp(PlayerPrefs.GetFloat(K_CONV, 2.5f),   ConvMin, ConvMax);
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
        PlayerPrefs.SetInt  (K_STEREO,   _stereo3d ? 1 : 0);
        PlayerPrefs.SetFloat(K_IPD,      _ipd);
        PlayerPrefs.SetFloat(K_CONV,     _conv);
    }

    // Flush a disco. El menú de pausa lo llama al salir del submenú / cerrarse.
    public void Save()
    {
        Store();
        PlayerPrefs.Save();
    }
}
