using System.Collections.Generic;
using UnityEngine;

public class EntityRegistry : MonoBehaviour
{
    public static EntityRegistry Instance { get; private set; }

    private readonly Dictionary<uint, NetworkEntity> _map = new();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Register(NetworkEntity e)   => _map[e.NetworkId] = e;
    public void Unregister(uint id)         => _map.Remove(id);

    public bool TryGet(uint id, out NetworkEntity e) => _map.TryGetValue(id, out e);

    public IEnumerable<NetworkEntity> All => _map.Values;

    public int Count => _map.Count;
}
