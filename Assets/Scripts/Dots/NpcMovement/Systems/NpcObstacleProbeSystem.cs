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
    /// Short-range fan of <see cref="NavMeshQuery.Raycast"/> probes along each NPC's intended travel
    /// direction (path corner / seek / velocity). Hits produce a tangent deflection in
    /// <see cref="NpcMovementState.ObstacleDeflectDir"/> for steering. Sequential job for a shared query.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcPathfindingSystem))]
    public partial struct NpcObstacleProbeSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NpcMovementTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var navQuery = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.TempJob, 64);
            var areaCosts = new NativeArray<float>(0, Allocator.TempJob);

            var workHandle = new ObstacleProbeJob
            {
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
        partial struct ObstacleProbeJob : IJobEntity
        {
            public NavMeshQuery NavQuery;
            public NativeArray<float> AreaCosts;

            public void Execute(
                in LocalTransform transformRO,
                in NpcMovementConfig cfg,
                in NpcSeekOverride seek,
                in NpcAnchorTarget anchor,
                in NpcPathState pathState,
                DynamicBuffer<NpcPathCorner> corners,
                ref NpcMovementState stateRW)
            {
                stateRW.ObstacleDeflectDir = float3.zero;

                if (cfg.UseNavMeshWhenAvailable == 0 || cfg.ObstacleProbeDistance <= 0f)
                    return;

                float3 origin = transformRO.Position;
                if (!math.all(math.isfinite(origin)))
                    return;

                if (!TryResolveIntent(origin, in cfg, in seek, in anchor, in pathState, corners, in stateRW,
                        out float3 dir))
                    return;

                if (!NpcNavMeshSampling.TryMapStartLocation(NavQuery, origin, cfg.NavMeshSampleMaxDistance,
                        out var startLoc))
                    return;

                float probeDist = cfg.ObstacleProbeDistance;
                float radius = math.max(0f, cfg.ObstacleProbeRadius);
                float3 right = math.normalizesafe(math.cross(new float3(0f, 1f, 0f), dir), new float3(1f, 0f, 0f));

                // Center + optional left/right fan using baked agent width.
                bool hitAny = false;
                float3 bestTangent = float3.zero;
                float bestDist = probeDist;

                TryProbeRay(origin, dir, 0f, right, radius, probeDist, startLoc, ref hitAny, ref bestTangent,
                    ref bestDist);
                if (radius > 1e-4f)
                {
                    TryProbeRay(origin, dir, -1f, right, radius, probeDist, startLoc, ref hitAny, ref bestTangent,
                        ref bestDist);
                    TryProbeRay(origin, dir, 1f, right, radius, probeDist, startLoc, ref hitAny, ref bestTangent,
                        ref bestDist);
                }

                if (hitAny)
                    stateRW.ObstacleDeflectDir = bestTangent;
            }

            void TryProbeRay(
                float3 origin,
                float3 dir,
                float sideSign,
                float3 right,
                float radius,
                float probeDist,
                NavMeshLocation startLoc,
                ref bool hitAny,
                ref float3 bestTangent,
                ref float bestDist)
            {
                float3 rayOrigin = origin + right * (sideSign * radius);
                // Remap from offset origin when fan rays start off the mapped center.
                NavMeshLocation loc = startLoc;
                if (sideSign != 0f)
                {
                    if (!NpcNavMeshSampling.TryMapStartLocation(NavQuery, rayOrigin, math.max(radius, 0.5f),
                            out loc))
                        return;
                }

                float3 endPoint = rayOrigin + dir * probeDist;
                const int allAreas = -1;
                var status = NavQuery.Raycast(out NavMeshHit hit, loc,
                    NpcNavMeshSampling.ToVector3(endPoint), allAreas, AreaCosts);
                if ((status & PathQueryStatus.Success) == 0)
                    return;
                if (hit.distance < 0f || hit.distance >= probeDist - 1e-4f)
                    return;

                float3 normal = new float3(hit.normal.x, 0f, hit.normal.z);
                if (math.lengthsq(normal) < 1e-6f)
                    return;
                normal = math.normalize(normal);
                float3 tangent = math.cross(new float3(0f, 1f, 0f), normal);
                if (math.lengthsq(tangent) < 1e-6f)
                    return;
                tangent = math.normalize(tangent);
                if (math.dot(tangent, dir) < 0f)
                    tangent = -tangent;

                if (!hitAny || hit.distance < bestDist)
                {
                    hitAny = true;
                    bestDist = hit.distance;
                    bestTangent = tangent;
                }
            }

            static bool TryResolveIntent(
                float3 origin,
                in NpcMovementConfig cfg,
                in NpcSeekOverride seek,
                in NpcAnchorTarget anchor,
                in NpcPathState pathState,
                DynamicBuffer<NpcPathCorner> corners,
                in NpcMovementState state,
                out float3 dir)
            {
                dir = float3.zero;

                // Prefer path corner, then seek/anchor goal, then current velocity.
                if (pathState.PathValid != 0 && corners.Length > 0)
                {
                    int corner = math.clamp(pathState.CurrentCorner, 0, corners.Length - 1);
                    float3 c = corners[corner].Value;
                    float3 toCorner = c - origin;
                    toCorner.y = 0f;
                    if (math.lengthsq(toCorner) > 1e-4f)
                    {
                        dir = math.normalize(toCorner);
                        return true;
                    }
                }

                float3 goal = origin;
                bool hasGoal = false;
                if (seek.HasOverride != 0)
                {
                    goal = seek.Position;
                    hasGoal = true;
                }
                else if (anchor.HasAnchor != 0)
                {
                    goal = anchor.Position;
                    hasGoal = true;
                }

                if (hasGoal)
                {
                    float3 toGoal = goal - origin;
                    toGoal.y = 0f;
                    if (math.lengthsq(toGoal) > cfg.ArriveThreshold * cfg.ArriveThreshold)
                    {
                        dir = math.normalize(toGoal);
                        return true;
                    }
                }

                float3 hvel = state.CurrentHorizontalVelocity;
                hvel.y = 0f;
                if (math.lengthsq(hvel) >= 0.01f)
                {
                    dir = math.normalize(hvel);
                    return true;
                }

                return false;
            }
        }
    }
}
