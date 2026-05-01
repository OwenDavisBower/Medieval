using System.Collections.Generic;
using Medieval.Npcs;
using Unity.Collections;
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
            public float3 PreviousPosition;
            public float3 CurrentPosition;
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            var pending = new List<PendingHit>(8);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (tf, motion, hitSphere, damage, shooter, owner, entity) in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRO<ProjectileMotionState>, RefRO<ProjectileHitSphere>,
                             RefRO<ProjectileDamage>, RefRO<ProjectileShooterId>, RefRO<ProjectileOwnerColliderId>>()
                         .WithAll<ProjectileTag>()
                         .WithEntityAccess())
            {
                float3 prev = motion.ValueRO.PreviousPosition;
                float3 cur = tf.ValueRO.Position;
                Vector3 disp = (Vector3)(cur - prev);
                float dist = disp.magnitude;
                if (dist < 1e-6f)
                    continue;

                Vector3 dir = disp / dist;
                float radius = math.max(0.001f, hitSphere.ValueRO.Radius);
                int shooterRootId = shooter.ValueRO.RootInstanceId;
                int ownerColliderId = owner.ValueRO.ColliderInstanceId;

                RaycastHit[] hits = Physics.SphereCastAll((Vector3)prev, radius, dir, dist, ~0, QueryTriggerInteraction.Ignore);

                int physBest = -1;
                float physBestDist = float.MaxValue;
                if (hits != null && hits.Length > 0)
                {
                    for (int i = 0; i < hits.Length; i++)
                    {
                        RaycastHit h = hits[i];
                        if (ShouldIgnoreHit(in h, shooterRootId, ownerColliderId))
                            continue;
                        if (h.distance < physBestDist)
                        {
                            physBestDist = h.distance;
                            physBest = i;
                        }
                    }
                }

                Entity dotsExclude = Entity.Null;
                if (em.HasComponent<ProjectileShooterNpcRoot>(entity))
                    dotsExclude = em.GetComponentData<ProjectileShooterNpcRoot>(entity).Value;

                bool hasDots = NpcProjectileDotsNpc.TryFindClosestAlongSegment(em, prev, cur, radius, dotsExclude,
                    out Entity dotsVictim, out float dotsDist);
                bool preferDots = hasDots && (physBest < 0 || dotsDist < physBestDist - 1e-4f);
                if (preferDots)
                {
                    NpcProjectileDotsNpc.ApplyProjectileDamage(em, dotsVictim, damage.ValueRO.Amount);
                    var st = em.GetComponentData<NpcCharacterCombatState>(dotsVictim);
                    if (st.IsDead != 0)
                        NpcEntityDestroyUtility.DestroyNpcWithLinked(ref ecb, em, dotsVictim);
                    ecb.DestroyEntity(entity);
                    continue;
                }

                if (physBest < 0)
                    continue;

                pending.Add(new PendingHit
                {
                    Entity = entity,
                    ShooterRootInstanceId = shooterRootId,
                    OwnerColliderInstanceId = ownerColliderId,
                    Damage = damage.ValueRO,
                    Hit = hits[physBest],
                    PreviousPosition = prev,
                    CurrentPosition = cur
                });
            }

            ecb.Playback(em);
            ecb.Dispose();

            for (int i = 0; i < pending.Count; i++)
            {
                PendingHit p = pending[i];
                if (p.Hit.collider == null)
                {
                    em.DestroyEntity(p.Entity);
                    continue;
                }

                ApplyHitStickOrDestroy(em, p.Entity, p.ShooterRootInstanceId, p.Damage, p.Hit, p.PreviousPosition,
                    p.CurrentPosition);
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

        static void ApplyHitStickOrDestroy(
            EntityManager em,
            Entity entity,
            int shooterRootInstanceId,
            ProjectileDamage damage,
            RaycastHit hit,
            float3 prevPos,
            float3 curPos)
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
                em.DestroyEntity(entity);
                return;
            }

            StickProjectile(em, entity, hit, prevPos, curPos);
        }

        static void StickProjectile(EntityManager em, Entity entity, RaycastHit hit, float3 prevPos, float3 curPos)
        {
            if (!em.Exists(entity))
                return;

            // Snap to impact point and orient into the surface.
            if (em.HasComponent<LocalTransform>(entity))
            {
                var tf = em.GetComponentData<LocalTransform>(entity);

                float3 travel = curPos - prevPos;
                float3 travelDir = math.normalizesafe(travel, new float3(0f, 0f, 1f));

                // Push slightly "into" the surface so it looks embedded.
                const float embed = 0.06f;
                float3 pos = (float3)hit.point - travelDir * embed;

                // Prefer travel direction for a natural look; fallback to surface normal.
                float3 forward = math.select(-((float3)hit.normal), travelDir, math.lengthsq(travel) > 1e-8f);
                tf.Position = pos;
                tf.Rotation = quaternion.LookRotationSafe(math.normalizesafe(forward, new float3(0f, 0f, 1f)),
                    new float3(0f, 1f, 0f));

                em.SetComponentData(entity, tf);
            }

            // Remove "projectile-ness" so it stops simulating and never times out.
            if (em.HasComponent<ProjectileVelocity>(entity)) em.RemoveComponent<ProjectileVelocity>(entity);
            if (em.HasComponent<ProjectileMotionState>(entity)) em.RemoveComponent<ProjectileMotionState>(entity);
            if (em.HasComponent<ProjectileLifetime>(entity)) em.RemoveComponent<ProjectileLifetime>(entity);
            if (em.HasComponent<ProjectileHitSphere>(entity)) em.RemoveComponent<ProjectileHitSphere>(entity);
            if (em.HasComponent<ProjectileDamage>(entity)) em.RemoveComponent<ProjectileDamage>(entity);
            if (em.HasComponent<ProjectileShooterId>(entity)) em.RemoveComponent<ProjectileShooterId>(entity);
            if (em.HasComponent<ProjectileOwnerColliderId>(entity)) em.RemoveComponent<ProjectileOwnerColliderId>(entity);
            if (em.HasComponent<ProjectileTag>(entity)) em.RemoveComponent<ProjectileTag>(entity);
        }
    }
}
