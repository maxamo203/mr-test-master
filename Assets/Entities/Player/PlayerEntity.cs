using UnityEngine;

// Pure game logic for the player. No knowledge of networking.
public class PlayerEntity : MonoBehaviour
{
    public Vector3 Position => transform.position;
    public int     Health   { get; private set; } = 100;
    public bool    IsDead   => Health <= 0;

    [SerializeField] public float Speed       = 5f;
    [SerializeField] public float AttackRange  = 1.5f;
    [SerializeField] public int   AttackDamage = 25;

    public void ApplyMoveInput(Vector3 dir, float deltaTime)
    {
        if (IsDead || dir == Vector3.zero) return;
        transform.position += dir.normalized * Speed * deltaTime;
    }

    public void TakeDamage(int amount)
    {
        Health = Mathf.Max(0, Health - amount);
    }

    public void SetPositionDirectly(Vector3 pos) => transform.position = pos;
    public void SetHealthDirectly(int h)         => Health = Mathf.Clamp(h, 0, 100);

    public void Respawn(Vector3 pos)
    {
        transform.position = pos;
        Health = 100;
    }
}
