using System.IO;
using UnityEngine;

[RequireComponent(typeof(VelethEntity))]
public class VelethNetwork : NetworkEntity
{
    private VelethEntity _veleth;
    private Vector3 _fromRelPos;
    private Vector3 _toRelPos;
    private Quaternion _fromRelRot;
    private Quaternion _toRelRot;
    private float _interpT;
    private bool _hasState;

    private void Awake()
    {
        EntityTypeId = EntityTypeIds.Veleth;
        _veleth = GetComponent<VelethEntity>();
    }

    public override void OnNetworkSpawn()
    {
        VelethPresentation.PlayInvocation(_veleth.Position);
    }

    public override byte[] SerializeState(uint tick)
    {
        var relPos = WorldOrigin.Instance.ToRelative(_veleth.Position);
        var relRot = WorldOrigin.Instance.ToRelativeRot(_veleth.transform.rotation);

        using var ms = new MemoryStream(40);
        using var writer = new BinaryWriter(ms);
        writer.Write(NetworkId);
        writer.Write(tick);
        MsgHelper.WriteV3(writer, relPos);
        writer.Write(relRot.x);
        writer.Write(relRot.y);
        writer.Write(relRot.z);
        writer.Write(relRot.w);
        writer.Write((byte)_veleth.State);
        return ms.ToArray();
    }

    public override void ApplyState(uint tick, byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));
        reader.ReadUInt32();
        reader.ReadUInt32();
        var relPos = MsgHelper.ReadV3(reader);
        var relRot = new Quaternion(reader.ReadSingle(), reader.ReadSingle(),
                                    reader.ReadSingle(), reader.ReadSingle());
        var state = (VelethState)reader.ReadByte();

        _veleth.SetState(state);
        _fromRelPos = _hasState ? _toRelPos : relPos;
        _fromRelRot = _hasState ? _toRelRot : relRot;
        _toRelPos = relPos;
        _toRelRot = relRot;
        _interpT = 0f;
        _hasState = true;
    }

    public override byte[] SerializeInput(uint tick) => null;

    private void Update()
    {
        if (NetworkManager.Instance == null || NetworkManager.Instance.IsServer || !_hasState)
            return;

        _interpT += Time.deltaTime / NetworkManager.Instance.TickInterval;
        float t = Mathf.Clamp01(_interpT);
        _veleth.SetPositionDirectly(WorldOrigin.Instance.ToWorld(
            Vector3.Lerp(_fromRelPos, _toRelPos, t)));
        _veleth.SetRotationDirectly(WorldOrigin.Instance.ToWorldRot(
            Quaternion.Slerp(_fromRelRot, _toRelRot, t)));
    }
}

