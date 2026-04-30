using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

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

    /// <summary>Managed link for GPU-style mesh rendering via classic <see cref="Transform"/>.</summary>
    public class ProjectileVisualCompanion : IComponentData
    {
        public Transform Visual;
        public Transform ShooterRoot;
        public Collider OwnerCollider;
    }
}
