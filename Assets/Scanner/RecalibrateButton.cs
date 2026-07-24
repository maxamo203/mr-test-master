using UnityEngine;

namespace Scanner
{
    // OBSOLETO: los botones de recalibrar (mantener o no la escena fija) se integraron
    // a la botonera de herramientas del escáner (ReticleController.DrawHerramientas,
    // ítems "RECALIBRAR (fijo)" / "RECALIBRAR (+mover)"). Este componente queda como
    // cáscara vacía para no romper la referencia en ScannerScene; se puede quitar del
    // ScannerRoot al editar la escena.
    public class RecalibrateButton : MonoBehaviour
    {
        [SerializeField] private ARImageAnchor _imageAnchor;   // referencia legacy (sin uso)
    }
}
