using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Medieval.Npcs
{
    public static class NpcCombatSpawnUtility
    {
        public static void RollAndAttachCombatState(EntityManager em, Entity npc)
        {
            if (!em.Exists(npc) || !em.HasComponent<NpcCharacterBakedStats>(npc))
                return;
            if (em.HasComponent<NpcCharacterCombatState>(npc))
                return;

            var bake = em.GetComponentData<NpcCharacterBakedStats>(npc);
            uint seed = 1u;
            if (em.HasComponent<LocalTransform>(npc))
            {
                float3 p = em.GetComponentData<LocalTransform>(npc).Position;
                seed = math.max(1u, math.hash(p) ^ (uint)npc.Index ^ 0x9E3779B9u);
            }

            var rng = new Random(seed);
            float maxHealth = rng.NextFloat(bake.MinHealth, bake.MaxHealth);
            float strength = rng.NextFloat(bake.MinStrength, bake.MaxStrength);
            float dexterity = rng.NextFloat(bake.MinDexterity, bake.MaxDexterity);
            float focus = rng.NextFloat(bake.MinFocus, bake.MaxFocus);
            float bravery = rng.NextFloat(bake.MinBravery, bake.MaxBravery);

            em.AddComponentData(npc, new NpcCharacterCombatState
            {
                CurrentHealth = maxHealth,
                MaxHealth = maxHealth,
                MeleeDamageMultiplier = StatMultiplier(strength, bake.MinStrength, bake.MaxStrength, 0.78f, 1.22f),
                MovementSpeedMultiplier = StatMultiplier(dexterity, bake.MinDexterity, bake.MaxDexterity, 0.86f, 1.14f),
                RangedAimErrorMultiplier = StatMultiplier(focus, bake.MinFocus, bake.MaxFocus, 1.28f, 0.62f),
                Bravery = bravery,
                IsDead = 0
            });
        }

        static float StatT(float value, float min, float max)
        {
            if (max <= min + 0.001f)
                return 0.5f;
            return math.clamp((value - min) / (max - min), 0f, 1f);
        }

        static float StatMultiplier(float value, float min, float max, float atMin, float atMax)
        {
            return math.lerp(atMin, atMax, StatT(value, min, max));
        }
    }
}
