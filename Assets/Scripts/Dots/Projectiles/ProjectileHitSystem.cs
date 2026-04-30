using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.Projectiles
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileMovementSystem))]
    [UpdateBefore(typeof(ProjectileLifetimeSystem))]
    public partial class ProjectileHitSystem : SystemBase
    {
        struct PendingHit
        {
            public Entity Entity;
            public ProjectileVisualCompanion Companion;
            public ProjectileDamage Damage;
            public RaycastHit Hit;
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            var pending = new List<PendingHit>(8);

            foreach (var (tf, motion, hitSphere, damage, companion, entity) in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRO<ProjectileMotionState>, RefRO<ProjectileHitSphere>,
                             RefRO<ProjectileDamage>, ProjectileVisualCompanion>()
                         .WithAll<ProjectileTag>()
                         .WithEntityAccess())
            {
                if (companion.Visual == null)
                {
                    pending.Add(new PendingHit
                    {
                        Entity = entity,
                        Companion = companion,
                        Damage = default,
                        Hit = default
                    });
                    continue;
                }

                Vector3 prev = motion.ValueRO.PreviousPosition;
                Vector3 cur = tf.ValueRO.Position;
                Vector3 disp = cur - prev;
                float dist = disp.magnitude;
                if (dist < 1e-6f)
                    continue;

                Vector3 dir = disp / dist;
                float radius = math.max(0.001f, hitSphere.ValueRO.Radius);
                RaycastHit[] hits = Physics.SphereCastAll(prev, radius, dir, dist, ~0, QueryTriggerInteraction.Ignore);
                if (hits == null || hits.Length == 0)
                    continue;

                int best = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < hits.Length; i++)
                {
                    RaycastHit h = hits[i];
                    if (ShouldIgnoreHit(in h, companion))
                        continue;
                    if (h.distance < bestDist)
                    {
                        bestDist = h.distance;
                        best = i;
                    }
                }

                if (best < 0)
                    continue;

                pending.Add(new PendingHit
                {
                    Entity = entity,
                    Companion = companion,
                    Damage = damage.ValueRO,
                    Hit = hits[best]
                });
            }

            for (int i = 0; i < pending.Count; i++)
            {
                PendingHit p = pending[i];
                if (p.Companion.Visual == null)
                {
                    em.DestroyEntity(p.Entity);
                    continue;
                }

                if (p.Hit.collider == null)
                {
                    em.DestroyEntity(p.Entity);
                    continue;
                }

                ApplyHitAndDestroy(em, p.Entity, p.Companion, p.Damage, p.Hit);
            }
        }

        static bool ShouldIgnoreHit(in RaycastHit hit, ProjectileVisualCompanion companion)
        {
            if (hit.collider == null || hit.transform == null)
                return true;
            if (companion.OwnerCollider != null && hit.collider == companion.OwnerCollider)
                return true;
            if (companion.ShooterRoot != null && hit.transform.IsChildOf(companion.ShooterRoot))
                return true;
            return false;
        }

        static void ApplyHitAndDestroy(EntityManager em, Entity entity, ProjectileVisualCompanion companion,
            ProjectileDamage damage, RaycastHit hit)
        {
            var victim = hit.collider.GetComponentInParent<IDamageableHealth>();
            if (victim != null && !victim.IsDead)
            {
                var victimMb = victim as MonoBehaviour;
                if (victimMb != null && companion.ShooterRoot != null && victimMb.transform.root == companion.ShooterRoot)
                {
                    DestroyProjectile(em, entity, companion);
                    return;
                }

                victim.TakeDamage(damage.Amount);
            }

            DestroyProjectile(em, entity, companion);
        }

        static void DestroyProjectile(EntityManager em, Entity entity, ProjectileVisualCompanion companion)
        {
            if (companion.Visual != null)
                Object.Destroy(companion.Visual.gameObject);
            em.DestroyEntity(entity);
        }
    }
}
