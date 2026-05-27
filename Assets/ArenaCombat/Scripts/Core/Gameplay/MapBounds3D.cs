// ARCH TAG: TARGET_3D
// ARCH SCOPE: Top-down 3D map bounds plus walkable/anchor validation helpers for server authority path.
// ARCH STATUS: ACTIVE_PREP

using UnityEngine;
using System.Collections.Generic;

namespace ArenaCombat.Core
{
    /// <summary>
    /// Defines 3D world bounds and map-validation helpers for top-down gameplay.
    /// Intended for server-side validation, rope target checks, and safe respawn positioning.
    /// </summary>
    public class MapBounds3D : MonoBehaviour
    {
        public static MapBounds3D Instance { get; private set; }

        [Header("=== 3D Bounds ===")]
        [SerializeField] private Vector3 minBounds = new Vector3(-75f, 0f, -75f);
        [SerializeField] private Vector3 maxBounds = new Vector3(75f, 20f, 75f);

        [Header("=== Kill Zone ===")]
        [SerializeField] private float killZoneY = -10f;

        [Header("=== Respawn ===")]
        [SerializeField] private Vector3 defaultRespawnPoint = new Vector3(0f, 1f, 0f);
        [SerializeField] private Transform[] respawnPoints;

        [Header("=== Walkable Validation (Optional) ===")]
        [SerializeField] private bool useWalkableValidation = false;
        [SerializeField] private LayerMask walkableMask;
        [SerializeField] private float walkableProbeHeight = 4f;
        [SerializeField] private float walkableSearchStep = 1f;
        [SerializeField] private int walkableSearchRings = 6;

        [Header("=== Rope Validation (Optional) ===")]
        [SerializeField] private LayerMask ropeObstacleMask;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public bool IsInsideBounds(Vector3 position)
        {
            return position.x >= minBounds.x && position.x <= maxBounds.x
                && position.y >= minBounds.y && position.y <= maxBounds.y
                && position.z >= minBounds.z && position.z <= maxBounds.z;
        }

        public bool IsInsideHorizontalBounds(Vector3 position)
        {
            return position.x >= minBounds.x && position.x <= maxBounds.x
                && position.z >= minBounds.z && position.z <= maxBounds.z;
        }

        public bool IsBelowKillZone(Vector3 position)
        {
            return position.y < killZoneY;
        }

        public Vector3 ClampToBounds(Vector3 position)
        {
            return new Vector3(
                Mathf.Clamp(position.x, minBounds.x, maxBounds.x),
                Mathf.Clamp(position.y, minBounds.y, maxBounds.y),
                Mathf.Clamp(position.z, minBounds.z, maxBounds.z)
            );
        }

        public Vector3 ClampToHorizontalBounds(Vector3 position)
        {
            return new Vector3(
                Mathf.Clamp(position.x, minBounds.x, maxBounds.x),
                position.y,
                Mathf.Clamp(position.z, minBounds.z, maxBounds.z)
            );
        }

        /// <summary>
        /// Resolves a server position using 2-step validation:
        /// 1) AABB clamp safety net, 2) optional walkable projection.
        /// </summary>
        public Vector3 ResolveServerPosition(Vector3 candidate, Vector3 fallback)
        {
            Vector3 clamped = ClampToHorizontalBounds(candidate);
            if (!useWalkableValidation || walkableMask.value == 0)
            {
                return clamped;
            }

            if (TryFindNearestWalkableXZ(clamped, out Vector3 resolved))
            {
                resolved.y = clamped.y;
                return resolved;
            }

            Vector3 fallbackClamped = ClampToHorizontalBounds(fallback);
            if (TryFindNearestWalkableXZ(fallbackClamped, out resolved))
            {
                resolved.y = fallbackClamped.y;
                return resolved;
            }

            return fallbackClamped;
        }

        /// <summary>
        /// Finds a safe spawn point near preferred location.
        /// </summary>
        public Vector3 GetSafeSpawnPoint(Vector3 preferred)
        {
            Vector3 candidate = ResolveServerPosition(preferred, defaultRespawnPoint);
            candidate.y = preferred.y;
            return candidate;
        }

        /// <summary>
        /// Returns a safe respawn point, prioritizing configured respawn points.
        /// </summary>
        public Vector3 GetRespawnPointNear(Vector3 preferred)
        {
            if (respawnPoints != null && respawnPoints.Length > 0)
            {
                Vector3 chosen = respawnPoints[0] != null ? respawnPoints[0].position : defaultRespawnPoint;
                float bestDist = float.MaxValue;

                foreach (Transform point in respawnPoints)
                {
                    if (point == null)
                    {
                        continue;
                    }

                    Vector3 pointPos = ClampToHorizontalBounds(point.position);
                    if (!IsInsideHorizontalBounds(pointPos))
                    {
                        continue;
                    }

                    float dist = (pointPos - preferred).sqrMagnitude;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        chosen = point.position;
                    }
                }

                return GetSafeSpawnPoint(chosen);
            }

