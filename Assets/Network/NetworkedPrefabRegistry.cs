using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PrefabRegistry", menuName = "Network/PrefabRegistry")]
public class NetworkedPrefabRegistry : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public byte       TypeId;
        public GameObject Prefab;
    }

    [SerializeField] private Entry[] entries;

    private Dictionary<byte, GameObject> _map;

    public GameObject Get(byte typeId)
    {
        if (_map == null)
        {
            _map = new();
            foreach (var e in entries) _map[e.TypeId] = e.Prefab;
        }
        return _map.TryGetValue(typeId, out var p) ? p
            : throw new Exception($"No prefab registered for typeId {typeId}");
    }
}
