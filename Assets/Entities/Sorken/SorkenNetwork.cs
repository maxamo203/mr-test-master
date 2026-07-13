using System.IO;
using UnityEngine;

// Sync del Sorken: el server serializa pos (anchor-relativa) + rotacion COMPLETA
// (quaternion anchor-relativo, para soportar el pitch/roll del offset del modelo) +
// estado; los clientes interpolan la pose y aplican el estado (SorkenAnimator).
[RequireComponent(typeof(SorkenEntity))]
public class SorkenNetwork : NetworkEntity
{
    private SorkenEntity _sorken;

    private Vector3    _fromRelPos, _toRelPos;
    private Quaternion _fromRelRot, _toRelRot;
    private float      _interpT;
    private bool       _hasState;

    private void Awake()
    {
        EntityTypeId = EntityTypeIds.Sorken;
        _sorken      = GetComponent<SorkenEntity>();
    }

    public override byte[] SerializeState(uint tick)
    {
        var relPos = WorldOrigin.Instance.ToRelative(_sorken.Position);
        var relRot = WorldOrigin.Instance.ToRelativeRot(_sorken.transform.rotation);

        using var ms = new MemoryStream(40);
        using var w  = new BinaryWriter(ms);
        w.Write(NetworkId);
        w.Write(tick);
        MsgHelper.WriteV3(w, relPos);
        w.Write(relRot.x); w.Write(relRot.y); w.Write(relRot.z); w.Write(relRot.w);
        w.Write((byte)_sorken.State);
        return ms.ToArray();
    }

    public override void ApplyState(uint tick, byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        r.ReadUInt32(); r.ReadUInt32();
        var relPos = MsgHelper.ReadV3(r);
        var relRot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        var state  = (SorkenState)r.ReadByte();

        _sorken.SetState(state); // en clientes: alimenta al SorkenAnimator

        _fromRelPos = _hasState ? _toRelPos : relPos;
        _fromRelRot = _hasState ? _toRelRot : relRot;
        _toRelPos   = relPos;
        _toRelRot   = relRot;
        _interpT    = 0f;
        _hasState   = true;
    }

    // El Sorken lo controla el GameDirector del server — no envia input.
    public override byte[] SerializeInput(uint tick) => null;

    private void Update()
    {
        if (NetworkManager.Instance == null || NetworkManager.Instance.IsServer) return;
        if (!_hasState) return;

        _interpT += Time.deltaTime / NetworkManager.Instance.TickInterval;
        float t     = Mathf.Clamp01(_interpT);
        var  relPos = Vector3.Lerp(_fromRelPos, _toRelPos, t);
        var  relRot = Quaternion.Slerp(_fromRelRot, _toRelRot, t);
        _sorken.SetPositionDirectly(WorldOrigin.Instance.ToWorld(relPos));
        _sorken.SetRotationDirectly(WorldOrigin.Instance.ToWorldRot(relRot));
    }
}
