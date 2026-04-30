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
            public int ShooterRootInstanceId;
            public int OwnerColliderInstanceId;
            public ProjectileDamage Damage;
            public RaycastHit Hit;
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            var pending = new List<PendingHit>(8);

            foreach (var (tf, motion, hitSphere, damage, shooter, owner, entity) in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRO<ProjectileMotionState>, RefRO<ProjectileHitSphere>,
                             RefRO<ProjectileDamage>, RefRO<ProjectileShooterId>, RefRO<ProjectileOwnerColliderId>>()
                         .WithAll<ProjectileTag>()
                         .WithEntityAccess())
            {
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

                int shooterRootId = shooter.ValueRO.RootInstanceId;
                int ownerColliderId = owner.ValueRO.ColliderInstanceId;

                int best = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < hits.Length; i++)
                {
                    RaycastHit h = hits[i];
                    if (ShouldIgnoreHit(in h, shooterRootId, ownerColliderId))
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
                    ShooterRootInstanceId = shooterRootId,
                    OwnerColliderInstanceId = ownerColliderId,
                    Damage = damage.ValueRO,
                    Hit = hits[best]
                });
            }

            for (int i = 0; i < pending.Count; i++)
            {
                PendingHit p = pending[i];
                if (p.Hit.collider == null)
                {
                    em.DestroyEntity(p.Entity);
                    continue;
                }

                ApplyHitAndDestroy(em, p.Entity, p.ShooterRootInstanceId, p.Damage, p.Hit);
            }
        }

        static bool ShouldIgnoreHit(in RaycastHit hit, int shooterRootInstanceId, int ownerColliderInstanceId)
        {
            if (hit.collider == null || hit.transform == null)
                return true;
            if (ownerColliderInstanceId != 0 && hit.collider.GetInstanceID() == ownerColliderInstanceId)
                return true;
            if (shooterRootInstanceId != 0 && hit.transform.root.GetInstanceID() == shooterRootInstanceId)
                return true;
            return false;
        }

        static void ApplyHitAndDestroy(EntityManager em, Entity entity, int shooterRootInstanceId, ProjectileDamage damage,
            RaycastHit hit)
        {
            var victim = hit.collider.GetComponentInParent<IDamageableHealth>();
            if (victim != null && !victim.IsDead)
            {
                var victimMb = victim as MonoBehaviour;
                if (victimMb != null && shooterRootInstanceId != 0 &&
                    victimMb.transform.root.GetInstanceID() == shooterRootInstanceId)
                {
                    em.DestroyEntity(entity);
                    return;
                }

                victim.TakeDamage(damage.Amount);
            }

            em.DestroyEntity(entity);
        }
    }
}
