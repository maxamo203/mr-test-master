using UnityEngine;
using Gameplay;

public enum VelethState : byte
{
    Hunting = 0,
    Grabbing = 1,
}

public class VelethEntity : MonoBehaviour
{
    public static VelethEntity Active { get; private set; }
    public Vector3 Position => transform.position;
    public VelethState State { get; private set; } = VelethState.Hunting;

    [SerializeField, Min(1f)] private float _turnSpeed = 260f;
    [SerializeField] private Animator _animator;
    [SerializeField, Min(0.1f)] private float _emergenceDuration = 5f;

    [Header("Desplazamiento sincronizado")]
    [Tooltip("Impulsos de brazos/piernas contenidos en el loop de reptado.")]
    [SerializeField, Min(1)] private int _crawlImpulsesPerLoop = 4;
    [Tooltip("Velocidad relativa conservada durante cada apoyo del reptado.")]
    [SerializeField, Range(0f, 1f)] private float _plantedSpeedRatio = 0.2f;

    private Vector3 _desiredPos;
    private Quaternion _desiredRot;
    private bool _hasDesired;
    private bool _emergenceStarted;
    private float _emergenceElapsed;
    private Renderer[] _visualRenderers;
    private MaterialPropertyBlock _revealProperties;
    private readonly SorkenFootstepClock _supportClock = new();
    private readonly SorkenFootstepClock _scrapeClock = new();
    private readonly System.Collections.Generic.Queue<bool> _pendingContacts = new();
    private static readonly float[] SupportContacts = { 0.07f, 0.30f, 0.54f, 0.79f };
    private static readonly float[] ScrapeContacts = { 0.17f, 0.41f, 0.66f, 0.91f };

    // El libro permanece con su oscuridad hasta que termina la emergencia.
    public float BookDisappearDelay => _emergenceDuration;
    public bool IsEmerging => _emergenceStarted && _emergenceElapsed < _emergenceDuration;

    public float MovementMultiplier
    {
        get
        {
            if (State != VelethState.Hunting || _animator == null) return 1f;
            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
            return AnimationMotionSync.EvaluateNormalized(
                state.normalizedTime, _crawlImpulsesPerLoop, _plantedSpeedRatio);
        }
    }

    private void Awake()
    {
        _desiredPos = transform.position;
        _desiredRot = transform.rotation;
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        _visualRenderers = GetComponentsInChildren<Renderer>(true);
        _revealProperties = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        Active = this;
        _emergenceStarted = false;
        _emergenceElapsed = 0f;
        _pendingContacts.Clear();
        _supportClock.Clear();
        _scrapeClock.Clear();
        SetRevealClip(false, 0f);
    }

    private void OnDisable() { if (Active == this) Active = null; }
    private void OnDestroy() { if (Active == this) Active = null; }
    public void SetState(VelethState state) => State = state;

    public float BeginEmergence()
    {
        if (_emergenceStarted) return _emergenceDuration;

        _emergenceStarted = true;
        _emergenceElapsed = 0f;
        float bookPlaneY = RitualBookView.Active != null ? RitualBookView.Active.PuntoDeLuz.y : transform.position.y;
        SetRevealClip(true, bookPlaneY);
        if (_animator != null)
        {
            _animator.Rebind();
            _animator.Update(0f);
            _animator.SetTrigger("BookConsumed");
        }
        StartCoroutine(DisableRevealAfterEmergence());
        return _emergenceDuration;
    }

    private void Update()
    {
        if (IsEmerging)
        {
            _emergenceElapsed = Mathf.Min(_emergenceDuration,
                                          _emergenceElapsed + Time.deltaTime);
            float phase = Mathf.Clamp01(_emergenceElapsed / Mathf.Max(0.01f, _emergenceDuration));
            RegistrarContactos(-10, phase, false);
            return;
        }

        if (State != VelethState.Hunting || _animator == null)
        {
            _supportClock.Clear();
            _scrapeClock.Clear();
            _pendingContacts.Clear();
            return;
        }

        AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
        RegistrarContactos(state.fullPathHash, Mathf.Repeat(state.normalizedTime, 1f), true);
    }

    private void RegistrarContactos(int state, float phase, bool loops)
    {
        if (_supportClock.Advance(state, phase, loops, SupportContacts, out _))
            _pendingContacts.Enqueue(true);
        if (_scrapeClock.Advance(state, phase, loops, ScrapeContacts, out _))
            _pendingContacts.Enqueue(false);
    }

    // true = apoyo breve; false = roce prolongado. El watcher consume cada contacto una
    // sola vez, sin inventar ráfagas al recuperar FPS.
    public bool TryConsumeAudioContact(out bool support, out Vector3 position)
    {
        position = transform.position + Vector3.up * 0.08f;
        if (_pendingContacts.Count == 0) { support = false; return false; }
        support = _pendingContacts.Dequeue();
        return true;
    }

    private System.Collections.IEnumerator DisableRevealAfterEmergence()
    {
        yield return new WaitForSeconds(_emergenceDuration);
        SetRevealClip(false, 0f);
    }

    private void SetRevealClip(bool active, float planeY)
    {
        if (_visualRenderers == null) _visualRenderers = GetComponentsInChildren<Renderer>(true);
        if (_revealProperties == null) _revealProperties = new MaterialPropertyBlock();

        foreach (var renderer in _visualRenderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(_revealProperties);
            _revealProperties.SetFloat("_RevealActive", active ? 1f : 0f);
            _revealProperties.SetFloat("_RevealPlaneY", planeY);
            renderer.SetPropertyBlock(_revealProperties);
        }
    }

    public void MoveTo(Vector3 target, float speed, float deltaTime)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-5f) { Capture(); return; }
        dir.Normalize();
        transform.position += dir * Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime);
        var targetRotation = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _turnSpeed * Mathf.Max(0f, deltaTime));
        Capture();
    }

    public void SetPositionDirectly(Vector3 position) { transform.position = position; Capture(); }
    public void SetRotationDirectly(Quaternion rotation) { transform.rotation = rotation; Capture(); }

    private void Capture()
    {
        _desiredPos = transform.position;
        _desiredRot = transform.rotation;
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        _hasDesired = true;
    }

    private void LateUpdate()
    {
        if (_hasDesired) transform.SetPositionAndRotation(_desiredPos, _desiredRot);
    }
}
