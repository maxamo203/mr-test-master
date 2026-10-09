using System.Collections;
using System.Collections.Generic;
using Gameplay;
using Gameplay.Spawning;
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
        public uint owner;
        public bool normalReserved;
        public bool lethalReserved;
        public int traceId;
        public Phase phase = Phase.Dormant;
        public float cooldown;
        public uint netId;
        public ArbmosEntity ent;
        public Vector3 stationaryPosition;
        public float hideTimer;
        public float exposureTimer;
        public float exposureGraceTimer;
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
    private string _lastSpawnRejection = "-";
    private int _rejectedSpawnAttempts;

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
        _lastSpawnRejection = "-";
        _rejectedSpawnAttempts = 0;
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
                haunt = new Haunt { owner = clientId, cooldown = RandomCooldown() };
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

        if (haunt.normalReserved &&
            !ThreatCoordinator.CanKeepNormalArbmos(clientId))
        {
            EndNormalHaunt(haunt);
            return;
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
        if (!spawn)
        {
            haunt.cooldown = RandomCooldown();
            return;
        }

        if (!ThreatCoordinator.TryBeginNormalArbmos(clientId))
        {
            GameplayTelemetry.Postponed("arbmos", "team_capacity", clientId);
            haunt.cooldown = 2f;
            return;
        }
        haunt.normalReserved = true;
        InvokePresent(clientId, haunt, playerPosition);
    }

    private void InvokePresent(uint clientId, Haunt haunt, Vector3 playerPosition)
    {
        if (!SpawnArbmosFor(clientId, haunt, playerPosition, aura: true,
                            lethal: false, distort: 0.3f))
        {
            ReleaseNormalReservation(haunt);
            return;
        }

        haunt.stationaryPosition = haunt.ent.Position;
        haunt.traceId = GameplayTelemetry.BeginThreat(
            "arbmos", clientId, haunt.stationaryPosition,
            $"grace={_night.arbmosExposureGraceSeconds:F2};exposure={_night.arbmosExposureSeconds:F2}");
        haunt.hideTimer = 0f;
        haunt.exposureTimer = 0f;
        haunt.exposureGraceTimer = Mathf.Max(0f, _night.arbmosExposureGraceSeconds);
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
        if (haunt.exposureGraceTimer > 0f)
        {
            haunt.exposureGraceTimer = Mathf.Max(0f, haunt.exposureGraceTimer - dt);
            haunt.exposureTimer = 0f;
            return;
        }
        if (OwnerIsLightingHead(clientId, haunt))
            haunt.exposureTimer += dt;
        else
            haunt.exposureTimer = 0f;

        if (haunt.exposureTimer < ArbmosDebug.ExposureSeconds(_night)) return;
        BeginNormalChase(haunt);
    }

    private void BeginNormalChase(Haunt haunt)
    {
        GameplayTelemetry.Phase(haunt.traceId, "arbmos", "normal_chase",
                                haunt.owner, haunt.ent.Position);
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
        GameplayTelemetry.End(haunt.traceId, "arbmos", "lethal_escalation",
                              clientId, haunt.ent != null ? haunt.ent.Position : playerPosition);
        haunt.traceId = 0;
        ReleaseNormalReservation(haunt);
        ThreatCoordinator.BeginLethalArbmos(clientId);
        haunt.lethalReserved = true;
        haunt.lethalTriggered = true;

        if (haunt.ent == null &&
            !SpawnArbmosFor(clientId, haunt, playerPosition, aura: false,
                            lethal: true, distort: 1f))
        {
            // Reintenta en el tick siguiente sin abrir otra ventana ni consumir cooldown.
            haunt.lethalTriggered = false;
            ReleaseLethalReservation(haunt);
            haunt.cooldown = 0f;
            haunt.phase = Phase.Dormant;
            return;
        }

        haunt.stationaryPosition = haunt.ent.Position;
        haunt.traceId = GameplayTelemetry.BeginThreat(
            "arbmos_lethal", clientId, haunt.stationaryPosition,
            $"speed={_night.arbmosLethalChaseSpeed:F2}");
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
        bool inGrabRange = HorizontalDistance(haunt.ent.Position, playerPosition) <=
                           _night.arbmosGrabRange;
        if (inGrabRange && HasCaptureLine(haunt.ent.Position, playerPosition))
        {
            CompleteLethal(clientId, haunt);
            return;
        }
        if (haunt.chaseTimer >= Mathf.Max(0.1f, _night.arbmosLethalMaxSeconds))
        {
            // El timeout no mata a distancia ni detrás de una pared: reprograma la
            // consecuencia desde una nueva posición válida.
            RetryLethal(haunt);
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
        GameplayTelemetry.End(haunt.traceId, "arbmos_lethal", "capture",
                              clientId, haunt.ent != null ? haunt.ent.Position : Vector3.zero);
        haunt.traceId = 0;
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
            // suficiente espacio en el mismo ambiente que el jugador, reintenta más adelante.
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
        DespawnHaunt(haunt, releaseLethal: false);
        haunt.lethalTriggered = false;
        haunt.cooldown = 0f;
        haunt.phase = Phase.Dormant;
    }

    private void DespawnHaunt(Haunt haunt, bool releaseLethal = true)
    {
        if (haunt.traceId != 0)
        {
            GameplayTelemetry.End(haunt.traceId,
                haunt.lethalTriggered ? "arbmos_lethal" : "arbmos",
                "despawn", haunt.owner,
                haunt.ent != null ? haunt.ent.Position : Vector3.zero);
            haunt.traceId = 0;
        }
        ReleaseNormalReservation(haunt);
        if (releaseLethal) ReleaseLethalReservation(haunt);
        if (haunt.netId != 0 && NetworkManager.Instance != null)
            NetworkManager.Instance.ServerDespawn(haunt.netId);
        haunt.netId = 0;
        haunt.ent = null;
        haunt.path.Clear();
    }

    private static void ReleaseNormalReservation(Haunt haunt)
    {
        if (haunt == null || !haunt.normalReserved) return;
        ThreatCoordinator.EndNormalArbmos(haunt.owner);
        haunt.normalReserved = false;
    }

    private static void ReleaseLethalReservation(Haunt haunt)
    {
        if (haunt == null || !haunt.lethalReserved) return;
        ThreatCoordinator.EndLethalArbmos(haunt.owner);
        haunt.lethalReserved = false;
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
        Vector3 horizontalForward = forward;
        horizontalForward.y = 0f;
        if (horizontalForward.sqrMagnitude < 1e-4f) horizontalForward = Vector3.forward;
        horizontalForward.Normalize();

        float desiredDistance = Mathf.Max(ArbmosSpawnGeometry.MinimumDistance,
                                          _night.arbmosSpawnDistance);
        float floorY = FloorWorldY(playerPosition.y);
        var nav = SorkerNav.Ensure();

        float beamAngle = _night.flashlightConeAngleDeg;
        float beamRange = _night.flashlightRange;
        if (PlayerLights.TryConoReal(out float realAngle, out float realRange))
        {
            beamAngle = realAngle;
            beamRange = realRange;
        }

        if (!InteriorSpawnValidator.TryEnsureReady(out var topologyReason) ||
            WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady)
        {
            _lastSpawnRejection = topologyReason ?? "WorldOrigin no esta listo";
            _rejectedSpawnAttempts++;
            spawn = default;
            return false;
        }

        // Recorre 360 grados: el Arbmos ya no esta obligado a aparecer dentro de la
        // camara. Se prioriza la distancia de diseño, se prueba hasta 2 m cuando el
        // cuarto es angosto y luego puntos más lejanos del mismo ambiente.
        float randomStart = Random.Range(0f, 360f);
        const int angularSamples = 24;
        const float distanceStep = 0.25f;
        var distances = new List<float> { desiredDistance };
        for (float distance = desiredDistance - distanceStep;
             distance >= ArbmosSpawnGeometry.MinimumDistance - 0.001f;
             distance -= distanceStep)
            distances.Add(distance);
        for (float distance = desiredDistance + distanceStep;
             distance <= desiredDistance + 1.5f;
             distance += distanceStep)
            distances.Add(distance);

        string lastReason = "no se encontro una posicion interior valida";
        for (int distanceSample = 0; distanceSample < distances.Count; distanceSample++)
        {
            float distance = distances[distanceSample];

            for (int sample = 0; sample < angularSamples; sample++)
            {
                float angle = randomStart + sample * (360f / angularSamples);
                Vector3 candidate = playerPosition +
                    ArbmosSpawnGeometry.LateralDirection(horizontalForward, angle) * distance;
                candidate.y = floorY;
                if (!IsValidSpawnCandidate(clientId, playerPosition, forward, candidate,
                                           beamAngle, beamRange, nav, out lastReason)) continue;

                _lastSpawnRejection = "-";
                spawn = candidate;
                return true;
            }
        }

        _lastSpawnRejection = lastReason;
        _rejectedSpawnAttempts++;
        GameplayTelemetry.Postponed("arbmos", "invalid_interior_spawn", clientId);
        spawn = default;
        return false;
    }

    private bool IsValidSpawnCandidate(uint clientId, Vector3 playerPosition,
                                       Vector3 playerForward, Vector3 candidate,
                                       float beamAngle, float beamRange, SorkerNav nav,
                                       out string reason)
    {
        Vector3 feet = candidate + Vector3.up * 0.2f;
        Vector3 torso = candidate + Vector3.up * 1.05f;
        Vector3 head = candidate + Vector3.up * ArbmosSpawnGeometry.BodyHeight;

        if (!InteriorSpawnValidator.TryValidateActorInPlayerRoom(
                playerPosition, candidate, ArbmosSpawnGeometry.BodyRadius,
                out _, out reason))
            return false;

        // Si la linterna está encendida, ninguna franja del cuerpo puede tocar el haz.
        if (PlayerLights.ModeFor(clientId) != FlashlightMode.Off &&
            (PlayerLights.Alcanza(playerPosition, playerForward, feet, beamAngle,
                                  beamRange, ArbmosSpawnGeometry.BodyRadius) ||
             PlayerLights.Alcanza(playerPosition, playerForward, torso, beamAngle,
                                  beamRange, ArbmosSpawnGeometry.BodyRadius) ||
             PlayerLights.Alcanza(playerPosition, playerForward, head, beamAngle,
                                  beamRange, ArbmosSpawnGeometry.BodyRadius)))
        {
            reason = "el candidato esta dentro del haz de luz";
            return false;
        }

        if (!HasBodyClearance(candidate, nav))
        {
            reason = "el cuerpo no tiene volumen libre";
            return false;
        }
        if (!nav.HasClearLine(playerPosition, torso) ||
            !nav.HasClearLine(playerPosition, head))
        {
            reason = "la navegacion detecta una pared de por medio";
            return false;
        }
        if (!PlayerLights.HasLineOfSight(playerPosition, torso,
                                         ArbmosSpawnGeometry.BodyRadius) ||
            !PlayerLights.HasLineOfSight(playerPosition, head,
                                         ArbmosSpawnGeometry.BodyRadius))
        {
            reason = "la vision al Arbmos esta ocluida";
            return false;
        }

        Vector3 capsuleBottom = candidate + Vector3.up * ArbmosSpawnGeometry.BodyRadius;
        Vector3 capsuleTop = candidate + Vector3.up *
            (ArbmosSpawnGeometry.BodyHeight - ArbmosSpawnGeometry.BodyRadius);
        if (Physics.CheckCapsule(capsuleBottom, capsuleTop,
                                 ArbmosSpawnGeometry.BodyRadius,
                                 Physics.DefaultRaycastLayers,
                                 QueryTriggerInteraction.Ignore))
        {
            reason = "el volumen del Arbmos intersecta geometria";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool HasBodyClearance(Vector3 candidate, SorkerNav nav)
    {
        if (!nav.IsWalkable(candidate)) return false;
        float r = ArbmosSpawnGeometry.BodyRadius;
        return nav.IsWalkable(candidate + Vector3.right * r) &&
               nav.IsWalkable(candidate - Vector3.right * r) &&
               nav.IsWalkable(candidate + Vector3.forward * r) &&
               nav.IsWalkable(candidate - Vector3.forward * r);
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
        if (Camera.main != null && ServerDeaths.IsAlive(0) &&
            TrackingReliability.LocalIsReliable()) yield return 0;
        foreach (uint clientId in net.ConnectedClients)
            if (ServerDeaths.IsAlive(clientId) &&
                TrackingReliability.PlayerIsReliable(clientId) &&
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

    private static bool HasCaptureLine(Vector3 entity, Vector3 player)
    {
        Vector3 from = entity + Vector3.up;
        Vector3 to = player;
        var nav = SorkerNav.Instance;
        return (nav == null || nav.HasClearLine(entity, player)) &&
               PlayerLights.HasLineOfSight(from, to, 0.2f);
    }

    public string DebugSnapshot()
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServer)
            return "[Arbmos] solo corre en el HOST\n";
        if (!_running) return "[Arbmos] director parado\n";
        if (_night == null) return "[Arbmos] sin NightConfig\n";
        if (!_night.arbmosActive) return $"[Arbmos] desactivado en '{_night.displayName}'\n";

        var text = new System.Text.StringBuilder();
        text.Append($"[Arbmos V2] noche='{_night.displayName}' jugadores={_haunts.Count} " +
                    $"spawnRechazados={_rejectedSpawnAttempts} ultimo='{_lastSpawnRejection}'\n");
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
