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
    /// Combat strikes for DOTS NPCs: ranged projectiles and melee hits vs ECS combat state, after
    /// <see cref="NpcCombatSeekSystem"/> (seek sets standoff / <see cref="NpcMovementState.MeleeEngageMovementLock"/>).
    /// </summary>
    [UpdateInGroup(typeof(NpcCombatSeekSystemGroup))]
    [UpdateAfter(typeof(NpcCombatSeekSystem))]
    public partial class NpcCombatAttackSystem : SystemBase
    {
        static readonly FixedString64Bytes k_ShootArrow = "ShootArrow";
        static readonly FixedString64Bytes k_SwordSlash = "SwordSlash";
        /// <summary>Matches <c>SwordSlash.anim</c> stop time so locomotion does not override the attack clip.</summary>
        const float k_SwordSlashLocomotionSuppressSeconds = 1.5f;

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

                if (em.HasComponent<NpcMeleeAttackState>(entity) && em.HasComponent<NpcMeleeCombatConfig>(entity))
                {
                    var meleeStateEarly = em.GetComponentData<NpcMeleeAttackState>(entity);
                    if (meleeStateEarly.HitInProgress != 0)
                    {
                        var meleeCfgEarly = em.GetComponentData<NpcMeleeCombatConfig>(entity);
                        TickInProgressMeleeHit(em, ref ecb, entity, selfFeet, in meleeCfgEarly, unityTime,
                            ref meleeStateEarly);
                        em.SetComponentData(entity, meleeStateEarly);
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

                bool wantRanged = profile.ValueRO.WeaponClass == NpcWeaponClass.Ranged ||
                    profile.ValueRO.WeaponClass == NpcWeaponClass.Both;

                if (profile.ValueRO.WeaponClass == NpcWeaponClass.Melee &&
                    em.HasComponent<NpcMeleeCombatConfig>(entity) &&
                    em.HasComponent<NpcMeleeAttackState>(entity))
                {
                    var meleeCfg = em.GetComponentData<NpcMeleeCombatConfig>(entity);
                    var meleeState = em.GetComponentData<NpcMeleeAttackState>(entity);
                    TryDotsNpcMeleeStrike(em, ref ecb, entity, selfFeet, ref combat, combatTarget.ValueRO,
                        ref meleeState, in meleeCfg, flatSq, unityTime);
                    em.SetComponentData(entity, meleeState);
                    continue;
                }

                bool hasRanged = em.HasComponent<NpcRangedCombatConfig>(entity);
                if (!hasRanged)
                    continue;

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
                rangedState.PendingGoalFeet = goal;
                rangedState.HasPendingGoalFeet = 1;
                rangedState.ShotInProgress = 1;

                move.RangedMovementLock = 1;
                move.ShootGestureSuppressLocomotionUntilUnityTime = lockUntil;
                em.SetComponentData(entity, move);

                TryPlayShootAnim(em, entity);

                if (lead <= 1e-4f)
                {
                    ReleaseRangedShot(em, ref ecb, entity, selfFeet, goal, in rangedCfg,
                        combat.RangedAimErrorMultiplier);
                    rangedState.ShotInProgress = 0;
                    rangedState.PendingTargetNpcEntity = Entity.Null;
                    rangedState.PendingGoalFeet = default;
                    rangedState.HasPendingGoalFeet = 0;
                }

                em.SetComponentData(entity, rangedState);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        static void TryDotsNpcMeleeStrike(
            EntityManager em,
            ref EntityCommandBuffer ecb,
            Entity attacker,
            float3 selfFeet,
            ref NpcCharacterCombatState attackerCombat,
            in NpcCombatTarget combatTarget,
            ref NpcMeleeAttackState meleeState,
            in NpcMeleeCombatConfig meleeCfg,
            float flatSq,
            float unityTime)
        {
            float meleeR = math.max(0.25f, meleeCfg.MeleeRange);
            if (flatSq > meleeR * meleeR)
                return;

            if (unityTime < meleeState.NextAttackAllowedUnityTime)
                return;

            meleeState.NextAttackAllowedUnityTime = unityTime + meleeCfg.AttackInterval;
            TryPlayAnimOnNpcRoot(em, attacker, k_SwordSlash);

            bool hitRollPassed = true;
            if (em.HasComponent<NpcMovementState>(attacker))
            {
                var move = em.GetComponentData<NpcMovementState>(attacker);
                float suppressUntil = unityTime + k_SwordSlashLocomotionSuppressSeconds;
                if (suppressUntil > move.ShootGestureSuppressLocomotionUntilUnityTime)
                    move.ShootGestureSuppressLocomotionUntilUnityTime = suppressUntil;

                var rng = EnsureNpcRng(ref move, attacker);
                hitRollPassed = rng.NextFloat() <= meleeCfg.HitChance;
                move.Rng = rng;
                em.SetComponentData(attacker, move);
            }

            if (!hitRollPassed)
                return;

            float dmg = meleeCfg.Damage * attackerCombat.MeleeDamageMultiplier;
            Entity tgt = combatTarget.TargetNpcEntity;
            bool targetIsPlayer = tgt == Entity.Null && combatTarget.HasCombatTarget != 0;
            if (!targetIsPlayer && tgt == Entity.Null)
                return;

            float lead = math.max(0f, meleeCfg.HitAnimationLeadSeconds);
            if (lead <= 1e-4f)
            {
                ApplyMeleeHit(em, ref ecb, attacker, selfFeet, tgt, targetIsPlayer, dmg, in meleeCfg, unityTime);
                return;
            }

            meleeState.ApplyHitAtUnityTime = unityTime + lead;
            meleeState.PendingTargetNpcEntity = tgt;
            meleeState.PendingDamage = dmg;
            meleeState.PendingTargetIsPlayer = targetIsPlayer ? (byte)1 : (byte)0;
            meleeState.HitInProgress = 1;
        }

        static void TickInProgressMeleeHit(
            EntityManager em,
            ref EntityCommandBuffer ecb,
            Entity attacker,
            float3 selfFeet,
            in NpcMeleeCombatConfig meleeCfg,
            float unityTime,
            ref NpcMeleeAttackState meleeState)
        {
            if (unityTime < meleeState.ApplyHitAtUnityTime)
                return;

            bool targetIsPlayer = meleeState.PendingTargetIsPlayer != 0;
            Entity tgt = meleeState.PendingTargetNpcEntity;
            float dmg = meleeState.PendingDamage;

            meleeState.HitInProgress = 0;
            meleeState.PendingTargetNpcEntity = Entity.Null;
            meleeState.PendingDamage = 0f;
            meleeState.PendingTargetIsPlayer = 0;

            ApplyMeleeHit(em, ref ecb, attacker, selfFeet, tgt, targetIsPlayer, dmg, in meleeCfg, unityTime);
        }

        static void ApplyMeleeHit(
            EntityManager em,
            ref EntityCommandBuffer ecb,
            Entity attacker,
            float3 selfFeet,
            Entity tgt,
            bool targetIsPlayer,
            float dmg,
            in NpcMeleeCombatConfig meleeCfg,
            float unityTime)
        {
            // HasCombatTarget + Null entity = GameObject player (see NpcCombatSeekSystem).
            if (targetIsPlayer)
            {
                TryMeleeDamagePlayer(em, ref ecb, attacker, dmg, meleeCfg.KnockbackImpulse,
                    meleeCfg.HitMeleeStunDuration);
                return;
            }

            if (!em.Exists(tgt) || !em.HasComponent<NpcCharacterCombatState>(tgt))
                return;

            var victim = em.GetComponentData<NpcCharacterCombatState>(tgt);
            if (victim.IsDead != 0 || victim.CurrentHealth <= 0f)
                return;

            float3 victimFeet = em.HasComponent<LocalTransform>(tgt)
                ? em.GetComponentData<LocalTransform>(tgt).Position
                : selfFeet;
            float dealt = NpcProjectileDotsNpc.ApplyProjectileDamage(em, tgt, dmg, out bool killed,
                victimFeet - selfFeet);
            NpcExperienceUtility.GrantDamageXp(em, ref ecb, attacker, dealt, killed);

            victim = em.GetComponentData<NpcCharacterCombatState>(tgt);
            if (victim.IsDead != 0)
                return;

            float stunEnd = unityTime + meleeCfg.HitMeleeStunDuration;
            if (stunEnd > victim.AttackStunUntilUnityTime)
            {
                victim.AttackStunUntilUnityTime = stunEnd;
                em.SetComponentData(tgt, victim);
            }
        }

        static void TryMeleeDamagePlayer(
            EntityManager em,
            ref EntityCommandBuffer ecb,
            Entity attacker,
            float damage,
            float knockbackImpulse,
            float hitMeleeStunDuration)
        {
            Transform playerTf = PlayerAnchorRegistration.Transform;
            if (playerTf == null)
                return;

            var victim = playerTf.GetComponentInParent<IDamageableHealth>();
            if (victim == null || victim.IsDead)
                return;

            float before = victim.CurrentHealth;
            if (victim is Character victimCharacter)
            {
                float3 attackerPos = em.HasComponent<LocalTransform>(attacker)
                    ? em.GetComponentData<LocalTransform>(attacker).Position
                    : default;
                Vector3 impact = playerTf.position - new Vector3(attackerPos.x, attackerPos.y, attackerPos.z);
                victimCharacter.TakeDamage(damage, impact);
                victimCharacter.ApplyAttackStun(hitMeleeStunDuration);
            }
            else
            {
                victim.TakeDamage(damage);
            }
            float dealt = math.max(0f, before - victim.CurrentHealth);
            NpcExperienceUtility.GrantDamageXp(em, ref ecb, attacker, dealt, victim.IsDead);

            Rigidbody victimRb = PlayerAnchorRegistration.Rigidbody;
            if (victimRb == null || knockbackImpulse <= 0f)
                return;

            float3 atkPos = em.HasComponent<LocalTransform>(attacker)
                ? em.GetComponentData<LocalTransform>(attacker).Position
                : default;
            Vector3 d = playerTf.position - new Vector3(atkPos.x, atkPos.y, atkPos.z);
            d.y = 0f;
            Vector3 push = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
            Vector3 v = victimRb.linearVelocity;
            v.x += push.x * knockbackImpulse;
            v.z += push.z * knockbackImpulse;
            victimRb.linearVelocity = v;
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
                else if (rangedState.HasPendingGoalFeet != 0)
                {
                    goalFeet = rangedState.PendingGoalFeet;
                    haveGoal = true;
                }

                if (haveGoal)
                    ReleaseRangedShot(em, ref ecb, entity, selfFeet, goalFeet, in rangedCfg, aimErrorMultiplier);

                rangedState.ShotInProgress = 0;
                rangedState.PendingTargetNpcEntity = Entity.Null;
                rangedState.PendingGoalFeet = default;
                rangedState.HasPendingGoalFeet = 0;
            }

            em.SetComponentData(entity, rangedState);
        }

        static void ReleaseRangedShot(
            EntityManager em,
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

            float horiz = cfg.HorizontalAimError * aimScale;
            float vert = cfg.VerticalAimError * aimScale;
            if (em.HasComponent<NpcMovementState>(shooterRoot))
            {
                var move = em.GetComponentData<NpcMovementState>(shooterRoot);
                var rng = EnsureNpcRng(ref move, shooterRoot);
                // Uniform disk in XZ (matches UnityEngine.Random.insideUnitCircle distribution).
                float2 dir = rng.NextFloat2Direction();
                float rad = math.sqrt(rng.NextFloat()) * horiz;
                float yErr = rng.NextFloat(-vert, vert);
                move.Rng = rng;
                em.SetComponentData(shooterRoot, move);
                aim += new Vector3(dir.x * rad, yErr, dir.y * rad);
            }

            Vector3 velocity = ProjectileBallistics.LobbedLaunchVelocity(origin, aim);
            ProjectileSpawnApi.SpawnFromDotsNpcShooterDeferred(ref ecb, origin, velocity, cfg.ArrowDamage,
                cfg.ArrowMaxLifetime, shooterRoot, cfg.ArrowHitRadius);
        }

        static Unity.Mathematics.Random EnsureNpcRng(ref NpcMovementState move, Entity entity)
        {
            var rng = move.Rng;
            if (rng.state == 0)
            {
                uint seed = math.max(1u, (uint)entity.Index ^ (uint)entity.Version * 0x9E3779B9u ^ 0xA341316Cu);
                rng = new Unity.Mathematics.Random(seed);
            }

            return rng;
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
