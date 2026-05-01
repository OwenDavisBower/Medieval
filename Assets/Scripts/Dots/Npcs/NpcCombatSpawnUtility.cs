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
                AttackStunUntilUnityTime = 0f,
                IsDead = 0
            });
        }

        /// <summary>Sets <see cref="NpcProfile.Role"/> from spawn and resolves <see cref="NpcProfile.WeaponClass"/> when <see cref="NpcWeaponClass.Unspecified"/>.</summary>
        public static void FinalizeSpawnProfile(EntityManager em, Entity npc, NpcRole role)
        {
            if (!em.Exists(npc))
                return;

            NpcWeaponClass resolved = ResolveWeaponClass(em, npc);
            if (!em.HasComponent<NpcProfile>(npc))
            {
                em.AddComponentData(npc, new NpcProfile { Role = role, WeaponClass = resolved });
                return;
            }

            var profile = em.GetComponentData<NpcProfile>(npc);
            profile.Role = role;
            if (profile.WeaponClass == NpcWeaponClass.Unspecified)
                profile.WeaponClass = resolved;
            em.SetComponentData(npc, profile);
        }

        /// <summary>Uses combat config presence on this entity (baked root). For configs on child entities, set <see cref="NpcProfile.WeaponClass"/> in authoring.</summary>
        public static NpcWeaponClass ResolveWeaponClass(EntityManager em, Entity npc)
        {
            if (!em.Exists(npc))
                return NpcWeaponClass.None;
            bool melee = em.HasComponent<NpcMeleeCombatConfig>(npc);
            bool ranged = em.HasComponent<NpcRangedCombatConfig>(npc);
            if (melee && ranged)
                return NpcWeaponClass.Both;
            if (melee)
                return NpcWeaponClass.Melee;
            if (ranged)
                return NpcWeaponClass.Ranged;
            return NpcWeaponClass.None;
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
