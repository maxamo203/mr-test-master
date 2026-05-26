using System.IO;
using UnityEngine;

[RequireComponent(typeof(Sorker))]
public class SorkerNetwork : NetworkEntity
{
    private Sorker _sorker;

    // Interpolacion (solo clientes) — guardamos en espacio RELATIVO al anchor
    // para que cualquier re-estimacion del AR se aplique automaticamente cada frame.
    private Vector3    _fromRelPos, _toRelPos;
    private float      _fromRelRotY, _toRelRotY;
    private int        _fromHp,  _toHp;
    private float      _interpT;
    private bool       _hasState;

    private void Awake()
    {
        EntityTypeId = EntityTypeIds.Sorker;
        _sorker      = GetComponent<Sorker>();
    }

    // ── Server: serializar posicion relativa al anchor ────────────────────

    public override byte[] SerializeState(uint tick)
    {
        var relPos = WorldOrigin.Instance.ToRelative(_sorker.Position);

        using var ms = new MemoryStream(32);
        using var w  = new BinaryWriter(ms);
        w.Write(NetworkId);
        w.Write(tick);
        MsgHelper.WriteV3(w, relPos);
        w.Write(_sorker.Health);
        w.Write(_sorker.transform.eulerAngles.y); // rotacion Y en grados
        return ms.ToArray();
    }

    // ── Cliente: recibir estado y bufferizar para interpolar ─────────────

    public override void ApplyState(uint tick, byte[] data)
    {
        using var r   = new BinaryReader(new MemoryStream(data));
        r.ReadUInt32(); r.ReadUInt32();
        var relPos   = MsgHelper.ReadV3(r);
        int hp       = r.ReadInt32();
        float rotY   = r.ReadSingle();

        _fromRelPos  = _hasState ? _toRelPos  : relPos;
        _fromRelRotY = _hasState ? _toRelRotY : rotY;
        _fromHp      = _hasState ? _toHp      : hp;
        _toRelPos    = relPos;
        _toRelRotY   = rotY;
        _toHp        = hp;
        _interpT     = 0f;
        _hasState    = true;
    }

    // Sorker es controlado por la IA del servidor — no envía input
    public override byte[] SerializeInput(uint tick) => null;

    // ── Cliente: interpolar posicion visual ───────────────────────────────

    private void Update()
    {
        if (NetworkManager.Instance == null || NetworkManager.Instance.IsServer) return;
        if (!_hasState) return;

        _interpT += Time.deltaTime / NetworkManager.Instance.TickInterval;
        float t      = Mathf.Clamp01(_interpT);
        // Convertimos de relativo → world AHORA, con el WorldOrigin actual,
        // para que cualquier re-estimacion del anchor no cause drift.
        var relPos   = Vector3.Lerp(_fromRelPos, _toRelPos, t);
        var rotY     = Mathf.LerpAngle(_fromRelRotY, _toRelRotY, t);
        _sorker.SetPositionDirectly(WorldOrigin.Instance.ToWorld(relPos));
        _sorker.SetRotationDirectly(Quaternion.Euler(0f, rotY, 0f));
        _sorker.SetHealthDirectly(Mathf.RoundToInt(Mathf.Lerp(_fromHp, _toHp, t)));
    }
}
