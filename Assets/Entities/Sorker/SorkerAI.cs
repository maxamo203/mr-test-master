using System.Collections.Generic;
using UnityEngine;

// Solo activo en el servidor (en clientes se desactiva en NetworkManager.SpawnLocally).
// Maneja el comportamiento del Sorker: persigue al jugador mas cercano respetando
// paredes/puertas/cubos via SorkerNav (A* 2D), y ataca cuando llega.
[RequireComponent(typeof(Sorker))]
public class SorkerAI : MonoBehaviour
{
    [SerializeField] private float _attackRange    = 1.5f;
    [SerializeField] private int   _attackDamage   = 10;
    [SerializeField] private float _attackCooldown = 1f;

    [Header("Navegacion")]
    [Tooltip("Cada cuanto recalcular el camino (s).")]
    [SerializeField] private float _repathInterval = 0.3f;
    [Tooltip("Distancia para dar por alcanzado un waypoint (m).")]
    [SerializeField] private float _waypointTolerance = 0.2f;
    [Tooltip("Si el jugador se movio mas que esto desde el ultimo path, recalcular ya (m).")]
    [SerializeField] private float _targetMovedThresh = 0.35f;

    private Sorker _sorker;
    private float  _attackTimer;

    private readonly List<Vector3> _path = new();
    private int     _pathIndex;
    private float   _repathTimer;
    private Vector3 _lastTargetPos;
    private bool    _hasTargetPos;

    private void Awake()
    {
        _sorker = GetComponent<Sorker>();
        SorkerNav.Ensure(); // asegura el navegador en el server
    }

    private void Update()
    {
        if (_sorker.IsDead) return;

        if (!FindNearestTarget(out Vector3 targetPos, out PlayerEntity targetEntity)) return;

        float dist = Vector3.Distance(_sorker.Position, targetPos);

        if (dist <= _attackRange)
        {
            _path.Clear();
            _attackTimer += Time.deltaTime;
            if (_attackTimer >= _attackCooldown && targetEntity != null)
            {
                _attackTimer = 0f;
                targetEntity.TakeDamage(_attackDamage);
                Debug.Log($"[Sorker] Hit player {targetEntity.name} -> {targetEntity.Health} HP");
            }
            return;
        }

        FollowOrRepath(targetPos);
    }

    // ── Pathfinding + movimiento ──────────────────────────────────────────
    private void FollowOrRepath(Vector3 targetPos)
    {
        _repathTimer -= Time.deltaTime;
        bool targetMoved = !_hasTargetPos || Vector3.Distance(targetPos, _lastTargetPos) > _targetMovedThresh;
        bool noPath      = _pathIndex >= _path.Count;

        if (_repathTimer <= 0f || targetMoved || noPath)
        {
            _repathTimer  = _repathInterval;
            _lastTargetPos = targetPos;
            _hasTargetPos  = true;

            if (SorkerNav.Instance != null &&
                SorkerNav.Instance.TryGetPath(_sorker.Position, targetPos, _path))
            {
                _pathIndex = 0;
                SkipReachedWaypoints();
            }
            else
            {
                _path.Clear(); // sin obstaculos o sin camino: ir derecho
            }
        }

        // Mover hacia el proximo waypoint, o derecho al target si no hay camino.
        Vector3 step = (_pathIndex < _path.Count) ? _path[_pathIndex] : targetPos;
        _sorker.MoveTo(step, Time.deltaTime);

        if (_pathIndex < _path.Count &&
            HorizontalDist(_sorker.Position, _path[_pathIndex]) <= _waypointTolerance)
            _pathIndex++;
    }

    // Si el primer waypoint ya quedo atras (estamos arriba de el), avanzarlo.
    private void SkipReachedWaypoints()
    {
        while (_pathIndex < _path.Count &&
               HorizontalDist(_sorker.Position, _path[_pathIndex]) <= _waypointTolerance)
            _pathIndex++;
    }

    private static float HorizontalDist(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ── Targeting ─────────────────────────────────────────────────────────
    // Considera los PlayerEntity spawneados Y la camara local del server (el host
    // es la camara AR, que puede no tener PlayerEntity). Devuelve la posicion mas
    // cercana; targetEntity es null si el mas cercano es la camara del host.
    private bool FindNearestTarget(out Vector3 pos, out PlayerEntity entity)
    {
        pos = Vector3.zero; entity = null;
        float minDist = float.MaxValue;
        bool found = false;

        if (EntityRegistry.Instance != null)
        {
            foreach (var e in EntityRegistry.Instance.All)
            {
                if (e.EntityTypeId != EntityTypeIds.Player) continue;
                var pe = e.GetComponent<PlayerEntity>();
                if (pe == null || pe.IsDead) continue;
                float d = Vector3.Distance(_sorker.Position, pe.Position);
                if (d < minDist) { minDist = d; pos = pe.Position; entity = pe; found = true; }
            }
        }

        // El host (server) tambien es un jugador: su camara AR.
        if (Camera.main != null)
        {
            float d = Vector3.Distance(_sorker.Position, Camera.main.transform.position);
            if (d < minDist) { minDist = d; pos = Camera.main.transform.position; entity = null; found = true; }
        }

        return found;
    }
}
