using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Scanner
{
    // Botonera de recalibración (abajo a la derecha). Dos modos:
    //
    //   • "Recalibrar anchor" (escena fija): vuelve a detectar la imagen y re-ancla,
    //     pero lo ya escaneado se queda EXACTAMENTE donde está en el mundo. Solo
    //     cambian las coordenadas relativas al nuevo anchor.
    //
    //   • "Recalibrar + mover escena": re-ancla y MUEVE lo escaneado junto con el
    //     anchor, manteniendo las coordenadas relativas.
    //
    // IMPORTANTE: NO usamos el valor de retorno de GUI.Button para disparar la
    // acción. Con el nuevo Input System en iOS, IMGUI recibe el touch-down (se ve
    // la animación de "pulsado") pero el click (down+up sobre el mismo control) no
    // se entrega de forma confiable, así que GUI.Button casi nunca devuelve true en
    // device. En su lugar detectamos el tap con EnhancedTouch (+ Mouse en editor) y
    // hacemos el hit-test contra los rects nosotros mismos — el mismo enfoque que
    // SelectionController / TransformGizmoController.
    public class RecalibrateButton : MonoBehaviour
    {
        [SerializeField] private ARImageAnchor _imageAnchor;

        private GUIStyle _btnStyle;

        // Tamaño/posición de los botones, en unidades de diseño (UIScale).
        private const float W = 300f, H = 70f, Gap = 12f, Margin = 20f, BottomLift = 200f;

        private void Awake()
        {
            if (_imageAnchor == null) _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        }

        // Rects en espacio virtual (pre-escala de UIScale).
        private void GetRects(out Rect upper, out Rect lower)
        {
            float vw = UIScale.VirtualWidth;
            float vh = UIScale.VirtualHeight;
            float x  = vw - W - Margin;
            float y2 = vh - H - BottomLift;   // botón inferior
            float y1 = y2 - H - Gap;          // botón superior
            upper = new Rect(x, y1, W, H);
            lower = new Rect(x, y2, W, H);
        }

        private void OnGUI()
        {
            UIScale.Begin();

            if (_btnStyle == null)
                _btnStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize  = 20,
                    wordWrap  = true,
                    alignment = TextAnchor.MiddleCenter,
                };

            GetRects(out var upper, out var lower);
            // Solo visual (la acción se dispara en Update). Ignoramos el retorno.
            GUI.Button(upper, "Recalibrar anchor\n(escena queda fija)", _btnStyle);
            GUI.Button(lower, "Recalibrar + mover\nescena con anchor", _btnStyle);
        }

        private void Update()
        {
            if (!TryGetTapRelease(out var tapScreenPos)) return;

            // Touch viene en píxeles con origen abajo-izquierda; los rects de GUI
            // (tras la escala de UIScale) están en píxeles con origen arriba-izq.
            float f = UIScale.Factor;
            var p = new Vector2(tapScreenPos.x, Screen.height - tapScreenPos.y);

            GetRects(out var upper, out var lower);
            if (ScaleRect(upper, f).Contains(p))      Recalibrate(keepVisualPosition: true);
            else if (ScaleRect(lower, f).Contains(p)) Recalibrate(keepVisualPosition: false);
        }

        private static Rect ScaleRect(Rect r, float f) =>
            new Rect(r.x * f, r.y * f, r.width * f, r.height * f);

        // Devuelve la posición (en píxeles, origen abajo-izq) del último tap que se
        // soltó este frame, vía EnhancedTouch (device) o Mouse (editor).
        private bool TryGetTapRelease(out Vector2 pos)
        {
            pos = Vector2.zero;

            foreach (var t in ETouch.activeTouches)
            {
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                    t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    pos = t.screenPosition;
                    return true;
                }
            }

            var ms = Mouse.current;
            if (ms != null && ms.leftButton.wasReleasedThisFrame)
            {
                pos = ms.position.ReadValue();
                return true;
            }
            return false;
        }

        private void Recalibrate(bool keepVisualPosition)
        {
            if (_imageAnchor == null) _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
            if (_imageAnchor == null) return;

            ScanStateMachine.Instance?.SetMode(ScannerMode.Calibrating);
            _imageAnchor.RestartTracking(keepVisualPosition);
        }
    }
}
