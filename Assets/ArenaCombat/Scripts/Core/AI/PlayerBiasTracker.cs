using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using ArenaCombat.Core.Skill;
using ArenaCombat.Core.Network;

namespace ArenaCombat.Core.AI
{
    [DisallowMultipleComponent]
    public class PlayerBiasTracker : MonoBehaviour
    {
        public static PlayerBiasTracker Instance { get; private set; }

        [SerializeField] float _evalInterval = 5f;
        [SerializeField] float _rangedDistanceThreshold = 15f;
        [SerializeField, Min(0.01f)] float _teamCloseThreshold = 10f;

        class PlayerBiasData
        {
            public int meleeAttempts;
            public int skillCasts;
            public int rangedSkillCasts;
            public int survivalSkillCasts;
            public int parryAttempts;
            public int ropeUses;
            public int totalActions;
            public float teamDistanceSum;
            public int teamDistanceSamples;
            public float[] biases = new float[9];
        }

        Dictionary<ulong, PlayerBiasData> _data = new();
        float _nextEvalTime;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(this); return; }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        // Lifecycle forwarding to PlayerArchetypeClassifier (C3a Phase B):
        // mirror Register/Unregister so the classifier's per-client dictionary
        // stays in sync without requiring new call sites in PNC3D.
        public void RegisterPlayer(ulong clientId)
        {
            if (IsServer) _data.TryAdd(clientId, new PlayerBiasData());
            PlayerArchetypeClassifier.Instance?.RegisterPlayer(clientId);
        }

        public void UnregisterPlayer(ulong clientId)
        {
            if (IsServer) _data.Remove(clientId);
            PlayerArchetypeClassifier.Instance?.UnregisterPlayer(clientId);
        }

        public void RecordMelee(ulong clientId)
        {
            if (!IsServer || !_data.TryGetValue(clientId, out var d)) return;
            d.meleeAttempts++;
            d.totalActions++;

            if (PlayerArchetypeClassifier.Instance != null)
                PlayerArchetypeClassifier.Instance.RecordMeleeHit(clientId, DistToBoss(clientId));
        }

        public void RecordSkillCast(ulong clientId, SkillDefinition def, SkillContext ctx)
        {
            if (!IsServer || !_data.TryGetValue(clientId, out var d)) return;
            d.skillCasts++;
            d.totalActions++;
            bool ranged = (def.RoleTags != null && System.Array.Exists(def.RoleTags, t => t == SkillRoleTag.Ranged))
                          || (ctx != null && ctx.TargetDistance > _rangedDistanceThreshold);
            if (ranged) d.rangedSkillCasts++;
            bool survival = def.RoleTags != null && System.Array.Exists(def.RoleTags, t =>
                t == SkillRoleTag.Heal || t == SkillRoleTag.Shield || t == SkillRoleTag.Survival || t == SkillRoleTag.Regen);
            if (survival) d.survivalSkillCasts++;

            // Use authoritative DistToBoss rather than ctx.TargetDistance — ctx may target
            // Self (TargetDistance=0) or a non-boss target, or be mutated by runtime steps.
            if (PlayerArchetypeClassifier.Instance != null)
                PlayerArchetypeClassifier.Instance.RecordSkillCast(clientId, def, DistToBoss(clientId));
        }

        public void RecordParry(ulong clientId)
        {
            if (!IsServer || !_data.TryGetValue(clientId, out var d)) return;
            d.parryAttempts++;
            d.totalActions++;

            PlayerArchetypeClassifier.Instance?.RecordParrySuccess(clientId);
        }

