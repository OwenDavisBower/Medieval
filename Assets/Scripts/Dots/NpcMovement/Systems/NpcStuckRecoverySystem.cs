using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.AI;
using UnityEngine.Experimental.AI;

// Experimental.AI NavMeshQuery is obsolete without replacement on Unity 6000.4; still the job-safe API.
#pragma warning disable CS0618

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Detects NPCs making no XZ progress toward their goal and forces repath / a lateral walkable
    /// recovery waypoint. Does not teleport (followers keep distance teleport separately).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcNavMeshPositionClampSystem))]
    [UpdateBefore(typeof(NpcGroundSnapSystem))]
    public partial struct NpcStuckRecoverySystem : ISystem
    {
        const float StuckTimeSeconds = 0.75f;
        const float ProgressDistance = 0.35f;
        const float GoalFarDistance = 1.25f;
        const byte StuckRepathsBeforeLateral = 2;
        const float LateralMinRadius = 1.25f;
        const float LateralMaxRadius = 4.5f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NpcMovementTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var navQuery = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.TempJob, 64);

            var workHandle = new StuckRecoveryJob
            {
                DeltaTime = dt,
                NavQuery = navQuery
            }.Schedule(state.Dependency);

            workHandle.Complete();
            navQuery.Dispose();
            state.Dependency = workHandle;
        }

        [BurstCompile]
        [WithAll(typeof(NpcMovementTag))]
        [WithNone(typeof(NpcDeadTag))]
        partial struct StuckRecoveryJob : IJobEntity
        {
            public float DeltaTime;
            public NavMeshQuery NavQuery;

            public void Execute(
                in LocalTransform tf,
                in NpcMovementConfig cfg,
                in NpcSeekOverride seek,
                in NpcAnchorTarget anchor,
                in NpcMovementState move,
                ref NpcPathState path,
                DynamicBuffer<NpcPathCorner> corners)
            {
                if (cfg.UseNavMeshWhenAvailable == 0)
                    return;

                if (move.RangedMovementLock != 0 || move.MeleeEngageMovementLock != 0)
                {
                    ResetProgress(ref path, tf.Position);
                    return;
                }

                if (!TryResolveGoal(in seek, in anchor, tf.Position, out float3 goal))
                {
                    ResetProgress(ref path, tf.Position);
                    return;
                }

                float3 self = tf.Position;
                float3 toGoal = goal - self;
                toGoal.y = 0f;
                float goalDistSq = math.lengthsq(toGoal);
                float far = GoalFarDistance;
                if (goalDistSq <= far * far)
                {
                    ResetProgress(ref path, self);
                    path.ConsecutiveStuckRepaths = 0;
                    path.HasRecoveryWaypoint = 0;
                    return;
                }

                if (path.ProgressInitialized == 0)
                {
                    path.LastProgressPosition = self;
                    path.StuckTimer = 0f;
                    path.ProgressInitialized = 1;
                    return;
                }

                float3 delta = self - path.LastProgressPosition;
                delta.y = 0f;
                float progressSq = ProgressDistance * ProgressDistance;
                if (math.lengthsq(delta) >= progressSq)
                {
                    path.LastProgressPosition = self;
                    path.StuckTimer = 0f;
                    path.ConsecutiveStuckRepaths = 0;
                    return;
                }

                path.StuckTimer += DeltaTime;
                if (path.StuckTimer < StuckTimeSeconds)
                    return;

                path.StuckTimer = 0f;
                path.LastProgressPosition = self;
                path.PathValid = 0;
                path.CurrentCorner = 0;
                corners.Clear();
                path.ConsecutiveStuckRepaths =
                    (byte)math.min(255, path.ConsecutiveStuckRepaths + 1);

                if (path.ConsecutiveStuckRepaths < StuckRepathsBeforeLateral)
                    return;

                float sample = math.max(cfg.NavMeshSampleMaxDistance, 3f);
                if (NpcNavMeshSampling.TrySampleWalkableNearby(
                        NavQuery, self, goal, sample, LateralMinRadius, LateralMaxRadius, out float3 waypoint))
                {
                    path.HasRecoveryWaypoint = 1;
                    path.RecoveryWaypoint = waypoint;
                    // Soft nudge so the next probe/steer can start sliding immediately.
                    corners.Add(new NpcPathCorner { Value = waypoint });
                    path.PathValid = 1;
                    path.CurrentCorner = 0;
                    path.LastPathGoal = waypoint;
                }
            }

            static void ResetProgress(ref NpcPathState path, float3 self)
            {
                path.LastProgressPosition = self;
                path.StuckTimer = 0f;
                path.ProgressInitialized = 1;
            }

            static bool TryResolveGoal(in NpcSeekOverride seek, in NpcAnchorTarget anchor, float3 self,
                out float3 goal)
            {
                if (seek.HasOverride != 0)
                {
                    goal = seek.Position;
                    return true;
                }

                if (anchor.HasAnchor != 0)
                {
                    goal = anchor.Position;
                    return true;
                }

                goal = self;
                return false;
            }
        }
    }
}
