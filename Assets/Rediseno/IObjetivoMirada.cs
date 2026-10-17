using UnityEngine;

namespace Rediseno
{
    // Lo implementan las acciones contextuales que apuntan a un objeto concreto (recoger
    // pila, recoger reliquia). El control por mirada lo usa para saber si seguís mirando
    // LA MISMA cosa mientras se llena el anillo, y a qué distancia está (acercarse lo
    // recoge más rápido).
    public interface IObjetivoMirada
    {
        // El objeto apuntado ahora, o null.
        Transform ObjetivoApuntado { get; }
    }
}