        // Returns float.NaN if boss not spawned or player position unavailable.
        // Classifier callers must check IsNaN and skip weight application.
        static float DistToBoss(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.ConnectedClients.TryGetValue(clientId, out var nc) || nc.PlayerObject == null)
                return float.NaN;
            if (BossManager.Instance == null || BossManager.Instance.CurrentBoss == null)
                return float.NaN;
            return Vector3.Distance(
                nc.PlayerObject.transform.position,
                BossManager.Instance.CurrentBoss.transform.position);
        }

        public void RecordRope(ulong clientId)
        {
            if (!IsServer || !_data.TryGetValue(clientId, out var d)) return;
            d.ropeUses++;
            d.totalActions++;
        }

        public void ResetAllCounters()
        {
            foreach (var kvp in _data)
            {
                var d = kvp.Value;
                d.meleeAttempts = d.skillCasts = d.rangedSkillCasts = d.survivalSkillCasts = 0;
                d.parryAttempts = d.ropeUses = d.totalActions = 0;
                d.teamDistanceSum = 0; d.teamDistanceSamples = 0;
                for (int i = 0; i < d.biases.Length; i++) d.biases[i] = 0f;
            }
            PlayerArchetypeClassifier.Instance?.ResetAllWeights();
        }

        public float[] GetBiases(ulong clientId)
        {
            if (_data.TryGetValue(clientId, out var d)) return d.biases;
            return null;
        }

        public float[] GetAverageBiases()
        {
            if (_data.Count == 0) return null;
            float[] avg = new float[9];
            foreach (var kvp in _data)
            {
                for (int i = 0; i < 9; i++)
                    avg[i] += kvp.Value.biases[i];
            }
            for (int i = 0; i < 9; i++)
                avg[i] /= _data.Count;
            return avg;
        }

        void FixedUpdate()
        {
            if (!IsServer) return;
            SampleTeamDistance();
            if (Time.time < _nextEvalTime) return;
            _nextEvalTime = Time.time + _evalInterval;
            EvaluateAll();
        }

        void SampleTeamDistance()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            var clients = nm.ConnectedClientsList;
            foreach (var kvp in _data)
            {
                ulong id = kvp.Key;
                if (!nm.ConnectedClients.TryGetValue(id, out var nc) || nc.PlayerObject == null) continue;
                Vector3 pos = nc.PlayerObject.transform.position;
                foreach (var other in clients)
                {
                    if (other.ClientId == id || other.PlayerObject == null) continue;
                    float dist = Vector3.Distance(pos, other.PlayerObject.transform.position);
                    kvp.Value.teamDistanceSum += dist;
                    kvp.Value.teamDistanceSamples++;
                }
            }
        }

        void EvaluateAll()
        {
            foreach (var kvp in _data)
            {
                var d = kvp.Value;
                int t = Mathf.Max(d.totalActions, 1);
                d.biases[0] = (float)d.meleeAttempts / t;
                d.biases[1] = (float)d.rangedSkillCasts / t;
                d.biases[2] = (float)(d.meleeAttempts + d.skillCasts) / t;
                d.biases[3] = (float)d.survivalSkillCasts / t;
                d.biases[4] = (float)d.parryAttempts / t;
                d.biases[5] = (float)d.ropeUses / t;
                d.biases[6] = (float)d.skillCasts / t;
                if (d.teamDistanceSamples > 0)
                {
                    float avgDist = d.teamDistanceSum / d.teamDistanceSamples;
                    float threshold = Mathf.Max(_teamCloseThreshold, 0.01f);
                    d.biases[7] = avgDist < threshold ? 1f - (avgDist / threshold) : 0f;
                    d.biases[8] = avgDist > threshold ? Mathf.Min((avgDist - threshold) / threshold, 1f) : 0f;
                }
                else
                {
                    d.biases[7] = 0f;
                    d.biases[8] = 0f;
                }
                Debug.Log($"[Bias] client={kvp.Key} M={d.biases[0]:F2} R={d.biases[1]:F2} AF={d.biases[2]:F2} S={d.biases[3]:F2} P={d.biases[4]:F2} Rp={d.biases[5]:F2} SF={d.biases[6]:F2} TC={d.biases[7]:F2} TS={d.biases[8]:F2}");
                d.meleeAttempts = d.skillCasts = d.rangedSkillCasts = d.survivalSkillCasts = 0;
                d.parryAttempts = d.ropeUses = d.totalActions = 0;
                d.teamDistanceSum = 0; d.teamDistanceSamples = 0;
            }
        }
    }
}
