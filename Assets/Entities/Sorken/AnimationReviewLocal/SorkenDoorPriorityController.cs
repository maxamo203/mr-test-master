using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class SorkenDoorPriorityController : MonoBehaviour
{
    public enum StateId { Disappearing, Despawned, Grab, Emerging, CoverStart, CoverWalk, Chase, Idle, Waiting }

    [Header("Scene references")]
    public Transform player;
    public Transform sorken;
    public Animator animator;
    public CharacterController sorkenController;
    public Renderer darknessRenderer;
    public Renderer defeatDarknessRenderer;
    public Renderer sorkenRenderer;
    public SorkenDoorTestFlashlight flashlight;

    [Header("Documented conditions")]
    public float darknessLeadTime = 5f;
    public float emergeDuration = 2.967f;
    public float emergeTravel = 2.15f;
    public float chaseSpeed = 1.35f;
    public float coveredChaseSpeed = 0.95f;
    public float grabDistance = 1.25f;
    public float grabAngle = 100f;
    public float grabDuration = 1.967f;
    public float disappearDuration = 2.2f;
    public float disappearSinkDistance = 1.5f;
    public float disappearSinkDuration = 0.45f;
    public float requiredDefenseExposure = 6f;
    public float defenseExposureDecayPerSecond = 2f;
    [Range(0f, 1f)] public float grabContactNormalizedTime = 0.68f;

    [Header("Darkness effects")]
    public Vector3 doorDarknessFullScale = new Vector3(3.15f, 2.45f, 1f);
    public float doorDarknessMinScale = 0.15f;
    public float defeatDarknessMaxRadius = 2.35f;
    public float defeatDarknessGrowDuration = 0.75f;

    private const string IdleAnimation = "01_Sorken_Idle";
    private const string ChaseAnimation = "02_Sorken_Base_InjuredWalk";
    private const string CoverStartAnimation = "03_Sorken_Cover_Start";
    private const string CoverWalkAnimation = "04_Sorken_Cover_Walk";
    private const string GrabAnimation = "06_Sorken_Grab_Attack";
    private const string DoorAnimation = "07_Sorken_Emerge_Door";
    private const string DisappearAnimation = "08_Sorken_Disappear";

    private abstract class State
    {
        protected readonly SorkenDoorPriorityController C;
        public abstract StateId Id { get; }
        public abstract int Priority { get; }
        protected State(SorkenDoorPriorityController controller) { C = controller; }
        public virtual void Enter() { }
        public virtual void Exit() { }
        public abstract void Tick();
    }

    private sealed class WaitingState : State
    {
        public override StateId Id => StateId.Waiting;
        public override int Priority => 1;
        public WaitingState(SorkenDoorPriorityController c) : base(c) { }
        public override void Enter()
        {
            C._stateTime = 0f;
            C.sorken.gameObject.SetActive(true);
            C.sorken.SetPositionAndRotation(C._emergeStart, C._emergeRotation);
            C.animator.Play(IdleAnimation, 0, 0f);
            C.animator.Update(0f);
            C.sorken.gameObject.SetActive(false);
            C.SetDoorDarknessVisible(true);
            C.SetDefeatDarknessVisible(false);
        }
        public override void Tick()
        {
            C.AnimateDoorDarkness(C._stateTime / C.darknessLeadTime);
            if (C._stateTime >= C.darknessLeadTime) C.ChangeState(StateId.Emerging);
        }
    }

    private sealed class EmergingState : State
    {
        public override StateId Id => StateId.Emerging;
        public override int Priority => 1;
        public EmergingState(SorkenDoorPriorityController c) : base(c) { }
        public override void Enter()
        {
            C._stateTime = 0f;
            C.sorken.gameObject.SetActive(true);
            C.sorken.SetPositionAndRotation(C._emergeStart, C._emergeRotation);
            C.animator.Play(DoorAnimation, 0, 0f);
            C.animator.Update(0f);
            C.SetDoorDarknessVisible(true);
        }
        public override void Tick()
        {
            C.AnimateDoorDarkness(1f);
            float t = Mathf.Clamp01(C._stateTime / C.emergeDuration);
            C.sorken.position = Vector3.Lerp(C._emergeStart, C._emergeEnd, SmoothStep(t));
            if (t >= 1f)
            {
                C.SetDoorDarknessVisible(false);
                C.ChangeState(C._holdPosition ? StateId.Idle : StateId.Chase);
            }
        }
    }

    private sealed class IdleState : State
    {
        public override StateId Id => StateId.Idle;
        public override int Priority => 5;
        public IdleState(SorkenDoorPriorityController c) : base(c) { }
        public override void Enter() { C.PlayLoop(IdleAnimation, 0.16f); }
        public override void Tick() { C.FacePlayer(90f); }
    }

    private sealed class ChaseState : State
    {
        public override StateId Id => StateId.Chase;
        public override int Priority => 4;
        public ChaseState(SorkenDoorPriorityController c) : base(c) { }
        public override void Enter() { C.PlayLoop(ChaseAnimation, 0.18f); }
        public override void Tick() { C.MoveTowardPlayer(C.chaseSpeed); }
    }

    private sealed class CoverStartState : State
    {
        public override StateId Id => StateId.CoverStart;
        public override int Priority => 2;
        public CoverStartState(SorkenDoorPriorityController c) : base(c) { }
        public override void Enter()
        {
            C._stateTime = 0f;
            C._exposureHandled = true;
            C.animator.CrossFadeInFixedTime(CoverStartAnimation, 0.10f, 0, 0f);
        }
        public override void Tick()
        {
            if (C._stateTime < 2.967f) return;
            C.ChangeState(C._lightHits ? StateId.CoverWalk
                                      : (C._holdPosition ? StateId.Idle : StateId.Chase));
        }
    }

    private sealed class CoverWalkState : State
    {
        public override StateId Id => StateId.CoverWalk;
        public override int Priority => 3;
        public CoverWalkState(SorkenDoorPriorityController c) : base(c) { }
        public override void Enter() { C.PlayLoop(CoverWalkAnimation, 0.12f); }
        public override void Tick() { C.MoveTowardPlayer(C.coveredChaseSpeed); }
    }

    private sealed class DisappearingState : State
    {
        public override StateId Id => StateId.Disappearing;
        public override int Priority => 0;
        public DisappearingState(SorkenDoorPriorityController c) : base(c) { }
public override void Enter()
        {
            C._stateTime = 0f;
            C._holdPosition = true;
            C.sorken.gameObject.SetActive(true);
            C._disappearStart = C.sorken.position;
            C._disappearEnd = C._disappearStart + Vector3.down * C.disappearSinkDistance;
            C._disappearGroundY = C.GetLowestRendererY();
            C._disappearReachedGround = false;
            C.SetDoorDarknessVisible(false);
            C.SetDefeatDarknessVisible(true);
            C.AnimateDefeatDarkness(0f, false);
            C.animator.CrossFadeInFixedTime(DisappearAnimation, 0.08f, 0, 0f);
        }
public override void Tick()
        {
            if (!C._disappearReachedGround)
            {
                float lowestY = C.GetLowestRendererY();
                if (!float.IsNaN(lowestY))
                    C.sorken.position += Vector3.up * (C._disappearGroundY - lowestY);

                C.AnimateDefeatDarkness(C._stateTime / C.defeatDarknessGrowDuration, false);
                if (C._stateTime < C.disappearDuration) return;

                C._disappearGroundedPosition = C.sorken.position;
                C._disappearEnd = C._disappearGroundedPosition + Vector3.down * C.disappearSinkDistance;
                C._disappearReachedGround = true;
            }

            float sinkTime = Mathf.Max(0.01f, C.disappearSinkDuration);
            float t = Mathf.Clamp01((C._stateTime - C.disappearDuration) / sinkTime);
            C.sorken.position = Vector3.Lerp(C._disappearGroundedPosition, C._disappearEnd, SmoothStep(t));
            C.AnimateDefeatDarkness(t, true);
            if (t >= 1f) C.ChangeState(StateId.Despawned);
        }
    }

    private sealed class DespawnedState : State
    {
        public override StateId Id => StateId.Despawned;
        public override int Priority => 0;
        public DespawnedState(SorkenDoorPriorityController c) : base(c) { }
        public override void Enter()
        {
            C.sorken.gameObject.SetActive(false);
            C.SetDoorDarknessVisible(false);
            C.SetDefeatDarknessVisible(false);
        }
        public override void Tick() { }
    }

    private sealed class GrabState : State
    {
        private bool _contactEvaluated;
        public override StateId Id => StateId.Grab;
        public override int Priority => 1;
        public GrabState(SorkenDoorPriorityController c) : base(c) { }
        public override void Enter()
        {
            C._stateTime = 0f;
            _contactEvaluated = false;
            C.FacePlayerImmediate();
            C.animator.CrossFadeInFixedTime(GrabAnimation, 0.08f, 0, 0f);
            C._grabResult = "ATAQUE EN CURSO";
        }
        public override void Tick()
        {
            if (!_contactEvaluated && C._stateTime >= C.grabDuration * C.grabContactNormalizedTime)
            {
                _contactEvaluated = true;
                bool hit = C.CanConfirmGrabContact();
                C._grabResult = hit ? "CONTACTO CONFIRMADO" : "ATAQUE FALLIDO";
            }
            if (C._stateTime < C.grabDuration) return;
            C._grabCooldown = 0.8f;
            if (C._grabResult == "CONTACTO CONFIRMADO")
            {
                C._holdPosition = true;
                C.ChangeState(StateId.Idle);
            }
            else C.ChangeState(StateId.Chase);
        }
    }

    private readonly Dictionary<StateId, State> _states = new Dictionary<StateId, State>();
    private State _current;
    private float _stateTime;
    private float _grabCooldown;
    private bool _lightHits;
    private float _defenseExposureTime;
    private bool _exposureHandled;
    private bool _holdPosition;
    private string _grabResult = "SIN ATAQUE";
    private Vector3 _emergeStart;
    private Vector3 _emergeEnd;
    private Quaternion _emergeRotation;
    private Vector3 _disappearStart;
    private Vector3 _disappearEnd;
    private Vector3 _disappearGroundedPosition;
    private float _disappearGroundY;
    private bool _disappearReachedGround;
    private Vector3 _initialPlayerPosition;
    private Quaternion _initialPlayerRotation;

    public StateId CurrentState => _current != null ? _current.Id : StateId.Waiting;

    private void Awake()
    {
        _states[StateId.Waiting] = new WaitingState(this);
        _states[StateId.Emerging] = new EmergingState(this);
        _states[StateId.Idle] = new IdleState(this);
        _states[StateId.Chase] = new ChaseState(this);
        _states[StateId.CoverStart] = new CoverStartState(this);
        _states[StateId.CoverWalk] = new CoverWalkState(this);
        _states[StateId.Grab] = new GrabState(this);
        _states[StateId.Disappearing] = new DisappearingState(this);
        _states[StateId.Despawned] = new DespawnedState(this);
    }

    private void Start()
    {
        _emergeStart = sorken.position;
        _emergeRotation = sorken.rotation;
        _emergeEnd = _emergeStart + sorken.forward * emergeTravel;
        _initialPlayerPosition = player.position;
        _initialPlayerRotation = player.rotation;
        ResetScenario();
    }

private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame) ResetScenario();
        if (keyboard != null && keyboard.iKey.wasPressedThisFrame) _holdPosition = !_holdPosition;

        _stateTime += Time.deltaTime;
        _grabCooldown = Mathf.Max(0f, _grabCooldown - Time.deltaTime);
        UpdateExposure();
        UpdateDefenseProgress();

        if (_current == null) return;
        if (_current.Id == StateId.Waiting || _current.Id == StateId.Emerging ||
            _current.Id == StateId.Disappearing || _current.Id == StateId.Despawned ||
            _current.Id == StateId.Grab)
        {
            _current.Tick();
            return;
        }

        if (_defenseExposureTime >= requiredDefenseExposure) ChangeState(StateId.Disappearing);
        else if (CanBeginGrab()) ChangeState(StateId.Grab);
        else if (_current.Id == StateId.CoverStart) _current.Tick();
        else if (_lightHits && !_exposureHandled) ChangeState(StateId.CoverStart);
        else if (_lightHits && !_holdPosition) ChangeState(StateId.CoverWalk);
        else if (_holdPosition) ChangeState(StateId.Idle);
        else ChangeState(StateId.Chase);

        if (_current != null && _current.Id != StateId.CoverStart) _current.Tick();
    }

