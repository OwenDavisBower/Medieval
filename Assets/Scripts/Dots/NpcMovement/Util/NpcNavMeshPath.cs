using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.AI;

// Experimental.AI NavMeshQuery is obsolete without replacement on Unity 6000.4; still the job-safe API.
#pragma warning disable CS0618

namespace Medieval.NpcMovement
{
    [Flags]
    enum NpcStraightPathFlags : byte
    {
        Start = 0x01,
        End = 0x02,
        OffMeshConnection = 0x04
    }

    /// <summary>
    /// Polygon pathfinding + string-pulling helpers for <see cref="NavMeshQuery"/>.
    /// Funnel algorithm adapted from Mikko Mononen / Unity Experimental.AI samples.
    /// </summary>
    [BurstCompile]
    internal static class NpcNavMeshPath
    {
        public const int DefaultNodePoolSize = 4096;
        public const int MaxPathPolygons = 128;
        public const int MaxCorners = 32;
        public const int MaxFindPathIterations = 4096;

        /// <summary>
        /// Finds a straight-path corner list from <paramref name="startWorld"/> to a mapped end location.
        /// Returns false on failure (caller should leave path invalid).
        /// </summary>
        public static bool TryFindCorners(
            NavMeshQuery query,
            NavMeshLocation startLoc,
            NavMeshLocation endLoc,
            float3 startWorld,
            float3 endWorld,
            NativeArray<float> areaCosts,
            NativeList<float3> corners,
            int maxCorners)
        {
            corners.Clear();
            if (!query.IsValid(startLoc) || !query.IsValid(endLoc) || maxCorners <= 0)
                return false;

            const int allAreas = -1;
            var status = query.BeginFindPath(startLoc, endLoc, allAreas, areaCosts);
            if ((status & PathQueryStatus.Failure) != 0)
                return false;

            int safety = 0;
            while ((status & PathQueryStatus.InProgress) != 0)
            {
                status = query.UpdateFindPath(MaxFindPathIterations, out _);
                if (++safety > 8)
                    break;
            }

            if ((status & PathQueryStatus.Success) == 0)
                return false;

            status = query.EndFindPath(out int polySize);
            if ((status & PathQueryStatus.Success) == 0 || polySize <= 0)
                return false;

            polySize = math.min(polySize, MaxPathPolygons);
            var polygons = new NativeArray<PolygonId>(polySize, Allocator.Temp);
            try
            {
                int copied = query.GetPathResult(polygons);
                if (copied <= 0)
                    return false;

                int cornerCap = math.min(maxCorners, MaxCorners);
                var straightPath = new NativeArray<NavMeshLocation>(cornerCap, Allocator.Temp);
                var straightFlags = new NativeArray<NpcStraightPathFlags>(cornerCap, Allocator.Temp);
                var vertexSide = new NativeArray<float>(cornerCap, Allocator.Temp);
                try
                {
                    int cornerCount = 0;
                    var pathStatus = FindStraightPath(
                        query,
                        NpcNavMeshSampling.ToVector3(startWorld),
                        NpcNavMeshSampling.ToVector3(endWorld),
                        new NativeSlice<PolygonId>(polygons, 0, copied),
                        copied,
                        ref straightPath,
                        ref straightFlags,
                        ref vertexSide,
                        ref cornerCount,
                        cornerCap);

                    if ((pathStatus & PathQueryStatus.Success) == 0 || cornerCount <= 0)
                        return false;

                    // Skip the start corner; steering seeks remaining waypoints + final goal.
                    int startIndex = cornerCount > 1 ? 1 : 0;
                    for (int i = startIndex; i < cornerCount; i++)
                    {
                        Vector3 p = straightPath[i].position;
                        corners.Add(new float3(p.x, p.y, p.z));
                    }

                    return corners.Length > 0;
                }
                finally
                {
                    straightPath.Dispose();
                    straightFlags.Dispose();
                    vertexSide.Dispose();
                }
            }
            finally
            {
                polygons.Dispose();
            }
        }

        static float Perp2D(Vector3 u, Vector3 v) => u.z * v.x - u.x * v.z;

        static void Swap(ref Vector3 a, ref Vector3 b)
        {
            var temp = a;
            a = b;
            b = temp;
        }

        static int RetracePortals(
            NavMeshQuery query,
            int startIndex,
            int endIndex,
            NativeSlice<PolygonId> path,
            int n,
            Vector3 termPos,
            ref NativeArray<NavMeshLocation> straightPath,
            ref NativeArray<NpcStraightPathFlags> straightPathFlags,
            int maxStraightPath)
        {
            for (var k = startIndex; k < endIndex - 1; ++k)
            {
                var type1 = query.GetPolygonType(path[k]);
                var type2 = query.GetPolygonType(path[k + 1]);
                if (type1 != type2)
                {
                    query.GetPortalPoints(path[k], path[k + 1], out Vector3 l, out Vector3 r);
                    SegmentSegmentCpa(out float3 cpa1, out _, l, r, straightPath[n - 1].position, termPos);
                    straightPath[n] = query.CreateLocation(NpcNavMeshSampling.ToVector3(cpa1), path[k + 1]);
                    straightPathFlags[n] = type2 == NavMeshPolyTypes.OffMeshConnection
                        ? NpcStraightPathFlags.OffMeshConnection
                        : 0;
                    if (++n == maxStraightPath)
                        return maxStraightPath;
                }
            }

            straightPath[n] = query.CreateLocation(termPos, path[endIndex]);
            straightPathFlags[n] = query.GetPolygonType(path[endIndex]) == NavMeshPolyTypes.OffMeshConnection
                ? NpcStraightPathFlags.OffMeshConnection
                : 0;
            return ++n;
        }

