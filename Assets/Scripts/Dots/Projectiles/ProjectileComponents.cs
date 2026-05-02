using Unity.Entities;
using Unity.Mathematics;

namespace Medieval.Projectiles
{
    public struct ProjectileTag : IComponentData { }

    public struct ProjectileVelocity : IComponentData
    {
        public float3 Value;
    }

    public struct ProjectileLifetime : IComponentData
    {
        public float SecondsRemaining;
    }

    public struct ProjectileDamage : IComponentData
    {
        public float Amount;
    }

    /// <summary>
    /// Combat root entity for friendly-fire, dodge filtering, and DOTS segment-hit exclusion.
    /// <see cref="Unity.Entities.Entity.Null"/> when the shooter is not represented in ECS (use legacy id) or is environmental.
    /// </summary>
    public struct ProjectileShooterRoot : IComponentData
    {
        public Entity Value;
    }

    /// <summary>
    /// When <see cref="ProjectileShooterRoot"/> is <see cref="Unity.Entities.Entity.Null"/>, Unity instance ID of
    /// <c>transform.root</c> for physics self-hit filtering (GameObject archers, towers) until those call sites pass an entity.
    /// </summary>
    public struct ProjectileShooterLegacyRootInstanceId : IComponentData
    {
        public int Value;
    }

    /// <summary>
    /// Shooter faction index for ECS projectile hit tests (allied targets are skipped). <c>-1</c> if unknown.
    /// </summary>
    public struct ProjectileShooterFactionId : IComponentData
    {
        public int Value;
    }

    /// <summary>Unity instance ID of the shooter's collider to ignore self-hits.</summary>
    public struct ProjectileOwnerColliderId : IComponentData
    {
        public int ColliderInstanceId;
    }

    public struct ProjectileHitSphere : IComponentData
    {
        public float Radius;
    }

    public struct ProjectileMotionState : IComponentData
    {
        public float3 PreviousPosition;
    }

    public struct ProjectileSimSettings : IComponentData
    {
        public float Gravity;

        /// <summary>
        /// Layers for <see cref="ProjectileHitSystem"/> physics sphere casts only (terrain, buildings, trees).
        /// DOTS NPCs are hit via ECS segment tests, not these casts.
        /// </summary>
        public int StaticEnvironmentLayerMask;
    }

    public struct ProjectileDodgeRegistryTag : IComponentData { }

    public struct ProjectileDodgeSnapshotElement : IBufferElementData
    {
        public float3 PositionFlat;
        public float3 VelocityFlat;
        public Entity ShooterRoot;
        public int LegacyShooterRootInstanceId;
    }
}
