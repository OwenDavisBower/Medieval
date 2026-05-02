using Medieval.NpcMovement;
using Medieval.Projectiles;
using ProjectDawn.Animation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.Npcs
{
    /// <summary>
    /// Ranged attacks for DOTS NPCs using baked configs; runs after <see cref="NpcCombatSeekSystem"/>.
    /// Melee loadouts pathfind via seek only (no DOTS melee strikes here).
    /// </summary>
    [UpdateInGroup(typeof(NpcCombatSeekSystemGroup))]
    [UpdateAfter(typeof(NpcCombatSeekSystem))]
    public partial class NpcCombatAttackSystem : SystemBase
    {
        static readonly FixedString64Bytes k_ShootArrow = "ShootArrow";

        protected override void OnUpdate()
        {
            float unityTime = UnityEngine.Time.time;
            var em = EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (seek, selfTf, profile, combatRw, cfg, combatTarget, entity) in SystemAPI
                         .Query<RefRO<NpcSeekOverride>, RefRO<LocalTransform>, RefRO<NpcProfile>,
                             RefRW<NpcCharacterCombatState>, RefRO<NpcCombatSeekConfig>, RefRO<NpcCombatTarget> >()
                         .WithAll<NpcMovementTag>()
                         .WithEntityAccess())
            {
                ref NpcCharacterCombatState combat = ref combatRw.ValueRW;
                if (combat.IsDead != 0 || combat.CurrentHealth <= 0f)
                    continue;
                if (unityTime < combat.AttackStunUntilUnityTime)
                    continue;

                float3 selfFeet = selfTf.ValueRO.Position;

                if (em.HasComponent<NpcRangedAttackState>(entity) && em.HasComponent<NpcRangedCombatConfig>(entity))
                {
                    var rangedStateEarly = em.GetComponentData<NpcRangedAttackState>(entity);
                    if (rangedStateEarly.ShotInProgress != 0)
                    {
                        var rangedCfgEarly = em.GetComponentData<NpcRangedCombatConfig>(entity);
                        TickInProgressRangedShot(em, ref ecb, entity, selfFeet, seek.ValueRO, in rangedCfgEarly,
                            combat.RangedAimErrorMultiplier, unityTime, ref rangedStateEarly);
                        continue;
                    }
                }

                if (seek.ValueRO.HasOverride == 0)
                    continue;

                if (combatTarget.ValueRO.HasCombatTarget == 0)
                    continue;

                float3 goal = seek.ValueRO.Position;
                float dx = goal.x - selfFeet.x;
                float dz = goal.z - selfFeet.z;
                float flatSq = dx * dx + dz * dz;

                bool hasRanged = em.HasComponent<NpcRangedCombatConfig>(entity);
                if (!hasRanged)
                    continue;

                bool wantRanged = profile.ValueRO.WeaponClass == NpcWeaponClass.Ranged ||
                    profile.ValueRO.WeaponClass == NpcWeaponClass.Both;

                if (!wantRanged || !em.HasComponent<NpcRangedAttackState>(entity))
                    continue;

                var rangedCfg = em.GetComponentData<NpcRangedCombatConfig>(entity);
                float combatRange = cfg.ValueRO.CombatRange;
                if (flatSq > combatRange * combatRange)
                {
                    var moveOutOfShootRange = em.GetComponentData<NpcMovementState>(entity);
                    moveOutOfShootRange.RangedMovementLock = 0;
                    em.SetComponentData(entity, moveOutOfShootRange);
                    continue;
                }

                var rangedState = em.GetComponentData<NpcRangedAttackState>(entity);
                var move = em.GetComponentData<NpcMovementState>(entity);

                if (unityTime < rangedState.NextFireAllowedUnityTime)
                {
                    // Cooldown between shots: keep steering/locomotion free; lock only applies during
                    // ShotInProgress (draw/release) via TickInProgressRangedShot.
                    move.RangedMovementLock = 0;
                    move.ShootGestureSuppressLocomotionUntilUnityTime = 0f;
                    em.SetComponentData(entity, move);
                    continue;
                }

                float lead = math.max(0f, rangedCfg.FireAnimationLeadSeconds);
                float lockUntil = unityTime + rangedCfg.MovementLockDuration + lead;
                float releaseAt = unityTime + lead;
                rangedState.NextFireAllowedUnityTime = unityTime + rangedCfg.FireInterval;
                rangedState.MovementLockUntilUnityTime = lockUntil;
                rangedState.ReleaseShotAtUnityTime = releaseAt;
                rangedState.PendingTargetNpcEntity = combatTarget.ValueRO.TargetNpcEntity;
                rangedState.ShotInProgress = 1;

                move.RangedMovementLock = 1;
                move.ShootGestureSuppressLocomotionUntilUnityTime = lockUntil;
                em.SetComponentData(entity, move);

                TryPlayShootAnim(em, entity);

                if (lead <= 1e-4f)
                {
                    ReleaseRangedShot(ref ecb, entity, selfFeet, goal, in rangedCfg, combat.RangedAimErrorMultiplier);
                    rangedState.ShotInProgress = 0;
                    rangedState.PendingTargetNpcEntity = Entity.Null;
                }

                em.SetComponentData(entity, rangedState);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        /// <summary>
        /// Finishes draw/release even when <see cref="NpcSeekOverride.HasOverride"/> dropped for a frame (LOS, leash, aggro edge).
        /// Uses pending target feet when seek is cleared so the arrow still releases toward the last hostile.
        /// </summary>
        static void TickInProgressRangedShot(
            EntityManager em,
            ref EntityCommandBuffer ecb,
            Entity entity,
            float3 selfFeet,
            in NpcSeekOverride seek,
            in NpcRangedCombatConfig rangedCfg,
            float aimErrorMultiplier,
            float unityTime,
            ref NpcRangedAttackState rangedState)
        {
            var move = em.GetComponentData<NpcMovementState>(entity);
            move.RangedMovementLock = unityTime < rangedState.MovementLockUntilUnityTime ? (byte)1 : (byte)0;
            em.SetComponentData(entity, move);

            if (unityTime >= rangedState.ReleaseShotAtUnityTime)
            {
                float3 goalFeet = default;
                bool haveGoal = false;
                if (seek.HasOverride != 0)
                {
                    goalFeet = seek.Position;
                    haveGoal = true;
                }
                else if (rangedState.PendingTargetNpcEntity != Entity.Null &&
                         em.Exists(rangedState.PendingTargetNpcEntity) &&
                         em.HasComponent<LocalTransform>(rangedState.PendingTargetNpcEntity))
                {
                    goalFeet = em.GetComponentData<LocalTransform>(rangedState.PendingTargetNpcEntity).Position;
                    haveGoal = true;
                }

                if (haveGoal)
                    ReleaseRangedShot(ref ecb, entity, selfFeet, goalFeet, in rangedCfg, aimErrorMultiplier);

                rangedState.ShotInProgress = 0;
                rangedState.PendingTargetNpcEntity = Entity.Null;
            }

            em.SetComponentData(entity, rangedState);
        }

        static void ReleaseRangedShot(
            ref EntityCommandBuffer ecb,
            Entity shooterRoot,
            float3 selfFeet,
            float3 goalFeet,
            in NpcRangedCombatConfig cfg,
            float aimErrorMultiplier)
        {
            float aimScale = math.max(0.05f, aimErrorMultiplier);
            Vector3 origin = new Vector3(selfFeet.x, selfFeet.y, selfFeet.z) + Vector3.up * cfg.LaunchHeight;

            float aimY = goalFeet.y + cfg.TargetAimHeight;
            Vector3 aim = new Vector3(goalFeet.x, aimY, goalFeet.z);
            Vector2 xz = UnityEngine.Random.insideUnitCircle * (cfg.HorizontalAimError * aimScale);
            aim += new Vector3(xz.x, UnityEngine.Random.Range(-cfg.VerticalAimError, cfg.VerticalAimError) * aimScale,
                xz.y);

            Vector3 velocity = ProjectileBallistics.LobbedLaunchVelocity(origin, aim);
            ProjectileSpawnApi.SpawnFromDotsNpcShooterDeferred(ref ecb, origin, velocity, cfg.ArrowDamage,
                cfg.ArrowMaxLifetime, shooterRoot, cfg.ArrowHitRadius);
        }

        static void TryPlayShootAnim(EntityManager em, Entity npcRoot)
        {
            TryPlayAnimOnNpcRoot(em, npcRoot, k_ShootArrow);
        }

        static void TryPlayAnimOnNpcRoot(EntityManager em, Entity npcRoot, FixedString64Bytes clipName)
        {
            if (!em.Exists(npcRoot))
                return;

            if (em.HasBuffer<LinkedEntityGroup>(npcRoot))
            {
                var buf = em.GetBuffer<LinkedEntityGroup>(npcRoot);
                for (int i = 0; i < buf.Length; i++)
                {
                    if (TryPlayNamedAnimOnEntity(em, buf[i].Value, clipName))
                        return;
                }
            }

            TryPlayNamedAnimOnEntity(em, npcRoot, clipName);
        }

        static bool TryPlayNamedAnimOnEntity(EntityManager em, Entity e, FixedString64Bytes clipName)
        {
            if (!em.HasComponent<Animatron>(e) || !em.HasComponent<MotionRef>(e))
                return false;

            MotionRef motionRef = em.GetSharedComponentManaged<MotionRef>(e);
            ref ProjectDawn.Animation.Motion motion = ref motionRef.Value.Value;
            if (!motion.TryFindAnimationIndex(clipName, out AnimationIndex clipIdx))
                return false;

            var anim = em.GetComponentData<Animatron>(e);
            if (em.HasComponent<CrossFader>(e))
            {
                var cross = em.GetComponentData<CrossFader>(e);
                cross.CrossFade(clipIdx);
                em.SetComponentData(e, cross);
            }
            else
            {
                anim.Play(clipIdx);
                em.SetComponentData(e, anim);
            }

            return true;
        }
    }
}
