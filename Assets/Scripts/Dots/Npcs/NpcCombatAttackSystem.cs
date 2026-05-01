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
    /// Ranged and melee attacks for DOTS NPCs using baked configs; runs after <see cref="NpcCombatSeekSystem"/>.
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

                bool hasMelee = em.HasComponent<NpcMeleeCombatConfig>(entity);
                bool hasRanged = em.HasComponent<NpcRangedCombatConfig>(entity);
                if (!hasMelee && !hasRanged)
                    continue;

                bool wantMelee = profile.ValueRO.WeaponClass == NpcWeaponClass.Melee ||
                    profile.ValueRO.WeaponClass == NpcWeaponClass.Both;
                bool wantRanged = profile.ValueRO.WeaponClass == NpcWeaponClass.Ranged ||
                    profile.ValueRO.WeaponClass == NpcWeaponClass.Both;

                if (hasMelee && wantMelee && em.HasComponent<NpcMeleeAttackState>(entity))
                {
                    var meleeCfg = em.GetComponentData<NpcMeleeCombatConfig>(entity);
                    float r = meleeCfg.MeleeRange;
                    if (flatSq <= r * r)
                    {
                        var meleeState = em.GetComponentData<NpcMeleeAttackState>(entity);
                        TryMeleeAttack(em, selfFeet, goal, combatTarget.ValueRO, in meleeCfg, ref combat,
                            ref meleeState, unityTime);
                        em.SetComponentData(entity, meleeState);
                        continue;
                    }
                }

                if (!hasRanged || !wantRanged || !em.HasComponent<NpcRangedAttackState>(entity))
                    continue;

                var rangedCfg = em.GetComponentData<NpcRangedCombatConfig>(entity);
                float combatRange = cfg.ValueRO.CombatRange;
                if (flatSq > combatRange * combatRange)
                    continue;

                var rangedState = em.GetComponentData<NpcRangedAttackState>(entity);
                var move = em.GetComponentData<NpcMovementState>(entity);

                if (unityTime < rangedState.NextFireAllowedUnityTime)
                {
                    move.RangedMovementLock = unityTime < rangedState.MovementLockUntilUnityTime ? (byte)1 : (byte)0;
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

        static void TryMeleeAttack(
            EntityManager em,
            float3 selfFeet,
            float3 goalFlat,
            NpcCombatTarget targetInfo,
            in NpcMeleeCombatConfig meleeCfg,
            ref NpcCharacterCombatState combat,
            ref NpcMeleeAttackState meleeState,
            float unityTime)
        {
            if (unityTime < meleeState.NextAttackAllowedUnityTime)
                return;

            meleeState.NextAttackAllowedUnityTime = unityTime + meleeCfg.AttackInterval;
            if (UnityEngine.Random.value > meleeCfg.HitChance)
                return;

            float dmg = meleeCfg.Damage * combat.MeleeDamageMultiplier;

            Entity victimNpc = targetInfo.TargetNpcEntity;
            if (victimNpc != Entity.Null && em.Exists(victimNpc) && em.HasComponent<NpcCharacterCombatState>(victimNpc))
            {
                var vState = em.GetComponentData<NpcCharacterCombatState>(victimNpc);
                if (vState.IsDead != 0 || vState.CurrentHealth <= 0f)
                    return;

                vState.CurrentHealth -= dmg;
                if (vState.CurrentHealth <= 0f)
                {
                    vState.CurrentHealth = 0f;
                    vState.IsDead = 1;
                }

                vState.AttackStunUntilUnityTime =
                    math.max(vState.AttackStunUntilUnityTime, unityTime + meleeCfg.HitMeleeStunDuration);
                em.SetComponentData(victimNpc, vState);
                if (vState.IsDead != 0)
                    NpcEntityDestroyUtility.DestroyNpcWithLinked(em, victimNpc);

                ApplyMeleeKnockback(em, victimNpc, selfFeet, goalFlat, meleeCfg.KnockbackImpulse);
                return;
            }

            Transform player = PlayerReference.TryGetTransform();
            if (player == null)
                return;

            Vector3 p = player.position;
            float pdx = p.x - selfFeet.x;
            float pdz = p.z - selfFeet.z;
            if (pdx * pdx + pdz * pdz > meleeCfg.MeleeRange * meleeCfg.MeleeRange)
                return;

            Character ch = PlayerReference.TryGetCharacter();
            if (ch == null || ch.IsDead)
                return;

            ch.TakeDamage(dmg);
            ch.ApplyAttackStun(meleeCfg.HitMeleeStunDuration);
            Rigidbody prb = PlayerReference.TryGetRigidbody();
            if (prb != null)
            {
                Vector3 d = p - (Vector3)selfFeet;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f)
                    d.Normalize();
                else
                    d = Vector3.forward;
                Vector3 deltaV = d * meleeCfg.KnockbackImpulse;
                Vector3 v = prb.linearVelocity;
                v.x += deltaV.x;
                v.z += deltaV.z;
                prb.linearVelocity = v;
            }
        }

        static void ApplyMeleeKnockback(EntityManager em, Entity victim, float3 selfFeet, float3 targetFeet, float impulse)
        {
            if (!em.HasComponent<NpcMovementState>(victim))
                return;
            float3 d = targetFeet - selfFeet;
            d.y = 0f;
            if (math.lengthsq(d) < 1e-6f)
                d = new float3(0f, 0f, 1f);
            d = math.normalize(d);
            var m = em.GetComponentData<NpcMovementState>(victim);
            float3 add = d * impulse;
            m.CurrentHorizontalVelocity =
                new float3(m.CurrentHorizontalVelocity.x + add.x, m.CurrentHorizontalVelocity.y,
                    m.CurrentHorizontalVelocity.z + add.z);
            em.SetComponentData(victim, m);
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
            if (!em.Exists(npcRoot))
                return;

            if (em.HasBuffer<LinkedEntityGroup>(npcRoot))
            {
                var buf = em.GetBuffer<LinkedEntityGroup>(npcRoot);
                for (int i = 0; i < buf.Length; i++)
                {
                    Entity e = buf[i].Value;
                    if (TryPlayShootOnEntity(em, e))
                        return;
                }
            }

            TryPlayShootOnEntity(em, npcRoot);
        }

        static bool TryPlayShootOnEntity(EntityManager em, Entity e)
        {
            if (!em.HasComponent<Animatron>(e) || !em.HasComponent<MotionRef>(e))
                return false;

            MotionRef motionRef = em.GetSharedComponentManaged<MotionRef>(e);
            ref ProjectDawn.Animation.Motion motion = ref motionRef.Value.Value;
            if (!motion.TryFindAnimationIndex(k_ShootArrow, out AnimationIndex shootIdx))
                return false;

            var anim = em.GetComponentData<Animatron>(e);
            if (em.HasComponent<CrossFader>(e))
            {
                var cross = em.GetComponentData<CrossFader>(e);
                cross.CrossFade(shootIdx);
                em.SetComponentData(e, cross);
            }
            else
            {
                anim.Play(shootIdx);
                em.SetComponentData(e, anim);
            }

            return true;
        }
    }
}
