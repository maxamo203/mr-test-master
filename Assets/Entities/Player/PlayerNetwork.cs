using System.IO;
using UnityEngine;

// En AR la posicion del jugador ES la posicion de la camara fisica.
// No hay WASD — el jugador se mueve en el mundo real.
// El owned player trackea Camera.main; los remotos interpolamos sus posiciones.
[RequireComponent(typeof(PlayerEntity))]
public class PlayerNetwork : NetworkEntity
{
    private PlayerEntity _player;

    // Interpolacion para jugadores remotos — en espacio RELATIVO al anchor.
    private Vector3 _fromRelPos, _toRelPos;
    private int     _fromHp,  _toHp;
    private float   _interpT;
    private bool    _hasState;

    private void Awake()
    {
        EntityTypeId = EntityTypeIds.Player;
        _player      = GetComponent<PlayerEntity>();
    }

    // ── Server: serializar estado en coordenadas relativas al anchor ──────

    public override byte[] SerializeState(uint tick)
    {
        // Convertir a espacio relativo al anchor antes de enviar
        var relPos = WorldOrigin.Instance.ToRelative(_player.Position);

        using var ms = new MemoryStream(28);
        using var w  = new BinaryWriter(ms);
        w.Write(NetworkId);
        w.Write(tick);
        MsgHelper.WriteV3(w, relPos);
        w.Write(_player.Health);
        return ms.ToArray();
    }

    // ── Cliente: aplicar estado recibido del servidor ─────────────────────

    public override void ApplyState(uint tick, byte[] data)
    {
        using var r      = new BinaryReader(new MemoryStream(data));
        r.ReadUInt32();             // NetworkId
        r.ReadUInt32();             // tick
        var relPos = MsgHelper.ReadV3(r);
        int srvHp  = r.ReadInt32();

        if (IsOwned)
        {
            // Posicion propia siempre viene de Camera.main (AR).
            // Solo sincronizamos salud desde el servidor.
            _player.SetHealthDirectly(srvHp);
        }
        else
        {
            // Jugador remoto: guardar en relativo, convertir a world en Update()
            BufferInterp(relPos, srvHp);
        }
    }

    // ── Cliente (owned): enviar posicion de camara al servidor ───────────

    public override byte[] SerializeInput(uint tick)
    {
        // Actualizar posicion local desde la camara AR
        if (Camera.main != null)
            _player.SetPositionDirectly(Camera.main.transform.position);

        // Enviar en espacio relativo al anchor
        var relPos = WorldOrigin.Instance.ToRelative(_player.Position);
        var attack = GetAttackInput();

        var msg = new PlayerInputMsg
        {
            NetworkId = NetworkId,
            Tick      = tick,
            Position  = relPos,
            Attack    = attack,
        };
        return msg.Serialize();
    }

    // ── Servidor: aplicar input recibido del cliente ─────────────────────

    public override void ApplyInputData(uint tick, byte[] data)
    {
        var input    = PlayerInputMsg.Deserialize(data);
        var worldPos = WorldOrigin.Instance.ToWorld(input.Position);
        _player.SetPositionDirectly(worldPos);
        if (input.Attack) ProcessAttack();
    }

    // ── Update: owned sigue camara; remotos interpolan ───────────────────

    private void Update()
    {
        if (IsOwned)
        {
            // Seguir la camara AR directamente (sin esperar el tick de red)
            if (Camera.main != null)
                _player.SetPositionDirectly(Camera.main.transform.position);
            return;
        }

        if (!_hasState) return;

        _interpT += Time.deltaTime / NetworkManager.Instance.TickInterval;
        float t      = Mathf.Clamp01(_interpT);
        // Convertir relativo → world cada frame para seguir las re-estimaciones del anchor.
        var relPos   = Vector3.Lerp(_fromRelPos, _toRelPos, t);
        _player.SetPositionDirectly(WorldOrigin.Instance.ToWorld(relPos));
        _player.SetHealthDirectly(Mathf.RoundToInt(Mathf.Lerp(_fromHp, _toHp, t)));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void BufferInterp(Vector3 relPos, int hp)
    {
        _fromRelPos = _hasState ? _toRelPos : relPos;
        _fromHp     = _hasState ? _toHp     : hp;
        _toRelPos   = relPos;
        _toHp       = hp;
        _interpT    = 0f;
        _hasState   = true;
    }

    private bool GetAttackInput()
    {
        // Tap en pantalla o tecla espacio en editor
        bool tap = Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began;
#if UNITY_EDITOR
        tap = tap || Input.GetKeyDown(KeyCode.Space);
#endif
        return tap;
    }

    private void ProcessAttack()
    {
        foreach (var entity in EntityRegistry.Instance.All)
        {
            if (entity.EntityTypeId != EntityTypeIds.Sorker) continue;
            var sorker = entity.GetComponent<Sorker>();
            if (sorker == null || sorker.IsDead) continue;
            if (Vector3.Distance(transform.position, sorker.transform.position) <= _player.AttackRange)
            {
                sorker.TakeDamage(_player.AttackDamage);
                Debug.Log($"[Player] Golpeó Sorker {entity.NetworkId} → {sorker.Health} HP");
            }
        }
    }
}
