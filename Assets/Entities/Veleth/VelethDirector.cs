using System;
using System.Collections.Generic;
using UnityEngine;
using Gameplay;
using Scanner;

// Persecucion SERVER-AUTHORITATIVE de Veleth. Se invoca una sola vez al perder el
// libro, no responde a la linterna y vuelve a calcular la ruta mientras el objetivo
// se mueve. En multijugador continua hasta atrapar a todos los jugadores vivos.
public class VelethDirector : MonoBehaviour
{
    public static VelethDirector Instance { get; private set; }

    public bool IsHunting => _running;
    public uint CurrentTarget { get; private set; }
    public event Action<uint> OnPlayerCaught;

    private NightConfig _night;
    private VelethEntity _veleth;
    private uint _netId;
    private bool _running;
    private float _repathTimer;
    private float _grabTimer;
    private readonly List<Vector3> _path = new();
    private int _pathIndex;

    public static VelethDirector Ensure()
    {
        if (Instance == null)
        {
            var go = new GameObject("VelethDirector");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<VelethDirector>();
        }
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool StartHunt(Vector3 invocationWorldPosition)
    {
        var net = NetworkManager.Instance;
        if (net == null || !net.IsServer || WorldOrigin.Instance == null ||
            EntityRegistry.Instance == null)
            return false;

        StopRun();
        _night = GameSession.Instance != null ? GameSession.Instance.SelectedNight : null;
        if (_night == null) _night = ScriptableObject.CreateInstance<NightConfig>();

        SorkerNav.Ensure();
        invocationWorldPosition.y = FloorWorldY(invocationWorldPosition.y);
        try
        {
            _netId = net.ServerSpawn(EntityTypeIds.Veleth, invocationWorldPosition, 0);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Veleth] No se pudo crear la entidad: {exception.Message}");
            _netId = 0;
            return false;
        }

        _veleth = GetVeleth(_netId);
        if (_veleth == null)
        {
            if (_netId != 0) net.ServerDespawn(_netId);
            _netId = 0;
            return false;
        }

        _veleth.SetState(VelethState.Hunting);
        _path.Clear();
        _pathIndex = 0;
        _repathTimer = 0f;
        _grabTimer = 0f;
        CurrentTarget = 0;
        _running = true;
        Debug.Log("[Veleth] Invocada: comienza la persecucion inevitable.");
        return true;
    }

    public void StopRun()
    {
        _running = false;
        CurrentTarget = 0;
        _path.Clear();
        _pathIndex = 0;
        _grabTimer = 0f;
        _veleth = null;
        _netId = 0;
    }

    private void Update()
    {
        if (!_running || _veleth == null) return;
        var net = NetworkManager.Instance;
        if (net == null || !net.IsServer) return;

        float dt = Time.deltaTime;
        if (_grabTimer > 0f)
        {
            _grabTimer -= dt;
            if (_grabTimer > 0f) return;
            _veleth.SetState(VelethState.Hunting);
            _repathTimer = 0f;
        }

        if (!NearestAlivePlayer(_veleth.Position, out uint targetId, out Vector3 targetPos))
        {
            EndNightByVeleth();
            return;
        }

        CurrentTarget = targetId;
        if (HorizontalDistance(_veleth.Position, targetPos) <= _night.velethGrabRange)
        {
            Catch(targetId);
            return;
        }

        _repathTimer -= dt;
        if (_repathTimer <= 0f || _pathIndex >= _path.Count)
        {
            _repathTimer = Mathf.Max(0.05f, _night.velethRepathSeconds);
            if (SorkerNav.Instance != null &&
                SorkerNav.Instance.TryGetPath(_veleth.Position, targetPos, _path))
                _pathIndex = 0;
            else
                _path.Clear();
        }

        Vector3 waypoint = _pathIndex < _path.Count ? _path[_pathIndex] : targetPos;
        _veleth.MoveTo(waypoint, _night.velethChaseSpeed, dt);
        if (_pathIndex < _path.Count &&
            HorizontalDistance(_veleth.Position, _path[_pathIndex]) <= 0.2f)
            _pathIndex++;
    }

    private void Catch(uint clientId)
    {
        _veleth.SetState(VelethState.Grabbing);
        _grabTimer = Mathf.Max(0f, _night.velethGrabHoldSeconds);
        if (ServerDeaths.Kill(clientId))
        {
            OnPlayerCaught?.Invoke(clientId);
            Debug.Log($"[Veleth] Jugador {clientId} atrapado.");
        }

        if (!AnyAlivePlayer()) EndNightByVeleth();
    }

    private void EndNightByVeleth()
    {
        _running = false;
        GameDirector.Instance?.StopRun();
        ArbmosDirector.Instance?.StopRun();
        RitualBookDirector.Instance?.StopRun();
        Debug.Log("[Veleth] No quedan jugadores vivos; la noche termino.");
    }

    private bool NearestAlivePlayer(Vector3 from, out uint clientId, out Vector3 position)
    {
        clientId = 0;
        position = Vector3.zero;
        float best = float.MaxValue;
        bool found = false;
        var net = NetworkManager.Instance;

        if (Camera.main != null && ServerDeaths.IsAlive(0))
        {
            best = (Camera.main.transform.position - from).sqrMagnitude;
            position = Camera.main.transform.position;
            found = true;
        }

        foreach (uint id in net.ConnectedClients)
        {
            if (ServerDeaths.IsDead(id) || !net.TryGetClientWorldPosition(id, out var candidate))
                continue;
            float distance = (candidate - from).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            clientId = id;
            position = candidate;
            found = true;
        }
        return found;
    }

    private bool AnyAlivePlayer()
    {
        if (Camera.main != null && ServerDeaths.IsAlive(0)) return true;
        var net = NetworkManager.Instance;
        foreach (uint id in net.ConnectedClients)
            if (ServerDeaths.IsAlive(id) && net.TryGetClientWorldPosition(id, out _)) return true;
        return false;
    }

    private static VelethEntity GetVeleth(uint netId)
    {
        if (EntityRegistry.Instance != null && EntityRegistry.Instance.TryGet(netId, out var entity))
            return entity.GetComponent<VelethEntity>();
        return null;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static float FloorWorldY(float fallback)
    {
        if (FloorPoint.Instance != null && WorldOrigin.Instance != null)
            return WorldOrigin.Instance.ToWorld(FloorPoint.Instance.LocalPosition).y;
        return fallback;
    }

    public string DebugLine() => !_running || _veleth == null
        ? null
        : $"veleth={_veleth.State} objetivo={CurrentTarget} pos={_veleth.Position:F1}";
}