private void UpdateDefenseProgress()
    {
        if (_current == null || !sorken.gameObject.activeSelf ||
            _current.Id == StateId.Waiting || _current.Id == StateId.Emerging ||
            _current.Id == StateId.Disappearing || _current.Id == StateId.Despawned ||
            _current.Id == StateId.Grab)
            return;

        if (_lightHits)
            _defenseExposureTime = Mathf.Min(requiredDefenseExposure,
                _defenseExposureTime + Time.deltaTime);
        else
            _defenseExposureTime = Mathf.Max(0f,
                _defenseExposureTime - defenseExposureDecayPerSecond * Time.deltaTime);
    }

    public void ResetScenario()
    {
        _holdPosition = false;
        _exposureHandled = false;
        _lightHits = false;
        _defenseExposureTime = 0f;
        _grabCooldown = 0f;
        _grabResult = "SIN ATAQUE";
        player.SetPositionAndRotation(_initialPlayerPosition, _initialPlayerRotation);
        ChangeState(StateId.Waiting, true);
    }

    private void ChangeState(StateId next, bool force = false)
    {
        if (!force && _current != null && _current.Id == next) return;
        if (_current != null) _current.Exit();
        _current = _states[next];
        _stateTime = 0f;
        _current.Enter();
    }

private void UpdateExposure()
    {
        _lightHits = sorken.gameObject.activeSelf && flashlight != null &&
                     flashlight.Illuminates(sorken, sorkenRenderer);
    }

