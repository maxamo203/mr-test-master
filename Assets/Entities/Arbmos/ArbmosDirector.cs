using System.Collections;
using System.Collections.Generic;
using Gameplay;
using Scanner;
using UnityEngine;

// Director server-authoritative del Arbmos. Cada jugador posee una alucinacion aislada:
// el estado vive por clientId y la entidad se crea mediante ServerSpawnFor. En el flujo V2
// una aparicion normal permanece quieta hasta que la luz compromete el ataque; entonces
// persigue brevemente, ejecuta el susto y quita cordura. Cordura cero usa el chase letal.
public class ArbmosDirector : MonoBehaviour
{
    public static ArbmosDirector Instance { get; private set; }

    private enum Phase
    {
        Dormant,
        PresentStill,
        NormalChase,
        AttackCommitted,
        LethalStalk,
        LethalChase,
        Done,
    }

    private sealed class Haunt
    {
        public Phase phase = Phase.Dormant;
        public float cooldown;
        public uint netId;
        public ArbmosEntity ent;
        public Vector3 stationaryPosition;
        public float hideTimer;
        public float exposureTimer;
        public float attackTimer;
        public float stalkTimer;
        public float stalkTotal;
        public float chaseTimer;
        public bool lethalTriggered;

        public readonly List<Vector3> path = new();
        public int pathIndex;
        public float repathTimer;
    }

    private readonly Dictionary<uint, Haunt> _haunts = new();
private readonly List<uint> _scratchRemove = new();
    private NightConfig _night;
    private bool _running;

