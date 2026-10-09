using System.Collections.Generic;
using Scanner;
using UnityEngine;

namespace Gameplay.Spawning
{
    public enum SpawnSurfaceKind
    {
        Floor,
        FurnitureTop
    }

    /// <summary>
    /// Punto unico de validacion para cualquier objeto derivado del mapa escaneado.
    /// Trabaja en espacio anchor-relativo para sobrevivir recalibraciones del origen.
    /// </summary>
    public static class InteriorSpawnValidator
    {
        public const float DefaultSnapTolerance = 0.16f;
        public const float DefaultWallMargin = 0.12f;
        public const float DefaultObjectRadius = 0.10f;
        public const float DefaultPlayerRadius = 0.22f;

        private static InteriorTopology _topology;
        private static int _cachedSignature = int.MinValue;
        private static string _failureReason = "geometria aun no evaluada";

        public static InteriorTopology Topology => _topology;
        public static int GeometryVersion => ComputeGeometrySignature();
        public static string FailureReason => _failureReason;

        public static bool TryEnsureReady(out string reason)
        {
            int signature = ComputeGeometrySignature();
            if (signature == _cachedSignature)
            {
                reason = _failureReason;
                return _topology != null;
            }

            _cachedSignature = signature;
            _topology = null;
            var registry = SceneRegistry.Instance;
            if (registry == null)
            {
                _failureReason = "SceneRegistry no esta disponible";
                reason = _failureReason;
                return false;
            }

            var segments = new List<InteriorTopology.Segment>();
            foreach (var wall in registry.Walls)
            {
                if (wall == null) continue;
                segments.Add(new InteriorTopology.Segment(
                    new Vector2(wall.ALocal.x, wall.ALocal.z),
                    new Vector2(wall.BLocal.x, wall.BLocal.z)));
            }

            if (!InteriorTopology.TryBuild(segments, DefaultSnapTolerance, out _topology, out _failureReason))
            {
                reason = _failureReason;
                return false;
            }

            _failureReason = null;
            reason = null;
            return true;
        }

        public static bool TryValidate(Vector3 relativePosition, SpawnSurfaceKind surface,
                                       CubeObject support, float objectRadius,
                                       float pickupRange, out string reason)
        {
            if (!TryEnsureReady(out reason)) return false;

            Vector2 point = Xz(relativePosition);
            float footprint = Mathf.Max(0.02f, objectRadius);
            if (!_topology.ContainsDisc(point, footprint + DefaultWallMargin))
            {
                reason = "la huella cruza una pared o queda fuera del interior";
                return false;
            }

            if (surface == SpawnSurfaceKind.FurnitureTop)
            {
                if (support == null)
                {
                    reason = "falta el mueble de soporte";
                    return false;
                }
                if (!FurnitureSupports(support, relativePosition, footprint, out reason)) return false;
                if (!FurnitureFootprintIsInside(support, out reason)) return false;
            }
            else if (OverlapsFurniture(point, footprint + 0.03f, null))
            {
                reason = "el volumen del objeto intersecta un mueble";
                return false;
            }

            if (!HasReachablePickup(point, Mathf.Max(0.5f, pickupRange), support))
            {
                reason = "no existe una posicion interior accesible para recogerlo";
                return false;
            }

            reason = null;
            return true;
        }

        public static bool CanPickupNow(uint clientId, Vector3 targetWorld,
                                        float maxDistance, float targetRadius,
                                        out string reason)
        {
            Vector3 origin;
            if (clientId == 0)
            {
                if (Camera.main == null)
                {
                    reason = "el host no tiene camara activa";
                    return false;
                }
                origin = Camera.main.transform.position;
            }
            else if (NetworkManager.Instance == null ||
                     !NetworkManager.Instance.TryGetClientWorldPosition(clientId, out origin))
            {
                reason = "el cliente no reporto una posicion valida";
                return false;
            }

            float allowed = Mathf.Max(0.1f, maxDistance);
            if ((origin - targetWorld).sqrMagnitude > allowed * allowed)
            {
                reason = "el jugador esta demasiado lejos";
                return false;
            }
            if (!PlayerLights.HasLineOfSight(origin, targetWorld, targetRadius))
            {
                reason = "una pared u obstaculo bloquea la interaccion";
                return false;
            }

            reason = null;
            return true;
        }

