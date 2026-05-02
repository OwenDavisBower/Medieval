using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Core steering: computes a per-frame desired horizontal velocity based on Mode (Orbit / MoveTowards
    /// / WanderAroundTarget), smooths the target via <see cref="NpcMath.SmoothDamp"/>, advances along path
    /// corners, blends separation repulsion and obstacle deflection, and clamps to the effective move
    /// speed. Output lands in <see cref="NpcMovementState.CurrentHorizontalVelocity"/> for the integration
    /// system to consume.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcObstacleProbeSystem))]
    public partial struct NpcSteeringSystem : ISystem
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

            state.Dependency = new SteeringJob
            {
                DeltaTime = dt,
                ElapsedTime = elapsed
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(NpcMovementTag))]
        partial struct SteeringJob : IJobEntity
        {
            public float DeltaTime;
            public float ElapsedTime;
            public void Execute(
                in LocalTransform tf,
                in NpcMovementConfig cfg,
                in NpcAnchorTarget anchor,
                in NpcSeekOverride seek,
                in NpcPathState pathState,
                DynamicBuffer<NpcPathCorner> corners,
                ref NpcMovementState mstate,
                Entity entity)
            {
                float3 selfPos = tf.Position;
                mstate.EffectiveMoveSpeed = cfg.MoveSpeed * cfg.MoveSpeedScale *
                                            NpcMath.WaterSpeedMultiplier(selfPos.y);

                if (mstate.RangedMovementLock != 0 || mstate.MeleeEngageMovementLock != 0)
                {
                    mstate.CurrentHorizontalVelocity = float3.zero;
                    mstate.HasSmoothTarget = 0;
                    return;
                }

                bool hasGoal = TryComputeRawGoal(ref mstate, in cfg, in anchor, in seek, selfPos,
                    out float3 rawGoal, out bool arrivedHold);

                if (!hasGoal || arrivedHold)
                {
                    DecelerateHorizontal(ref mstate, cfg, DeltaTime);
                    mstate.HasSmoothTarget = 0;
                    return;
                }

                if (cfg.UseNavMeshWhenAvailable != 0 && seek.HasOverride != 0 &&
                    (pathState.PathValid == 0 || corners.Length == 0))
                {
                    mstate.HasSmoothTarget = 0;
                    DecelerateHorizontal(ref mstate, cfg, DeltaTime);
                    return;
                }

                if (mstate.HasSmoothTarget == 0)
                {
                    mstate.SmoothTarget = rawGoal;
                    mstate.SmoothTargetVel = float3.zero;
                    mstate.HasSmoothTarget = 1;
                }
                else
                {
                    float3 vel = mstate.SmoothTargetVel;
                    mstate.SmoothTarget = NpcMath.SmoothDamp(
                        mstate.SmoothTarget, rawGoal, ref vel, cfg.TargetSmoothTime, DeltaTime);
                    mstate.SmoothTargetVel = vel;
                }

                float3 seekPoint = mstate.SmoothTarget;
                if (cfg.UseNavMeshWhenAvailable != 0 && corners.Length > 0)
                {
                    float min = cfg.MinCornerAdvanceDistance;
                    float minSq = min * min;
                    for (int i = 0; i < corners.Length; i++)
                    {
                        float3 c = corners[i].Value;
                        float3 diff = c - selfPos;
                        diff.y = 0f;
                        if (math.lengthsq(diff) > minSq)
                        {
                            seekPoint = c;
                            break;
                        }
                        if (i == corners.Length - 1)
                            seekPoint = c;
                    }
                }

                if (anchor.HasAnchor != 0 && seek.HasOverride == 0)
                    seekPoint = NpcMath.AdjustSeekAroundAnchorDisc(selfPos, seekPoint, anchor.Position, cfg.MinLoiterRadius,
                        rawGoal);

                float3 flat = seekPoint - selfPos;
                flat.y = 0f;

                float3 velocity = mstate.CurrentHorizontalVelocity;
                velocity.y = 0f;

                if (math.lengthsq(flat) > cfg.ArriveThreshold * cfg.ArriveThreshold)
                {
                    float3 desiredDir = math.normalize(flat);
                    if (math.lengthsq(mstate.ObstacleDeflectDir) > 1e-4f)
                        desiredDir = math.normalizesafe(desiredDir * 0.35f + mstate.ObstacleDeflectDir * 0.65f, desiredDir);
                    float3 desired = desiredDir * mstate.EffectiveMoveSpeed;
                    desired += mstate.SeparationAccum;
                    float maxHoriz = mstate.EffectiveMoveSpeed;
                    if (math.lengthsq(desired) > maxHoriz * maxHoriz)
                        desired = math.normalize(desired) * maxHoriz;

                    float maxDelta = cfg.Acceleration * DeltaTime;
                    velocity = NpcMath.MoveTowards(velocity, desired, maxDelta);
                }
                else
                    velocity = NpcMath.MoveTowards(velocity, float3.zero, cfg.Acceleration * DeltaTime);

                mstate.CurrentHorizontalVelocity = velocity;
            }

            void DecelerateHorizontal(ref NpcMovementState mstate, in NpcMovementConfig cfg, float dt)
            {
                float3 v = mstate.CurrentHorizontalVelocity;
                v.y = 0f;
                mstate.CurrentHorizontalVelocity = NpcMath.MoveTowards(v, float3.zero, cfg.Acceleration * dt);
            }

            bool TryComputeRawGoal(
                ref NpcMovementState mstate,
                in NpcMovementConfig cfg,
                in NpcAnchorTarget anchor,
                in NpcSeekOverride seek,
                float3 selfPos,
                out float3 rawGoal,
                out bool arrivedHold)
            {
                rawGoal = selfPos;
                arrivedHold = false;

                if (seek.HasOverride != 0)
                {
                    if (seek.SeekHoldDistance > 0f)
                    {
                        float3 flat = seek.Position - selfPos;
                        flat.y = 0f;
                        if (math.lengthsq(flat) <= seek.SeekHoldDistance * seek.SeekHoldDistance)
                        {
                            arrivedHold = true;
                            return true;
                        }
                    }
                    rawGoal = seek.Position;
                    return true;
                }

                if (anchor.HasAnchor == 0)
                    return false;

                switch (mstate.Mode)
                {
                    case NpcMovementMode.Orbit:
                        rawGoal = NpcLoiterKernels.ComputeOrbit(in mstate, in cfg, in anchor, ElapsedTime);
                        return true;
                    case NpcMovementMode.MoveTowards:
                        rawGoal = anchor.Position;
                        return true;
                    case NpcMovementMode.WanderAroundTarget:
                        NpcLoiterKernels.AdvanceWanderRepick(ref mstate, in cfg, in anchor, ElapsedTime);
                        rawGoal = NpcLoiterKernels.ComputeWanderPosition(in mstate, in cfg, in anchor, ElapsedTime);
                        return true;
                    default:
                        rawGoal = anchor.Position;
                        return true;
                }
            }
        }
    }
}
