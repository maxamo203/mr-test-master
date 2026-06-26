using UnityEngine;

public abstract class NetworkEntity : MonoBehaviour
{
    public uint NetworkId     { get; private set; }
    public bool IsOwned       { get; private set; }
    public uint OwnerClientId { get; private set; }
    public byte EntityTypeId  { get; protected set; }

    public void Initialize(uint networkId, bool isOwned, uint ownerClientId)
    {
        NetworkId     = networkId;
        IsOwned       = isOwned;
        OwnerClientId = ownerClientId;
    }

    // Server: serialize current game state to bytes for broadcast
    public abstract byte[] SerializeState(uint tick);

    // Client: apply an authoritative state snapshot received from server
    public abstract void ApplyState(uint tick, byte[] data);

    // Client (owned only): serialize local input, apply prediction, return bytes.
    // Return null if this entity type never sends input (e.g. NPCs).
    public virtual byte[] SerializeInput(uint tick) => null;

    // Server: apply a player input payload received from the owning client
    public virtual void ApplyInputData(uint tick, byte[] data) { }

    public virtual void OnNetworkSpawn()   { }
    public virtual void OnNetworkDespawn() { }
}

public static class EntityTypeIds
{
    public const byte Player = 1;
    public const byte Sorker = 2;
}
