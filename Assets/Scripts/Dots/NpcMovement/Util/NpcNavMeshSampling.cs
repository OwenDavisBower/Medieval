using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.AI;

// Experimental.AI NavMeshQuery is obsolete without replacement on Unity 6000.4; still the job-safe API.
#pragma warning disable CS0618

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

        /// <summary>
        /// Maps an agent position to the navmesh. Tries a tight vertical extent first so a rooftop / tower
        /// platform is not snapped down to ground under the same horizontal box (which breaks island raycasts).
        /// Falls back to full <see cref="SampleExtents"/> for ramps and rough spawn alignment.
        /// </summary>
        public static bool TryMapStartLocation(NavMeshQuery query, float3 worldPos, float navMeshSampleMaxDistance,
            out NavMeshLocation location)
        {
            float halfXZ = math.max(1e-2f, navMeshSampleMaxDistance);
            float halfY = math.min(0.75f, halfXZ * 0.35f);
            var tightExtents = new Vector3(halfXZ, halfY, halfXZ);
            location = query.MapLocation(ToVector3(worldPos), tightExtents, 0);
            if (query.IsValid(location))
                return true;

            location = query.MapLocation(ToVector3(worldPos), SampleExtents(navMeshSampleMaxDistance), 0);
            return query.IsValid(location);
        }

        /// <summary>If the goal maps to the navmesh, returns the snapped position; otherwise returns <paramref name="goal"/>.</summary>
        public static float3 SnapGoalToNavMeshOrRaw(NavMeshQuery query, float3 goal, float navMeshSampleMaxDistance)
        {
            if (TrySnapGoalToWalkable(query, goal, navMeshSampleMaxDistance, out float3 snapped, out _))
                return snapped;
            return goal;
        }

        /// <summary>
        /// Maps <paramref name="goal"/> onto the navmesh, then ring-samples outward if the direct map fails
        /// (tree carve holes, building interiors, etc.).
        /// </summary>
        public static bool TrySnapGoalToWalkable(
            NavMeshQuery query,
            float3 goal,
            float navMeshSampleMaxDistance,
            out float3 snapped,
            out NavMeshLocation location)
        {
            snapped = goal;
            location = MapLocation(query, goal, navMeshSampleMaxDistance);
            if (query.IsValid(location))
            {
                Vector3 gp = location.position;
                snapped = new float3(gp.x, gp.y, gp.z);
                return true;
            }

            // Broader sample before ring search.
            float expanded = math.max(navMeshSampleMaxDistance * 2.5f, 6f);
            location = MapLocation(query, goal, expanded);
            if (query.IsValid(location))
            {
                Vector3 gp = location.position;
                snapped = new float3(gp.x, gp.y, gp.z);
                return true;
            }

            const int rings = 4;
            const int spokes = 8;
            float maxRing = math.max(expanded, 8f);
            for (int r = 1; r <= rings; r++)
            {
                float radius = maxRing * (r / (float)rings);
                for (int s = 0; s < spokes; s++)
                {
                    float ang = (s / (float)spokes) * math.PI * 2f;
                    float3 sample = goal + new float3(math.cos(ang) * radius, 0f, math.sin(ang) * radius);
                    location = MapLocation(query, sample, navMeshSampleMaxDistance);
                    if (!query.IsValid(location))
                        continue;
                    Vector3 gp = location.position;
                    snapped = new float3(gp.x, gp.y, gp.z);
                    return true;
                }
            }

            location = default;
            return false;
        }

        /// <summary>
        /// Samples walkable points around <paramref name="origin"/> (rings + spokes), preferring points that
        /// make progress toward <paramref name="towardGoal"/> when provided.
        /// </summary>
        public static bool TrySampleWalkableNearby(
            NavMeshQuery query,
            float3 origin,
            float3 towardGoal,
            float navMeshSampleMaxDistance,
            float minRadius,
            float maxRadius,
            out float3 walkable)
        {
            walkable = origin;
            float3 prefer = towardGoal - origin;
            prefer.y = 0f;
            bool hasPrefer = math.lengthsq(prefer) > 1e-4f;
            if (hasPrefer)
                prefer = math.normalize(prefer);

            float bestScore = float.MinValue;
            bool found = false;
            const int rings = 3;
            const int spokes = 8;
            minRadius = math.max(0.25f, minRadius);
            maxRadius = math.max(minRadius + 0.25f, maxRadius);

            for (int r = 0; r < rings; r++)
            {
                float t = (r + 1) / (float)rings;
                float radius = math.lerp(minRadius, maxRadius, t);
                for (int s = 0; s < spokes; s++)
                {
                    float ang = (s / (float)spokes) * math.PI * 2f + r * 0.35f;
                    float3 dir = new float3(math.cos(ang), 0f, math.sin(ang));
                    float3 sample = origin + dir * radius;
                    var loc = MapLocation(query, sample, navMeshSampleMaxDistance);
                    if (!query.IsValid(loc))
                        continue;

                    Vector3 gp = loc.position;
                    float3 p = new float3(gp.x, gp.y, gp.z);
                    float score = hasPrefer ? math.dot(dir, prefer) : 0f;
                    // Slight preference for farther samples so we leave the stuck rim.
                    score += t * 0.15f;
                    if (!found || score > bestScore)
                    {
                        bestScore = score;
                        walkable = p;
                        found = true;
                    }
                }
            }

            return found;
        }

        public static Vector3 ToVector3(float3 p) => new Vector3(p.x, p.y, p.z);
    }
}
