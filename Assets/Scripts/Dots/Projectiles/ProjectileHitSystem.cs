using System.Collections.Generic;
using Medieval.Dots.Factions;
using Medieval.NpcMovement;
using Medieval.Npcs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.Projectiles
{
    /// <summary>
    /// Resolves hits along each projectile segment: ECS tests against DOTS NPCs; physics sphere casts use
    /// <see cref="ProjectileSimSettings.StaticEnvironmentLayerMask"/> only (terrain, buildings, etc.), not Character.
    /// </summary>
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileMovementSystem))]
    [UpdateBefore(typeof(ProjectileLifetimeSystem))]
    public partial struct ProjectileHitSystem : ISystem
    {
        int m_StaticEnvLayerMask;
        /// <summary>Non-alloc physics path; grown lazily if a cast ever fills the buffer.</summary>
        const int InitialSphereCastHitCapacity = 64;

        static RaycastHit[] s_SphereCastHits;
        static List<PendingHit> s_PendingHits;

        EntityQuery _projectileQuery;
        EntityQuery _npcProjectileGridQuery;

        struct PendingHit
        {
            public Entity Entity;
            public Entity ShooterRoot;
            public EntityId LegacyShooterRootEntityId;
            public EntityId OwnerColliderEntityId;
            public ProjectileDamage Damage;
            public RaycastHit Hit;
            public float3 PreviousPosition;
            public float3 CurrentPosition;
        }

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ProjectileSimSettings>();
            _projectileQuery = state.GetEntityQuery(ComponentType.ReadOnly<ProjectileTag>());
            _npcProjectileGridQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<NpcCharacterCombatState>(),
                ComponentType.ReadOnly<NpcMovementTag>());
        }

        public void OnUpdate(ref SystemState state)
        {
            m_StaticEnvLayerMask = SystemAPI.GetSingleton<ProjectileSimSettings>().StaticEnvironmentLayerMask;
            if (m_StaticEnvLayerMask == 0)
                m_StaticEnvLayerMask = ProjectileSimSettingsBootstrapSystem.DefaultStaticEnvironmentLayerMask();

            EnsureSphereCastBuffer(InitialSphereCastHitCapacity);
            s_PendingHits ??= new List<PendingHit>(16);
            s_PendingHits.Clear();

            if (_projectileQuery.IsEmpty)
                return;

            var em = state.EntityManager;
            var pending = s_PendingHits;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            float cellSize = NpcProjectileDotsNpc.ProjectileNpcSpatialCellSize;
            int npcCount = _npcProjectileGridQuery.CalculateEntityCount();
            var npcCellMap = new NativeParallelMultiHashMap<int2, Entity>(math.max(256, npcCount * 2), Allocator.Temp);
            try
            {
                foreach (var (tf, combat, entity) in SystemAPI
                             .Query<RefRO<LocalTransform>, RefRO<NpcCharacterCombatState>>()
                             .WithAll<NpcMovementTag>()
                             .WithEntityAccess())
                {
                    if (combat.ValueRO.IsDead != 0 || combat.ValueRO.CurrentHealth <= 0f)
                        continue;
                    float3 p = tf.ValueRO.Position;
                    var cell = new int2((int)math.floor(p.x / cellSize), (int)math.floor(p.z / cellSize));
                    npcCellMap.Add(cell, entity);
                }

                var factionLookup = SystemAPI.GetComponentLookup<NpcFactionId>(true);
                var combatLookup = SystemAPI.GetComponentLookup<NpcCharacterCombatState>(true);
                var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
                var legacyRootLookup = SystemAPI.GetComponentLookup<ProjectileShooterLegacyRootInstanceId>(true);
                var ownerColliderLookup = SystemAPI.GetComponentLookup<ProjectileOwnerColliderId>(true);
                factionLookup.Update(ref state);
                combatLookup.Update(ref state);
                transformLookup.Update(ref state);
                legacyRootLookup.Update(ref state);
                ownerColliderLookup.Update(ref state);

                bool hasRel = SystemAPI.TryGetSingleton(out FactionRelationshipState relState) && relState.MatrixSize > 0;
                var relBuf = hasRel ? SystemAPI.GetSingletonBuffer<FactionRelationshipCell>() : default;
                int relSize = hasRel ? relState.MatrixSize : 0;

                foreach (var (tf, motion, hitSphere, damage, shooter, shooterFaction, entity) in SystemAPI
                             .Query<RefRO<LocalTransform>, RefRO<ProjectileMotionState>, RefRO<ProjectileHitSphere>,
                                 RefRO<ProjectileDamage>, RefRO<ProjectileShooterRoot>, RefRO<ProjectileShooterFactionId>>()
                             .WithAll<ProjectileTag, ProjectileShooterLegacyRootInstanceId, ProjectileOwnerColliderId>()
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
                    EntityId legacyRootId = legacyRootLookup[entity].Value;
                    EntityId ownerColliderId = ownerColliderLookup[entity].ColliderEntityId;

                    bool hasPhys = TryGetClosestPhysicsHit((Vector3)prev, radius, dir, dist, legacyRootId,
                        ownerColliderId, m_StaticEnvLayerMask, out RaycastHit physBestHit, out float physBestDist);

                    Entity dotsExclude = shooterRoot;

                    bool hasDots = NpcProjectileDotsNpc.TryFindClosestAlongSegment(in npcCellMap, cellSize,
                        in factionLookup, in combatLookup, in transformLookup, in relBuf, relSize, prev, cur, radius,
                        dotsExclude, shooterFaction.ValueRO.Value, out Entity dotsVictim, out float dotsDist);
                    bool preferDots = hasDots && (!hasPhys || dotsDist < physBestDist - 1e-4f);
                    if (preferDots)
                    {
                        float dealt = NpcProjectileDotsNpc.ApplyProjectileDamage(em, dotsVictim,
                            damage.ValueRO.Amount, out bool killed);
                        if (shooterRoot != Entity.Null && shooterRoot != dotsVictim)
                            NpcExperienceUtility.GrantDamageXp(em, ref ecb, shooterRoot, dealt, killed);
                        ecb.DestroyEntity(entity);
                        continue;
                    }

                    if (!hasPhys)
                        continue;

                    pending.Add(new PendingHit
                    {
                        Entity = entity,
                        ShooterRoot = shooterRoot,
                        LegacyShooterRootEntityId = legacyRootId,
                        OwnerColliderEntityId = ownerColliderId,
                        Damage = damage.ValueRO,
                        Hit = physBestHit,
                        PreviousPosition = prev,
                        CurrentPosition = cur
                    });
                }
            }
            finally
            {
                npcCellMap.Dispose();
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

                ApplyHitStickOrDestroy(em, p.Entity, p.ShooterRoot, p.LegacyShooterRootEntityId, p.Damage, p.Hit,
                    p.PreviousPosition, p.CurrentPosition);
            }
        }

        static void EnsureSphereCastBuffer(int minCapacity)
        {
            if (s_SphereCastHits == null || s_SphereCastHits.Length < minCapacity)
                s_SphereCastHits = new RaycastHit[minCapacity];
        }

        /// <summary>
        /// Environment / statics only — use <paramref name="staticEnvironmentLayerMask"/> so Character colliders
        /// are not tested here (DOTS NPCs use <see cref="NpcProjectileDotsNpc.TryFindClosestAlongSegment"/>).
        /// Uses <see cref="Physics.SphereCastNonAlloc"/> into a reused buffer; falls back to
        /// <see cref="Physics.SphereCastAll"/> only if the buffer is full (possible truncation).
        /// </summary>
        static bool TryGetClosestPhysicsHit(Vector3 origin, float radius, Vector3 direction, float maxDistance,
            EntityId legacyShooterRootEntityId, EntityId ownerColliderEntityId, int staticEnvironmentLayerMask,
            out RaycastHit bestHit, out float bestDist)
        {
            int n = Physics.SphereCastNonAlloc(origin, radius, direction, s_SphereCastHits, maxDistance,
                staticEnvironmentLayerMask, QueryTriggerInteraction.Ignore);

            if (n <= 0)
            {
                bestHit = default;
                bestDist = default;
                return false;
            }

            if (n < s_SphereCastHits.Length)
                return TryPickClosestValidHit(s_SphereCastHits, n, legacyShooterRootEntityId, ownerColliderEntityId,
                    out bestHit, out bestDist);

            RaycastHit[] all = Physics.SphereCastAll(origin, radius, direction, maxDistance, staticEnvironmentLayerMask,
                QueryTriggerInteraction.Ignore);
            return TryPickClosestValidHit(all, all.Length, legacyShooterRootEntityId, ownerColliderEntityId,
                out bestHit, out bestDist);
        }

        static bool TryPickClosestValidHit(RaycastHit[] hits, int count, EntityId legacyShooterRootEntityId,
            EntityId ownerColliderEntityId, out RaycastHit bestHit, out float bestDist)
        {
            bestHit = default;
            bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = hits[i];
                if (ShouldIgnoreHit(in h, legacyShooterRootEntityId, ownerColliderEntityId))
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

        /// <summary>GameObject archers / towers: do not damage allied <see cref="IDamageableHealth"/> targets.</summary>
        static bool ShouldSuppressAlliedProjectileDamage(EntityId legacyShooterRootEntityId, Collider victimCollider)
        {
            if (legacyShooterRootEntityId == EntityId.None || victimCollider == null)
                return false;

            var shooterObj = Resources.EntityIdToObject(legacyShooterRootEntityId);
            if (shooterObj is not Transform shooterTr)
                return false;

            var shooterAff = shooterTr.GetComponentInParent<Affiliation>();
            if (shooterAff == null || !Affiliation.TryGetForCollider(victimCollider, out var victimAff))
                return false;

            FactionManager fm = FactionManager.Instance;
            return fm != null && fm.GetRelationship(shooterAff.FactionId, victimAff.FactionId) == Relationship.Allied;
        }

        static bool ShouldIgnoreHit(in RaycastHit hit, EntityId legacyShooterRootEntityId, EntityId ownerColliderEntityId)
        {
            if (hit.collider == null || hit.transform == null)
                return true;
            if (ownerColliderEntityId != EntityId.None && hit.collider.GetEntityId() == ownerColliderEntityId)
                return true;
            if (legacyShooterRootEntityId != EntityId.None && hit.transform.root != null &&
                hit.transform.root.GetEntityId() == legacyShooterRootEntityId)
                return true;
            return false;
        }

        static void ApplyHitStickOrDestroy(
            EntityManager em,
            Entity entity,
            Entity shooterRoot,
            EntityId legacyShooterRootEntityId,
            ProjectileDamage damage,
            RaycastHit hit,
            float3 prevPos,
            float3 curPos)
        {
            var victim = hit.collider.GetComponentInParent<IDamageableHealth>();
            if (victim != null && !victim.IsDead)
            {
                var victimMb = victim as MonoBehaviour;
                if (victimMb != null && legacyShooterRootEntityId != EntityId.None && victimMb.transform.root != null &&
                    victimMb.transform.root.GetEntityId() == legacyShooterRootEntityId)
                {
                    em.DestroyEntity(entity);
                    return;
                }

                if (victimMb != null && ShouldSuppressAlliedProjectileDamage(legacyShooterRootEntityId, hit.collider))
                {
                    em.DestroyEntity(entity);
                    return;
                }

                float before = victim.CurrentHealth;
                float amount = damage.Amount;
                float dealt = math.min(amount, math.max(0f, before));
                bool lethal = before > 0f && before - amount <= 0f;

                if (victim is Building)
                {
                    victim.TakeDamage(amount);
                    if (shooterRoot != Entity.Null && dealt > 0f)
                        NpcExperienceUtility.GrantBuildingDamageXp(em, shooterRoot, dealt, lethal);
                }
                else
                {
                    victim.TakeDamage(amount);
                    if (shooterRoot != Entity.Null)
                    {
                        float after = victim.CurrentHealth;
                        dealt = math.max(0f, before - after);
                        NpcExperienceUtility.GrantDamageXp(em, shooterRoot, dealt, victim.IsDead);
                    }
                }

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
            if (em.HasComponent<ProjectileShooterFactionId>(entity)) em.RemoveComponent<ProjectileShooterFactionId>(entity);
            if (em.HasComponent<ProjectileOwnerColliderId>(entity)) em.RemoveComponent<ProjectileOwnerColliderId>(entity);
            if (em.HasComponent<ProjectileTag>(entity)) em.RemoveComponent<ProjectileTag>(entity);
        }
    }
}
