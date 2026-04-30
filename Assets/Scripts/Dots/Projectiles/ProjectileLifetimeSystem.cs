using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

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
            var expired = new List<(Entity entity, ProjectileVisualCompanion companion)>(4);

            foreach (var (life, companion, entity) in SystemAPI.Query<RefRW<ProjectileLifetime>, ProjectileVisualCompanion>()
                         .WithAll<ProjectileTag>()
                         .WithEntityAccess())
            {
                life.ValueRW.SecondsRemaining -= dt;
                if (life.ValueRO.SecondsRemaining <= 0f)
                    expired.Add((entity, companion));
            }

            for (int i = 0; i < expired.Count; i++)
            {
                (Entity entity, ProjectileVisualCompanion companion) = expired[i];
                if (companion.Visual != null)
                    Object.Destroy(companion.Visual.gameObject);
                em.DestroyEntity(entity);
            }
        }
    }
}
