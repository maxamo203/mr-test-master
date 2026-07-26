using System.IO;
using UnityEngine;

// Sync del Arbmos. A diferencia del Sorken, NO va en broadcast: el server lo spawnea
// DIRIGIDO a un solo jugador (ServerSpawnFor) y el WorldState se filtra por VisibleTo,
// asi cada jugador ve unicamente SU alucinacion. El server serializa pos (anchor-
// relativa) + rotacion completa + estado + flags de presentacion (aura / letal /
// distorsion); el dueño interpola la pose y aplica todo a su ArbmosEntity.
[RequireComponent(typeof(ArbmosEntity))]
public class ArbmosNetwork : NetworkEntity
{
    private ArbmosEntity _arbmos;

    private Vector3    _fromRelPos, _toRelPos;
    private Quaternion _fromRelRot, _toRelRot;
    private float      _interpT;
    private bool       _hasState;

    private void Awake()
    {
        EntityTypeId = EntityTypeIds.Arbmos;
        _arbmos      = GetComponent<ArbmosEntity>();
    }

    public override void OnNetworkSpawn()
    {
        // La copia que se dibuja aca (host: la propia; cliente: la recibida) se registra
        // como Active para que el HUD de distorsion la use. Las copias que el server
        // simula para otros jugadores vienen con Rendered=false y no se registran.
        _arbmos.RefreshActive();
        if (_arbmos.Rendered) ArbmosDistortionHUD.Ensure();
    }

    public override byte[] SerializeState(uint tick)
    {
        var relPos = WorldOrigin.Instance.ToRelative(_arbmos.Position);
        var relRot = WorldOrigin.Instance.ToRelativeRot(_arbmos.transform.rotation);

        byte flags = 0;
        if (_arbmos.AuraOn) flags |= 0x01;
        if (_arbmos.Lethal) flags |= 0x02;
        byte distort = (byte)Mathf.RoundToInt(Mathf.Clamp01(_arbmos.Distort01) * 255f);

        using var ms = new MemoryStream(42);
        using var w  = new BinaryWriter(ms);
        w.Write(NetworkId);
        w.Write(tick);
        MsgHelper.WriteV3(w, relPos);
        w.Write(relRot.x); w.Write(relRot.y); w.Write(relRot.z); w.Write(relRot.w);
        w.Write((byte)_arbmos.State);
        w.Write(flags);
        w.Write(distort);
        return ms.ToArray();
    }

    public override void ApplyState(uint tick, byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        r.ReadUInt32(); r.ReadUInt32();
        var relPos  = MsgHelper.ReadV3(r);
        var relRot  = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        var state   = (ArbmosState)r.ReadByte();
        byte flags  = r.ReadByte();
        byte distort = r.ReadByte();

        _arbmos.SetState(state);
        _arbmos.SetAura((flags & 0x01) != 0);
        _arbmos.SetLethal((flags & 0x02) != 0);
        _arbmos.SetDistort(distort / 255f);

        _fromRelPos = _hasState ? _toRelPos : relPos;
        _fromRelRot = _hasState ? _toRelRot : relRot;
        _toRelPos   = relPos;
        _toRelRot   = relRot;
        _interpT    = 0f;
        _hasState   = true;
    }

    // El Arbmos lo controla el ArbmosDirector del server — no envia input.
    public override byte[] SerializeInput(uint tick) => null;

    private void Update()
    {
        if (NetworkManager.Instance == null || NetworkManager.Instance.IsServer) return;
        if (!_hasState) return;

        _interpT += Time.deltaTime / NetworkManager.Instance.TickInterval;
        float t     = Mathf.Clamp01(_interpT);
        var  relPos = Vector3.Lerp(_fromRelPos, _toRelPos, t);
        var  relRot = Quaternion.Slerp(_fromRelRot, _toRelRot, t);
        _arbmos.SetPositionDirectly(WorldOrigin.Instance.ToWorld(relPos));
        _arbmos.SetRotationDirectly(WorldOrigin.Instance.ToWorldRot(relRot));
    }
}
