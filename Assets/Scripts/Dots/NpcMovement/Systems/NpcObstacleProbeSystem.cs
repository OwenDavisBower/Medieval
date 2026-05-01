using Unity.Collections;
using Unity.Entities;
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
    /// the desired direction.
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
            state.Dependency.Complete();

            var query = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.TempJob, 32);
            // Empty: use NavMesh area costs from project settings (see NavMeshQuery.Raycast costs param).
            var areaCosts = new NativeArray<float>(0, Allocator.TempJob);
            const int allAreas = -1;

            foreach (var (transformRO, cfg, stateRW) in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRO<NpcMovementConfig>, RefRW<NpcMovementState>>()
                         .WithAll<NpcMovementTag>())
            {
                stateRW.ValueRW.ObstacleDeflectDir = float3.zero;

                float3 hvel = stateRW.ValueRO.CurrentHorizontalVelocity;
                hvel.y = 0f;
                float speedSq = math.lengthsq(hvel);
                if (speedSq < 0.01f || cfg.ValueRO.UseNavMeshWhenAvailable == 0 ||
                    cfg.ValueRO.ObstacleProbeDistance <= 0f)
                    continue;

                float3 dir = math.normalize(hvel);
                float3 origin = transformRO.ValueRO.Position;
                if (!math.all(math.isfinite(origin)))
                    continue;

                if (!NpcNavMeshSampling.TryMapStartLocation(query, origin, cfg.ValueRO.NavMeshSampleMaxDistance,
                        out var startLoc))
                    continue;

                float3 endPoint = origin + dir * cfg.ValueRO.ObstacleProbeDistance;
                var status = query.Raycast(out NavMeshHit hit, startLoc,
                    NpcNavMeshSampling.ToVector3(endPoint), allAreas, areaCosts);
                if ((status & PathQueryStatus.Success) == 0)
                    continue;
                if (hit.distance >= cfg.ValueRO.ObstacleProbeDistance - 1e-4f)
                    continue;

                float3 normal = new float3(hit.normal.x, 0f, hit.normal.z);
                if (math.lengthsq(normal) < 1e-6f)
                    continue;
                normal = math.normalize(normal);
                float3 tangent = math.cross(new float3(0f, 1f, 0f), normal);
                if (math.lengthsq(tangent) < 1e-6f)
                    continue;
                tangent = math.normalize(tangent);
                if (math.dot(tangent, dir) < 0f)
                    tangent = -tangent;

                stateRW.ValueRW.ObstacleDeflectDir = tangent;
            }

            areaCosts.Dispose();
            query.Dispose();
        }
    }
}
