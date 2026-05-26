using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using ArenaCombat.Core.AI;
using ArenaCombat.Core.Skill;

namespace ArenaCombat.Core.Network
{
    [DisallowMultipleComponent]
    public class RoundRecordLogger : MonoBehaviour
    {
        public static RoundRecordLogger Instance { get; private set; }

        [System.Serializable]
        public struct SkillLoadoutRecord
        {
            public string entityName;
            public int stage;
            public string[] skills;
        }

        [SerializeField] private List<SkillLoadoutRecord> _records = new();

        private GameStateManager _subscribedGSM;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(this); return; }
        }

        void OnEnable() { TrySubscribe(); }
        void OnDisable() { Unsubscribe(); }

        void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (_subscribedGSM == null) TrySubscribe();
        }

        void TrySubscribe()
        {
            if (_subscribedGSM != null || GameStateManager.Instance == null) return;
            _subscribedGSM = GameStateManager.Instance;
            _subscribedGSM.OnCardDraftEndedServer += HandleDraftEnded;
            _subscribedGSM.OnMatchStateChanged += HandleMatchStateChanged;
        }

        void Unsubscribe()
        {
            if (_subscribedGSM == null) return;
            _subscribedGSM.OnCardDraftEndedServer -= HandleDraftEnded;
            _subscribedGSM.OnMatchStateChanged -= HandleMatchStateChanged;
            _subscribedGSM = null;
        }

        void HandleDraftEnded(int round)
        {
            if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer) return;
            SnapshotAll(round);
        }

        void SnapshotAll(int stage)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            foreach (var client in nm.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                var skillMgr = client.PlayerObject.GetComponentInChildren<SkillManager>();
                if (skillMgr == null) continue;

                RecordLoadout($"Player_{client.ClientId}", stage, skillMgr);
            }

            if (BossManager.Instance != null && BossManager.Instance.CurrentBoss != null)
            {
                var bossSkillMgr = BossManager.Instance.CurrentBoss.GetComponentInChildren<SkillManager>();
                if (bossSkillMgr != null)
                    RecordLoadout("Boss", stage, bossSkillMgr);
            }
        }

        void RecordLoadout(string entityName, int stage, SkillManager skillMgr)
        {
            var slots = skillMgr.Slots;
            var skills = new string[slots.Count];
            for (int i = 0; i < slots.Count; i++)
                skills[i] = slots[i] != null ? (slots[i].DisplayName ?? slots[i].name) : "(empty)";

            _records.Add(new SkillLoadoutRecord
            {
                entityName = entityName,
                stage = stage,
                skills = skills
            });

            Debug.Log($"[RoundRecord] {entityName} | stage={stage} | [{string.Join(", ", skills)}]");
        }

        void HandleMatchStateChanged(MatchState prev, MatchState next)
        {
            if (next != MatchState.MatchEnd && next != MatchState.RoundEnd) return;
            if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer) return;
            DumpPlayerClassification(next);
        }

        void DumpPlayerClassification(MatchState endState)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            var classifier = PlayerArchetypeClassifier.Instance;
            var biasTracker = PlayerBiasTracker.Instance;
            var poolMgr = BossAIPoolManager.Instance;

            Debug.Log("══════════════════════════════════════════");
            Debug.Log($"[{endState}] Player Classification Report");
            Debug.Log("══════════════════════════════════════════");

            foreach (var client in nm.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                ulong id = client.ClientId;

                var archetype = classifier != null ? classifier.GetArchetype(id) : PlayerArchetype.Hybrid;

                string weightsStr = "(N/A)";
                if (classifier != null && classifier.TryGetWeights(id, out float m, out float r, out float c))
                    weightsStr = $"M={m:F2} R={r:F2} C={c:F2} (total={m + r + c:F2})";

                string biasStr = "(N/A)";
                if (biasTracker != null)
                {
                    float[] biases = biasTracker.GetBiases(id);
                    if (biases != null)
                        biasStr = $"Melee={biases[0]:F2} Ranged={biases[1]:F2} ActFreq={biases[2]:F2} " +
                                  $"Surv={biases[3]:F2} Parry={biases[4]:F2} Rope={biases[5]:F2} " +
                                  $"SkillFreq={biases[6]:F2} TeamClose={biases[7]:F2} TeamSpread={biases[8]:F2}";
                }

                Debug.Log($"  Player_{id}: Archetype={archetype} | Weights: {weightsStr}");
                Debug.Log($"    Biases: {biasStr}");
            }

            if (poolMgr != null)
            {
                var current = poolMgr.CurrentVariant;
                Debug.Log($"  Boss AI Variant: {(current != null ? current.variantName ?? current.name : "(none)")}");
            }

            Debug.Log("══════════════════════════════════════════");
        }

        public void RecordManual(string entityName, int stage, SkillManager skillMgr)
        {
            RecordLoadout(entityName, stage, skillMgr);
        }

        public IReadOnlyList<SkillLoadoutRecord> Records => _records;

        public void Clear() { _records.Clear(); }

        public void DumpAll()
        {
            Debug.Log("══════════════════════════════════════════");
            Debug.Log("[RoundRecord] Full Dump");
            Debug.Log("══════════════════════════════════════════");
            if (_records.Count == 0)
            {
                Debug.Log("  (empty)");
                return;
            }
            foreach (var r in _records)
                Debug.Log($"  {r.entityName} | stage={r.stage} | [{string.Join(", ", r.skills)}]");
            Debug.Log("══════════════════════════════════════════");
        }
    }
}
