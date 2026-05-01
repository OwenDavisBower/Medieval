using Unity.Entities;

namespace Medieval.Npcs
{
    /// <summary>Serialized stat ranges from <see cref="Character"/> for DOTS NPC prefabs.</summary>
    public struct NpcCharacterBakedStats : IComponentData
    {
        public float MinHealth;
        public float MaxHealth;
        public float MinStrength;
        public float MaxStrength;
        public float MinDexterity;
        public float MaxDexterity;
        public float MinFocus;
        public float MaxFocus;
        public float MinBravery;
        public float MaxBravery;
        public float FleeFracLowBravery;
        public float FleeFracHighBravery;
    }

    /// <summary>Runtime combat state rolled at spawn for baked NPCs.</summary>
    public struct NpcCharacterCombatState : IComponentData
    {
        public float CurrentHealth;
        public float MaxHealth;
        public float MeleeDamageMultiplier;
        public float MovementSpeedMultiplier;
        public float RangedAimErrorMultiplier;
        public float Bravery;
        public byte IsDead;
    }

    public struct NpcRangedCombatConfig : IComponentData
    {
        public float ArrowDamage;
        public float ArrowMaxLifetime;
        public float ArrowHitRadius;
        public float FireInterval;
        public float LaunchHeight;
        public float TargetAimHeight;
        public float HorizontalAimError;
        public float VerticalAimError;
        public float FireAnimationLeadSeconds;
        public float MovementLockDuration;
    }

    public struct NpcMeleeCombatConfig : IComponentData
    {
        public float AttackInterval;
        public float MeleeRange;
        public float HitChance;
        public float Damage;
        public float KnockbackImpulse;
        public float HitMeleeStunDuration;
    }
}
