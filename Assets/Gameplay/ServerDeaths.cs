using System.Collections.Generic;

namespace Gameplay
{
    // Registro SERVER-authoritative de jugadores muertos (por clientId; 0 = host).
    // Compartido por los directores de entidades (GameDirector / ArbmosDirector) para
    // que un jugador ya atrapado por una entidad no siga siendo objetivo de la otra, y
    // para despachar la muerte una sola vez al dispositivo correcto.
    public static class ServerDeaths
    {
        private static readonly HashSet<uint> _dead = new();

        public static int Count => _dead.Count;
        public static bool IsDead(uint clientId) => _dead.Contains(clientId);
        public static bool IsAlive(uint clientId) => !_dead.Contains(clientId);

        // Marca al jugador como muerto y le avisa (host: local; cliente: por red).
        // Devuelve false si ya estaba muerto (no re-despacha).
        public static bool Kill(uint clientId)
        {
            if (!_dead.Add(clientId)) return false;
            if (clientId == 0) LocalDeath.Ensure().Die();
            else               NetworkManager.Instance?.ServerSendPlayerDied(clientId);
            return true;
        }

        // Mata a TODOS los jugadores vivos de una: game over compartido, no la muerte
        // individual de siempre (lo usa el libro ritual al cerrarse). Devuelve cuántos
        // murieron en esta llamada.
        public static int KillAll()
        {
            int n = Kill(0) ? 1 : 0;   // host

            var net = NetworkManager.Instance;
            if (net != null)
                foreach (var cid in net.ConnectedClients)
                    if (Kill(cid)) n++;

            return n;
        }

        public static void Reset() => _dead.Clear();
    }
}
