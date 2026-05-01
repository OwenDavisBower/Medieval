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
    /// Short-range <see cref="NavMeshQuery.Raycast"/> probe along each NPC's current horizontal velocity
    /// direction. When a raycast hits a navmesh edge within <c>ObstacleProbeDistance</c>, a tangent
    /// deflection direction (perpendicular to the hit normal, biased toward the current travel direction)
    /// is stored in <see cref="NpcMovementState.ObstacleDeflectDir"/>; the steering system blends it into
    /// the desired direction. Uses sequential <see cref="IJobEntity"/> for a single shared
    /// <see cref="NavMeshQuery"/>.
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
            var navQuery = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.TempJob, 32);
            // Empty: use NavMesh area costs from project settings (see NavMeshQuery.Raycast costs param).
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

            public void Execute(in LocalTransform transformRO, in NpcMovementConfig cfg, ref NpcMovementState stateRW)
            {
                stateRW.ObstacleDeflectDir = float3.zero;

                float3 hvel = stateRW.CurrentHorizontalVelocity;
                hvel.y = 0f;
                float speedSq = math.lengthsq(hvel);
                if (speedSq < 0.01f || cfg.UseNavMeshWhenAvailable == 0 || cfg.ObstacleProbeDistance <= 0f)
                    return;

                float3 dir = math.normalize(hvel);
                float3 origin = transformRO.Position;
                if (!math.all(math.isfinite(origin)))
                    return;

                if (!NpcNavMeshSampling.TryMapStartLocation(NavQuery, origin, cfg.NavMeshSampleMaxDistance,
                        out var startLoc))
                    return;

                float3 endPoint = origin + dir * cfg.ObstacleProbeDistance;
                const int allAreas = -1;
                var status = NavQuery.Raycast(out NavMeshHit hit, startLoc,
                    NpcNavMeshSampling.ToVector3(endPoint), allAreas, AreaCosts);
                if ((status & PathQueryStatus.Success) == 0)
                    return;
                if (hit.distance >= cfg.ObstacleProbeDistance - 1e-4f)
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

                stateRW.ObstacleDeflectDir = tangent;
            }
        }
    }
}
