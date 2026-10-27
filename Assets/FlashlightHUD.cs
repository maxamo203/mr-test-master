using UnityEngine;
using Scanner;   // UIScale
using T = MortuoriumTheme;

// Muestra el estado de la linterna (barra de carga) en pantalla. Es SOLO display:
// lee la carga de la Flashlight de su mismo GameObject y la dibuja. Poné este componente
// en el GameObject de la linterna. Se dibuja abajo-izquierda (fila 0), con la barra de
// cordura apilada encima (ver SanityHUD).
[RequireComponent(typeof(Flashlight))]
public class FlashlightHUD : MonoBehaviour
{
    [Tooltip("Mostrar la barra de carga de la linterna.")]
    public bool showChargeBar = true;

    private Flashlight _fl;

    private void Awake() => _fl = GetComponent<Flashlight>();

    private void OnGUI()
    {
        if (!showChargeBar || _fl == null) return;
        // Fuera de partida la linterna no opera: tampoco mostramos su barra.
        if (!_fl.Operational) return;

        UIScale.Begin();
        float pct = _fl.Charge01;
        var modo = _fl.Mode;

        // Ámbar (luz); más brillante con la intensa; rojo latiendo con poca carga.
        bool baja = _fl.IsEmpty || pct <= BateriaBaja;
        Color color = baja ? T.Red : modo == FlashlightMode.Bright ? Intensa : Ambar;
        if (baja) color.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));

        string tag = null;
        Color? colorTag = null;
        if (modo == FlashlightMode.Bright)
        {
            tag = ProporcionIntensa();
            colorTag = Intensa;
        }
        else if (modo == FlashlightMode.Off && !_fl.IsEmpty)
        {
            tag = "APAGADA";
            colorTag = T.CreamDim;
        }

        var icono = new T.IconoHud
        {
            tex = Rediseno.IconosHud.Pila, color = color, nivel = pct,
            rayo = modo == FlashlightMode.Bright, texRayo = Rediseno.IconosHud.Rayo,
        };
        // Abajo-izq; en Cardboard, una copia bajo cada ojo (ver T.HudBarRects).
        T.HudFila(UIScale.VirtualWidth, UIScale.VirtualHeight, 0, pct, color, icono,
                  "LINTERNA", Porcentaje(pct), tag, colorTag);
    }

    private const float BateriaBaja = 0.2f;
    private static readonly Color Ambar   = new(0.914f, 0.761f, 0.478f, 1f);   // #e9c27a
    private static readonly Color Intensa = new(1f, 0.851f, 0.541f, 1f);       // #ffd98a

    // "INTENSA ×3": cuánto más gasta la intensa que la normal (cacheado: OnGUI corre varias
    // veces por frame toda la noche).
    private string _tagIntensa;
    private float _tagDim = -1f, _tagBright = -1f;
    private string ProporcionIntensa()
    {
        if (_tagIntensa != null && _tagDim == _fl.drainPerSecond && _tagBright == _fl.brightDrainPerSecond)
            return _tagIntensa;
        _tagDim = _fl.drainPerSecond;
        _tagBright = _fl.brightDrainPerSecond;
        _tagIntensa = _tagDim > 0.01f ? $"INTENSA ×{Mathf.RoundToInt(_tagBright / _tagDim)}" : "INTENSA";
        return _tagIntensa;
    }

    private static readonly string[] _porcentajes = CrearPorcentajes();
    private static string[] CrearPorcentajes()
    {
        var a = new string[101];
        for (int i = 0; i <= 100; i++) a[i] = i + "%";
        return a;
    }
    public static string Porcentaje(float pct01) => _porcentajes[Mathf.Clamp(Mathf.RoundToInt(pct01 * 100f), 0, 100)];
}