    public static ArbmosDirector Ensure()
    {
        if (Instance == null)
        {
            var go = new GameObject("ArbmosDirector");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ArbmosDirector>();
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
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.OnClientLeft -= HandleClientLeft;
        if (Instance == this) Instance = null;
    }

    public void StartRun()
    {
        var net = NetworkManager.Instance;
        if (net == null || !net.IsServer) return;

        foreach (var haunt in _haunts.Values) DespawnHaunt(haunt);
        _haunts.Clear();
        _night = GameDirector.Instance != null ? GameDirector.Instance.NocheActual : null;
        if (_night == null && GameSession.Instance != null) _night = GameSession.Instance.SelectedNight;

        net.OnClientLeft -= HandleClientLeft;
        net.OnClientLeft += HandleClientLeft;
        _running = true;
    }

    public void StopRun()
    {
        _running = false;
        foreach (var haunt in _haunts.Values) DespawnHaunt(haunt);
        _haunts.Clear();
    }

    private void HandleClientLeft(uint clientId)
    {
        if (!_haunts.TryGetValue(clientId, out var haunt)) return;
        DespawnHaunt(haunt);
        _haunts.Remove(clientId);
    }

    private void Update()
    {
        var net = NetworkManager.Instance;
        if (!_running || net == null || !net.IsServer || !net.GameStarted) return;
        if (_night == null || !_night.arbmosActive) return;

        float dt = Time.deltaTime;
        foreach (uint clientId in AlivePlayersWithPose())
        {
            if (!_haunts.TryGetValue(clientId, out var haunt))
            {
                haunt = new Haunt { cooldown = RandomCooldown() };
                _haunts.Add(clientId, haunt);
            }
            TickPlayer(clientId, haunt, dt);
        }

        _scratchRemove.Clear();
        foreach (var pair in _haunts)
        {
            // Done conserva la entidad durante la toma de muerte; la coroutine la
            // despawnea. Otras muertes sí limpian una aparicion normal inmediatamente.
            if (!ServerDeaths.IsDead(pair.Key) || pair.Value.phase == Phase.Done) continue;
            DespawnHaunt(pair.Value);
            _scratchRemove.Add(pair.Key);
        }
        foreach (uint clientId in _scratchRemove) _haunts.Remove(clientId);
    }

    private void TickPlayer(uint clientId, Haunt haunt, float dt)
    {
        if (!TryGetPlayerPosition(clientId, out var playerPosition)) return;

        // Cordura cero tiene prioridad absoluta: no espera cooldown, movimiento ni azar y
        // reutiliza la instancia presente para garantizar una sola entidad por jugador.
        if (!haunt.lethalTriggered &&
            SanitySystem.Instance != null &&
            SanitySystem.Instance.IsAtZero(clientId))
        {
            BeginLethal(clientId, haunt, playerPosition);
        }

        switch (haunt.phase)
        {
            case Phase.Dormant:
                TickDormant(clientId, haunt, dt, playerPosition);
                break;
            case Phase.PresentStill:
                TickPresentStill(clientId, haunt, dt, playerPosition);
                break;
            case Phase.NormalChase:
                TickNormalChase(clientId, haunt, dt, playerPosition);
                break;
            case Phase.AttackCommitted:
                TickAttackCommitted(clientId, haunt, dt, playerPosition);
                break;
            case Phase.LethalStalk:
                TickLethalStalk(clientId, haunt, dt, playerPosition);
                break;
            case Phase.LethalChase:
                TickLethalChase(clientId, haunt, dt, playerPosition);
                break;
        }
    }

    private void TickDormant(uint clientId, Haunt haunt, float dt, Vector3 playerPosition)
    {
        if (haunt.lethalTriggered) return;
        haunt.cooldown -= dt;
        if (haunt.cooldown > 0f) return;

        bool spawn = _night.arbmosForceSpawnAfterCooldown ||
                     Random.value <= Mathf.Clamp01(_night.arbmosSpawnChancePerAttempt);
        if (spawn) InvokePresent(clientId, haunt, playerPosition);
        else haunt.cooldown = 3f;
    }

    private void InvokePresent(uint clientId, Haunt haunt, Vector3 playerPosition)
    {
        if (!SpawnArbmosFor(clientId, haunt, playerPosition, aura: true,
                            lethal: false, distort: 0.3f))
            return;

        haunt.stationaryPosition = haunt.ent.Position;
        haunt.hideTimer = 0f;
        haunt.exposureTimer = 0f;
        haunt.attackTimer = 0f;
        haunt.phase = Phase.PresentStill;
    }

    private void TickPresentStill(uint clientId, Haunt haunt, float dt,
                                  Vector3 playerPosition)
    {
        if (!MaintainStationary(haunt, playerPosition)) { EndNormalHaunt(haunt); return; }

        if (PlayerLights.ModeFor(clientId) == FlashlightMode.Off)
        {
            haunt.exposureTimer = 0f;
            haunt.hideTimer += dt;
            if (haunt.hideTimer >= ArbmosDebug.HideSeconds(_night))
                EndNormalHaunt(haunt);
            return;
        }

        haunt.hideTimer = 0f;
        if (OwnerIsLightingHead(clientId, haunt))
            haunt.exposureTimer += dt;
        else
            haunt.exposureTimer = 0f;

        if (haunt.exposureTimer < ArbmosDebug.ExposureSeconds(_night)) return;
        BeginNormalChase(haunt);
    }

    private void BeginNormalChase(Haunt haunt)
    {
        haunt.ent.SetState(ArbmosState.Attacking);
        haunt.path.Clear();
        haunt.pathIndex = 0;
        haunt.repathTimer = 0f;
        haunt.chaseTimer = 0f;
        haunt.phase = Phase.NormalChase;
    }

    private void TickNormalChase(uint clientId, Haunt haunt, float dt,
                                 Vector3 playerPosition)
    {
        if (haunt.ent == null) { EndNormalHaunt(haunt); return; }

        haunt.chaseTimer += dt;
        bool reached = HorizontalDistance(haunt.ent.Position, playerPosition) <=
                       Mathf.Max(0.05f, _night.arbmosAttackGrabRange);
        bool timedOut = haunt.chaseTimer >= Mathf.Max(0.1f, _night.arbmosAttackMaxSeconds);
        if (reached)
        {
            // Pausa breve frente al jugador para que el acercamiento termine en un susto
            // legible antes de aplicar el único golpe y retirar la aparición.
            haunt.stationaryPosition = haunt.ent.Position;
            FacePlayer(haunt, playerPosition);
            haunt.attackTimer = ArbmosDebug.AttackCommitSeconds(_night);
            haunt.phase = Phase.AttackCommitted;
            return;
        }
        if (timedOut)
        {
            // El límite es sólo una salvaguarda. Nunca aplica daño a distancia: si no
            // consiguió llegar caminando, esta aparición termina sin golpear.
            EndNormalHaunt(haunt);
            return;
        }

        haunt.repathTimer -= dt;
        if (haunt.repathTimer <= 0f || haunt.pathIndex >= haunt.path.Count)
        {
            haunt.repathTimer = Mathf.Max(0.05f, _night.arbmosLethalRepathSeconds);
            if (SorkerNav.Instance != null &&
                SorkerNav.Instance.TryGetPath(haunt.ent.Position, playerPosition, haunt.path))
                haunt.pathIndex = 0;
            else
                haunt.path.Clear();
        }

        bool hasPath = haunt.pathIndex < haunt.path.Count;
        bool directIsSafe = SorkerNav.Instance == null || !SorkerNav.Instance.HasObstacleGrid;
        if (!hasPath && !directIsSafe)
        {
            FacePlayer(haunt, playerPosition);
            return;
        }

        Vector3 step = hasPath ? haunt.path[haunt.pathIndex] : playerPosition;
        haunt.ent.MoveTo(step,
            AnimationMatchedChaseSpeed(haunt.ent, _night.arbmosAttackChaseSpeed) *
            EntitySpeedSettings.Multiplier * haunt.ent.ChaseMovementMultiplier, dt);
        if (hasPath &&
            HorizontalDistance(haunt.ent.Position, haunt.path[haunt.pathIndex]) <= 0.2f)
            haunt.pathIndex++;
    }

    private void TickAttackCommitted(uint clientId, Haunt haunt, float dt,
                                     Vector3 playerPosition)
    {
        if (!MaintainStationary(haunt, playerPosition)) { EndNormalHaunt(haunt); return; }

        // Una vez comprometido, apagar o apartar la linterna ya no cancela el golpe.
        haunt.attackTimer -= dt;
        if (haunt.attackTimer > 0f) return;

        bool reachedZero = SanitySystem.Instance != null &&
                           SanitySystem.Instance.ServerApplyDamage(
                               clientId, _night.arbmosSanityDamage);
        if (reachedZero || (SanitySystem.Instance != null &&
                            SanitySystem.Instance.IsAtZero(clientId)))
        {
            BeginLethal(clientId, haunt, playerPosition);
            return;
        }
        EndNormalHaunt(haunt);
    }

    private void BeginLethal(uint clientId, Haunt haunt, Vector3 playerPosition)
    {
        if (haunt.lethalTriggered) return;
        haunt.lethalTriggered = true;

        if (haunt.ent == null &&
            !SpawnArbmosFor(clientId, haunt, playerPosition, aura: false,
                            lethal: true, distort: 1f))
        {
            // Reintenta en el tick siguiente sin abrir otra ventana ni consumir cooldown.
            haunt.lethalTriggered = false;
            haunt.cooldown = 0f;
            haunt.phase = Phase.Dormant;
            return;
        }

        haunt.stationaryPosition = haunt.ent.Position;
        haunt.ent.SetPositionDirectly(haunt.stationaryPosition);
        FacePlayer(haunt, playerPosition);
        haunt.ent.SetState(ArbmosState.Idle);
        haunt.ent.SetAura(false);
        haunt.ent.SetLethal(true);
        haunt.ent.SetDistort(1f);
        haunt.stalkTotal = Mathf.Max(0f, _night.arbmosLethalStalkSeconds);
        haunt.stalkTimer = haunt.stalkTotal;
        haunt.path.Clear();
        haunt.pathIndex = 0;
        haunt.repathTimer = 0f;
        haunt.chaseTimer = 0f;
        haunt.phase = Phase.LethalStalk;
    }

    private void TickLethalStalk(uint clientId, Haunt haunt, float dt,
                                 Vector3 playerPosition)
    {
        if (!MaintainStationary(haunt, playerPosition))
        {
            RetryLethal(haunt);
            return;
        }
        haunt.ent.SetState(ArbmosState.Idle);
        haunt.ent.SetAura(false);

        haunt.stalkTimer -= dt;
        if (haunt.stalkTimer > 0f) return;
        haunt.ent.SetState(ArbmosState.Chasing);
        haunt.path.Clear();
        haunt.pathIndex = 0;
        haunt.repathTimer = 0f;
        haunt.chaseTimer = 0f;
        haunt.phase = Phase.LethalChase;
    }

    private void TickLethalChase(uint clientId, Haunt haunt, float dt,
                                 Vector3 playerPosition)
    {
        if (haunt.ent == null) { RetryLethal(haunt); return; }

        haunt.chaseTimer += dt;
        if (HorizontalDistance(haunt.ent.Position, playerPosition) <= _night.arbmosGrabRange ||
            haunt.chaseTimer >= Mathf.Max(0.1f, _night.arbmosLethalMaxSeconds))
        {
            CompleteLethal(clientId, haunt);
            return;
        }

        haunt.repathTimer -= dt;
        if (haunt.repathTimer <= 0f || haunt.pathIndex >= haunt.path.Count)
        {
            haunt.repathTimer = Mathf.Max(0.05f, _night.arbmosLethalRepathSeconds);
            if (SorkerNav.Instance != null &&
                SorkerNav.Instance.TryGetPath(haunt.ent.Position, playerPosition, haunt.path))
                haunt.pathIndex = 0;
            else
                haunt.path.Clear();
        }

        bool hasPath = haunt.pathIndex < haunt.path.Count;
        bool directIsSafe = SorkerNav.Instance == null || !SorkerNav.Instance.HasObstacleGrid;
        if (!hasPath && !directIsSafe)
        {
            // Con geometria escaneada nunca atraviesa paredes: espera una ruta nueva. El
            // limite de seguridad de la fase garantiza que la secuencia final termine.
            FacePlayer(haunt, playerPosition);
            return;
        }

        Vector3 step = hasPath ? haunt.path[haunt.pathIndex] : playerPosition;
        haunt.ent.MoveTo(step,
            AnimationMatchedChaseSpeed(haunt.ent, _night.arbmosLethalChaseSpeed) *
            EntitySpeedSettings.Multiplier * haunt.ent.ChaseMovementMultiplier, dt);
        if (hasPath &&
            HorizontalDistance(haunt.ent.Position, haunt.path[haunt.pathIndex]) <= 0.2f)
            haunt.pathIndex++;
    }

    private static float AnimationMatchedChaseSpeed(ArbmosEntity entity,
                                                     float configuredMaximum)
    {
        float configured = Mathf.Max(0.01f, configuredMaximum);
        float animated = entity != null ? entity.ChaseAverageSpeed : 0f;
        return animated > 0.01f ? Mathf.Min(configured, animated) : configured;
    }

    private void CompleteLethal(uint clientId, Haunt haunt)
    {
        ServerDeaths.Kill(clientId, haunt.ent != null ? haunt.ent.transform : null);
        StartCoroutine(DespawnAfterDeathReveal(haunt));
        haunt.phase = Phase.Done;
    }

    private bool MaintainStationary(Haunt haunt, Vector3 playerPosition)
    {
        if (haunt.ent == null) return false;
        haunt.ent.SetPositionDirectly(haunt.stationaryPosition);
        FacePlayer(haunt, playerPosition);
        return true;
    }

    private bool OwnerIsLightingHead(uint clientId, Haunt haunt)
    {
        if (haunt.ent == null) return false;
        float angle = _night.flashlightConeAngleDeg;
        float range = _night.flashlightRange;
        if (PlayerLights.TryConoReal(out float realAngle, out float realRange))
        {
            angle = realAngle;
            range = realRange;
        }
        Vector3 target = haunt.ent.LookTargetPosition(_night.arbmosLookTargetHeight);
        return PlayerLights.IsPlayerIlluminating(
            clientId, target, angle, range, _night.arbmosLookTargetRadius,
            FlashlightMode.Dim, requireLineOfSight: true);
    }

private bool SpawnArbmosFor(uint clientId, Haunt haunt, Vector3 playerPosition,
                                bool aura, bool lethal, float distort)
    {
        var net = NetworkManager.Instance;
        if (net == null) return false;

        if (!TrySpawnPositionNear(clientId, playerPosition, out Vector3 spawn))
        {
            // Con geometría escaneada nunca aparece al otro lado de una pared. Si no hay
            // suficiente espacio interior delante del jugador, reintenta más adelante.
            haunt.cooldown = 2f;
            return false;
        }

        uint netId = net.ServerSpawnFor(clientId, EntityTypeIds.Arbmos, spawn);
        ArbmosEntity entity = GetArbmos(netId);
        if (entity == null)
        {
            haunt.cooldown = 2f;
            return false;
        }

        entity.SetPositionDirectly(spawn);
        FaceWorld(entity, playerPosition);
        entity.SetState(ArbmosState.Idle);
        entity.SetAura(aura);
        entity.SetLethal(lethal);
        entity.SetDistort(distort);
        haunt.netId = netId;
        haunt.ent = entity;
        return true;
    }

    private void EndNormalHaunt(Haunt haunt)
    {
        DespawnHaunt(haunt);
        haunt.cooldown = RandomCooldown();
        haunt.hideTimer = 0f;
        haunt.exposureTimer = 0f;
        haunt.attackTimer = 0f;
        haunt.phase = Phase.Dormant;
    }

    private void RetryLethal(Haunt haunt)
    {
        DespawnHaunt(haunt);
        haunt.lethalTriggered = false;
        haunt.cooldown = 0f;
        haunt.phase = Phase.Dormant;
    }

    private void DespawnHaunt(Haunt haunt)
    {
        if (haunt.netId != 0 && NetworkManager.Instance != null)
            NetworkManager.Instance.ServerDespawn(haunt.netId);
        haunt.netId = 0;
        haunt.ent = null;
        haunt.path.Clear();
    }

    private IEnumerator DespawnAfterDeathReveal(Haunt haunt)
    {
        yield return new WaitForSecondsRealtime(1.55f);
        DespawnHaunt(haunt);
    }

    private float RandomCooldown()
    {
        float a = _night != null ? _night.arbmosCooldownMin : 20f;
        float b = _night != null ? _night.arbmosCooldownMax : 40f;
        return Random.Range(Mathf.Min(a, b), Mathf.Max(a, b));
    }

private bool TrySpawnPositionNear(uint clientId, Vector3 playerPosition,
                                      out Vector3 spawn)
    {
        Vector3 forward = PlayerForward(clientId);
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        forward.Normalize();

        float desiredDistance = Mathf.Max(0.75f, _night.arbmosSpawnDistance);
        const float minimumDistance = 0.75f;
        const float searchStep = 0.25f;
        float floorY = FloorWorldY(playerPosition.y);
        var nav = SorkerNav.Ensure();

        // El candidato siempre queda sobre el eje de visión del jugador. Se prueba desde
        // la distancia ideal hacia adentro y sólo se acepta con una línea visual libre:
        // si hay una pared de por medio, ese punto nunca se utiliza.
        for (float distance = desiredDistance; distance >= minimumDistance; distance -= searchStep)
        {
            Vector3 candidate = playerPosition + forward * distance;
            candidate.y = floorY;
            if (nav.IsWalkable(candidate) && nav.HasClearLine(playerPosition, candidate))
            {
                spawn = candidate;
                return true;
            }

            // Sin paredes escaneadas, la dirección frontal sigue siendo válida.
            if (!nav.HasObstacleGrid)
            {
                spawn = candidate;
                return true;
            }
        }

        spawn = default;
        return false;
    }

    private static void FacePlayer(Haunt haunt, Vector3 playerPosition)
    {
        if (haunt.ent != null) FaceWorld(haunt.ent, playerPosition);
    }

    private static void FaceWorld(ArbmosEntity entity, Vector3 target)
    {
        Vector3 direction = target - entity.Position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 1e-4f) entity.FaceDirection(direction);
    }

