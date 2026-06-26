using UnityEngine;

// Pure game logic for the Sorker NPC. No knowledge of networking.
public class Sorker : MonoBehaviour
{
    public Vector3 Position => transform.position;
    public int     Health   { get; private set; } = 150;
    public bool    IsDead   => Health <= 0;

    [SerializeField] public float Speed = 3f;
    // Velocidad de giro en grados/seg. Mas chico = giro mas suave/lento.
    [SerializeField] public float TurnSpeed = 180f;

    public void MoveTo(Vector3 target, float deltaTime)
    {
        if (IsDead) return;
        var dir = (target - transform.position);
        dir.y = 0f; // mantener upright (giro solo en horizontal)
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        transform.position += dir * Speed * deltaTime;

        // Giro suave hacia la direccion de movimiento (en vez de snap instantaneo).
        var targetRot = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, TurnSpeed * deltaTime);
    }

    public void SetRotationDirectly(Quaternion rot) => transform.rotation = rot;

    public void TakeDamage(int amount)
    {
        Health = Mathf.Max(0, Health - amount);
    }

    public void SetPositionDirectly(Vector3 pos) => transform.position = pos;
    public void SetHealthDirectly(int h)         => Health = Mathf.Clamp(h, 0, 150);
}
