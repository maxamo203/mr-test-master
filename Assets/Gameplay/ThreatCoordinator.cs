using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Presupuesto autoritativo de amenazas. Cada tarea normal necesita un defensor
    // distinto; un Arbmos ocupa especificamente a su propietario.
    public static class ThreatCoordinator
    {
        public const int MaxConcurrentThreats = 3;

        private static readonly HashSet<uint> NormalArbmosOwners = new();
        private static readonly HashSet<uint> LethalArbmosOwners = new();
        private static bool _sorken;
        private static bool _book;
        private static int _generation;

        public static bool SorkenActive => _sorken;
        public static bool BookActive => _book;
        public static int ActiveNormalArbmos => NormalArbmosOwners.Count;
        public static int Generation => _generation;
        public static bool HasAnyCapablePlayer => CurrentCapablePlayers() > 0;
        public static bool LethalConsequenceActive => LethalArbmosOwners.Count > 0;

        public static void ResetAll()
        {
            _sorken = false;
            _book = false;
            NormalArbmosOwners.Clear();
            LethalArbmosOwners.Clear();
            _generation++;
        }

        public static bool TryBeginSorken()
        {
            if (LethalConsequenceActive) return false;
            if (_sorken) return true;
            if (!CanFit(CurrentCapablePlayers(), GenericTasks() + 1,
                        NormalArbmosOwners.Count)) return false;
            _sorken = true;
            return true;
        }

        public static bool TryBeginSorkenAt(Vector3 target, float range,
                                            float targetRadius)
        {
            return CanAnyCapableReach(target, range, targetRadius) &&
                   TryBeginSorken();
        }

        // La entrada desde el exterior es un aviso y no consume un defensor. Esto permite
        // que el libro o un Arbmos comiencen mientras el Sorken todavía cruza la abertura.
        // Recién se registra como amenaza normal cuando ya alcanzó el interior.
        public static bool CanBeginSorkenEntryAt(Vector3 target, float range,
                                                  float targetRadius) =>
            !LethalConsequenceActive &&
            CanAnyCapableReach(target, range, targetRadius);

        public static void BeginSorkenInside() => _sorken = true;

        public static void EndSorken() => _sorken = false;

        public static bool CanBeginBook() => _book ||
            (!LethalConsequenceActive &&
             CanFit(CurrentCapablePlayers(), GenericTasks() + 1,
                    NormalArbmosOwners.Count));

        public static bool CanBeginBookAt(Vector3 target, float range,
                                          float targetRadius) =>
            CanBeginBook() && CanAnyCapableReach(target, range, targetRadius);

        public static bool TryBeginBook()
        {
            if (_book) return true;
            if (!CanBeginBook()) return false;
            _book = true;
            return true;
        }

        public static void EndBook() => _book = false;

        // El presupuesto se consulta al COMENZAR una amenaza. Una amenaza ya iniciada
        // conserva su lugar aunque Sorken entre despues; de otro modo su llegada
        // despawneaba Arbmos y congelaba un libro que ya se estaba consumiendo.
        public static bool CanKeepBook() => !_book ||
            CanKeepExistingThreat(CurrentCapablePlayers() > 0);

        public static bool TryBeginNormalArbmos(uint owner)
        {
            if (LethalConsequenceActive) return false;
            if (NormalArbmosOwners.Contains(owner)) return true;
            int capable = CurrentCapablePlayers();
            if (!IsCapable(owner) ||
                !CanFit(capable, GenericTasks(), NormalArbmosOwners.Count + 1))
                return false;
            NormalArbmosOwners.Add(owner);
            return true;
        }

        public static void EndNormalArbmos(uint owner) => NormalArbmosOwners.Remove(owner);

        public static bool CanKeepNormalArbmos(uint owner)
        {
            if (!NormalArbmosOwners.Contains(owner)) return false;
            return CanKeepExistingThreat(IsCapable(owner));
        }

        // Politica comun para eventos ya activos. Sorken, el libro u otros Arbmos no
        // aparecen aca a proposito: solamente condicionan intentos posteriores.
        public static bool CanKeepExistingThreat(bool participantCapable) =>
            participantCapable && !LethalConsequenceActive;

        public static void BeginLethalArbmos(uint owner)
        {
            NormalArbmosOwners.Remove(owner);
            LethalArbmosOwners.Add(owner);
        }

        public static void EndLethalArbmos(uint owner) =>
            LethalArbmosOwners.Remove(owner);

        public static bool CanFit(int capablePlayers, int genericTasks, int ownedTasks)
        {
            int total = Mathf.Max(0, genericTasks) + Mathf.Max(0, ownedTasks);
            return total <= Mathf.Min(MaxConcurrentThreats, Mathf.Max(0, capablePlayers));
        }

        public static bool CanAnyCapableReach(Vector3 target, float range,
                                              float targetRadius)
        {
            var net = NetworkManager.Instance;
            if (net == null) return false;
            float maxDistance = Mathf.Max(0f, range) + Mathf.Max(0f, targetRadius);

            if (IsCapable(0) && Camera.main != null &&
                Vector3.Distance(Camera.main.transform.position, target) <= maxDistance &&
                PlayerLights.HasLineOfSight(Camera.main.transform.position,
                                             target, targetRadius)) return true;

            foreach (uint clientId in net.ConnectedClients)
            {
                if (!IsCapable(clientId) ||
                    !net.TryGetClientWorldPosition(clientId, out Vector3 position)) continue;
                if (Vector3.Distance(position, target) <= maxDistance &&
                    PlayerLights.HasLineOfSight(position, target, targetRadius)) return true;
            }
            return false;
        }

        private static int GenericTasks() => (_sorken ? 1 : 0) + (_book ? 1 : 0);

        private static int CurrentCapablePlayers()
        {
            var net = NetworkManager.Instance;
            if (net == null) return 0;

            int count = Camera.main != null && ServerDeaths.IsAlive(0) ? 1 : 0;
            foreach (uint clientId in net.ConnectedClients)
                if (IsCapable(clientId)) count++;
            return count;
        }

        private static bool IsCapable(uint clientId)
        {
            if (ServerDeaths.IsDead(clientId)) return false;
            if (!TrackingReliability.PlayerIsReliable(clientId)) return false;
            var net = NetworkManager.Instance;
            if (clientId == 0)
                return Camera.main != null && net != null &&
                       net.LocalFlashlightCharge01() > 0.001f;
            return net != null && net.TryGetClientWorldPosition(clientId, out _) &&
                   net.TryGetClientForward(clientId, out _) &&
                   net.TryGetClientFlashlightCharge01(clientId, out float charge) &&
                   charge > 0.001f;
        }
    }
}
