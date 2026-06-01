using UnityEngine;

namespace Scanner
{
    // Botonera de recalibración (abajo a la derecha). Dos modos:
    //
    //   • "Recalibrar anchor" (escena fija): vuelve a detectar la imagen y re-ancla,
    //     pero lo ya escaneado se queda EXACTAMENTE donde está en el mundo. Solo
    //     cambian las coordenadas relativas al nuevo anchor. Útil cuando la escena
    //     ya está bien colocada y solo querés refrescar el anchor.
    //
    //   • "Recalibrar + mover escena": re-ancla y MUEVE lo escaneado junto con el
    //     anchor, manteniendo las coordenadas relativas. Útil cuando el marcador es
    //     la verdad de referencia y la escena debe seguirlo.
    //
    // Ambos sobreviven a la recalibración porque ARImageAnchor suelta WorldOrigin
    // del anchor antes de destruirlo.
    public class RecalibrateButton : MonoBehaviour
    {
        [SerializeField] private ARImageAnchor _imageAnchor;

        private GUIStyle _btnStyle;

        private void Awake()
        {
            if (_imageAnchor == null) _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
        }

        private void OnGUI()
        {
            UIScale.Begin();
            float vw = UIScale.VirtualWidth;
            float vh = UIScale.VirtualHeight;

            if (_btnStyle == null)
                _btnStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize  = 20,
                    wordWrap  = true,
                    alignment = TextAnchor.MiddleCenter,
                };

            const float w = 300f, h = 70f, gap = 12f, margin = 20f;
            // Los subimos bastante del borde inferior: en iPhone la zona de abajo
            // (gestos/home indicator) se come los toques, y además así no chocan
            // con el botón COLOCAR central.
            const float bottomLift = 200f;

            float x  = vw - w - margin;
            float y2 = vh - h - bottomLift;   // botón inferior
            float y1 = y2 - h - gap;          // botón superior

            if (GUI.Button(new Rect(x, y1, w, h), "Recalibrar anchor\n(escena queda fija)", _btnStyle))
                Recalibrate(keepVisualPosition: true);

            if (GUI.Button(new Rect(x, y2, w, h), "Recalibrar + mover\nescena con anchor", _btnStyle))
                Recalibrate(keepVisualPosition: false);
        }

        private void Recalibrate(bool keepVisualPosition)
        {
            if (_imageAnchor == null) return;
            _imageAnchor.RestartTracking(keepVisualPosition);
            ScanStateMachine.Instance?.SetMode(ScannerMode.Calibrating);
        }
    }
}
