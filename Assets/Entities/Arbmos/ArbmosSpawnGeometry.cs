using UnityEngine;

public static class ArbmosSpawnGeometry
{
    public const float MinimumDistance = 2f;
    public const float BodyRadius = 0.35f;
    public const float BodyHeight = 1.85f;

    // Devuelve una banda lateral que mantiene todo el ancho del cuerpo dentro de
    // cámara, pero fuera del haz y de su borde perceptible.
    public static bool TryLateralAngleBand(float horizontalHalfFov,
                                           float beamHalfAngle,
                                           float distance,
                                           out float minimumAngle,
                                           out float maximumAngle)
    {
        float safeDistance = Mathf.Max(0.01f, distance);
        float bodyAngle = Mathf.Atan(BodyRadius / safeDistance) * Mathf.Rad2Deg;
        minimumAngle = Mathf.Max(beamHalfAngle + bodyAngle + 3f, bodyAngle + 8f);
        maximumAngle = horizontalHalfFov - bodyAngle - 2f;
        return maximumAngle >= minimumAngle;
    }

    public static Vector3 LateralDirection(Vector3 forward, float signedAngle)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        return Quaternion.AngleAxis(signedAngle, Vector3.up) * forward.normalized;
    }

    public static bool PointInsideView(Vector3 cameraPosition, Vector3 cameraForward,
                                       Vector3 point, float horizontalHalfFov,
                                       float verticalHalfFov, float marginDeg = 1f)
    {
        Vector3 forward = cameraForward.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
        Vector3 up = Vector3.Cross(forward, right).normalized;
        Vector3 to = point - cameraPosition;
        float depth = Vector3.Dot(to, forward);
        if (depth <= 0.01f) return false;

        float horizontal = Mathf.Abs(Mathf.Atan2(Vector3.Dot(to, right), depth) *
                                     Mathf.Rad2Deg);
        float vertical = Mathf.Abs(Mathf.Atan2(Vector3.Dot(to, up), depth) *
                                   Mathf.Rad2Deg);
        return horizontal <= horizontalHalfFov - marginDeg &&
               vertical <= verticalHalfFov - marginDeg;
    }
}
