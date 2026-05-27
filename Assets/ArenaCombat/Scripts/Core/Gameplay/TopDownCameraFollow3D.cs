// ARCH TAG: TARGET_3D
// ARCH SCOPE: Top-down 3D camera follow behavior for local owner presentation.
// ARCH STATUS: ACTIVE_PREP

using UnityEngine;

namespace ArenaCombat.Core
{
    /// <summary>
    /// Smooth top-down 3D camera follow.
    /// Keeps fixed camera pitch while tracking target on XZ plane.
    /// </summary>
    public class TopDownCameraFollow3D : MonoBehaviour
    {
        [Header("=== Follow Settings ===")]
        [SerializeField] private Vector3 offset = new Vector3(0f, 24f, -12f);
        [SerializeField] private Vector3 rotationEuler = new Vector3(60f, 0f, 0f);
        [SerializeField] private float smoothSpeed = 8f;
        [SerializeField] private float spectateZoomMultiplier = 1.5f;

        [Header("=== Bounds (Optional) ===")]
        [SerializeField] private bool useBounds = false;
        [SerializeField] private Vector2 minXZ = new Vector2(-50f, -50f);
        [SerializeField] private Vector2 maxXZ = new Vector2(50f, 50f);

        private Transform target;
        private Vector3 velocity;
        private bool _spectating;
        private Vector3 _baseOffset;

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            _spectating = false;
            _baseOffset = offset;
            if (target == null)
            {
                return;
            }

            Vector3 snapPos = target.position + offset;
            snapPos = ClampXZ(snapPos);
            transform.position = snapPos;
            transform.rotation = Quaternion.Euler(rotationEuler);
        }

        public void SetSpectateTarget(Transform newTarget)
        {
            target = newTarget;
            _spectating = true;
            _baseOffset = offset;
            if (target == null) return;

            Vector3 snapPos = target.position + offset * spectateZoomMultiplier;
            snapPos = ClampXZ(snapPos);
            transform.position = snapPos;
            transform.rotation = Quaternion.Euler(rotationEuler);
        }

        public Transform GetTarget()
        {
            return target;
        }

        public bool IsSpectating => _spectating;

        public void ClearTarget()
        {
            target = null;
            _spectating = false;
            velocity = Vector3.zero;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 activeOffset = _spectating ? offset * spectateZoomMultiplier : offset;
            Vector3 desiredPosition = target.position + activeOffset;
            desiredPosition = ClampXZ(desiredPosition);

            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPosition,
                ref velocity,
                1f / Mathf.Max(0.01f, smoothSpeed)
            );

            transform.rotation = Quaternion.Euler(rotationEuler);
        }

        private Vector3 ClampXZ(Vector3 position)
        {
            if (!useBounds)
            {
                return position;
            }

            return new Vector3(
                Mathf.Clamp(position.x, minXZ.x, maxXZ.x),
                position.y,
                Mathf.Clamp(position.z, minXZ.y, maxXZ.y)
            );
        }
    }
}

