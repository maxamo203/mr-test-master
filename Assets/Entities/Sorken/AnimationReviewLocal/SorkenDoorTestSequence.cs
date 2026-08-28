using UnityEngine;
using UnityEngine.InputSystem;

public sealed class SorkenDoorTestSequence : MonoBehaviour
{
    private enum Phase { Waiting, Emerging, Chasing, Grabbing }

    [Header("Scene references")]
    public Transform player;
    public Transform sorken;
    public Animator animator;
    public CharacterController sorkenController;
    public Renderer darknessRenderer;

    [Header("Timing")]
    public float darknessLeadTime = 2f;
    public float emergeDuration = 2.967f;
    public float emergeTravel = 2.15f;
    public float chaseSpeed = 1.35f;
    public float grabDistance = 1.25f;

    private const string IdleState = "01_Sorken_Idle";
    private const string ChaseState = "02_Sorken_Base_InjuredWalk";
    private const string GrabState = "06_Sorken_Grab_Attack";
    private const string DoorState = "07_Sorken_Emerge_Door";

    private Phase _phase;
    private float _phaseTime;
    private Vector3 _startPosition;
    private Vector3 _doorEndPosition;
    private Vector3 _initialPlayerPosition;
    private Quaternion _initialPlayerRotation;

    private void Start()
    {
        _startPosition = sorken.position;
        _doorEndPosition = _startPosition + sorken.forward * emergeTravel;
        _initialPlayerPosition = player.position;
        _initialPlayerRotation = player.rotation;
        ResetSequence();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            ResetSequence();

        _phaseTime += Time.deltaTime;
        PulseDarkness();

        switch (_phase)
        {
            case Phase.Waiting:
                if (_phaseTime >= darknessLeadTime) BeginEmergence();
                break;

            case Phase.Emerging:
                float t = Mathf.Clamp01(_phaseTime / emergeDuration);
                sorken.position = Vector3.Lerp(_startPosition, _doorEndPosition, SmoothStep(t));
                if (t >= 1f) BeginChase();
                break;

            case Phase.Chasing:
                ChasePlayer();
                break;

            case Phase.Grabbing:
                FacePlayer(360f);
                break;
        }
    }

private void ResetSequence()
{
    _phase = Phase.Waiting;
    _phaseTime = 0f;
    sorken.gameObject.SetActive(true);
    sorken.position = _startPosition;
    sorken.rotation = Quaternion.identity;
    animator.Play(IdleState, 0, 0f);
    animator.Update(0f);
    sorken.gameObject.SetActive(false);
    if (darknessRenderer != null) darknessRenderer.gameObject.SetActive(true);
    player.SetPositionAndRotation(_initialPlayerPosition, _initialPlayerRotation);
}

    private void BeginEmergence()
    {
        _phase = Phase.Emerging;
        _phaseTime = 0f;
        sorken.gameObject.SetActive(true);
        animator.Play(DoorState, 0, 0f);
        animator.Update(0f);
    }

    private void BeginChase()
    {
        _phase = Phase.Chasing;
        _phaseTime = 0f;
        if (darknessRenderer != null) darknessRenderer.gameObject.SetActive(false);
        animator.CrossFadeInFixedTime(ChaseState, 0.18f, 0, 0f);
    }

    private void BeginGrab()
    {
        _phase = Phase.Grabbing;
        _phaseTime = 0f;
        animator.CrossFadeInFixedTime(GrabState, 0.08f, 0, 0f);
    }

    private void ChasePlayer()
    {
        Vector3 toPlayer = player.position - sorken.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance <= grabDistance)
        {
            BeginGrab();
            return;
        }

        if (distance < 0.001f) return;
        Vector3 desired = toPlayer / distance;
        Vector3 origin = sorken.position + Vector3.up * 0.85f;

        if (Physics.SphereCast(origin, 0.34f, desired, out RaycastHit hit, 1.25f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            !hit.transform.IsChildOf(player))
        {
            Vector3 tangent = Vector3.Cross(Vector3.up, hit.normal).normalized;
            if (Vector3.Dot(tangent, desired) < 0f) tangent = -tangent;
            desired = Vector3.Slerp(desired, tangent, 0.85f).normalized;
        }

        Quaternion targetRotation = Quaternion.LookRotation(desired, Vector3.up);
        sorken.rotation = Quaternion.RotateTowards(sorken.rotation, targetRotation, 120f * Time.deltaTime);
        sorkenController.Move(desired * chaseSpeed * Time.deltaTime);
    }

    private void FacePlayer(float degreesPerSecond)
    {
        Vector3 direction = player.position - sorken.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;
        Quaternion target = Quaternion.LookRotation(direction.normalized, Vector3.up);
        sorken.rotation = Quaternion.RotateTowards(sorken.rotation, target, degreesPerSecond * Time.deltaTime);
    }

    private void PulseDarkness()
    {
        if (darknessRenderer == null || !darknessRenderer.gameObject.activeSelf) return;
        float pulse = 1f + Mathf.Sin(Time.time * 2.7f) * 0.025f;
        darknessRenderer.transform.localScale = new Vector3(3.15f * pulse, 2.45f * pulse, 1f);
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);

    private void OnGUI()
    {
        GUI.Box(new Rect(18, 18, 390, 112), "PRUEBA SORKEN — PUERTA");
        GUI.Label(new Rect(34, 47, 350, 22), "WASD mover | Mouse mirar | Shift correr | R reiniciar");
        GUI.Label(new Rect(34, 70, 350, 22), $"Estado: {_phase}");
        if (player != null && sorken != null)
            GUI.Label(new Rect(34, 93, 350, 22), $"Distancia: {Vector3.Distance(player.position, sorken.position):0.00} m");
    }
}
