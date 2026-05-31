using UnityEngine;

namespace Scanner
{
    // Boton global "Recalibrar Anchor". Llama ARImageAnchor.RestartTracking() y
    // pone la FSM en modo Calibrating. Los objetos colocados sobreviven porque
    // son hijos de WorldOrigin, que se reparentea al nuevo anchor preservando
    // los localPosition.
    public class RecalibrateButton : MonoBehaviour
    {
        [SerializeField] private ARImageAnchor _imageAnchor;

        private void Awake()
        {
            if (_imageAnchor == null) _imageAnchor = FindFirstObjectByType<ARImageAnchor>();
        }

        private void OnGUI()
        {
            float w = 220, h = 50;
            if (GUI.Button(new Rect(Screen.width - w - 10, Screen.height - h - 10, w, h), "Recalibrar Anchor"))
            {
                if (_imageAnchor != null)
                {
                    _imageAnchor.RestartTracking();
                    ScanStateMachine.Instance?.SetMode(ScannerMode.Calibrating);
                }
            }
        }
    }
}
