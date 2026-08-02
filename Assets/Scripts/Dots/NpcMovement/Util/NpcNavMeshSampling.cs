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
        /// <summary>Matches Project Settings &gt; AI Navigation Areas index for "Water".</summary>
        public const int WaterAreaIndex = 3;

        /// <summary>Refuse snaps that drop more than this (bridge deck → river underfoot).</summary>
        public const float MaxVerticalDrop = 0.85f;

        /// <summary>
        /// Bank → riverbed wading may drop farther than <see cref="MaxVerticalDrop"/>; must cover
        /// <c>TerrainGenerator</c> riverBedDepth (~6) plus a little margin.
        /// </summary>
        public const float MaxWadeVerticalDrop = 7f;

        /// <summary>Allow climbing onto nearby mesh (spawns, ramps, bank→deck).</summary>
        public const float MaxVerticalClimb = 2.25f;

        const int AreaMaskAll = -1;
        const int AreaMaskExcludeWater = ~(1 << WaterAreaIndex);
        const int AreaMaskWaterOnly = 1 << WaterAreaIndex;

        public static Vector3 SampleExtents(float navMeshSampleMaxDistance)
        {
            float halfExtent = math.max(1e-2f, navMeshSampleMaxDistance);
            return new Vector3(halfExtent, halfExtent, halfExtent);
        }

        public static Vector3 SampleExtentsXZ(float halfXZ, float halfY)
        {
            halfXZ = math.max(1e-2f, halfXZ);
            halfY = math.max(1e-2f, halfY);
            return new Vector3(halfXZ, halfY, halfXZ);
        }

        public static NavMeshLocation MapLocation(NavMeshQuery query, float3 worldPos, float navMeshSampleMaxDistance)
        {
            return MapLocationMasked(query, worldPos, SampleExtents(navMeshSampleMaxDistance),
                AreaMaskForHeight(worldPos.y));
        }

        /// <summary>
        /// Maps an agent position to the navmesh. Prefers the surface near the query Y (bridge / rooftop)
        /// and, when above water, excludes Water polys so river mesh under a bridge cannot win.
        /// Wide XZ with tight Y is tried before expanding Y.
        /// </summary>
        public static bool TryMapStartLocation(NavMeshQuery query, float3 worldPos, float navMeshSampleMaxDistance,
            out NavMeshLocation location)
        {
            return TryMapNearHeight(query, worldPos, navMeshSampleMaxDistance, MaxVerticalDrop, MaxVerticalClimb,
                out location);
        }

        /// <summary>
        /// Like <see cref="TryMapStartLocation"/> with explicit vertical drop/climb limits (clamp uses a
        /// tighter drop so bridge traffic cannot fall onto river polys).
        /// </summary>
        public static bool TryMapNearHeight(
            NavMeshQuery query,
            float3 worldPos,
            float navMeshSampleMaxDistance,
            float maxVerticalDrop,
            float maxVerticalClimb,
            out NavMeshLocation location)
        {
            return TryMapNearHeight(query, worldPos, navMeshSampleMaxDistance, maxVerticalDrop, maxVerticalClimb,
                preferWade: false, out location);
        }

        /// <param name="preferWade">
        /// When true (path corner is in/below water), try Water before a wide land search so the clamp
        /// does not pull agents back onto the bank while fording. When false, wide Walkable is preferred
        /// so bridge traffic is not dropped onto the river.
        /// </param>
        public static bool TryMapNearHeight(
            NavMeshQuery query,
            float3 worldPos,
            float navMeshSampleMaxDistance,
            float maxVerticalDrop,
            float maxVerticalClimb,
            bool preferWade,
            out NavMeshLocation location)
        {
            int mask = AreaMaskForHeight(worldPos.y);
            float halfXZ = math.max(1e-2f, navMeshSampleMaxDistance);
            float tightY = math.min(0.75f, halfXZ * 0.35f);
            float wideXZ = math.max(halfXZ * 2.5f, 6f);

            // Fording: try local Water first so clamp does not keep snapping XZ back onto the bank poly.
            // Tight extents only — wide search here would yank agents sideways into the river from the bank.
            if (preferWade && mask != AreaMaskAll &&
                TryMapWaterWadeLocal(query, worldPos, halfXZ, tightY, maxVerticalClimb, out location))
                return true;

            // Local elevated / dry surface (bridge deck, bank underfoot).
            if (TryMapAccept(query, worldPos, SampleExtentsXZ(halfXZ, tightY), mask, maxVerticalDrop,
                    maxVerticalClimb, out location))
                return true;

            if (TryMapAccept(query, worldPos, SampleExtents(halfXZ), mask, maxVerticalDrop, maxVerticalClimb,
                    out location))
                return true;

            // Wider XZ — brief off-edge dips on bridges / platforms without dropping onto river mesh.
            if (TryMapAccept(query, worldPos, SampleExtentsXZ(wideXZ, tightY), mask, maxVerticalDrop,
                    maxVerticalClimb, out location))
                return true;

            if (TryMapAccept(query, worldPos, SampleExtents(wideXZ), mask, maxVerticalDrop, maxVerticalClimb,
                    out location))
                return true;

            // No elevated hit: Water-only fallback (wading / fording without a bridge).
            if (mask != AreaMaskAll &&
                TryMapWaterWade(query, worldPos, halfXZ, wideXZ, tightY, maxVerticalClimb, out location))
                return true;

            location = default;
            return false;
        }

        static bool TryMapWaterWadeLocal(
            NavMeshQuery query,
            float3 worldPos,
            float halfXZ,
            float tightY,
            float maxVerticalClimb,
            out NavMeshLocation location)
        {
            float wadeClimb = math.max(maxVerticalClimb, MaxWadeVerticalDrop);
            if (TryMapAccept(query, worldPos, SampleExtentsXZ(halfXZ, tightY), AreaMaskWaterOnly,
                    MaxWadeVerticalDrop, wadeClimb, out location))
                return true;
            return TryMapAccept(query, worldPos, SampleExtents(halfXZ), AreaMaskWaterOnly,
                MaxWadeVerticalDrop, wadeClimb, out location);
        }

        static bool TryMapWaterWade(
            NavMeshQuery query,
            float3 worldPos,
            float halfXZ,
            float wideXZ,
            float tightY,
            float maxVerticalClimb,
            out NavMeshLocation location)
        {
            if (TryMapWaterWadeLocal(query, worldPos, halfXZ, tightY, maxVerticalClimb, out location))
                return true;

            float wadeClimb = math.max(maxVerticalClimb, MaxWadeVerticalDrop);
            if (TryMapAccept(query, worldPos, SampleExtentsXZ(wideXZ, tightY), AreaMaskWaterOnly,
                    MaxWadeVerticalDrop, wadeClimb, out location))
                return true;
            return TryMapAccept(query, worldPos, SampleExtents(wideXZ), AreaMaskWaterOnly,
                MaxWadeVerticalDrop, wadeClimb, out location);
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
        /// (tree carve holes, building interiors, etc.). Prefers hits near the goal's Y (bridges over water).
        /// </summary>
        public static bool TrySnapGoalToWalkable(
            NavMeshQuery query,
            float3 goal,
            float navMeshSampleMaxDistance,
            out float3 snapped,
            out NavMeshLocation location)
        {
            snapped = goal;
            if (TryMapNearHeight(query, goal, navMeshSampleMaxDistance, MaxVerticalDrop * 2f, MaxVerticalClimb,
                    out location))
            {
                Vector3 gp = location.position;
                snapped = new float3(gp.x, gp.y, gp.z);
                return true;
            }

            const int rings = 4;
            const int spokes = 8;
            float maxRing = math.max(navMeshSampleMaxDistance * 2.5f, 8f);
            float bestYDelta = float.MaxValue;
            bool found = false;
            NavMeshLocation bestLoc = default;
            float3 bestPos = goal;

            for (int r = 1; r <= rings; r++)
            {
                float radius = maxRing * (r / (float)rings);
                for (int s = 0; s < spokes; s++)
                {
                    float ang = (s / (float)spokes) * math.PI * 2f;
                    float3 sample = goal + new float3(math.cos(ang) * radius, 0f, math.sin(ang) * radius);
                    if (!TryMapNearHeight(query, sample, navMeshSampleMaxDistance, MaxVerticalDrop * 2f,
                            MaxVerticalClimb, out var loc))
                        continue;

                    Vector3 gp = loc.position;
                    float3 p = new float3(gp.x, gp.y, gp.z);
                    float yDelta = math.abs(p.y - goal.y);
                    // Prefer closer rings; within a ring prefer matching height (bridge deck vs river).
                    float score = yDelta + r * 0.35f;
                    if (!found || score < bestYDelta)
                    {
                        bestYDelta = score;
                        bestLoc = loc;
                        bestPos = p;
                        found = true;
                    }
                }

                if (found && bestYDelta < MaxVerticalDrop)
                    break;
            }

            if (!found)
            {
                location = default;
                return false;
            }

            location = bestLoc;
            snapped = bestPos;
            return true;
        }

        /// <summary>
        /// Samples walkable points around <paramref name="origin"/> (rings + spokes), preferring points that
        /// make progress toward <paramref name="towardGoal"/> when provided, and staying near origin Y.
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
                    if (!TryMapNearHeight(query, sample, navMeshSampleMaxDistance, MaxVerticalDrop, MaxVerticalClimb,
                            out var loc))
                        continue;

                    Vector3 gp = loc.position;
                    float3 p = new float3(gp.x, gp.y, gp.z);
                    float score = hasPrefer ? math.dot(dir, prefer) : 0f;
                    // Prefer farther samples to leave a stuck rim, but stay on the same elevation band.
                    score += t * 0.15f;
                    score -= math.abs(p.y - origin.y) * 0.85f;
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

        static int AreaMaskForHeight(float worldY) =>
            worldY >= NpcMath.WaterSurfaceY ? AreaMaskExcludeWater : AreaMaskAll;

        static bool TryMapAccept(
            NavMeshQuery query,
            float3 worldPos,
            Vector3 extents,
            int areaMask,
            float maxVerticalDrop,
            float maxVerticalClimb,
            out NavMeshLocation location)
        {
            location = MapLocationMasked(query, worldPos, extents, areaMask);
            if (!query.IsValid(location))
                return false;
            float dy = location.position.y - worldPos.y;
            if (dy < -maxVerticalDrop || dy > maxVerticalClimb)
                return false;
            return true;
        }

        static NavMeshLocation MapLocationMasked(
            NavMeshQuery query, float3 worldPos, Vector3 extents, int areaMask)
        {
            return query.MapLocation(ToVector3(worldPos), extents, 0, areaMask);
        }
    }
}
