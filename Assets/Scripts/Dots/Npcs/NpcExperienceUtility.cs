using Unity.Entities;
using Unity.Mathematics;

namespace Medieval.Npcs
{
    /// <summary>Grants combat XP to DOTS NPCs and applies level-up stat growth.</summary>
    public static class NpcExperienceUtility
    {
        public const float XpPerDamagePoint = 0.45f;
        public const float KillBonusXp = 85f;
        public const float BuildingDestroyBonusXp = 120f;
        public const float BaseXpToNextLevel = 50f;
        public const int MaxLevel = 99;

        public const float HealthGainPerLevel = 12f;
        public const float HealthRestoreFractionOnLevelUp = 0.2f;
        public const float MeleeDamageMultPerLevel = 1.06f;
        public const float MoveSpeedMultPerLevel = 1.035f;
        public const float AimErrorMultPerLevel = 0.94f;
        public const float BraveryGainPerLevel = 0.4f;
        public const float RangedDamageMultPerLevel = 1.05f;
        const float LevelUpFxSeconds = 1.65f;

        public static void GrantDamageXp(EntityManager em, Entity attacker, float damageDealt, bool killed)
        {
            if (attacker == Entity.Null)
                return;

            float xp = math.max(0f, damageDealt) * XpPerDamagePoint;
            if (killed)
                xp += KillBonusXp;
            if (xp <= 0f)
                return;
            AddXp(em, attacker, xp);
        }

        /// <summary>
        /// Same as <see cref="GrantDamageXp(EntityManager, Entity, float, bool)"/> but defers
        /// <see cref="NpcLevelUpFx"/> adds via ECB (safe while iterating entities).
        /// </summary>
        public static void GrantDamageXp(EntityManager em, ref EntityCommandBuffer ecb, Entity attacker,
            float damageDealt, bool killed)
        {
            if (attacker == Entity.Null)
                return;

            float xp = math.max(0f, damageDealt) * XpPerDamagePoint;
            if (killed)
                xp += KillBonusXp;
            if (xp <= 0f)
                return;
            AddXp(em, ref ecb, attacker, xp);
        }

        public static void GrantBuildingDamageXp(EntityManager em, Entity attacker, float damageDealt, bool destroyed)
        {
            if (attacker == Entity.Null)
                return;

            float xp = math.max(0f, damageDealt) * XpPerDamagePoint;
            if (destroyed)
                xp += BuildingDestroyBonusXp;
            if (xp <= 0f)
                return;
            AddXp(em, attacker, xp);
        }

        public static void GrantBuildingDamageXp(EntityManager em, ref EntityCommandBuffer ecb, Entity attacker,
            float damageDealt, bool destroyed)
        {
            if (attacker == Entity.Null)
                return;

            float xp = math.max(0f, damageDealt) * XpPerDamagePoint;
            if (destroyed)
                xp += BuildingDestroyBonusXp;
            if (xp <= 0f)
                return;
            AddXp(em, ref ecb, attacker, xp);
        }

        public static void AddXp(EntityManager em, Entity npc, float amount)
        {
            if (!TryAccumulateXp(em, npc, amount, out int levelsGained))
                return;
            if (levelsGained > 0)
                RequestLevelUpFx(em, npc, levelsGained);
        }

        public static void AddXp(EntityManager em, ref EntityCommandBuffer ecb, Entity npc, float amount)
        {
            if (!TryAccumulateXp(em, npc, amount, out int levelsGained))
                return;
            if (levelsGained > 0)
                RequestLevelUpFx(em, ref ecb, npc, levelsGained);
        }

        public static float XpRequiredForLevel(int level)
        {
            int safeLevel = math.max(1, level);
            return BaseXpToNextLevel * safeLevel;
        }

        public static NpcExperience CreateStarting()
        {
            return new NpcExperience
            {
                Level = 1,
                CurrentXp = 0f,
                XpToNextLevel = XpRequiredForLevel(1)
            };
        }

        static bool TryAccumulateXp(EntityManager em, Entity npc, float amount, out int levelsGained)
        {
            levelsGained = 0;
            if (amount <= 0f || !em.Exists(npc) || !em.HasComponent<NpcExperience>(npc))
                return false;
            if (em.HasComponent<NpcCharacterCombatState>(npc) &&
                em.GetComponentData<NpcCharacterCombatState>(npc).IsDead != 0)
                return false;

            var xp = em.GetComponentData<NpcExperience>(npc);
            xp.CurrentXp += amount;

            while (xp.Level < MaxLevel && xp.XpToNextLevel > 0.01f && xp.CurrentXp >= xp.XpToNextLevel)
            {
                xp.CurrentXp -= xp.XpToNextLevel;
                xp.Level++;
                xp.XpToNextLevel = XpRequiredForLevel(xp.Level);
                levelsGained++;
                ApplyLevelBonuses(em, npc);
            }

            if (xp.Level >= MaxLevel)
                xp.CurrentXp = 0f;

            em.SetComponentData(npc, xp);
            return true;
        }

        static void ApplyLevelBonuses(EntityManager em, Entity npc)
        {
            if (em.HasComponent<NpcCharacterCombatState>(npc))
            {
                var combat = em.GetComponentData<NpcCharacterCombatState>(npc);
                combat.MaxHealth += HealthGainPerLevel;
                float heal = combat.MaxHealth * HealthRestoreFractionOnLevelUp;
                combat.CurrentHealth = math.min(combat.MaxHealth, combat.CurrentHealth + heal);
                combat.MeleeDamageMultiplier *= MeleeDamageMultPerLevel;
                combat.MovementSpeedMultiplier *= MoveSpeedMultPerLevel;
                combat.RangedAimErrorMultiplier *= AimErrorMultPerLevel;
                combat.Bravery += BraveryGainPerLevel;
                em.SetComponentData(npc, combat);
            }

            if (em.HasComponent<NpcRangedCombatConfig>(npc))
            {
                var ranged = em.GetComponentData<NpcRangedCombatConfig>(npc);
                ranged.ArrowDamage *= RangedDamageMultPerLevel;
                em.SetComponentData(npc, ranged);
            }
        }

        static void RequestLevelUpFx(EntityManager em, Entity npc, int levelsGained)
        {
            if (levelsGained <= 0)
                return;
            if (em.HasComponent<NpcLevelUpFx>(npc))
            {
                var fx = em.GetComponentData<NpcLevelUpFx>(npc);
                fx.SecondsRemaining = LevelUpFxSeconds;
                fx.Spawned = 0;
                em.SetComponentData(npc, fx);
                return;
            }

            em.AddComponentData(npc, new NpcLevelUpFx
            {
                SecondsRemaining = LevelUpFxSeconds,
                Spawned = 0
            });
        }

        static void RequestLevelUpFx(EntityManager em, ref EntityCommandBuffer ecb, Entity npc, int levelsGained)
        {
            if (levelsGained <= 0)
                return;
            if (em.HasComponent<NpcLevelUpFx>(npc))
            {
                var fx = em.GetComponentData<NpcLevelUpFx>(npc);
                fx.SecondsRemaining = LevelUpFxSeconds;
                fx.Spawned = 0;
                em.SetComponentData(npc, fx);
                return;
            }

            ecb.AddComponent(npc, new NpcLevelUpFx
            {
                SecondsRemaining = LevelUpFxSeconds,
                Spawned = 0
            });
        }
    }
}
