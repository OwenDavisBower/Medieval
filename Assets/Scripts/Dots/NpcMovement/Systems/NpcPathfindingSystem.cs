using Unity.Collections;
using Unity.Entities;
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

            var query = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.TempJob, 128);
            var areaCosts = new NativeArray<float>(0, Allocator.TempJob);

            state.Dependency.Complete();

            foreach (var (transformRO, cfg, anchor, seek, stateRW, pathStateRW, corners) in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRO<NpcMovementConfig>, RefRO<NpcAnchorTarget>,
                                RefRO<NpcSeekOverride>, RefRW<NpcMovementState>, RefRW<NpcPathState>,
                                DynamicBuffer<NpcPathCorner>>()
                         .WithAll<NpcMovementTag>())
            {
                if (cfg.ValueRO.UseNavMeshWhenAvailable == 0)
                {
                    corners.Clear();
                    pathStateRW.ValueRW.PathValid = 0;
                    continue;
                }

                if (!TryResolveGoal(stateRW.ValueRO, cfg.ValueRO, seek.ValueRO, anchor.ValueRO,
                        transformRO.ValueRO.Position, elapsed, out float3 goal))
                {
                    corners.Clear();
                    pathStateRW.ValueRW.PathValid = 0;
                    continue;
                }

                float goalShiftSq = math.lengthsq(goal - pathStateRW.ValueRO.LastPathGoal);
                bool timeElapsed = (elapsed - pathStateRW.ValueRO.LastPathTime) >= cfg.ValueRO.RepathInterval;
                bool goalShifted = goalShiftSq > cfg.ValueRO.RepathGoalShiftSqr;
                bool noPath = pathStateRW.ValueRO.PathValid == 0;
                if (!(timeElapsed || goalShifted || noPath))
                    continue;

                float3 origin = transformRO.ValueRO.Position;
                var extents = new UnityEngine.Vector3(
                    cfg.ValueRO.NavMeshSampleMaxDistance,
                    cfg.ValueRO.NavMeshSampleMaxDistance,
                    cfg.ValueRO.NavMeshSampleMaxDistance);
                var startLoc = query.MapLocation(new UnityEngine.Vector3(origin.x, origin.y, origin.z),
                    extents, 0);
                if (!query.IsValid(startLoc))
                {
                    corners.Clear();
                    corners.Add(new NpcPathCorner { Value = goal });
                    pathStateRW.ValueRW.PathValid = 1;
                    pathStateRW.ValueRW.CurrentCorner = 0;
                    pathStateRW.ValueRW.LastPathTime = elapsed;
                    pathStateRW.ValueRW.LastPathGoal = goal;
                    continue;
                }

                var goalLoc = query.MapLocation(new UnityEngine.Vector3(goal.x, goal.y, goal.z), extents, 0);
                float3 endPoint;
                if (query.IsValid(goalLoc))
                {
                    UnityEngine.Vector3 gp = goalLoc.position;
                    endPoint = new float3(gp.x, gp.y, gp.z);
                }
                else
                {
                    endPoint = goal;
                }

                const int allAreas = -1;
                var raycastStatus = query.Raycast(out NavMeshHit hit, startLoc,
                    new UnityEngine.Vector3(endPoint.x, endPoint.y, endPoint.z), allAreas, areaCosts);

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
                            float backoff = math.min(0.25f, cfg.ValueRO.MinCornerAdvanceDistance);
                            float hitDist = math.max(0f, hit.distance - backoff);
                            corner = origin + unit * hitDist;
                        }
                    }
                    corners.Add(new NpcPathCorner { Value = corner });
                    pathStateRW.ValueRW.PathValid = 1;
                }
                else
                {
                    corners.Add(new NpcPathCorner { Value = goal });
                    pathStateRW.ValueRW.PathValid = 1;
                }

                pathStateRW.ValueRW.CurrentCorner = 0;
                pathStateRW.ValueRW.LastPathTime = elapsed;
                pathStateRW.ValueRW.LastPathGoal = goal;
            }

            areaCosts.Dispose();
            query.Dispose();
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