        public static bool TryValidateActorInPlayerRoom(Vector3 playerWorld,
                                                        Vector3 candidateWorld,
                                                        float actorRadius,
                                                        out int roomIndex,
                                                        out string reason)
        {
            roomIndex = -1;
            if (!TryEnsureReady(out reason)) return false;
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady)
            {
                reason = "WorldOrigin no esta listo";
                return false;
            }

            Vector3 playerRelative = WorldOrigin.Instance.ToRelative(playerWorld);
            Vector3 candidateRelative = WorldOrigin.Instance.ToRelative(candidateWorld);
            Vector2 player = Xz(playerRelative);
            Vector2 candidate = Xz(candidateRelative);

            if (!_topology.TryGetRoomIndex(player, out roomIndex))
            {
                reason = "el jugador no esta dentro de un ambiente cerrado";
                return false;
            }
            if (!_topology.ContainsDiscInRoom(roomIndex, candidate,
                    Mathf.Max(0.02f, actorRadius) + DefaultWallMargin))
            {
                reason = "el cuerpo no entra en el mismo ambiente que el jugador";
                return false;
            }
            if (!_topology.HasClearSegment(player, candidate))
            {
                reason = "una pared separa al jugador del punto de aparicion";
                return false;
            }

            reason = null;
            return true;
        }

