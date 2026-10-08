using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Registro SERVER-authoritative de jugadores muertos (por clientId; 0 = host).
    public static class ServerDeaths
    {
        private static readonly HashSet<uint> _dead = new();

        public static int Count => _dead.Count;
        public static bool IsDead(uint clientId) => _dead.Contains(clientId);
        public static bool IsAlive(uint clientId) => !_dead.Contains(clientId);

        public static bool Kill(uint clientId, Transform killer = null)
        {
            if (!_dead.Add(clientId)) return false;
            bool allPlayersDead = AllPlayersDead();
            Vector3 face = FacePosition(killer);
            uint killerNetworkId = KillerNetworkId(killer);
            if (allPlayersDead)
                foreach (uint deadId in _dead) DispatchDeath(deadId, face, killerNetworkId, true);
            else
                DispatchDeath(clientId, face, killerNetworkId, false);
            return true;
        }

        public static int KillAll(Transform killer = null)
        {
            var killed = new List<uint>();
            if (_dead.Add(0)) killed.Add(0);

            var net = NetworkManager.Instance;
            if (net != null)
                foreach (var cid in net.ConnectedClients)
                    if (_dead.Add(cid)) killed.Add(cid);

            Vector3 face = FacePosition(killer);
            uint killerNetworkId = KillerNetworkId(killer);
            // Igual que Kill: si con esto cayeron todos, avisar también a los que ya
            // estaban muertos (y quizás espectando) para que vuelvan a su pantalla final.
            if (killed.Count > 0 && AllPlayersDead())
                foreach (uint deadId in _dead) DispatchDeath(deadId, face, killerNetworkId, true);
            else
                foreach (uint cid in killed) DispatchDeath(cid, face, killerNetworkId, false);
            return killed.Count;
        }

        private static void DispatchDeath(uint clientId, Vector3 facePosition, uint killerNetworkId, bool allPlayersDead)
        {
            if (clientId == 0) LocalDeath.Ensure().Die(facePosition, killerNetworkId, allPlayersDead);
            else NetworkManager.Instance?.ServerSendPlayerDied(clientId, facePosition, killerNetworkId, allPlayersDead);
        }

        private static bool AllPlayersDead()
        {
            var net = NetworkManager.Instance;
            int players = 1 + (net != null ? net.ConnectedClients.Count : 0);
            return _dead.Count >= players;
        }

        private static uint KillerNetworkId(Transform killer)
        {
            var entity = killer != null ? killer.GetComponentInParent<NetworkEntity>() : null;
            return entity != null ? entity.NetworkId : 0;
        }

        private static Vector3 FacePosition(Transform killer)
        {
            if (killer == null) return Camera.main != null
                ? Camera.main.transform.position + Camera.main.transform.forward * 0.8f
                : Vector3.zero;

            var renderers = killer.GetComponentsInChildren<Renderer>();
            bool found = false;
            Bounds bounds = default;
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!found) return killer.position + Vector3.up * 1.2f;
            return new Vector3(bounds.center.x, bounds.max.y - bounds.extents.y * 0.18f, bounds.center.z);
        }

        public static void Reset() => _dead.Clear();
    }
}
