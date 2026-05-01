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
    public partial struct ProjectileHitSystem : ISystem
    {
        /// <summary>Non-alloc physics path; grown lazily if a cast ever fills the buffer.</summary>
        const int InitialSphereCastHitCapacity = 64;

        static RaycastHit[] s_SphereCastHits;
        static List<PendingHit> s_PendingHits;

        struct PendingHit
        {
            public Entity Entity;
            public int LegacyShooterRootInstanceId;
            public int OwnerColliderInstanceId;
            public ProjectileDamage Damage;
            public RaycastHit Hit;
            public float3 PreviousPosition;
            public float3 CurrentPosition;
        }

        public void OnUpdate(ref SystemState state)
        {
            EnsureSphereCastBuffer(InitialSphereCastHitCapacity);
            s_PendingHits ??= new List<PendingHit>(16);
            s_PendingHits.Clear();

            var em = state.EntityManager;
            var pending = s_PendingHits;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (tf, motion, hitSphere, damage, shooter, legacyRoot, owner, entity) in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRO<ProjectileMotionState>, RefRO<ProjectileHitSphere>,
                             RefRO<ProjectileDamage>, RefRO<ProjectileShooterRoot>,
                             RefRO<ProjectileShooterLegacyRootInstanceId>, RefRO<ProjectileOwnerColliderId>>()
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
                Entity shooterRoot = shooter.ValueRO.Value;
                int legacyRootId = legacyRoot.ValueRO.Value;
                int ownerColliderId = owner.ValueRO.ColliderInstanceId;

                bool hasPhys = TryGetClosestPhysicsHit((Vector3)prev, radius, dir, dist, legacyRootId, ownerColliderId,
                    out RaycastHit physBestHit, out float physBestDist);

                Entity dotsExclude = shooterRoot;

                bool hasDots = NpcProjectileDotsNpc.TryFindClosestAlongSegment(em, prev, cur, radius, dotsExclude,
                    out Entity dotsVictim, out float dotsDist);
                bool preferDots = hasDots && (!hasPhys || dotsDist < physBestDist - 1e-4f);
                if (preferDots)
                {
                    NpcProjectileDotsNpc.ApplyProjectileDamage(em, dotsVictim, damage.ValueRO.Amount);
                    ecb.DestroyEntity(entity);
                    continue;
                }

                if (!hasPhys)
                    continue;

                pending.Add(new PendingHit
                {
                    Entity = entity,
                    LegacyShooterRootInstanceId = legacyRootId,
                    OwnerColliderInstanceId = ownerColliderId,
                    Damage = damage.ValueRO,
                    Hit = physBestHit,
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

                ApplyHitStickOrDestroy(em, p.Entity, p.LegacyShooterRootInstanceId, p.Damage, p.Hit, p.PreviousPosition,
                    p.CurrentPosition);
            }
        }

        static void EnsureSphereCastBuffer(int minCapacity)
        {
            if (s_SphereCastHits == null || s_SphereCastHits.Length < minCapacity)
                s_SphereCastHits = new RaycastHit[minCapacity];
        }

        /// <summary>
        /// Uses <see cref="Physics.SphereCastNonAlloc"/> into a reused buffer; falls back to
        /// <see cref="Physics.SphereCastAll"/> only if the buffer is full (possible truncation).
        /// </summary>
        static bool TryGetClosestPhysicsHit(Vector3 origin, float radius, Vector3 direction, float maxDistance,
            int legacyShooterRootInstanceId, int ownerColliderInstanceId, out RaycastHit bestHit, out float bestDist)
        {
            int n = Physics.SphereCastNonAlloc(origin, radius, direction, s_SphereCastHits, maxDistance, ~0,
                QueryTriggerInteraction.Ignore);

            if (n <= 0)
            {
                bestHit = default;
                bestDist = default;
                return false;
            }

            if (n < s_SphereCastHits.Length)
                return TryPickClosestValidHit(s_SphereCastHits, n, legacyShooterRootInstanceId, ownerColliderInstanceId,
                    out bestHit, out bestDist);

            RaycastHit[] all = Physics.SphereCastAll(origin, radius, direction, maxDistance, ~0,
                QueryTriggerInteraction.Ignore);
            return TryPickClosestValidHit(all, all.Length, legacyShooterRootInstanceId, ownerColliderInstanceId,
                out bestHit, out bestDist);
        }

        static bool TryPickClosestValidHit(RaycastHit[] hits, int count, int legacyShooterRootInstanceId,
            int ownerColliderInstanceId, out RaycastHit bestHit, out float bestDist)
        {
            bestHit = default;
            bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = hits[i];
                if (ShouldIgnoreHit(in h, legacyShooterRootInstanceId, ownerColliderInstanceId))
                    continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    bestHit = h;
                    found = true;
                }
            }

            if (!found)
            {
                bestDist = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Same rule as DOTS NPCs: allied factions within horizontal range do not take ranged damage when bunched.
        /// </summary>
        static bool ShouldSuppressDamageCloseAlliedFaction(int legacyShooterRootInstanceId, Collider victimCollider,
            Transform victimTransform)
        {
            if (legacyShooterRootInstanceId == 0 || victimCollider == null || victimTransform == null)
                return false;

            var shooterObj = Resources.InstanceIDToObject(legacyShooterRootInstanceId);
            if (shooterObj is not Transform shooterTr)
                return false;

            var shooterAff = shooterTr.GetComponentInParent<Affiliation>();
            if (shooterAff == null || !Affiliation.TryGetForCollider(victimCollider, out var victimAff))
                return false;

            FactionManager fm = FactionManager.Instance;
            if (fm == null || fm.GetRelationship(shooterAff.FactionId, victimAff.FactionId) != Relationship.Allied)
                return false;

            Transform victimRoot = victimTransform.root;
            float dx = victimRoot.position.x - shooterTr.position.x;
            float dz = victimRoot.position.z - shooterTr.position.z;
            float r = NpcProjectileDotsNpc.CloseGroupedAlliedRangedFriendlyFireHorizMeters;
            return dx * dx + dz * dz <= r * r;
        }

        static bool ShouldIgnoreHit(in RaycastHit hit, int legacyShooterRootInstanceId, int ownerColliderInstanceId)
        {
            if (hit.collider == null || hit.transform == null)
                return true;
            if (ownerColliderInstanceId != 0 && hit.collider.GetInstanceID() == ownerColliderInstanceId)
                return true;
            if (legacyShooterRootInstanceId != 0 && hit.transform.root != null &&
                hit.transform.root.GetInstanceID() == legacyShooterRootInstanceId)
                return true;
            return false;
        }

        static void ApplyHitStickOrDestroy(
            EntityManager em,
            Entity entity,
            int legacyShooterRootInstanceId,
            ProjectileDamage damage,
            RaycastHit hit,
            float3 prevPos,
            float3 curPos)
        {
            var victim = hit.collider.GetComponentInParent<IDamageableHealth>();
            if (victim != null && !victim.IsDead)
            {
                var victimMb = victim as MonoBehaviour;
                if (victimMb != null && legacyShooterRootInstanceId != 0 && victimMb.transform.root != null &&
                    victimMb.transform.root.GetInstanceID() == legacyShooterRootInstanceId)
                {
                    em.DestroyEntity(entity);
                    return;
                }

                if (victimMb != null && ShouldSuppressDamageCloseAlliedFaction(legacyShooterRootInstanceId,
                        hit.collider, victimMb.transform))
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
            if (em.HasComponent<ProjectileShooterRoot>(entity)) em.RemoveComponent<ProjectileShooterRoot>(entity);
            if (em.HasComponent<ProjectileShooterLegacyRootInstanceId>(entity))
                em.RemoveComponent<ProjectileShooterLegacyRootInstanceId>(entity);
            if (em.HasComponent<ProjectileOwnerColliderId>(entity)) em.RemoveComponent<ProjectileOwnerColliderId>(entity);
            if (em.HasComponent<ProjectileTag>(entity)) em.RemoveComponent<ProjectileTag>(entity);
        }
    }
}