            return GetSafeSpawnPoint(defaultRespawnPoint);
        }

        /// <summary>
        /// Tries to resolve rope target with bounds clamp and optional obstacle filtering.
        /// </summary>
        public bool TryResolveRopeTarget(
            Vector3 origin,
            Vector3 anchorHint,
            bool hasAnchorHint,
            Vector3 direction,
            float maxDistance,
            LayerMask anchorMask,
            out Vector3 resolvedTarget,
            out string detail)
        {
            resolvedTarget = Vector3.zero;
            detail = "Unknown";

            Vector3 moveDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
            Vector3 candidate;

            if (moveDirection != Vector3.zero)
            {
                if (Physics.Raycast(origin, moveDirection, out RaycastHit anchorHit, maxDistance, anchorMask))
                {
                    candidate = anchorHit.point;
                }
                else
                {
                    detail = "NoAnchor";
                    return false;
                }
            }
            else
            {
                if (!hasAnchorHint)
                {
                    detail = "InvalidAnchorHint";
                    return false;
                }
                candidate = anchorHint;
            }

            Vector3 toCandidate = candidate - origin;
            float distance = toCandidate.magnitude;
            if (distance > maxDistance)
            {
                candidate = origin + toCandidate.normalized * maxDistance;
            }

            candidate = ClampToHorizontalBounds(candidate);
            if (!IsInsideHorizontalBounds(candidate))
            {
                detail = "OutOfBounds";
                return false;
            }

            if (ropeObstacleMask.value != 0 && Physics.Linecast(origin, candidate, ropeObstacleMask, QueryTriggerInteraction.Ignore))
            {
                detail = "BlockedByObstacle";
                return false;
            }

            resolvedTarget = candidate;
            detail = "Resolved";
            return true;
        }

        public Vector3 GetRespawnPoint()
        {
            return GetRespawnPointNear(defaultRespawnPoint);
        }

        private bool TryFindNearestWalkableXZ(Vector3 position, out Vector3 walkableXZ)
        {
            // First probe current XZ.
            if (TryProjectToWalkable(position, out walkableXZ))
            {
                return true;
            }

            if (walkableSearchStep <= 0f || walkableSearchRings <= 0)
            {
                return false;
            }

            // Spiral-like ring sampling around requested XZ.
            for (int ring = 1; ring <= walkableSearchRings; ring++)
            {
                float radius = ring * walkableSearchStep;
                int samples = Mathf.Max(8, ring * 8);
                for (int i = 0; i < samples; i++)
                {
                    float angle = (i / (float)samples) * Mathf.PI * 2f;
                    Vector3 sample = new Vector3(
                        position.x + Mathf.Cos(angle) * radius,
                        position.y,
                        position.z + Mathf.Sin(angle) * radius
                    );

                    if (!IsInsideHorizontalBounds(sample))
                    {
                        continue;
                    }

                    if (TryProjectToWalkable(sample, out walkableXZ))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryProjectToWalkable(Vector3 position, out Vector3 projected)
        {
            Vector3 rayOrigin = new Vector3(position.x, position.y + walkableProbeHeight, position.z);
            float rayDistance = walkableProbeHeight * 2f;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayDistance, walkableMask, QueryTriggerInteraction.Ignore))
            {
                projected = new Vector3(hit.point.x, position.y, hit.point.z);
                return true;
            }

            projected = position;
            return false;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Vector3 center = (minBounds + maxBounds) * 0.5f;
            Vector3 size = maxBounds - minBounds;
            Gizmos.DrawWireCube(center, size);

            Gizmos.color = Color.red;
            Vector3 a = new Vector3(minBounds.x, killZoneY, minBounds.z);
            Vector3 b = new Vector3(maxBounds.x, killZoneY, minBounds.z);
            Vector3 c = new Vector3(maxBounds.x, killZoneY, maxBounds.z);
            Vector3 d = new Vector3(minBounds.x, killZoneY, maxBounds.z);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(defaultRespawnPoint, 0.5f);

            if (respawnPoints != null)
            {
                Gizmos.color = new Color(0f, 1f, 1f, 0.9f);
                foreach (Transform point in respawnPoints)
                {
                    if (point == null)
                    {
                        continue;
                    }

                    Gizmos.DrawWireSphere(point.position, 0.35f);
                }
            }
        }
    }
}
