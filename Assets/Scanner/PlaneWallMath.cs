using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Scanner
{
    // Conversión ARPlane (vertical) → segmento recto de pared, en coordenadas
    // anchor-relativas (listas para WallObject.Create). Extraído de
    // AutoWallBuilder (escaneo BETA con confirmación manual) para que
    // LiveWallDetector (detección en vivo durante la partida, sin confirmación)
    // lo reuse sin duplicar la geometría — ver conversación/plan sobre el modo
    // de detección en vivo.
    public static class PlaneWallMath
    {
        // false si el plano no se puede interpretar como una pared recta
        // (demasiado inclinado, o el boundary es degenerado/muy angosto).
        public static bool TryComputeWallFromPlane(ARPlane plane,
            out Vector3 aLocal, out Vector3 bLocal, out float height, out int side)
        {
            aLocal = bLocal = Vector3.zero;
            height = 0f;
            side = 1;

            if (WorldOrigin.Instance == null) return false;

            var boundary = plane.boundary;
            if (boundary.Length < 2) return false;

            // ARPlane siempre guarda su boundary en espacio local XZ con normal =
            // +Y local, sea el plano horizontal o vertical — TransformPoint da los
            // vértices reales en mundo aunque el plano sea una pared.
            var normalHoriz = new Vector3(plane.transform.up.x, 0f, plane.transform.up.z);
            if (normalHoriz.sqrMagnitude < 1e-6f) return false; // demasiado inclinado: no es una pared
            normalHoriz.Normalize();
            var baseHat = Vector3.Cross(Vector3.up, normalHoriz).normalized;
            float perp = Vector3.Dot(plane.transform.position, normalHoriz);

            float minProj = float.MaxValue, maxProj = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var pt in boundary)
            {
                var world = plane.transform.TransformPoint(new Vector3(pt.x, 0f, pt.y));
                float proj = Vector3.Dot(world, baseHat);
                if (proj < minProj) minProj = proj;
                if (proj > maxProj) maxProj = proj;
                if (world.y < minY) minY = world.y;
                if (world.y > maxY) maxY = world.y;
            }
            if (maxProj - minProj < 0.05f) return false;

            var aWorld = normalHoriz * perp + baseHat * minProj; aWorld.y = minY;
            var bWorld = normalHoriz * perp + baseHat * maxProj; bWorld.y = minY;
            height = Mathf.Max(0.1f, maxY - minY);

            aLocal = WorldOrigin.Instance.ToRelative(aWorld);
            bLocal = WorldOrigin.Instance.ToRelative(bWorld);

            // Lado de extrusión: a diferencia de WallBuilder.DecideSide (que adivina
            // con la posición de la cámara), acá ya tenemos la normal REAL sensada
            // por ARCore — side es el que hace coincidir WallObject.Normal con ella.
            var baseHatLocal = (bLocal - aLocal).normalized;
            var n0Local = Vector3.Cross(Vector3.up, baseHatLocal);
            var targetNormalLocal = WorldOrigin.Instance.ToRelativeDir(normalHoriz);
            side = n0Local.sqrMagnitude > 1e-6f && Vector3.Dot(n0Local.normalized, targetNormalLocal) >= 0f ? 1 : -1;

            return true;
        }
    }
}
