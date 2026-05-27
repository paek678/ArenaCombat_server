using Unity.InferenceEngine;
using UnityEngine;
using ArenaCombat.Core.Skill;

namespace ArenaCombat.Core.AI
{
    [CreateAssetMenu(menuName = "ArenaCombat/AI/BossAIDefinition", fileName = "BossAI_")]
    public class BossAIDefinition : ScriptableObject
    {
        public string variantName;

        [Header("Archetype Pair")]
        public PlayerArchetype playerType1 = PlayerArchetype.Hybrid;
        public PlayerArchetype playerType2 = PlayerArchetype.Hybrid;

        public (PlayerArchetype, PlayerArchetype) PairKey =>
            playerType1 <= playerType2 ? (playerType1, playerType2) : (playerType2, playerType1);

        public bool isDefault = false;

        [Header("ML Model")]
        public ModelAsset model;

        public SkillDefinition[] skillSlots = new SkillDefinition[SkillManager.SlotCount];

        public float[] slotWeights = new float[SkillManager.SlotCount];

        [Range(0.1f, 2f)] public float cooldownScale = 1f;

        void OnValidate()
        {
            if (skillSlots == null || skillSlots.Length != SkillManager.SlotCount)
                System.Array.Resize(ref skillSlots, SkillManager.SlotCount);

            if (slotWeights == null || slotWeights.Length != SkillManager.SlotCount)
                System.Array.Resize(ref slotWeights, SkillManager.SlotCount);

            for (int i = 0; i < slotWeights.Length; i++)
            {
                float w = slotWeights[i];
                if (float.IsNaN(w) || float.IsInfinity(w) || w <= 0f) slotWeights[i] = 1f;
            }
        }
    }
}
