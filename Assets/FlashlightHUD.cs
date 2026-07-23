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

        Color fill = _fl.IsEmpty ? T.Red
                   : pct < 0.25f ? T.Tan
                                 : T.Green;

        var r = T.HudBarRect(UIScale.VirtualWidth, UIScale.VirtualHeight, fila: 0);
        T.Barra(r, pct, fill, "LINTERNA", $"{Mathf.RoundToInt(pct * 100f)}%");
    }
}
