using UnityEngine;

namespace Gameplay
{
    // Total de reliquias recogidas ESTA noche (contador compartido, no por jugador —
    // ver Collectibles.CollectibleSpawnManager). El server lo escribe al confirmar un
    // pickup y lo transmite a todos con NetworkManager.ServerBroadcastCollectibleTotal;
    // el host recibe ese mismo valor por invocación local (no le vuelve por red), así
    // que host y clientes siempre coinciden sin ramas especiales por dispositivo.
    public static class NightLoot
    {
        public static int Total { get; private set; }

        public static void SetTotal(int total) => Total = Mathf.Max(0, total);

        public static void Reset() => Total = 0;
    }
}
