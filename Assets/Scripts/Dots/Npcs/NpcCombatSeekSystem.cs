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
        /// <summary>
        /// SwordSlash humanoid body yaw is baked ~15° left of aim in the Animatron rig; rotate entity
        /// facing this much to the right so the mesh looks at the target while melee-engaged.
        /// </summary>
        const float MeleeFacingYawCompensationDegrees = 15f;

        /// <summary>Keep current target while within AggroRadius * this (exit hysteresis).</summary>
        const float StickyAggroMul = 1.15f;

        /// <summary>
        /// Switch from sticky only when challenger distance-sq is below stickySq * this
        /// (challenger clearly closer; ~0.8 ≈ ~10% nearer in linear distance).
        /// </summary>
        const float StickSwitchRatio = 0.8f;

        /// <summary>Frames of failed LOS before dropping a sticky target.</summary>
        const byte LosMissGraceFrames = 3;

        /// <summary>Exit melee lock beyond MeleeRange * this; enter at MeleeRange.</summary>
        const float MeleeEngageExitMul = 1.2f;

        /// <summary>After leash clear, must return within MaxDistanceFromLeader * this to re-engage.</summary>
        const float LeashReenterMul = 0.85f;

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
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            combatLookup.Update(ref state);
            factionLookup.Update(ref state);
            transformLookup.Update(ref state);

            int matrixSize = relState.MatrixSize;
            bool playerAlive = hasPlayer && IsPlayerAlive();

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

                int selfFaction = factionLookup.HasComponent(entity) ? factionLookup[entity].Value : -1;

                if (move.Group == NpcSeparationGroup.Followers && cfg.ValueRO.MaxDistanceFromLeader > 0f &&
                    hasPlayer)
                {
                    float3 p = playerAnchor.Position;
                    float maxDist = cfg.ValueRO.MaxDistanceFromLeader;
                    float exitSq = maxDist * maxDist;
                    float enterDist = maxDist * LeashReenterMul;
                    float enterSq = enterDist * enterDist;
                    float leaderSq = NpcMath.DistanceSqXZ(selfFeet, p);

                    if (move.CombatLeashBlocked != 0)
                    {
                        if (leaderSq > enterSq)
                        {
                            ClearSeek(ref seek, ref facing, ref move, ref combatTarget);
                            continue;
                        }

                        move.CombatLeashBlocked = 0;
                    }
                    else if (leaderSq > exitSq)
                    {
                        move.CombatLeashBlocked = 1;
                        ClearSeek(ref seek, ref facing, ref move, ref combatTarget);
                        continue;
                    }
                }

                float aggro = cfg.ValueRO.AggroRadius;
                float aggroSq = aggro * aggro;
                float stickyAggroSq = (aggro * StickyAggroMul) * (aggro * StickyAggroMul);

                float bestSq = float.MaxValue;
                float3 bestPos = default;
                Entity bestHostileNpc = Entity.Null;
                var found = false;

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

                    if (!HasLos(selfFeet, op, in cfg.ValueRO))
                        continue;

                    bestSq = sq;
                    bestPos = op;
                    bestHostileNpc = candEnts[i];
                    found = true;
                }

                if (playerAlive && playerAnchor.PlayerFactionId >= 0 && selfFaction >= 0 &&
                    FactionRelationshipBufferUtil.IsHostile(in relBuf, matrixSize, selfFaction,
                        playerAnchor.PlayerFactionId))
                {
                    float3 op = playerAnchor.Position;
                    float sq = NpcMath.DistanceSqXZ(op, selfFeet);
                    if (sq <= aggroSq && sq < bestSq && HasLos(selfFeet, op, in cfg.ValueRO))
                    {
                        bestSq = sq;
                        bestPos = op;
                        bestHostileNpc = Entity.Null;
                        found = true;
                    }
                }

                // Sticky: prefer current target unless invalid or a challenger is clearly closer.
                if (combatTarget.HasCombatTarget != 0)
                {
                    bool stickyIsPlayer = combatTarget.TargetNpcEntity == Entity.Null;
                    bool stickyOkExceptLos = false;
                    bool stickyLosOk = false;
                    float stickySq = float.MaxValue;
                    float3 stickyPos = default;
                    Entity stickyHostile = Entity.Null;

                    if (stickyIsPlayer)
                    {
                        if (playerAlive && playerAnchor.PlayerFactionId >= 0 && selfFaction >= 0 &&
                            FactionRelationshipBufferUtil.IsHostile(in relBuf, matrixSize, selfFaction,
                                playerAnchor.PlayerFactionId))
                        {
                            stickyPos = playerAnchor.Position;
                            stickySq = NpcMath.DistanceSqXZ(stickyPos, selfFeet);
                            stickyHostile = Entity.Null;
                            if (stickySq <= stickyAggroSq)
                            {
                                stickyOkExceptLos = true;
                                stickyLosOk = HasLos(selfFeet, stickyPos, in cfg.ValueRO);
                            }
                        }
                    }
                    else
                    {
                        Entity stickyEnt = combatTarget.TargetNpcEntity;
                        if (stickyEnt != entity &&
                            em.Exists(stickyEnt) &&
                            combatLookup.HasComponent(stickyEnt) &&
                            transformLookup.HasComponent(stickyEnt) &&
                            factionLookup.HasComponent(stickyEnt))
                        {
                            NpcCharacterCombatState sc = combatLookup[stickyEnt];
                            if (sc.IsDead == 0 && sc.CurrentHealth > 0f)
                            {
                                int otherFaction = factionLookup[stickyEnt].Value;
                                if (selfFaction >= 0 && otherFaction >= 0 &&
                                    FactionRelationshipBufferUtil.IsHostile(in relBuf, matrixSize, selfFaction,
                                        otherFaction))
                                {
                                    stickyPos = transformLookup[stickyEnt].Position;
                                    stickySq = NpcMath.DistanceSqXZ(stickyPos, selfFeet);
                                    stickyHostile = stickyEnt;
                                    if (stickySq <= stickyAggroSq)
                                    {
                                        stickyOkExceptLos = true;
                                        stickyLosOk = HasLos(selfFeet, stickyPos, in cfg.ValueRO);
                                    }
                                }
                            }
                        }
                    }

                    bool stickyValid = stickyOkExceptLos &&
                        (stickyLosOk || combatTarget.LosMissFrames < LosMissGraceFrames);

                    if (stickyValid)
                    {
                        bool switchToChallenger = found && bestSq < stickySq * StickSwitchRatio;
                        if (!switchToChallenger)
                        {
                            bestPos = stickyPos;
                            bestHostileNpc = stickyHostile;
                            found = true;
                            combatTarget.LosMissFrames = stickyLosOk
                                ? (byte)0
                                : (byte)(combatTarget.LosMissFrames + 1);
                        }
                        else
                            combatTarget.LosMissFrames = 0;
                    }
                    else
                        combatTarget.LosMissFrames = 0;
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

                bool meleeEngaged = false;
                if (!useRangedHold && em.HasComponent<NpcMeleeCombatConfig>(entity))
                {
                    var meleeCfg = em.GetComponentData<NpcMeleeCombatConfig>(entity);
                    float meleeR = math.max(0.25f, meleeCfg.MeleeRange);
                    float enterSq = meleeR * meleeR;
                    float exitR = meleeR * MeleeEngageExitMul;
                    float exitSq = exitR * exitR;
                    if (move.MeleeEngageMovementLock != 0)
                        meleeEngaged = flatSq <= exitSq;
                    else
                        meleeEngaged = flatSq <= enterSq;
                }

                move.MeleeEngageMovementLock = (byte)(meleeEngaged ? 1 : 0);

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
                else if (meleeEngaged)
                {
                    float3 d = bestPos - selfFeet;
                    d.y = 0f;
                    if (math.lengthsq(d) > 1e-6f)
                    {
                        d = math.normalize(d);
                        facing.FlatDirection = RotateYawDegrees(d, MeleeFacingYawCompensationDegrees);
                        facing.HasOverride = 1;
                    }
                    else
                        facing = default;
                }
                else
                    facing = default;
            }
        }

        static bool HasLos(float3 selfFeet, float3 targetFeet, in NpcCombatSeekConfig cfg) =>
            LineOfSightUtility.HasClearLineOfSightWorldPoints(
                new Vector3(selfFeet.x, selfFeet.y, selfFeet.z),
                new Vector3(targetFeet.x, targetFeet.y, targetFeet.z),
                cfg.EyeHeight,
                cfg.TargetAimHeight,
                cfg.ObstacleLayerMask,
                null);

        static bool IsPlayerAlive()
        {
            Transform playerTf = PlayerAnchorRegistration.Transform;
            if (playerTf == null)
                return false;
            var health = playerTf.GetComponentInParent<IDamageableHealth>();
            return health == null || !health.IsDead;
        }

        static void ClearSeek(ref NpcSeekOverride seek, ref NpcOverrideFacing facing, ref NpcMovementState move,
            ref NpcCombatTarget combatTarget)
        {
            seek.HasOverride = 0;
            seek.Position = default;
            seek.SeekHoldDistance = 0f;
            facing = default;
            move.RangedMovementLock = 0;
            move.MeleeEngageMovementLock = 0;
            move.RangedCombatSeparationBoost = 0;
            combatTarget = default;
        }

        static float3 RotateYawDegrees(float3 flatDirection, float degrees)
        {
            float rad = math.radians(degrees);
            math.sincos(rad, out float s, out float c);
            return new float3(flatDirection.x * c + flatDirection.z * s, 0f,
                -flatDirection.x * s + flatDirection.z * c);
        }
    }
}
