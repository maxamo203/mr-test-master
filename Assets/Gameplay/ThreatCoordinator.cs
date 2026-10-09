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
        private static bool _sorken;
        private static bool _book;
        private static int _generation;

        public static bool SorkenActive => _sorken;
        public static bool BookActive => _book;
        public static int ActiveNormalArbmos => NormalArbmosOwners.Count;
        public static int Generation => _generation;

        public static void ResetAll()
        {
            _sorken = false;
            _book = false;
            NormalArbmosOwners.Clear();
            _generation++;
        }

        public static bool TryBeginSorken()
        {
            if (_sorken) return true;
            if (!CanFit(CurrentCapablePlayers(), GenericTasks() + 1,
                        NormalArbmosOwners.Count)) return false;
            _sorken = true;
            return true;
        }

        public static void EndSorken() => _sorken = false;

        public static bool CanBeginBook() => _book ||
            CanFit(CurrentCapablePlayers(), GenericTasks() + 1,
                   NormalArbmosOwners.Count);

        public static bool TryBeginBook()
        {
            if (_book) return true;
            if (!CanBeginBook()) return false;
            _book = true;
            return true;
        }

        public static void EndBook() => _book = false;

        // Sorken tiene prioridad sobre libro, y libro sobre Arbmos normal. Al perder
        // capacidad, el libro solo se pausa si ya no alcanza ni ignorando los Arbmos.
        public static bool CanKeepBook() => !_book ||
            CanFit(CurrentCapablePlayers(), GenericTasks(), 0);

        public static bool TryBeginNormalArbmos(uint owner)
        {
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
            return IsCapable(owner) &&
                   CanFit(CurrentCapablePlayers(), GenericTasks(),
                          NormalArbmosOwners.Count);
        }

        public static bool CanFit(int capablePlayers, int genericTasks, int ownedTasks)
        {
            int total = Mathf.Max(0, genericTasks) + Mathf.Max(0, ownedTasks);
            return total <= Mathf.Min(MaxConcurrentThreats, Mathf.Max(0, capablePlayers));
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
            if (clientId == 0) return Camera.main != null;
            var net = NetworkManager.Instance;
            return net != null && net.TryGetClientWorldPosition(clientId, out _) &&
                   net.TryGetClientForward(clientId, out _);
        }
    }
}
