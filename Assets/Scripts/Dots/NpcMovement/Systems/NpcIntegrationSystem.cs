using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Integrates horizontal velocity into <see cref="LocalTransform.Position"/>, applies the
    /// facing-follow-velocity rule (optionally overridden by <see cref="NpcOverrideFacing"/>), and
    /// consumes any scheduled <see cref="NpcPendingDodge"/> impulses. Frame-rate independent via
    /// <c>SystemAPI.Time.DeltaTime</c>.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcSteeringSystem))]
    public partial struct NpcIntegrationSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NpcMovementTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            float elapsed = (float)SystemAPI.Time.ElapsedTime;
            float worldTime = Time.time;

            state.Dependency = new IntegrationJob
            {
                DeltaTime = dt,
                ElapsedTime = elapsed,
                WorldTime = worldTime
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(NpcMovementTag))]
        partial struct IntegrationJob : IJobEntity
        {
            public float DeltaTime;
            public float ElapsedTime;
            public float WorldTime;

            public void Execute(
                ref LocalTransform tf,
                in NpcMovementConfig cfg,
                in NpcOverrideFacing facing,
                ref NpcPendingDodge dodge,
                ref NpcMovementState mstate)
            {
                bool gestureHold = WorldTime < mstate.ShootGestureSuppressLocomotionUntilUnityTime;
                if (!gestureHold && dodge.HasPending != 0 && ElapsedTime >= dodge.FireTime)
                {
                    ApplyDodge(ref mstate, in cfg, tf.Position, dodge.ReferencePosition);
                    dodge.HasPending = 0;
                    mstate.LastDodgeApplyTime = ElapsedTime;
                }

                float3 hvel = mstate.CurrentHorizontalVelocity;
                hvel.y = 0f;

                float maxHoriz = math.max(0.05f, mstate.EffectiveMoveSpeed);
                float cap = maxHoriz;
                if (mstate.DodgeImpulseThisFrame != 0)
                {
                    cap *= 2.05f;
                    mstate.DodgeImpulseThisFrame = 0;
                }
                float sq = math.lengthsq(hvel);
                if (sq > cap * cap)
                    hvel = math.normalize(hvel) * cap;
                if (gestureHold)
                {
                    hvel = float3.zero;
                    sq = 0f;
                }
                mstate.CurrentHorizontalVelocity = hvel;

                tf.Position += new float3(hvel.x, 0f, hvel.z) * DeltaTime;

                quaternion targetRot;
                bool hasRot = false;

                if (facing.HasOverride != 0 && math.lengthsq(facing.FlatDirection) > 1e-6f)
                {
                    float3 d = math.normalize(new float3(facing.FlatDirection.x, 0f, facing.FlatDirection.z));
                    targetRot = quaternion.LookRotationSafe(d, new float3(0f, 1f, 0f));
                    hasRot = true;
                }
                else
                {
                    float minSq = cfg.FacingMinHorizontalSpeed * cfg.FacingMinHorizontalSpeed;
                    if (sq >= minSq)
                    {
                        targetRot = quaternion.LookRotationSafe(math.normalize(new float3(hvel.x, 0f, hvel.z)),
                            new float3(0f, 1f, 0f));
                        hasRot = true;
                    }
                    else
                    {
                        targetRot = tf.Rotation;
                    }
                }

                if (hasRot)
                {
                    float maxRad = math.radians(cfg.FacingTurnSpeedDegreesPerSecond) * DeltaTime;
                    tf.Rotation = RotateTowards(tf.Rotation, targetRot, maxRad);
                }
            }

            static quaternion RotateTowards(quaternion from, quaternion to, float maxRadiansDelta)
            {
                float dot = math.clamp(math.dot(from.value, to.value), -1f, 1f);
                if (dot < 0f)
                {
                    to = new quaternion(-to.value);
                    dot = -dot;
                }
                float angle = math.acos(math.min(dot, 1f)) * 2f;
                if (angle < 1e-5f)
                    return to;
                float t = math.min(1f, maxRadiansDelta / angle);
                return math.slerp(from, to, t);
            }

            static void ApplyDodge(ref NpcMovementState mstate, in NpcMovementConfig cfg,
                float3 selfPos, float3 targetPos)
            {
                if (cfg.PostRangedDodgeImpulse <= 0f)
                    return;
                float3 flat = targetPos - selfPos;
                flat.y = 0f;
                if (math.lengthsq(flat) < 1e-4f)
                    return;
                flat = math.normalize(flat);
                float3 perp = math.cross(new float3(0f, 1f, 0f), flat);
                if (math.lengthsq(perp) < 1e-6f)
                    return;
                perp = math.normalize(perp);

                // Deterministic side pick from current velocity direction (steer slightly away).
                if (math.dot(mstate.CurrentHorizontalVelocity, perp) < 0f)
                    perp = -perp;

                float3 retreat = -flat * (cfg.PostRangedDodgeImpulse * cfg.PostRangedDodgeRetreatRatio);
                float3 add = perp * cfg.PostRangedDodgeImpulse + retreat;

                float3 v = mstate.CurrentHorizontalVelocity;
                v.x += add.x;
                v.z += add.z;
                mstate.CurrentHorizontalVelocity = v;
                mstate.DodgeImpulseThisFrame = 1;
            }
        }
    }
}
