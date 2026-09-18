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

    private Vector3 _desiredPos;
    private Quaternion _desiredRot;
    private bool _hasDesired;
    private bool _emergenceStarted;
    private Renderer[] _visualRenderers;
    private MaterialPropertyBlock _revealProperties;

    // El libro permanece con su oscuridad hasta que termina la emergencia.
    public float BookDisappearDelay => _emergenceDuration;

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
        SetRevealClip(false, 0f);
    }

    private void OnDisable() { if (Active == this) Active = null; }
    private void OnDestroy() { if (Active == this) Active = null; }
    public void SetState(VelethState state) => State = state;

    public float BeginEmergence()
    {
        if (_emergenceStarted) return _emergenceDuration;

        _emergenceStarted = true;
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
