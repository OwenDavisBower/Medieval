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

            foreach (var (seekRw, facingRw, moveRw, selfTf, profile, combat, cfg, entity) in SystemAPI
                         .Query<RefRW<NpcSeekOverride>, RefRW<NpcOverrideFacing>, RefRW<NpcMovementState>,
                             RefRO<LocalTransform>, RefRO<NpcProfile>, RefRO<NpcCharacterCombatState>,
                             RefRO<NpcCombatSeekConfig>>()
                         .WithAll<NpcMovementTag>()
                         .WithEntityAccess())
            {
                ref NpcSeekOverride seek = ref seekRw.ValueRW;
                ref NpcOverrideFacing facing = ref facingRw.ValueRW;
                ref NpcMovementState move = ref moveRw.ValueRW;
                float3 selfFeet = selfTf.ValueRO.Position;

                if (profile.ValueRO.Role == NpcRole.Villager || profile.ValueRO.Role == NpcRole.Unknown)
                {
                    ClearSeek(ref seek, ref facing, ref move);
                    continue;
                }

                if (combat.ValueRO.IsDead != 0 || combat.ValueRO.CurrentHealth <= 0f)
                {
                    ClearSeek(ref seek, ref facing, ref move);
                    continue;
                }

                if (em.HasComponent<NpcCharacterBakedStats>(entity))
                {
                    var bake = em.GetComponentData<NpcCharacterBakedStats>(entity);
                    if (NpcCombatStateQueries.ShouldFleeFromCombatThreat(combat.ValueRO, bake))
                    {
                        ClearSeek(ref seek, ref facing, ref move);
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
                        ClearSeek(ref seek, ref facing, ref move);
                        continue;
                    }
                }

                float aggroSq = cfg.ValueRO.AggroRadius * cfg.ValueRO.AggroRadius;
                float bestSq = float.MaxValue;
                float3 bestPos = default;
                var found = false;

                for (int i = 0; i < candEnts.Length; i++)
                {
                    if (candEnts[i] == entity)
                        continue;
                    NpcRole otherRole = candProf[i].Role;
                    if (!IsHostilePair(profile.ValueRO.Role, otherRole))
                        continue;
                    if (candCombat[i].IsDead != 0 || candCombat[i].CurrentHealth <= 0f)
                        continue;

                    float3 op = candTf[i].Position;
                    float dx = op.x - selfFeet.x;
                    float dz = op.z - selfFeet.z;
                    float sq = dx * dx + dz * dz;
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
                    found = true;
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
                        found = true;
                    }
                }

                if (!found)
                {
                    seek.HasOverride = 0;
                    seek.Position = default;
                    facing = default;
                    move.RangedMovementLock = 0;
                    continue;
                }

                bool useRangedHold = profile.ValueRO.WeaponClass == NpcWeaponClass.Ranged ||
                    profile.ValueRO.WeaponClass == NpcWeaponClass.Both;
                float holdDist = useRangedHold ? cfg.ValueRO.CombatRange : 0f;

                seek.Position = bestPos;
                seek.SeekHoldDistance = holdDist;
                seek.HasOverride = 1;

                float flatSq = (bestPos.x - selfFeet.x) * (bestPos.x - selfFeet.x) +
                    (bestPos.z - selfFeet.z) * (bestPos.z - selfFeet.z);
                float range = cfg.ValueRO.CombatRange;
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

                move.RangedMovementLock = 0;
            }
        }

        static void ClearSeek(ref NpcSeekOverride seek, ref NpcOverrideFacing facing, ref NpcMovementState move)
        {
            seek.HasOverride = 0;
            seek.Position = default;
            seek.SeekHoldDistance = 0f;
            facing = default;
            move.RangedMovementLock = 0;
        }

        static bool IsHostilePair(NpcRole self, NpcRole other)
        {
            if (self == NpcRole.Follower && other == NpcRole.Bandit)
                return true;
            if (self == NpcRole.Bandit && (other == NpcRole.Follower || other == NpcRole.Villager))
                return true;
            return false;
        }
    }
}
