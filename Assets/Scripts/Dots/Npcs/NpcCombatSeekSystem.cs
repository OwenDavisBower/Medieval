using Medieval.NpcMovement;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.Npcs
{
    [UpdateInGroup(typeof(NpcCombatSeekSystemGroup))]
    public partial class NpcCombatSeekSystem : SystemBase
    {
        EntityQuery _candidateQuery;

        protected override void OnCreate()
        {
            _candidateQuery = GetEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<NpcProfile>(),
                ComponentType.ReadOnly<NpcCharacterCombatState>(),
                ComponentType.ReadOnly<NpcMovementTag>());
        }

        protected override void OnUpdate()
        {
            if (_candidateQuery.IsEmpty)
                return;

            using var candEnts = _candidateQuery.ToEntityArray(Allocator.TempJob);
            using var candTf = _candidateQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            using var candProf = _candidateQuery.ToComponentDataArray<NpcProfile>(Allocator.TempJob);
            using var candCombat = _candidateQuery.ToComponentDataArray<NpcCharacterCombatState>(Allocator.TempJob);

            bool hasPlayer = SystemAPI.TryGetSingleton(out NpcPlayerAnchor playerAnchor) && playerAnchor.HasPlayer != 0;
            var em = EntityManager;

            foreach (var (seekRw, facingRw, moveRw, combatTargetRw, selfTf, profile, cfg, entity) in SystemAPI
                         .Query<RefRW<NpcSeekOverride>, RefRW<NpcOverrideFacing>, RefRW<NpcMovementState>,
                             RefRW<NpcCombatTarget>, RefRO<LocalTransform>, RefRO<NpcProfile>,
                             RefRO<NpcCombatSeekConfig>>()
                         .WithAll<NpcMovementTag, NpcMovementConfig>()
                         .WithEntityAccess())
            {
                ref NpcSeekOverride seek = ref seekRw.ValueRW;
                ref NpcOverrideFacing facing = ref facingRw.ValueRW;
                ref NpcMovementState move = ref moveRw.ValueRW;
                ref NpcCombatTarget combatTarget = ref combatTargetRw.ValueRW;
                float3 selfFeet = selfTf.ValueRO.Position;
                var combat = em.GetComponentData<NpcCharacterCombatState>(entity);

                if (profile.ValueRO.Role == NpcRole.Villager || profile.ValueRO.Role == NpcRole.Unknown)
                {
                    ClearSeek(ref seek, ref facing, ref move, ref combatTarget);
                    continue;
                }

                if (combat.IsDead != 0 || combat.CurrentHealth <= 0f)
                {
                    ClearSeek(ref seek, ref facing, ref move, ref combatTarget);
                    continue;
                }

                if (em.HasComponent<NpcCharacterBakedStats>(entity))
                {
                    var bake = em.GetComponentData<NpcCharacterBakedStats>(entity);
                    if (NpcCombatStateQueries.ShouldFleeFromCombatThreat(combat, bake))
                    {
                        ClearSeek(ref seek, ref facing, ref move, ref combatTarget);
                        continue;
                    }
                }

                if (profile.ValueRO.Role == NpcRole.Follower && cfg.ValueRO.MaxDistanceFromLeader > 0f && hasPlayer)
                {
                    float3 p = playerAnchor.Position;
                    float maxSq = cfg.ValueRO.MaxDistanceFromLeader * cfg.ValueRO.MaxDistanceFromLeader;
                    float dx = selfFeet.x - p.x;
                    float dz = selfFeet.z - p.z;
                    if (dx * dx + dz * dz > maxSq)
                    {
                        ClearSeek(ref seek, ref facing, ref move, ref combatTarget);
                        continue;
                    }
                }

                float aggroSq = cfg.ValueRO.AggroRadius * cfg.ValueRO.AggroRadius;
                float bestSq = float.MaxValue;
                float3 bestPos = default;
                Entity bestHostileNpc = Entity.Null;
                bool found = TryFindBestHostileInRadius(entity, profile.ValueRO.Role, selfFeet, aggroSq, cfg.ValueRO,
                    candEnts, candTf, candProf, candCombat, ref bestSq, ref bestPos, ref bestHostileNpc);

                bool rangedShotBusy = em.HasComponent<NpcRangedAttackState>(entity) &&
                    em.GetComponentData<NpcRangedAttackState>(entity).ShotInProgress != 0;

                if (!found && !rangedShotBusy && profile.ValueRO.Role == NpcRole.Follower &&
                    cfg.ValueRO.LosChaseRadius > cfg.ValueRO.AggroRadius)
                {
                    float losSq = cfg.ValueRO.LosChaseRadius * cfg.ValueRO.LosChaseRadius;
                    found = TryFindBestHostileInRadius(entity, profile.ValueRO.Role, selfFeet, losSq, cfg.ValueRO,
                        candEnts, candTf, candProf, candCombat, ref bestSq, ref bestPos, ref bestHostileNpc);
                }

                if (profile.ValueRO.Role == NpcRole.Bandit && hasPlayer)
                {
                    float3 op = playerAnchor.Position;
                    float dx = op.x - selfFeet.x;
                    float dz = op.z - selfFeet.z;
                    float sq = dx * dx + dz * dz;
                    if (sq <= aggroSq && sq < bestSq &&
                        LineOfSightUtility.HasClearLineOfSightWorldPoints(
                            new Vector3(selfFeet.x, selfFeet.y, selfFeet.z),
                            new Vector3(op.x, op.y, op.z),
                            cfg.ValueRO.EyeHeight,
                            cfg.ValueRO.TargetAimHeight,
                            cfg.ValueRO.ObstacleLayerMask,
                            null))
                    {
                        bestSq = sq;
                        bestPos = op;
                        bestHostileNpc = Entity.Null;
                        found = true;
                    }
                }

                if (!found)
                {
                    if (!rangedShotBusy && profile.ValueRO.Role == NpcRole.Follower && hasPlayer)
                    {
                        var mcfg = em.GetComponentData<NpcMovementConfig>(entity);
                        // Regroup when outside the normal orbit annulus (not only when very far). Orbit sampling
                        // can sit near the agent for many frames, so velocity stays ~0 until the anchor moves.
                        float joinDist = mcfg.MaxLoiterRadius + 0.5f;
                        float joinSq = joinDist * joinDist;
                        float3 p = playerAnchor.Position;
                        float pdx = selfFeet.x - p.x;
                        float pdz = selfFeet.z - p.z;
                        if (pdx * pdx + pdz * pdz > joinSq)
                        {
                            seek.Position = p;
                            seek.SeekHoldDistance = math.max(1.5f, mcfg.MinLoiterRadius);
                            seek.HasOverride = 1;
                            facing = default;
                            move.RangedMovementLock = 0;
                            move.RangedCombatSeparationBoost = 0;
                            combatTarget = default;
                            continue;
                        }
                    }

                    seek.HasOverride = 0;
                    seek.Position = default;
                    seek.SeekHoldDistance = 0f;
                    facing = default;
                    move.RangedMovementLock = 0;
                    move.RangedCombatSeparationBoost = 0;
                    combatTarget = default;
                    continue;
                }

                bool useRangedHold = profile.ValueRO.WeaponClass == NpcWeaponClass.Ranged ||
                    profile.ValueRO.WeaponClass == NpcWeaponClass.Both;
                float combatRange = math.max(0.25f, cfg.ValueRO.CombatRange);
                float holdDist = 0f;
                if (useRangedHold)
                {
                    float configured = cfg.ValueRO.RangedStandoffHoldDistance;
                    holdDist = configured > 0f
                        ? math.min(configured, combatRange * 0.94f)
                        : combatRange * 0.72f;
                    holdDist = math.clamp(holdDist, 0.25f, combatRange * 0.9f);
                    move.RangedCombatSeparationBoost = 1;
                }
                else
                    move.RangedCombatSeparationBoost = 0;

                seek.Position = bestPos;
                seek.SeekHoldDistance = holdDist;
                seek.HasOverride = 1;

                combatTarget.TargetNpcEntity = bestHostileNpc;
                combatTarget.HasCombatTarget = 1;

                float flatSq = (bestPos.x - selfFeet.x) * (bestPos.x - selfFeet.x) +
                    (bestPos.z - selfFeet.z) * (bestPos.z - selfFeet.z);
                float range = combatRange;
                bool standoff = useRangedHold && flatSq <= range * range;
                if (standoff)
                {
                    float3 d = bestPos - selfFeet;
                    d.y = 0f;
                    if (math.lengthsq(d) > 1e-6f)
                    {
                        d = math.normalize(d);
                        facing.FlatDirection = d;
                        facing.HasOverride = 1;
                    }
                    else
                        facing = default;
                }
                else
                    facing = default;
            }
        }

        static void ClearSeek(ref NpcSeekOverride seek, ref NpcOverrideFacing facing, ref NpcMovementState move,
            ref NpcCombatTarget combatTarget)
        {
            seek.HasOverride = 0;
            seek.Position = default;
            seek.SeekHoldDistance = 0f;
            facing = default;
            move.RangedMovementLock = 0;
            move.RangedCombatSeparationBoost = 0;
            combatTarget = default;
        }

        static bool TryFindBestHostileInRadius(
            Entity self,
            NpcRole selfRole,
            float3 selfFeet,
            float maxRadiusSq,
            NpcCombatSeekConfig cfg,
            NativeArray<Entity> candEnts,
            NativeArray<LocalTransform> candTf,
            NativeArray<NpcProfile> candProf,
            NativeArray<NpcCharacterCombatState> candCombat,
            ref float bestSq,
            ref float3 bestPos,
            ref Entity bestHostileNpc)
        {
            bool found = false;
            for (int i = 0; i < candEnts.Length; i++)
            {
                if (candEnts[i] == self)
                    continue;
                NpcRole otherRole = candProf[i].Role;
                if (!NpcCombatRoleHostility.IsHostilePair(selfRole, otherRole))
                    continue;
                if (candCombat[i].IsDead != 0 || candCombat[i].CurrentHealth <= 0f)
                    continue;

                float3 op = candTf[i].Position;
                float dx = op.x - selfFeet.x;
                float dz = op.z - selfFeet.z;
                float sq = dx * dx + dz * dz;
                if (sq > maxRadiusSq || sq >= bestSq)
                    continue;

                if (!LineOfSightUtility.HasClearLineOfSightWorldPoints(
                        new Vector3(selfFeet.x, selfFeet.y, selfFeet.z),
                        new Vector3(op.x, op.y, op.z),
                        cfg.EyeHeight,
                        cfg.TargetAimHeight,
                        cfg.ObstacleLayerMask,
                        null))
                    continue;

                bestSq = sq;
                bestPos = op;
                bestHostileNpc = candEnts[i];
                found = true;
            }

            return found;
        }

    }
}
