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

    /// <summary>Unity instance ID of <c>transform.root</c> for friendly-fire and dodge filtering.</summary>
    public struct ProjectileShooterId : IComponentData
    {
        public int RootInstanceId;
    }

    /// <summary>Unity instance ID of the shooter's collider to ignore self-hits.</summary>
    public struct ProjectileOwnerColliderId : IComponentData
    {
        public int ColliderInstanceId;
    }

    /// <summary>When set, projectile was fired by this DOTS NPC root; used to skip self in ECS hit tests.</summary>
    public struct ProjectileShooterNpcRoot : IComponentData
    {
        public Entity Value;
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
    }

    public struct ProjectileDodgeRegistryTag : IComponentData { }

    public struct ProjectileDodgeSnapshotElement : IBufferElementData
    {
        public float3 PositionFlat;
        public float3 VelocityFlat;
        public int ShooterRootInstanceId;
    }
}
