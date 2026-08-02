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
    /// Per-NPC NavMesh polygon pathfinding via <see cref="NavMeshQuery"/>. Repaths on interval, goal shift,
    /// invalid path, or stuck recovery. Writes a multi-corner <see cref="NpcPathCorner"/> buffer for steering.
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

            var navQuery = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.TempJob,
                NpcNavMeshPath.DefaultNodePoolSize);
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
                    ClearPath(ref pathStateRW, corners);
                    return;
                }

                if (!TryResolveGoal(stateRW, cfg, seek, anchor, transformRO.Position, ElapsedTime, out float3 goal))
                {
                    ClearPath(ref pathStateRW, corners);
                    return;
                }

                bool useRecovery = pathStateRW.HasRecoveryWaypoint != 0;
                if (useRecovery)
                    goal = pathStateRW.RecoveryWaypoint;

                float goalShiftSq = math.lengthsq(goal - pathStateRW.LastPathGoal);
                bool timeElapsed = (ElapsedTime - pathStateRW.LastPathTime) >= cfg.RepathInterval;
                bool goalShifted = goalShiftSq > cfg.RepathGoalShiftSqr;
                bool noPath = pathStateRW.PathValid == 0;
                if (!(timeElapsed || goalShifted || noPath || useRecovery))
                    return;

                float3 origin = transformRO.Position;
                if (!math.all(math.isfinite(origin)) || !math.all(math.isfinite(goal)))
                {
                    ClearPath(ref pathStateRW, corners);
                    return;
                }

                if (!NpcNavMeshSampling.TryMapStartLocation(NavQuery, origin, cfg.NavMeshSampleMaxDistance,
                        out var startLoc))
                {
                    ClearPath(ref pathStateRW, corners);
                    return;
                }

                if (!NpcNavMeshSampling.TrySnapGoalToWalkable(NavQuery, goal, cfg.NavMeshSampleMaxDistance,
                        out float3 endPoint, out var endLoc))
                {
                    ClearPath(ref pathStateRW, corners);
                    return;
                }

                // Same poly / already there: trivial path.
                float3 toEnd = endPoint - origin;
                toEnd.y = 0f;
                if (math.lengthsq(toEnd) <= cfg.ArriveThreshold * cfg.ArriveThreshold)
                {
                    corners.Clear();
                    corners.Add(new NpcPathCorner { Value = endPoint });
                    pathStateRW.PathValid = 1;
                    pathStateRW.CurrentCorner = 0;
                    pathStateRW.LastPathTime = ElapsedTime;
                    pathStateRW.LastPathGoal = goal;
                    if (useRecovery)
                        pathStateRW.HasRecoveryWaypoint = 0;
                    return;
                }

                var tempCorners = new NativeList<float3>(NpcNavMeshPath.MaxCorners, Allocator.Temp);
                bool ok = NpcNavMeshPath.TryFindCorners(
                    NavQuery, startLoc, endLoc, origin, endPoint, AreaCosts, tempCorners, NpcNavMeshPath.MaxCorners);

                if (!ok)
                {
                    tempCorners.Dispose();
                    ClearPath(ref pathStateRW, corners);
                    return;
                }

                corners.Clear();
                for (int i = 0; i < tempCorners.Length; i++)
                    corners.Add(new NpcPathCorner { Value = tempCorners[i] });
                tempCorners.Dispose();

                pathStateRW.PathValid = 1;
                pathStateRW.CurrentCorner = 0;
                pathStateRW.LastPathTime = ElapsedTime;
                pathStateRW.LastPathGoal = goal;
                if (useRecovery)
                    pathStateRW.HasRecoveryWaypoint = 0;
            }

            static void ClearPath(ref NpcPathState pathState, DynamicBuffer<NpcPathCorner> corners)
            {
                corners.Clear();
                pathState.PathValid = 0;
                pathState.CurrentCorner = 0;
            }

            static bool TryResolveGoal(in NpcMovementState state, in NpcMovementConfig cfg, in NpcSeekOverride seek,
                in NpcAnchorTarget anchor, in float3 selfPos, float elapsedTime, out float3 goal)
            {
                if (seek.HasOverride != 0)
                {
                    if (seek.SeekHoldDistance > 0f)
                    {
                        float3 toEnemy = seek.Position - selfPos;
                        toEnemy.y = 0f;
                        float distSq = math.lengthsq(toEnemy);
                        float hold = seek.SeekHoldDistance;
                        if (distSq <= hold * hold)
                        {
                            goal = selfPos;
                            return true;
                        }

                        float dist = math.sqrt(distSq);
                        goal = seek.Position - (toEnemy / dist) * hold;
                        return true;
                    }

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
}
