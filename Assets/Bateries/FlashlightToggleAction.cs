using UnityEngine;

namespace Bateries
{
    // Acción contextual: prender/apagar la linterna. Prioridad baja: es el fallback cuando
    // no hay algo más específico apuntado (como una pila). Comparte el botón A con el pickup.
    public class FlashlightToggleAction : MonoBehaviour, IContextAction
    {
        [SerializeField] private int priority = 0;

        public int Priority => priority;

        private Flashlight _fl;

        private Flashlight Fl()
        {
            if (_fl == null) _fl = FindFirstObjectByType<Flashlight>();
            return _fl;
        }

        public bool TryResolve(out string label)
        {
            var fl = Fl();
            if (fl == null) { label = null; return false; }

            label = fl.isOn      ? "Apagar linterna"
                  : fl.IsEmpty   ? "Linterna sin carga"
                                 : "Prender linterna";
            return true;
        }

        public void Execute() => Fl()?.Toggle();
    }
}
