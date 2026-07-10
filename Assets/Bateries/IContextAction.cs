namespace Bateries
{
    // Una acción asociada al botón primario (A del joystick / botón en pantalla / tecla).
    // El ContextActionController elige, cada frame, la acción disponible de mayor Priority
    // y la ejecuta al presionar. Así el mismo botón hace cosas distintas según el contexto
    // (recoger pila, prender linterna, abrir puerta, accionar interruptor…).
    //
    // Para agregar una acción nueva: implementá esta interfaz. Lo más simple es hacerlo en
    // un MonoBehaviour puesto en el mismo GameObject que el ContextActionController (se
    // auto-descubre en Start), o registrarla por código con
    // ContextActionController.Instance.Register(...).
    public interface IContextAction
    {
        // Mayor gana el contexto cuando varias acciones están disponibles a la vez.
        int Priority { get; }

        // ¿Mostrar el botón en pantalla cuando esta acción es la activa? (Ej.: recoger
        // pila = sí; prender/apagar linterna = no, se hace directo con A.)
        bool ShowActionButton { get; }

        // ¿Está disponible ahora? Si sí, devolvé true y el texto para el HUD.
        bool TryResolve(out string label);

        // Qué hace al presionar el botón.
        void Execute();
    }
}
