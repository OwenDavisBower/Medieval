using Unity.Entities;
using Unity.Mathematics;

namespace Medieval.DotsCombat
{
    public enum CombatRole : byte
    {
        Ranged = 0,
        Melee = 1
    }

    public enum RangedAttackMode : byte
    {
        DirectDamage = 0,
        Projectile = 1
    }

    /// <summary>Static tuning for target acquisition and attacks.</summary>
    public struct NpcCombatConfig : IComponentData
    {
        public CombatRole Role;
        public RangedAttackMode RangedMode;

        public float AggroRadius;
        public float CombatRange;

        public float AttackInterval;
        public float Damage;

        // Ranged.
        public float RangedWindupSeconds;
        public Entity ProjectilePrefab;
        public float ProjectileSpeed;
        public float ProjectileHitRadius;
        public float ProjectileMaxLifetimeSeconds;
        public float3 ProjectileSpawnOffset;

        // Melee.
        public float MeleeRange;
        public float MeleeStunSeconds;
    }

    /// <summary>Runtime combat state (cooldowns / in-progress windups).</summary>
    public struct NpcCombatState : IComponentData
    {
        public float NextAttackTime;
        public float RangedFireTime;
        public byte RangedShotQueued;
    }

    /// <summary>Current chosen target entity (or Null).</summary>
    public struct NpcCurrentTarget : IComponentData
    {
        public Entity Value;
        public float3 LastKnownPosition;
        public byte HasTarget;
    }
}

