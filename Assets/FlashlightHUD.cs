using UnityEngine;

// Muestra el estado de la linterna (barra de carga) en pantalla. Es SOLO display:
// lee la carga de la Flashlight de su mismo GameObject y la dibuja. Poné este componente
// en el GameObject de la linterna.
[RequireComponent(typeof(Flashlight))]
public class FlashlightHUD : MonoBehaviour
{
    [Tooltip("Mostrar la barra de carga de la linterna.")]
    public bool showChargeBar = true;

    private Flashlight _fl;
    private GUIStyle   _labelStyle;
    private bool       _styleReady;

    private void Awake() => _fl = GetComponent<Flashlight>();

    private void OnGUI()
    {
        if (!showChargeBar || _fl == null) return;
        // Fuera de partida la linterna no opera: tampoco mostramos su barra.
        if (!_fl.Operational) return;

        // Estilo cacheado (crear GUIStyle en cada OnGUI genera GC).
        if (!_styleReady) { _labelStyle = new GUIStyle(GUI.skin.label); _styleReady = true; }

        float pct = _fl.Charge01;
        var barRect = new Rect(20, Screen.height - 40, 220, 22);
        GUI.Box(barRect, GUIContent.none);
        var fill = new Rect(barRect.x + 2, barRect.y + 2, (barRect.width - 4) * pct, barRect.height - 4);
        var prev = GUI.color;
        GUI.color = _fl.IsEmpty ? Color.red
                  : pct < 0.25f ? new Color(1f, 0.5f, 0f) : Color.green;
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        GUI.color = prev;
        GUI.Label(new Rect(barRect.x + 6, barRect.y, barRect.width, barRect.height),
                  $"Linterna {Mathf.RoundToInt(pct * 100f)}%", _labelStyle);
    }
}
