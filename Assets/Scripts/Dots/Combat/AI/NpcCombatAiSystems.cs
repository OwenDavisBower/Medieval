using Medieval.DotsCombat;
using Medieval.NpcMovement;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Medieval.DotsCombatSystems
{
    [BurstCompile]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial struct NpcTargetAcquisitionSystem : ISystem
    {
        EntityQuery _targetsQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using var qb = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalTransform, Faction, Health>();

            _targetsQuery = qb.Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float3 up = new float3(0f, 1f, 0f);
            _ = up;

            using var targetEntities = _targetsQuery.ToEntityArray(Allocator.Temp);
            using var targetTransforms = _targetsQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var targetFactions = _targetsQuery.ToComponentDataArray<Faction>(Allocator.Temp);
            using var targetHealth = _targetsQuery.ToComponentDataArray<Health>(Allocator.Temp);

            bool hasMatrix = SystemAPI.TryGetSingleton<FactionRelationships>(out var rels) && rels.Matrix.IsCreated;
            int matrixSize = hasMatrix ? rels.Matrix.Value.Size : 0;

            foreach (var (cfg, myFaction, myTf, curTargetRW, entity) in SystemAPI
                         .Query<RefRO<NpcCombatConfig>, RefRO<Faction>, RefRO<LocalTransform>, RefRW<NpcCurrentTarget>>()
                         .WithNone<DeadTag>()
                         .WithEntityAccess())
            {
                float aggro = cfg.ValueRO.AggroRadius;
                if (aggro <= 0.01f)
                {
                    curTargetRW.ValueRW = default;
                    continue;
                }

                float3 myPos = myTf.ValueRO.Position;
                int myId = myFaction.ValueRO.Id;
                float bestSq = aggro * aggro;
                Entity best = Entity.Null;
                float3 bestPos = default;

                for (int i = 0; i < targetEntities.Length; i++)
                {
                    Entity t = targetEntities[i];
                    if (t == entity)
                        continue;

                    // Skip dead targets (health is in the query; but we still need to check current).
                    if (targetHealth[i].Current <= 0f)
                        continue;

                    int otherId = targetFactions[i].Id;
                    if (hasMatrix)
                    {
                        ref var matrixValues = ref rels.Matrix.Value.Values;
                        if (!IsEnemyMatrix(myId, otherId, matrixSize, ref matrixValues))
                            continue;
                    }
                    else
                    {
                        if (!IsEnemyFallback(myId, otherId))
                            continue;
                    }

                    float3 p = targetTransforms[i].Position;
                    float sq = math.lengthsq(p - myPos);
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        best = t;
                        bestPos = p;
                    }
                }

                if (best == Entity.Null)
                {
                    curTargetRW.ValueRW = default;
                    continue;
                }

                curTargetRW.ValueRW = new NpcCurrentTarget
                {
                    Value = best,
                    LastKnownPosition = bestPos,
                    HasTarget = 1
                };
            }
        }

        static bool IsEnemyFallback(int a, int b)
        {
            if (a < 0 || b < 0)
                return false;

            return a != b;
        }

        static bool IsEnemyMatrix(int a, int b, int size, ref BlobArray<byte> values)
        {
            if (a < 0 || b < 0)
                return false;

            if ((uint)a < (uint)size && (uint)b < (uint)size)
                return values[a * size + b] == (byte)Relationship.Enemy;

            return a != b;
        }
    }

    /// <summary>Writes movement inputs (seek/facing/lock) from the acquired target.</summary>
    [BurstCompile]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(NpcTargetAcquisitionSystem))]
    public partial struct NpcCombatMovementInputSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float now = (float)SystemAPI.Time.ElapsedTime;
            var tfLookup = SystemAPI.GetComponentLookup<LocalTransform>(isReadOnly: true);

            foreach (var (cfg, combatState, target, tf, seekRW, facingRW, moveStateRW) in SystemAPI
                         .Query<RefRO<NpcCombatConfig>, RefRO<NpcCombatState>, RefRO<NpcCurrentTarget>, RefRO<LocalTransform>,
                             RefRW<NpcSeekOverride>, RefRW<NpcOverrideFacing>, RefRW<NpcMovementState>>()
                         .WithNone<DeadTag>())
            {
                if (target.ValueRO.HasTarget == 0 || target.ValueRO.Value == Entity.Null)
                {
                    seekRW.ValueRW = new NpcSeekOverride { SeekHoldDistance = cfg.ValueRO.Role == CombatRole.Ranged ? cfg.ValueRO.CombatRange : 0f, HasOverride = 0 };
                    facingRW.ValueRW = default;
                    moveStateRW.ValueRW.RangedMovementLock = 0;
                    continue;
                }

                float3 targetPos = target.ValueRO.LastKnownPosition;
                if (tfLookup.HasComponent(target.ValueRO.Value))
                    targetPos = tfLookup[target.ValueRO.Value].Position;

                seekRW.ValueRW = new NpcSeekOverride
                {
                    Position = targetPos,
                    SeekHoldDistance = cfg.ValueRO.Role == CombatRole.Ranged ? cfg.ValueRO.CombatRange : 0f,
                    HasOverride = 1
                };

                float3 myPos = tf.ValueRO.Position;
                float3 d = targetPos - myPos;
                d.y = 0f;
                float dsq = math.lengthsq(d);

                bool inRangedStandoff = cfg.ValueRO.Role == CombatRole.Ranged &&
                                        cfg.ValueRO.CombatRange > 0.01f &&
                                        dsq <= cfg.ValueRO.CombatRange * cfg.ValueRO.CombatRange;

                facingRW.ValueRW = inRangedStandoff && dsq > 1e-6f
                    ? new NpcOverrideFacing { FlatDirection = math.normalize(d), HasOverride = 1 }
                    : default;

                // Lock while a ranged shot is queued/in flight (windup window).
                moveStateRW.ValueRW.RangedMovementLock = (byte)(combatState.ValueRO.RangedShotQueued != 0 && now < combatState.ValueRO.RangedFireTime ? 1 : 0);
            }
        }
    }

    /// <summary>Executes attacks by appending <see cref="DamageEvent"/> to targets.</summary>
    [BurstCompile]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(NpcCombatMovementInputSystem))]
    public partial struct NpcCombatExecutionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float now = (float)SystemAPI.Time.ElapsedTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var stunLookup = SystemAPI.GetComponentLookup<AttackStun>(isReadOnly: true);

            foreach (var (cfg, myTf, target, combatStateRW, entity) in SystemAPI
                         .Query<RefRO<NpcCombatConfig>, RefRO<LocalTransform>, RefRO<NpcCurrentTarget>, RefRW<NpcCombatState>>()
                         .WithNone<DeadTag>()
                         .WithEntityAccess())
            {
                ref var st = ref combatStateRW.ValueRW;

                if (stunLookup.HasComponent(entity) && now < stunLookup[entity].StunnedUntilTime)
                    continue;

                // Resolve queued ranged shot.
                if (st.RangedShotQueued != 0)
                {
                    if (now >= st.RangedFireTime && target.ValueRO.HasTarget != 0 && target.ValueRO.Value != Entity.Null)
                    {
                        if (cfg.ValueRO.RangedMode == RangedAttackMode.Projectile && cfg.ValueRO.ProjectilePrefab != Entity.Null)
                        {
                            Entity p = ecb.Instantiate(cfg.ValueRO.ProjectilePrefab);
                            float3 spawnPos = myTf.ValueRO.Position + math.rotate(myTf.ValueRO.Rotation, cfg.ValueRO.ProjectileSpawnOffset);
                            ecb.SetComponent(p, LocalTransform.FromPositionRotation(spawnPos, myTf.ValueRO.Rotation));
                            ecb.AddComponent(p, new CombatProjectile
                            {
                                Target = target.ValueRO.Value,
                                LastKnownTargetPosition = target.ValueRO.LastKnownPosition,
                                Source = entity,
                                Damage = cfg.ValueRO.Damage,
                                Speed = cfg.ValueRO.ProjectileSpeed,
                                HitRadius = cfg.ValueRO.ProjectileHitRadius,
                                ExpireAtTime = now + cfg.ValueRO.ProjectileMaxLifetimeSeconds
                            });
                        }
                        else
                        {
                            DamageApi.Enqueue(ecb, target.ValueRO.Value, cfg.ValueRO.Damage, entity);
                        }
                        st.RangedShotQueued = 0;
                    }
                }

                if (target.ValueRO.HasTarget == 0 || target.ValueRO.Value == Entity.Null)
                    continue;

                if (now < st.NextAttackTime)
                    continue;

                float3 targetPos = target.ValueRO.LastKnownPosition;
                float3 d = targetPos - myTf.ValueRO.Position;
                d.y = 0f;
                float dsq = math.lengthsq(d);

                if (cfg.ValueRO.Role == CombatRole.Melee)
                {
                    float r = cfg.ValueRO.MeleeRange;
                    if (r > 0.01f && dsq <= r * r)
                    {
                        DamageApi.Enqueue(ecb, target.ValueRO.Value, cfg.ValueRO.Damage, entity);

                        if (cfg.ValueRO.MeleeStunSeconds > 0.01f)
                        {
                            float until = now + cfg.ValueRO.MeleeStunSeconds;
                            if (SystemAPI.HasComponent<AttackStun>(target.ValueRO.Value))
                            {
                                var s = SystemAPI.GetComponent<AttackStun>(target.ValueRO.Value);
                                s.StunnedUntilTime = math.max(s.StunnedUntilTime, until);
                                ecb.SetComponent(target.ValueRO.Value, s);
                            }
                            else
                            {
                                ecb.AddComponent(target.ValueRO.Value, new AttackStun { StunnedUntilTime = until });
                            }
                        }

                        st.NextAttackTime = now + math.max(0.02f, cfg.ValueRO.AttackInterval);
                    }
                }
                else
                {
                    st.NextAttackTime = now + math.max(0.02f, cfg.ValueRO.AttackInterval);
                    st.RangedShotQueued = 1;
                    st.RangedFireTime = now + math.max(0f, cfg.ValueRO.RangedWindupSeconds);
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}

