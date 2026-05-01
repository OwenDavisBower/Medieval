using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Medieval.Projectiles
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct ProjectileDodgeRegistryInitSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            using var q = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectileDodgeRegistryTag>());
            if (q.CalculateEntityCount() != 0)
                return;

            Entity e = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponent<ProjectileDodgeRegistryTag>(e);
            state.EntityManager.AddBuffer<ProjectileDodgeSnapshotElement>(e);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.Enabled = false;
        }
    }

    /// <summary>Rebuilds the dodge query buffer each fixed step after simulation.</summary>
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileLifetimeSystem))]
    public partial struct ProjectileDodgeBufferRebuildSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            EntityManager em = state.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<ProjectileDodgeRegistryTag>());
            if (q.CalculateEntityCount() == 0)
                return;

            Entity reg = q.GetSingletonEntity();
            var buffer = em.GetBuffer<ProjectileDodgeSnapshotElement>(reg);
            buffer.Clear();

            foreach (var (lt, vel, shooter, legacy) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<ProjectileVelocity>,
                         RefRO<ProjectileShooterRoot>, RefRO<ProjectileShooterLegacyRootInstanceId>>()
                         .WithAll<ProjectileTag>())
            {
                float3 p = lt.ValueRO.Position;
                float3 v = vel.ValueRO.Value;
                buffer.Add(new ProjectileDodgeSnapshotElement
                {
                    PositionFlat = new float3(p.x, 0f, p.z),
                    VelocityFlat = new float3(v.x, 0f, v.z),
                    ShooterRoot = shooter.ValueRO.Value,
                    LegacyShooterRootInstanceId = legacy.ValueRO.Value
                });
            }
        }
    }
}
