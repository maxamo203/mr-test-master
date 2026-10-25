using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Spawning
{
    /// <summary>Reservas compartidas por todos los sistemas de objetos del mapa.</summary>
    public static class SpawnReservationRegistry
    {
        private sealed class Reservation
        {
            public string owner;
            public string key;
            public Vector3 position;
            public float radius;
        }

        private static readonly List<Reservation> Items = new();
        private static int _geometryVersion = int.MinValue;

        public static int Count => Items.Count;

        public static void BeginGeometry(int geometryVersion)
        {
            if (_geometryVersion == geometryVersion) return;
            _geometryVersion = geometryVersion;
            Items.Clear();
        }

        public static void ClearOwner(string owner)
        {
            Items.RemoveAll(item => item.owner == owner);
        }

        // Libera una reserva puntual (p. ej. el lugar de una pila recogida que va a
        // reaparecer en otro lado).
        public static void Release(string owner, string key)
        {
            Items.RemoveAll(item => item.owner == owner && item.key == key);
        }

        // Mismo chequeo que TryReserve, sin reservar.
        public static bool IsFree(Vector3 position, float radius, float extraSeparation, out string reason)
        {
            float ownRadius = Mathf.Max(0f, radius);
            for (int i = 0; i < Items.Count; i++)
            {
                Reservation other = Items[i];
                float minDistance = ownRadius + other.radius + Mathf.Max(0f, extraSeparation);
                Vector2 delta = new(position.x - other.position.x, position.z - other.position.z);
                if (delta.sqrMagnitude < minDistance * minDistance)
                {
                    reason = $"interfiere con reserva {other.owner}/{other.key}";
                    return false;
                }
            }
            reason = null;
            return true;
        }

        public static bool TryReserve(string owner, string key, Vector3 position,
                                      float radius, float extraSeparation, out string reason)
        {
            float ownRadius = Mathf.Max(0f, radius);
            if (!IsFree(position, ownRadius, extraSeparation, out reason)) return false;

            Items.Add(new Reservation
            {
                owner = owner,
                key = key,
                position = position,
                radius = ownRadius
            });
            reason = null;
            return true;
        }

        public static void ResetForTests()
        {
            Items.Clear();
            _geometryVersion = int.MinValue;
        }
    }
}