private bool CanBeginGrab()
{
    if (_grabResult == "CONTACTO CONFIRMADO" || _grabCooldown > 0f ||
        !sorken.gameObject.activeSelf) return false;
    Vector3 delta = player.position - sorken.position;
    delta.y = 0f;
    float distance = delta.magnitude;
    if (distance > grabDistance || distance < 0.001f) return false;
    if (Vector3.Angle(sorken.forward, delta / distance) > grabAngle * 0.5f) return false;
    return HasLineOfSight(grabDistance + 0.35f);
}

    private bool CanConfirmGrabContact()
    {
        Vector3 delta = player.position - sorken.position;
        delta.y = 0f;
        return delta.magnitude <= grabDistance + 0.15f && HasLineOfSight(grabDistance + 0.35f);
    }

    private bool HasLineOfSight(float maximumDistance)
    {
        Vector3 origin = sorken.position + Vector3.up * 1.05f;
        Vector3 target = player.position + Vector3.up * 0.9f;
        Vector3 delta = target - origin;
        if (delta.magnitude > maximumDistance) return false;
        if (!Physics.Raycast(origin, delta.normalized, out RaycastHit hit, delta.magnitude + 0.15f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;
        return hit.transform == player || hit.transform.IsChildOf(player);
    }

    private void MoveTowardPlayer(float speed)
    {
        Vector3 delta = player.position - sorken.position;
        delta.y = 0f;
        if (delta.sqrMagnitude < 0.001f) return;
        Vector3 desired = delta.normalized;
        Vector3 origin = sorken.position + Vector3.up * 0.85f;
        if (Physics.SphereCast(origin, 0.34f, desired, out RaycastHit hit, 1.25f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            hit.transform != player && !hit.transform.IsChildOf(player))
        {
            Vector3 tangent = Vector3.Cross(Vector3.up, hit.normal).normalized;
            if (Vector3.Dot(tangent, desired) < 0f) tangent = -tangent;
            desired = Vector3.Slerp(desired, tangent, 0.85f).normalized;
        }
        Quaternion target = Quaternion.LookRotation(desired, Vector3.up);
        sorken.rotation = Quaternion.RotateTowards(sorken.rotation, target, 120f * Time.deltaTime);
        sorkenController.Move(desired * speed * Time.deltaTime);
    }

    private void FacePlayer(float degreesPerSecond)
    {
        Vector3 direction = player.position - sorken.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;
        Quaternion target = Quaternion.LookRotation(direction.normalized, Vector3.up);
        sorken.rotation = Quaternion.RotateTowards(sorken.rotation, target, degreesPerSecond * Time.deltaTime);
    }

    private void FacePlayerImmediate()
    {
        Vector3 direction = player.position - sorken.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
            sorken.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void PlayLoop(string state, float fade)
    {
        animator.CrossFadeInFixedTime(state, fade, 0, 0f);
    }
    private void SetDoorDarknessVisible(bool visible)
    {
        if (darknessRenderer == null) return;
        bool wasActive = darknessRenderer.gameObject.activeSelf;
        darknessRenderer.gameObject.SetActive(visible);
        if (visible && !wasActive) AnimateDoorDarkness(0f);
    }

    private void SetDefeatDarknessVisible(bool visible)
    {
        EnsureDefeatDarkness();
        if (defeatDarknessRenderer == null) return;
        defeatDarknessRenderer.gameObject.SetActive(visible);
    }

    private void AnimateDoorDarkness(float normalized)
    {
        if (darknessRenderer == null || !darknessRenderer.gameObject.activeSelf) return;
        float t = SmoothStep(Mathf.Clamp01(normalized));
        float pulse = 1f + Mathf.Sin(Time.time * 2.7f) * 0.025f;
        float start = Mathf.Max(0.01f, doorDarknessMinScale);
        Vector3 scale = Vector3.Lerp(doorDarknessFullScale * start, doorDarknessFullScale, t) * pulse;
        darknessRenderer.transform.localScale = scale;
    }

    private void AnimateDefeatDarkness(float normalized, bool closing)
    {
        EnsureDefeatDarkness();
        if (defeatDarknessRenderer == null || !defeatDarknessRenderer.gameObject.activeSelf) return;

        float t = SmoothStep(Mathf.Clamp01(normalized));
        float radius = closing ? Mathf.Lerp(defeatDarknessMaxRadius, 0.08f, t)
                               : Mathf.Lerp(0.12f, defeatDarknessMaxRadius, t);
        float y = float.IsNaN(_disappearGroundY) ? sorken.position.y : _disappearGroundY + 0.015f;
        defeatDarknessRenderer.transform.position = new Vector3(sorken.position.x, y, sorken.position.z);
        defeatDarknessRenderer.transform.localScale = new Vector3(radius, 0.025f, radius);
    }

    private void EnsureDefeatDarkness()
    {
        if (defeatDarknessRenderer != null) return;

        GameObject darkness = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        darkness.name = "Sorken_Defeat_Floor_Darkness_Runtime";
        Collider collider = darkness.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        Renderer renderer = darkness.GetComponent<Renderer>();
        if (renderer != null && darknessRenderer != null && darknessRenderer.sharedMaterial != null)
            renderer.material = new Material(darknessRenderer.sharedMaterial);

        darkness.SetActive(false);
        defeatDarknessRenderer = renderer;
    }
    private static float SmoothStep(float t) => t * t * (3f - 2f * t);

private void OnGUI()
    {
        GUI.Box(new Rect(18, 18, 430, 182), "PRUEBA SORKEN — ESTADOS PRIORIZADOS");
        GUI.Label(new Rect(34, 47, 390, 22), "WASD mover | Mouse mirar | Shift correr | F linterna");
        GUI.Label(new Rect(34, 70, 390, 22), "R reiniciar | I alternar persecución / Idle");
        GUI.Label(new Rect(34, 93, 390, 22), $"Estado: {CurrentState}  Prioridad: {(_current != null ? _current.Priority : -1)}");
        GUI.Label(new Rect(34, 116, 390, 22), $"Linterna impacta: {_lightHits}  Distancia: {Vector3.Distance(player.position, sorken.position):0.00} m");
        GUI.Label(new Rect(34, 139, 390, 22), $"Defensa: {_defenseExposureTime:0.0} / {requiredDefenseExposure:0.0} s");
        GUI.Label(new Rect(34, 162, 390, 22), $"Agarre: {_grabResult}");
    }


private float GetLowestRendererY()
    {
        float lowest = float.PositiveInfinity;
        foreach (Renderer renderer in sorken.GetComponentsInChildren<Renderer>())
        {
            if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                lowest = Mathf.Min(lowest, renderer.bounds.min.y);
        }
        return float.IsPositiveInfinity(lowest) ? float.NaN : lowest;
    }
}