    private IEnumerable<uint> AlivePlayersWithPose()
    {
        var net = NetworkManager.Instance;
        if (Camera.main != null && ServerDeaths.IsAlive(0)) yield return 0;
        foreach (uint clientId in net.ConnectedClients)
            if (ServerDeaths.IsAlive(clientId) &&
                net.TryGetClientWorldPosition(clientId, out _))
                yield return clientId;
    }

    private bool TryGetPlayerPosition(uint clientId, out Vector3 position)
    {
        if (clientId == 0)
        {
            if (Camera.main != null)
            {
                position = Camera.main.transform.position;
                return true;
            }
            position = Vector3.zero;
            return false;
        }
        return NetworkManager.Instance.TryGetClientWorldPosition(clientId, out position);
    }

    private Vector3 PlayerForward(uint clientId)
    {
        if (clientId == 0)
            return Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
        return NetworkManager.Instance.TryGetClientForward(clientId, out var forward)
            ? forward
            : Vector3.forward;
    }

    private static ArbmosEntity GetArbmos(uint netId)
    {
        if (EntityRegistry.Instance != null &&
            EntityRegistry.Instance.TryGet(netId, out var entity))
            return entity.GetComponent<ArbmosEntity>();
        return null;
    }

    private static float FloorWorldY(float fallback)
    {
        var worldOrigin = WorldOrigin.Instance;
        if (FloorPoint.Instance != null && worldOrigin != null)
            return worldOrigin.ToWorld(FloorPoint.Instance.LocalPosition).y;
        return fallback;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    public string DebugSnapshot()
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer)
            return "[Arbmos] solo corre en el HOST\n";
        if (!_running) return "[Arbmos] director parado\n";
        if (_night == null) return "[Arbmos] sin NightConfig\n";
        if (!_night.arbmosActive) return $"[Arbmos] desactivado en '{_night.displayName}'\n";

        var text = new System.Text.StringBuilder();
        text.Append($"[Arbmos V2] noche='{_night.displayName}' jugadores={_haunts.Count}\n");
        foreach (var pair in _haunts)
        {
            Haunt haunt = pair.Value;
            text.Append($" p{pair.Key}: {haunt.phase} cd={haunt.cooldown:F1} " +
                        $"hide={haunt.hideTimer:F1} expose={haunt.exposureTimer:F1} " +
                        $"lethal={(haunt.lethalTriggered ? "SI" : "no")}\n");
        }
        return text.ToString();
    }
}
