using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.AI;

namespace Medieval.NpcMovement
{
    /// <summary>Shared NavMeshQuery MapLocation / extent rules for NPC movement systems.</summary>
    [BurstCompile]
    internal static class NpcNavMeshSampling
    {
        public static Vector3 SampleExtents(float navMeshSampleMaxDistance)
        {
            float halfExtent = math.max(1e-2f, navMeshSampleMaxDistance);
            return new Vector3(halfExtent, halfExtent, halfExtent);
        }

        public static NavMeshLocation MapLocation(NavMeshQuery query, float3 worldPos, float navMeshSampleMaxDistance)
        {
            return query.MapLocation(ToVector3(worldPos), SampleExtents(navMeshSampleMaxDistance), 0);
        }

        public static bool TryMapStartLocation(NavMeshQuery query, float3 worldPos, float navMeshSampleMaxDistance,
            out NavMeshLocation location)
        {
            location = MapLocation(query, worldPos, navMeshSampleMaxDistance);
            return query.IsValid(location);
        }

        /// <summary>If the goal maps to the navmesh, returns the snapped position; otherwise returns <paramref name="goal"/>.</summary>
        public static float3 SnapGoalToNavMeshOrRaw(NavMeshQuery query, float3 goal, float navMeshSampleMaxDistance)
        {
            var goalLoc = MapLocation(query, goal, navMeshSampleMaxDistance);
            if (query.IsValid(goalLoc))
            {
                Vector3 gp = goalLoc.position;
                return new float3(gp.x, gp.y, gp.z);
            }

            return goal;
        }

        public static Vector3 ToVector3(float3 p) => new Vector3(p.x, p.y, p.z);
    }
}
