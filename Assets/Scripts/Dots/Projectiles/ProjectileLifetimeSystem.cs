using Unity.Entities;

namespace Medieval.Projectiles
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileHitSystem))]
    public partial struct ProjectileLifetimeSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            foreach (var (life, entity) in SystemAPI.Query<RefRW<ProjectileLifetime>>()
                         .WithAll<ProjectileTag>()
                         .WithEntityAccess())
            {
                life.ValueRW.SecondsRemaining -= dt;
                if (life.ValueRO.SecondsRemaining <= 0f)
                    em.DestroyEntity(entity);
            }
        }
    }
}
