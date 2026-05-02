using Medieval.Dots.Factions;
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
        /// <summary>Horizontal capsule-ish width: allied NPC whose disk crosses the shot segment blocks ranged acquisition.</summary>
        const float RangedFriendlyFireBlockHorizRadius = 0.55f;

        EntityQuery _candidateQuery;

        public void OnCreate(ref SystemState state)
        {
            _candidateQuery = state.GetEntityQuery(NpcCombatCandidateQuery.All);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_candidateQuery.IsEmpty)
                return;

            if (!SystemAPI.TryGetSingleton(out FactionRelationshipState relState) || relState.MatrixSize <= 0)
                return;

            var relBuf = SystemAPI.GetSingletonBuffer<FactionRelationshipCell>();

            using var candEnts = _candidateQuery.ToEntityArray(Allocator.TempJob);
            using var candTf = _candidateQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            using var candFaction = _candidateQuery.ToComponentDataArray<NpcFactionId>(Allocator.TempJob);
            using var candCombat = _candidateQuery.ToComponentDataArray<NpcCharacterCombatState>(Allocator.TempJob);

            bool hasPlayer = SystemAPI.TryGetSingleton(out NpcPlayerAnchor playerAnchor) && playerAnchor.HasPlayer != 0;
            var em = state.EntityManager;
            var combatLookup = SystemAPI.GetComponentLookup<NpcCharacterCombatState>(true);
            var factionLookup = SystemAPI.GetComponentLookup<NpcFactionId>(true);
            combatLookup.Update(ref state);
            factionLookup.Update(ref state);

            int matrixSize = relState.MatrixSize;

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

                if (cfg.ValueRO.SeeksCombatTargets == 0)
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

                int selfFaction = factionLookup.HasComponent(entity) ? factionLookup[entity].Value : -1;

                if (move.Group == NpcSeparationGroup.Followers && cfg.ValueRO.MaxDistanceFromLeader > 0f &&
                    hasPlayer)
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

                var weaponClass = profile.ValueRO.WeaponClass;
                bool mayUseRanged = weaponClass == NpcWeaponClass.Ranged || weaponClass == NpcWeaponClass.Both;
                float meleeRangeSq = 0f;
                if (weaponClass == NpcWeaponClass.Both && em.HasComponent<NpcMeleeCombatConfig>(entity))
                {
                    float mr = em.GetComponentData<NpcMeleeCombatConfig>(entity).MeleeRange;
                    meleeRangeSq = mr * mr;
                }

                for (int i = 0; i < candEnts.Length; i++)
                {
                    if (candEnts[i] == entity)
                        continue;
                    if (candCombat[i].IsDead != 0 || candCombat[i].CurrentHealth <= 0f)
                        continue;

                    int otherFaction = candFaction[i].Value;
                    if (selfFaction < 0 || otherFaction < 0 ||
                        !FactionRelationshipBufferUtil.IsHostile(in relBuf, matrixSize, selfFaction, otherFaction))
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

                    bool requireClearRangedLane = mayUseRanged &&
                        (weaponClass == NpcWeaponClass.Ranged || sq > meleeRangeSq);
                    if (requireClearRangedLane && AlliedNpcInRangedLine(selfFeet, op, selfFaction, entity, candEnts[i],
                            in candEnts, in candTf, in candFaction, in candCombat, in relBuf, matrixSize))
                        continue;

                    bestSq = sq;
                    bestPos = op;
                    bestHostileNpc = candEnts[i];
                    found = true;
                }

                if (hasPlayer && playerAnchor.PlayerFactionId >= 0 && selfFaction >= 0 &&
                    FactionRelationshipBufferUtil.IsHostile(in relBuf, matrixSize, selfFaction,
                        playerAnchor.PlayerFactionId))
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
                        bool requireClearRangedLanePlayer = mayUseRanged &&
                            (weaponClass == NpcWeaponClass.Ranged || sq > meleeRangeSq);
                        if (!(requireClearRangedLanePlayer && AlliedNpcInRangedLine(selfFeet, op, selfFaction, entity,
                                Entity.Null, in candEnts, in candTf, in candFaction, in candCombat, in relBuf,
                                matrixSize)))
                        {
                            bestSq = sq;
                            bestPos = op;
                            bestHostileNpc = Entity.Null;
                            found = true;
                        }
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
                    move.RangedCombatSeparationBoost = 1;

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

        static bool AlliedNpcInRangedLine(
            float3 selfFeet,
            float3 targetFeet,
            int selfFaction,
            Entity selfEntity,
            Entity hostileNpcEntity,
            in NativeArray<Entity> candEnts,
            in NativeArray<LocalTransform> candTf,
            in NativeArray<NpcFactionId> candFaction,
            in NativeArray<NpcCharacterCombatState> candCombat,
            in DynamicBuffer<FactionRelationshipCell> relBuf,
            int matrixSize)
        {
            float2 p = selfFeet.xz;
            float2 q = targetFeet.xz;
            float r = RangedFriendlyFireBlockHorizRadius;

            for (int j = 0; j < candEnts.Length; j++)
            {
                if (candEnts[j] == selfEntity || candEnts[j] == hostileNpcEntity)
                    continue;
                if (candCombat[j].IsDead != 0 || candCombat[j].CurrentHealth <= 0f)
                    continue;

                int allyFaction = candFaction[j].Value;
                if (selfFaction < 0 || allyFaction < 0 ||
                    !FactionRelationshipBufferUtil.IsAllied(in relBuf, matrixSize, selfFaction, allyFaction))
                    continue;

                if (NpcMath.HorizSegmentEntersDisk(p, q, candTf[j].Position.xz, r))
                    return true;
            }

            return false;
        }
    }
}
