using Unity.Entities;

namespace Medieval.Projectiles
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileHitSystem))]
    public partial class ProjectileLifetimeSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;

            // Structural changes are small (arrows); keep it simple for now.
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
