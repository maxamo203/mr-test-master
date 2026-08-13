using UnityEngine;

public enum VelethState : byte
{
    Hunting  = 0,
    Grabbing = 1,
}

// Entidad compartida e inevitable que aparece cuando se pierde el libro. El director
// del servidor fija su pose; VelethNetwork la replica a todos los dispositivos.
public class VelethEntity : MonoBehaviour
{
    public static VelethEntity Active { get; private set; }

    public Vector3 Position => transform.position;
    public VelethState State { get; private set; } = VelethState.Hunting;

    [SerializeField, Min(1f)] private float _turnSpeed = 260f;

    private Vector3 _desiredPos;
    private Quaternion _desiredRot;
    private bool _hasDesired;

    private void Awake()
    {
        _desiredPos = transform.position;
        _desiredRot = transform.rotation;
    }

    private void OnEnable() => Active = this;
    private void OnDisable() { if (Active == this) Active = null; }
    private void OnDestroy() { if (Active == this) Active = null; }

    public void SetState(VelethState state) => State = state;

    public void MoveTo(Vector3 target, float speed, float deltaTime)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-5f) { Capture(); return; }

        dir.Normalize();
        transform.position += dir * Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime);
        var targetRotation = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRotation, _turnSpeed * Mathf.Max(0f, deltaTime));
        Capture();
    }

    public void SetPositionDirectly(Vector3 position)
    {
        transform.position = position;
        Capture();
    }

    public void SetRotationDirectly(Quaternion rotation)
    {
        transform.rotation = rotation;
        Capture();
    }

    private void Capture()
    {
        _desiredPos = transform.position;
        _desiredRot = transform.rotation;
        _hasDesired = true;
    }

    private void LateUpdate()
    {
        if (!_hasDesired) return;
        transform.SetPositionAndRotation(_desiredPos, _desiredRot);
    }
}

