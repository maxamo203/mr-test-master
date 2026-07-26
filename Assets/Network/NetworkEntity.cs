using UnityEngine;

public abstract class NetworkEntity : MonoBehaviour
{
    // VisibleTo == Everyone => entidad compartida (broadcast a todos, comportamiento
    // clasico: Sorken, pilas). Un clientId concreto => entidad DIRIGIDA: la ve solo ese
    // jugador (spawn/estado/despawn se envian a el solo). Lo usa el Arbmos (alucinacion
    // individual). 0 = host: el host la ve y no se envia a ningun cliente.
    public const uint Everyone = 0xFFFFFFFF;

    public uint NetworkId     { get; private set; }
    public bool IsOwned       { get; private set; }
    public uint OwnerClientId { get; private set; }
    public byte EntityTypeId  { get; protected set; }
    public uint VisibleTo     { get; private set; } = Everyone;

    public void Initialize(uint networkId, bool isOwned, uint ownerClientId)
    {
        NetworkId     = networkId;
        IsOwned       = isOwned;
        OwnerClientId = ownerClientId;
    }

    public void SetVisibleTo(uint clientId) => VisibleTo = clientId;

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
    public const byte Sorken = 3;   // entidad de terror (gameplay nuevo): emerge/chase/grab
    public const byte Arbmos = 4;   // alucinacion de cordura (individual por jugador, spawn dirigido)

    // Pilas: una rareza por typeId a partir de esta base. rarityIndex 0/1/2 =>
    // typeId 10/11/12. El typeId encodea la rareza para que el cliente instancie el
    // prefab correcto (SpawnEntityMsg solo lleva typeId, sin payload extra).
    public const byte BatteryBase = 10;
}
