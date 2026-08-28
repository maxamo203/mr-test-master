using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Light))]
public sealed class SorkenDoorTestFlashlight : MonoBehaviour
{
    public Key toggleKey = Key.F;
    public bool startsOn = false;
    [Min(0f)] public float reactionDistance = 10f;
    [Range(0.05f, 0.5f)] public float faceSampleRadius = 0.22f;
    [Range(0.5f, 1f)] public float faceHeightNormalized = 0.84f;

    private Light _light;

    public bool IsOn => _light != null && _light.enabled;

    private void Awake()
    {
        _light = GetComponent<Light>();
        _light.type = LightType.Spot;
        _light.enabled = startsOn;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
            _light.enabled = !_light.enabled;
    }

public bool Illuminates(Transform target, Renderer targetRenderer)
    {
        if (!IsOn || target == null) return false;

        Bounds bounds = targetRenderer != null
            ? targetRenderer.bounds
            : new Bounds(target.position + Vector3.up, Vector3.one);
        Vector3 faceCenter = new Vector3(
            bounds.center.x,
            Mathf.Lerp(bounds.min.y, bounds.max.y, faceHeightNormalized),
            bounds.center.z);
        Vector3 right = transform.right * faceSampleRadius;
        Vector3 up = transform.up * faceSampleRadius;
        Vector3[] samples =
        {
            faceCenter,
            faceCenter + right,
            faceCenter - right,
            faceCenter + up,
            faceCenter - up,
            faceCenter + right + up,
            faceCenter + right - up,
            faceCenter - right + up,
            faceCenter - right - up
        };

        float maximumDistance = Mathf.Min(_light.range, reactionDistance);
        foreach (Vector3 point in samples)
        {
            Vector3 delta = point - transform.position;
            float distance = delta.magnitude;
            if (distance < 0.001f || distance > maximumDistance) continue;

            Vector3 direction = delta / distance;
            if (Vector3.Angle(transform.forward, direction) > _light.spotAngle * 0.5f)
                continue;

            if (!Physics.Raycast(transform.position, direction, out RaycastHit hit, distance + 0.2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                continue;

            if (hit.transform == target || hit.transform.IsChildOf(target))
                return true;
        }

        return false;
    }
}