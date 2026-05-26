using UnityEngine;

// Only active on the server. Drives Sorker behavior.
[RequireComponent(typeof(Sorker))]
public class SorkerAI : MonoBehaviour
{
    [SerializeField] private float _attackRange   = 1.5f;
    [SerializeField] private int   _attackDamage  = 10;
    [SerializeField] private float _attackCooldown = 1f;

    private Sorker _sorker;
    private float  _attackTimer;

    private void Awake() => _sorker = GetComponent<Sorker>();

    private void Update()
    {
        if (_sorker.IsDead) return;

        var target = FindNearestPlayer();
        if (target == null) return;

        float dist = Vector3.Distance(_sorker.Position, target.Position);

        if (dist > _attackRange)
        {
            _sorker.MoveTo(target.Position, Time.deltaTime);
        }
        else
        {
            _attackTimer += Time.deltaTime;
            if (_attackTimer >= _attackCooldown)
            {
                _attackTimer = 0f;
                target.TakeDamage(_attackDamage);
                Debug.Log($"[Sorker] Hit player {target.name} → {target.Health} HP");
            }
        }
    }

    private PlayerEntity FindNearestPlayer()
    {
        PlayerEntity nearest = null;
        float        minDist = float.MaxValue;

        foreach (var entity in EntityRegistry.Instance.All)
        {
            if (entity.EntityTypeId != EntityTypeIds.Player) continue;
            var pe = entity.GetComponent<PlayerEntity>();
            if (pe == null || pe.IsDead) continue;

            float d = Vector3.Distance(_sorker.Position, pe.Position);
            if (d < minDist) { minDist = d; nearest = pe; }
        }

        return nearest;
    }
}