        public static float EstimatePrefabFootprintRadius(GameObject prefab, float fallback)
        {
            float radius = Mathf.Max(0.02f, fallback);
            if (prefab == null) return radius;

            Transform root = prefab.transform;
            // Conserva rotacion y escala del root; solo elimina su traslacion de asset.
            Matrix4x4 toSpawnOrigin = Matrix4x4.Translate(-root.position);
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                radius = Mathf.Max(radius, BoundsRadiusXZ(
                    filter.sharedMesh.bounds, toSpawnOrigin * filter.transform.localToWorldMatrix));
            }
            foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                radius = Mathf.Max(radius, BoundsRadiusXZ(
                    renderer.localBounds, toSpawnOrigin * renderer.transform.localToWorldMatrix));
            }
            return radius;
        }

        private static bool FurnitureSupports(CubeObject cube, Vector3 relativePosition,
                                              float radius, out string reason)
        {
            if (WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady)
            {
                reason = "WorldOrigin no esta listo";
                return false;
            }
            Vector3 local = cube.transform.InverseTransformPoint(
                WorldOrigin.Instance.ToWorld(relativePosition));
            Vector3 scale = cube.transform.lossyScale;
            float marginX = radius / Mathf.Max(0.001f, Mathf.Abs(scale.x));
            float marginZ = radius / Mathf.Max(0.001f, Mathf.Abs(scale.z));
            if (Mathf.Abs(local.x) > 0.5f - marginX || Mathf.Abs(local.z) > 0.5f - marginZ)
            {
                reason = "la huella no entra completa sobre el mueble";
                return false;
            }
            reason = null;
            return true;
        }

        private static bool FurnitureFootprintIsInside(CubeObject cube, out string reason)
        {
            Vector3 center = cube.transform.localPosition;
            Quaternion rotation = cube.transform.localRotation;
            Vector3 scale = cube.transform.localScale;
            float hx = Mathf.Abs(scale.x) * 0.5f;
            float hz = Mathf.Abs(scale.z) * 0.5f;
            var corners = new[]
            {
                center + rotation * new Vector3(-hx, 0f, -hz),
                center + rotation * new Vector3( hx, 0f, -hz),
                center + rotation * new Vector3( hx, 0f,  hz),
                center + rotation * new Vector3(-hx, 0f,  hz)
            };
            for (int i = 0; i < corners.Length; i++)
            {
                if (_topology.ContainsDisc(Xz(corners[i]), DefaultWallMargin)) continue;
                reason = "el mueble de soporte cruza el limite interior";
                return false;
            }
            reason = null;
            return true;
        }

        private static bool HasReachablePickup(Vector2 target, float pickupRange, CubeObject support)
        {
            float accessDistance = Mathf.Clamp(pickupRange * 0.55f, 0.55f, 1.35f);
            const int samples = 12;
            for (int i = 0; i < samples; i++)
            {
                float angle = i * Mathf.PI * 2f / samples;
                Vector2 access = target + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * accessDistance;
                if (!_topology.ContainsDisc(access, DefaultPlayerRadius + DefaultWallMargin)) continue;
                if (OverlapsFurniture(access, DefaultPlayerRadius, support)) continue;
                if (!_topology.HasClearSegment(access, target)) continue;
                return true;
            }
            return false;
        }

        private static bool OverlapsFurniture(Vector2 point, float radius, CubeObject ignored)
        {
            var registry = SceneRegistry.Instance;
            if (registry == null || WorldOrigin.Instance == null || !WorldOrigin.Instance.IsReady) return false;
            Vector3 relative = new(point.x, FloorPoint.Instance != null ? FloorPoint.Instance.LocalY : 0f, point.y);
            Vector3 world = WorldOrigin.Instance.ToWorld(relative);
            foreach (var cube in registry.Cubes)
            {
                if (cube == null || cube == ignored) continue;
                Vector3 local = cube.transform.InverseTransformPoint(world);
                Vector3 scale = cube.transform.lossyScale;
                float xMargin = radius / Mathf.Max(0.001f, Mathf.Abs(scale.x));
                float zMargin = radius / Mathf.Max(0.001f, Mathf.Abs(scale.z));
                if (Mathf.Abs(local.x) <= 0.5f + xMargin &&
                    Mathf.Abs(local.z) <= 0.5f + zMargin) return true;
            }
            return false;
        }

        private static int ComputeGeometrySignature()
        {
            unchecked
            {
                var registry = SceneRegistry.Instance;
                if (registry == null) return 0;
                int hash = 17;
                hash = hash * 31 + registry.Generacion;
                hash = hash * 31 + registry.Walls.Count;
                hash = hash * 31 + registry.Cubes.Count;
                foreach (var wall in registry.Walls)
                {
                    if (wall == null) { hash = hash * 31; continue; }
                    hash = hash * 31 + QuantizedHash(wall.ALocal);
                    hash = hash * 31 + QuantizedHash(wall.BLocal);
                }
                foreach (var cube in registry.Cubes)
                {
                    if (cube == null) { hash = hash * 31; continue; }
                    hash = hash * 31 + QuantizedHash(cube.transform.localPosition);
                    hash = hash * 31 + QuantizedHash(cube.transform.localScale);
                    hash = hash * 31 + Mathf.RoundToInt(cube.transform.localEulerAngles.y * 10f);
                }
                return hash;
            }
        }

        private static int QuantizedHash(Vector3 value)
        {
            unchecked
            {
                int x = Mathf.RoundToInt(value.x * 1000f);
                int y = Mathf.RoundToInt(value.y * 1000f);
                int z = Mathf.RoundToInt(value.z * 1000f);
                return ((x * 397) ^ y) * 397 ^ z;
            }
        }

        private static float BoundsRadiusXZ(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            float radius = 0f;
            for (int ix = 0; ix < 2; ix++)
            for (int iy = 0; iy < 2; iy++)
            for (int iz = 0; iz < 2; iz++)
            {
                Vector3 corner = new(
                    ix == 0 ? min.x : max.x,
                    iy == 0 ? min.y : max.y,
                    iz == 0 ? min.z : max.z);
                Vector3 local = matrix.MultiplyPoint3x4(corner);
                radius = Mathf.Max(radius, new Vector2(local.x, local.z).magnitude);
            }
            return radius;
        }

        private static Vector2 Xz(Vector3 value) => new(value.x, value.z);
    }
}
