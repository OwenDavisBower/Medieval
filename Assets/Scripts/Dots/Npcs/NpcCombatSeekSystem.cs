using Medieval.NpcMovement;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.Npcs
{
    [UpdateInGroup(typeof(NpcCombatSeekSystemGroup))]
    public partial struct NpcCombatSeekSystem : ISystem
    {
        EntityQuery _candidateQuery;

        public void OnCreate(ref SystemState state)
        {
            _candidateQuery = state.GetEntityQuery(NpcCombatCandidateQuery.All);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_candidateQuery.IsEmpty)
                return;

            using var candEnts = _candidateQuery.ToEntityArray(Allocator.TempJob);
            using var candTf = _candidateQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            using var candProf = _candidateQuery.ToComponentDataArray<NpcProfile>(Allocator.TempJob);
            using var candCombat = _candidateQuery.ToComponentDataArray<NpcCharacterCombatState>(Allocator.TempJob);

            bool hasPlayer = SystemAPI.TryGetSingleton(out NpcPlayerAnchor playerAnchor) && playerAnchor.HasPlayer != 0;
            var em = state.EntityManager;
            var combatLookup = SystemAPI.GetComponentLookup<NpcCharacterCombatState>(true);
            combatLookup.Update(ref state);

            foreach (var (seekRw, facingRw, moveRw, combatTargetRw, selfTf, profile, cfg, entity) in SystemAPI
                         .Query<RefRW<NpcSeekOverride>, RefRW<NpcOverrideFacing>, RefRW<NpcMovementState>,
                             RefRW<NpcCombatTarget>, RefRO<LocalTransform>, RefRO<NpcProfile>,
                             RefRO<NpcCombatSeekConfig>>()
                         .WithAll<NpcMovementTag, NpcCharacterCombatState>()
                         .WithEntityAccess())
            {
                ref NpcSeekOverride seek = ref seekRw.ValueRW;
                ref NpcOverrideFacing facing = ref facingRw.ValueRW;
                ref NpcMovementState move = ref moveRw.ValueRW;
                ref NpcCombatTarget combatTarget = ref combatTargetRw.ValueRW;
                float3 selfFeet = selfTf.ValueRO.Position;
                NpcCharacterCombatState combat = combatLookup[entity];

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
                    if (NpcMath.DistanceSqXZ(selfFeet, p) > maxSq)
                    {
                        ClearSeek(ref seek, ref facing, ref move, ref combatTarget);
                        continue;
                    }
                }

                float aggroSq = cfg.ValueRO.AggroRadius * cfg.ValueRO.AggroRadius;
                float bestSq = float.MaxValue;
                float3 bestPos = default;
                Entity bestHostileNpc = Entity.Null;
                var found = false;

                for (int i = 0; i < candEnts.Length; i++)
                {
                    if (candEnts[i] == entity)
                        continue;
                    NpcRole otherRole = candProf[i].Role;
                    if (!NpcCombatRoleHostility.IsHostilePair(profile.ValueRO.Role, otherRole))
                        continue;
                    if (candCombat[i].IsDead != 0 || candCombat[i].CurrentHealth <= 0f)
                        continue;

                    float3 op = candTf[i].Position;
                    float sq = NpcMath.DistanceSqXZ(op, selfFeet);
                    if (sq > aggroSq || sq >= bestSq)
                        continue;

                    if (!LineOfSightUtility.HasClearLineOfSightWorldPoints(
                            new Vector3(selfFeet.x, selfFeet.y, selfFeet.z),
                            new Vector3(op.x, op.y, op.z),
                            cfg.ValueRO.EyeHeight,
                            cfg.ValueRO.TargetAimHeight,
                            cfg.ValueRO.ObstacleLayerMask,
                            null))
                        continue;

                    bestSq = sq;
                    bestPos = op;
                    bestHostileNpc = candEnts[i];
                    found = true;
                }

                if (profile.ValueRO.Role == NpcRole.Bandit && hasPlayer)
                {
                    float3 op = playerAnchor.Position;
                    float sq = NpcMath.DistanceSqXZ(op, selfFeet);
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
                    ClearSeek(ref seek, ref facing, ref move, ref combatTarget);
                    continue;
                }

                bool useRangedHold = profile.ValueRO.WeaponClass == NpcWeaponClass.Ranged ||
                    profile.ValueRO.WeaponClass == NpcWeaponClass.Both;
                float combatRange = math.max(0.25f, cfg.ValueRO.CombatRange);
                float flatSq = NpcMath.DistanceSqXZ(bestPos, selfFeet);

                float holdDist = 0f;
                if (useRangedHold)
                {
                    if (flatSq > combatRange * combatRange)
                        move.RangedMovementLock = 0;
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

                bool standoff = useRangedHold && flatSq <= combatRange * combatRange;
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
    }
}