        static PathQueryStatus FindStraightPath(
            NavMeshQuery query,
            Vector3 startPos,
            Vector3 endPos,
            NativeSlice<PolygonId> path,
            int pathSize,
            ref NativeArray<NavMeshLocation> straightPath,
            ref NativeArray<NpcStraightPathFlags> straightPathFlags,
            ref NativeArray<float> vertexSide,
            ref int straightPathCount,
            int maxStraightPath)
        {
            if (pathSize <= 0 || !query.IsValid(path[0]))
            {
                straightPathCount = 0;
                return PathQueryStatus.Failure;
            }

            straightPath[0] = query.CreateLocation(startPos, path[0]);
            straightPathFlags[0] = NpcStraightPathFlags.Start;

            var apexIndex = 0;
            var n = 1;

            if (pathSize > 1)
            {
                var startPolyWorldToLocal = query.PolygonWorldToLocalMatrix(path[0]);

                var apex = startPolyWorldToLocal.MultiplyPoint(startPos);
                var left = new Vector3(0, 0, 0);
                var right = new Vector3(0, 0, 0);
                var leftIndex = -1;
                var rightIndex = -1;

                for (var i = 1; i <= pathSize; ++i)
                {
                    var polyWorldToLocal = query.PolygonWorldToLocalMatrix(path[apexIndex]);

                    Vector3 vl, vr;
                    if (i == pathSize)
                    {
                        vl = vr = polyWorldToLocal.MultiplyPoint(endPos);
                    }
                    else
                    {
                        if (!query.GetPortalPoints(path[i - 1], path[i], out vl, out vr))
                        {
                            straightPathCount = 0;
                            return PathQueryStatus.Failure;
                        }

                        vl = polyWorldToLocal.MultiplyPoint(vl);
                        vr = polyWorldToLocal.MultiplyPoint(vr);
                    }

                    vl -= apex;
                    vr -= apex;

                    if (Perp2D(vl, vr) < 0)
                        Swap(ref vl, ref vr);

                    if (Perp2D(left, vr) < 0)
                    {
                        var polyLocalToWorld = query.PolygonLocalToWorldMatrix(path[apexIndex]);
                        var termPos = polyLocalToWorld.MultiplyPoint(apex + left);

                        n = RetracePortals(query, apexIndex, leftIndex, path, n, termPos, ref straightPath,
                            ref straightPathFlags, maxStraightPath);
                        if (vertexSide.Length > 0)
                            vertexSide[n - 1] = -1;

                        if (n == maxStraightPath)
                        {
                            straightPathCount = n;
                            return PathQueryStatus.Success;
                        }

                        apex = polyWorldToLocal.MultiplyPoint(termPos);
                        left.Set(0, 0, 0);
                        right.Set(0, 0, 0);
                        i = apexIndex = leftIndex;
                        continue;
                    }

                    if (Perp2D(right, vl) > 0)
                    {
                        var polyLocalToWorld = query.PolygonLocalToWorldMatrix(path[apexIndex]);
                        var termPos = polyLocalToWorld.MultiplyPoint(apex + right);

                        n = RetracePortals(query, apexIndex, rightIndex, path, n, termPos, ref straightPath,
                            ref straightPathFlags, maxStraightPath);
                        if (vertexSide.Length > 0)
                            vertexSide[n - 1] = 1;

                        if (n == maxStraightPath)
                        {
                            straightPathCount = n;
                            return PathQueryStatus.Success;
                        }

                        apex = polyWorldToLocal.MultiplyPoint(termPos);
                        left.Set(0, 0, 0);
                        right.Set(0, 0, 0);
                        i = apexIndex = rightIndex;
                        continue;
                    }

                    if (Perp2D(left, vl) >= 0)
                    {
                        left = vl;
                        leftIndex = i;
                    }

                    if (Perp2D(right, vr) <= 0)
                    {
                        right = vr;
                        rightIndex = i;
                    }
                }
            }

            if (n > 0 && straightPath[n - 1].position == endPos)
                n--;

            n = RetracePortals(query, apexIndex, pathSize - 1, path, n, endPos, ref straightPath,
                ref straightPathFlags, maxStraightPath);
            if (vertexSide.Length > 0)
                vertexSide[n - 1] = 0;

            if (n == maxStraightPath)
            {
                straightPathCount = n;
                return PathQueryStatus.Success;
            }

            straightPathFlags[n - 1] = NpcStraightPathFlags.End;
            straightPathCount = n;
            return PathQueryStatus.Success;
        }

        static bool SegmentSegmentCpa(out float3 c0, out float3 c1, float3 p0, float3 p1, float3 q0, float3 q1)
        {
            var u = p1 - p0;
            var v = q1 - q0;
            var w0 = p0 - q0;

            float a = math.dot(u, u);
            float b = math.dot(u, v);
            float c = math.dot(v, v);
            float d = math.dot(u, w0);
            float e = math.dot(v, w0);

            float den = a * c - b * b;
            float sc, tc;

            if (den == 0f)
            {
                sc = 0f;
                tc = b != 0f ? d / b : 0f;
            }
            else
            {
                sc = (b * e - c * d) / den;
                tc = (a * e - b * d) / den;
            }

            sc = math.clamp(sc, 0f, 1f);
            tc = math.clamp(tc, 0f, 1f);
            c0 = math.lerp(p0, p1, sc);
            c1 = math.lerp(q0, q1, tc);
            return den != 0f;
        }
    }
}
