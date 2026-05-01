using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.AI;
using UnityEngine.Experimental.AI;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Per-frame pathfinding using <see cref="NavMeshQuery"/>. Each qualifying NPC re-runs a simplified
    /// pathfind toward its current goal (seek override or anchor) every <c>RepathInterval</c> seconds, or
    /// earlier if the goal has moved by more than <c>RepathGoalShiftSqr</c>. Paths are stored as a single
    /// next corner in <see cref="NpcPathCorner"/>; the steering system advances to that corner.
    /// Uses sequential <see cref="IJobEntity"/> so a single <see cref="NavMeshQuery"/> is safe across entities.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcSeparationSystem))]
    public partial struct NpcPathfindingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NpcMovementTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float elapsed = (float)SystemAPI.Time.ElapsedTime;

            var navQuery = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.TempJob, 128);
            var areaCosts = new NativeArray<float>(0, Allocator.TempJob);

            var workHandle = new PathfindingJob
            {
                ElapsedTime = elapsed,
                NavQuery = navQuery,
                AreaCosts = areaCosts
            }.Schedule(state.Dependency);

            workHandle.Complete();
            navQuery.Dispose();
            if (areaCosts.IsCreated)
                areaCosts.Dispose();
            state.Dependency = workHandle;
        }

        [BurstCompile]
        [WithAll(typeof(NpcMovementTag))]
        partial struct PathfindingJob : IJobEntity
        {
            public float ElapsedTime;
            public NavMeshQuery NavQuery;
            public NativeArray<float> AreaCosts;

            public void Execute(
                in LocalTransform transformRO,
                in NpcMovementConfig cfg,
                in NpcAnchorTarget anchor,
                in NpcSeekOverride seek,
                ref NpcMovementState stateRW,
                ref NpcPathState pathStateRW,
                DynamicBuffer<NpcPathCorner> corners)
            {
                if (cfg.UseNavMeshWhenAvailable == 0)
                {
                    corners.Clear();
                    pathStateRW.PathValid = 0;
                    return;
                }

                if (!TryResolveGoal(stateRW, cfg, seek, anchor, transformRO.Position, ElapsedTime, out float3 goal))
                {
                    corners.Clear();
                    pathStateRW.PathValid = 0;
                    return;
                }

                float goalShiftSq = math.lengthsq(goal - pathStateRW.LastPathGoal);
                bool timeElapsed = (ElapsedTime - pathStateRW.LastPathTime) >= cfg.RepathInterval;
                bool goalShifted = goalShiftSq > cfg.RepathGoalShiftSqr;
                bool noPath = pathStateRW.PathValid == 0;
                if (!(timeElapsed || goalShifted || noPath))
                    return;

                float3 origin = transformRO.Position;
                if (!math.all(math.isfinite(origin)) || !math.all(math.isfinite(goal)))
                {
                    corners.Clear();
                    pathStateRW.PathValid = 0;
                    return;
                }

                if (!NpcNavMeshSampling.TryMapStartLocation(NavQuery, origin, cfg.NavMeshSampleMaxDistance,
                        out var startLoc))
                {
                    corners.Clear();
                    corners.Add(new NpcPathCorner { Value = goal });
                    pathStateRW.PathValid = 1;
                    pathStateRW.CurrentCorner = 0;
                    pathStateRW.LastPathTime = ElapsedTime;
                    pathStateRW.LastPathGoal = goal;
                    return;
                }

                float3 endPoint = NpcNavMeshSampling.SnapGoalToNavMeshOrRaw(NavQuery, goal, cfg.NavMeshSampleMaxDistance);

                const int allAreas = -1;
                var raycastStatus = NavQuery.Raycast(out NavMeshHit hit, startLoc,
                    NpcNavMeshSampling.ToVector3(endPoint), allAreas, AreaCosts);

                corners.Clear();
                if ((raycastStatus & PathQueryStatus.Success) != 0)
                {
                    float3 corner;
                    float fullDist = math.distance(origin, endPoint);
                    if (hit.distance < 0f || hit.distance >= fullDist - 1e-4f)
                    {
                        corner = endPoint;
                    }
                    else
                    {
                        float3 diff = endPoint - origin;
                        float len = math.length(diff);
                        if (len < 1e-4f)
                        {
                            corner = endPoint;
                        }
                        else
                        {
                            float3 unit = diff / len;
                            float backoff = math.min(0.25f, cfg.MinCornerAdvanceDistance);
                            float hitDist = math.max(0f, hit.distance - backoff);
                            corner = origin + unit * hitDist;
                        }
                    }
                    corners.Add(new NpcPathCorner { Value = corner });
                    pathStateRW.PathValid = 1;
                }
                else
                {
                    corners.Add(new NpcPathCorner { Value = goal });
                    pathStateRW.PathValid = 1;
                }

                pathStateRW.CurrentCorner = 0;
                pathStateRW.LastPathTime = ElapsedTime;
                pathStateRW.LastPathGoal = goal;
            }
        }

        static bool TryResolveGoal(in NpcMovementState state, in NpcMovementConfig cfg, in NpcSeekOverride seek,
            in NpcAnchorTarget anchor, in float3 selfPos, float elapsedTime, out float3 goal)
        {
            if (seek.HasOverride != 0)
            {
                goal = seek.Position;
                return true;
            }
            if (anchor.HasAnchor == 0)
            {
                goal = selfPos;
                return false;
            }

            switch (state.Mode)
            {
                case NpcMovementMode.Orbit:
                    goal = NpcLoiterKernels.ComputeOrbit(in state, in cfg, in anchor, elapsedTime);
                    return true;
                case NpcMovementMode.WanderAroundTarget:
                    goal = NpcLoiterKernels.ComputeWanderPosition(in state, in cfg, in anchor, elapsedTime);
                    return true;
                default:
                    goal = anchor.Position;
                    return true;
            }
        }
    }
}
