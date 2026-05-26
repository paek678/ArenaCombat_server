// ARCH TAG: SHARED
// ARCH SCOPE: Server-side request validation shared across gameplay modes.
// ARCH STATUS: TARGET_3D_PENDING

using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

namespace ArenaCombat.Core.Network
{
    /// <summary>
    /// Server-side input validation utility (currently rate-limit focused).
    /// Typical flow: Client -> ServerRpc -> server validation -> ClientRpc.
    /// </summary>
    public class InputValidator : MonoBehaviour
    {
        public static InputValidator Instance { get; private set; }

        [Header("=== Rate Limit Settings ===")]
        [SerializeField] private int maxRequestsPerSecond = 60;
        [SerializeField] private float rateLimitWindow = 1f;

        [Header("=== Tick Validation Settings ===")]
        [SerializeField] private int maxAcceptedTickGap = 128;

        // Per-client per-request-type tracking.
        private readonly Dictionary<ulong, Dictionary<string, RequestTracker>> clientTrackers
            = new Dictionary<ulong, Dictionary<string, RequestTracker>>();

        // Per-client, per-request-type monotonic tick tracking.
        private readonly Dictionary<ulong, Dictionary<string, int>> lastProcessedTickByClientAndType
            = new Dictionary<ulong, Dictionary<string, int>>();

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

        /// <summary>
        /// Registers a client for request tracking.
        /// </summary>
        public void RegisterClient(ulong clientId)
        {
            if (!clientTrackers.ContainsKey(clientId))
            {
                clientTrackers[clientId] = new Dictionary<string, RequestTracker>();
                Debug.Log($"[InputValidator] Registered client {clientId}");
            }

            if (!lastProcessedTickByClientAndType.ContainsKey(clientId))
            {
                lastProcessedTickByClientAndType[clientId] = new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// Unregisters a client.
        /// </summary>
        public void UnregisterClient(ulong clientId)
        {
            if (clientTrackers.Remove(clientId))
            {
                Debug.Log($"[InputValidator] Unregistered client {clientId}");
            }

            lastProcessedTickByClientAndType.Remove(clientId);
        }

        /// <summary>
        /// Rate-limit check. Returns false when request budget is exceeded.
        /// </summary>
        public bool CheckRateLimit(ulong clientId, string requestType = "generic")
        {
            if (!clientTrackers.TryGetValue(clientId, out var trackersByType))
            {
                RegisterClient(clientId);
                trackersByType = clientTrackers[clientId];
            }

            if (!trackersByType.TryGetValue(requestType, out var tracker))
            {
                tracker = new RequestTracker();
                trackersByType[requestType] = tracker;
            }

            tracker.CleanOldRequests(rateLimitWindow);

            if (tracker.RequestCount >= maxRequestsPerSecond)
            {
                Debug.LogWarning($"[InputValidator] Client {clientId} rate limited ({requestType})");
                return false;
            }

            tracker.RecordRequest();
            return true;
        }

        /// <summary>
        /// Validates monotonic client tick ordering. Rejects duplicate/out-of-order ticks.
        /// </summary>
        public bool ValidateTickOrder(ulong clientId, int clientTick, string requestType = "generic")
        {
            string normalizedType = NormalizeRequestType(requestType);

            if (!lastProcessedTickByClientAndType.TryGetValue(clientId, out var tickTable))
            {
                RegisterClient(clientId);
                tickTable = lastProcessedTickByClientAndType[clientId];
            }

            if (!tickTable.TryGetValue(normalizedType, out int lastTick))
            {
                lastTick = int.MinValue;
            }

            if (clientTick <= lastTick)
            {
                Debug.LogWarning($"[InputValidator] Tick rejected ({requestType}) client={clientId}, tick={clientTick}, last={lastTick}");
                return false;
            }

            if (lastTick != int.MinValue)
            {
                int gap = clientTick - lastTick;
                if (gap > maxAcceptedTickGap)
                {
                    Debug.LogWarning($"[InputValidator] Tick gap too large ({requestType}) client={clientId}, gap={gap}");
                    return false;
                }
            }

            tickTable[normalizedType] = clientTick;
            return true;
        }

        /// <summary>
        /// Validates and clamps 2D move input vector to legal magnitude.
        /// </summary>
        public bool ValidateMoveInput2D(Vector2 rawInput, out Vector2 sanitizedInput)
        {
            sanitizedInput = SanitizeVector2(rawInput);

            if (sanitizedInput == Vector2.zero)
            {
                return true;
            }

            if (sanitizedInput.sqrMagnitude > TopDown3DConstants.MAX_MOVE_INPUT_MAGNITUDE * TopDown3DConstants.MAX_MOVE_INPUT_MAGNITUDE)
            {
                sanitizedInput = sanitizedInput.normalized * TopDown3DConstants.MAX_MOVE_INPUT_MAGNITUDE;
            }

            return true;
        }

        /// <summary>
        /// Sanitizes float input (NaN/Infinity protection).
        /// </summary>
        public float SanitizeFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;
            return value;
        }

        /// <summary>
        /// Sanitizes Vector2 input.
        /// </summary>
        public Vector2 SanitizeVector2(Vector2 value)
        {
            return new Vector2(SanitizeFloat(value.x), SanitizeFloat(value.y));
        }

        /// <summary>
        /// Sanitizes Vector3 input.
        /// </summary>
        public Vector3 SanitizeVector3(Vector3 value)
        {
            return new Vector3(
                SanitizeFloat(value.x),
                SanitizeFloat(value.y),
                SanitizeFloat(value.z)
            );
        }

        /// <summary>
        /// Clears a client's tick history.
        /// </summary>
        public void ResetTickTracking(ulong clientId)
        {
            if (lastProcessedTickByClientAndType.TryGetValue(clientId, out var tickTable))
            {
                tickTable.Clear();
            }
        }

        private static string NormalizeRequestType(string requestType)
        {
            if (string.IsNullOrWhiteSpace(requestType))
            {
                return "generic";
            }

            return requestType.Trim().ToLowerInvariant();
        }

        #region Internal

        private class RequestTracker
        {
            private readonly Queue<float> requestTimes = new Queue<float>();

            public int RequestCount => requestTimes.Count;

            public void RecordRequest()
            {
                requestTimes.Enqueue(Time.time);
            }

            public void CleanOldRequests(float windowSize)
            {
                float cutoff = Time.time - windowSize;
                while (requestTimes.Count > 0 && requestTimes.Peek() < cutoff)
                {
                    requestTimes.Dequeue();
                }
            }
        }

        #endregion
    }
}
