using UnityEngine;

// Pure game logic for the Sorker NPC. No knowledge of networking.
public class Sorker : MonoBehaviour
{
    public Vector3 Position => transform.position;
    public int     Health   { get; private set; } = 150;
    public bool    IsDead   => Health <= 0;

    [SerializeField] public float Speed = 3f;

    public void MoveTo(Vector3 target, float deltaTime)
    {
        if (IsDead) return;
        var dir = (target - transform.position);
        if (dir.sqrMagnitude < 0.01f) return;
        dir.y = 0f; // mantener upright
        transform.position += dir.normalized * Speed * deltaTime;
        transform.rotation  = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    public void SetRotationDirectly(Quaternion rot) => transform.rotation = rot;

    public void TakeDamage(int amount)
    {
        Health = Mathf.Max(0, Health - amount);
    }

    public void SetPositionDirectly(Vector3 pos) => transform.position = pos;
    public void SetHealthDirectly(int h)         => Health = Mathf.Clamp(h, 0, 150);
}
