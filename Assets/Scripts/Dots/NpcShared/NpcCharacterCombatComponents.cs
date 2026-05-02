using Unity.Entities;

namespace Medieval.Npcs
{
    /// <summary>High-level NPC kind for DOTS logic (spawn API sets this at runtime; optional bake for subscenes).</summary>
    public enum NpcRole : byte
    {
        Unknown = 0,
        Follower = 1,
        Bandit = 2,
        Villager = 3,
    }

    /// <summary>Weapon capability for AI and gameplay queries (see <see cref="NpcCombatSpawnUtility.FinalizeSpawnProfile"/>).</summary>
    public enum NpcWeaponClass : byte
    {
        /// <summary>Infer from <see cref="NpcMeleeCombatConfig"/> / <see cref="NpcRangedCombatConfig"/> on the spawned root entity.</summary>
        Unspecified = 0,
        None = 1,
        Melee = 2,
        Ranged = 3,
        Both = 4,
    }

    /// <summary>Role and weapon class for ECS NPCs; mirrors faction/weapon setup on <see cref="Character"/> + combat behaviours.</summary>
    public struct NpcProfile : IComponentData
    {
        public NpcRole Role;
        public NpcWeaponClass WeaponClass;
    }

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
        /// <summary>Unity <see cref="UnityEngine.Time.time"/> until melee-style attack stun clears; mirrors <see cref="Character.CanAttack"/>.</summary>
        public float AttackStunUntilUnityTime;
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

    /// <summary>Runtime ranged cadence and release scheduling (Unity time).</summary>
    public struct NpcRangedAttackState : IComponentData
    {
        public float NextFireAllowedUnityTime;
        public float MovementLockUntilUnityTime;
        public float ReleaseShotAtUnityTime;
        public Entity PendingTargetNpcEntity;
        public byte ShotInProgress;
    }

    /// <summary>Runtime melee cooldown (Unity time).</summary>
    public struct NpcMeleeAttackState : IComponentData
    {
        public float NextAttackAllowedUnityTime;
    }
}
