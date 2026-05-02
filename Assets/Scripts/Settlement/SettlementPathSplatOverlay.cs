using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Builds organic path rings and corridors around structures and paints them via <see cref="TerrainGenerator.ApplySettlementPathSplatOverlay"/>.
/// </summary>
public static class SettlementPathSplatOverlay
{
    public static void ApplyToTerrain(
        TerrainGenerator gen,
        Transform center,
        List<GameObject> structureRoots,
        float pathRingOutsideFootprint,
        float pathSegmentStepMeters,
        float pathWobbleAmplitude)
    {
        if (gen == null || center == null || structureRoots == null || structureRoots.Count == 0)
            return;

        var ringCenters = new List<Vector2>(structureRoots.Count);
        var ringRadii = new List<float>(structureRoots.Count);
        for (int i = 0; i < structureRoots.Count; i++)
        {
            var b = CombineRendererBounds(structureRoots[i]);
            var xz = new Vector2(b.center.x, b.center.z);
            var halfW = Mathf.Max(b.extents.x, b.extents.z);
            ringCenters.Add(xz);
            ringRadii.Add(halfW + pathRingOutsideFootprint);
        }

        var nodes = new List<Vector2>(1 + structureRoots.Count);
        nodes.Add(new Vector2(center.position.x, center.position.z));
        for (int i = 0; i < structureRoots.Count; i++)
            nodes.Add(ringCenters[i]);

        var chains = BuildOrganicPathChains(nodes, pathSegmentStepMeters, pathWobbleAmplitude);
        gen.ApplySettlementPathSplatOverlay(ringCenters, ringRadii, chains);
    }

    public static Bounds CombineRendererBounds(GameObject root)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0)
            return new Bounds(root.transform.position, Vector3.one * 2f);

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);
        return b;
    }

    static List<List<Vector2>> BuildOrganicPathChains(
        IReadOnlyList<Vector2> nodes,
        float pathSegmentStepMeters,
        float pathWobbleAmplitude)
    {
        var chains = new List<List<Vector2>>();
        if (nodes.Count < 2)
            return chains;

        var mstEdges = PrimMstEdges(nodes);
        float seed = Random.value * 127.1f;
        for (int e = 0; e < mstEdges.Count; e++)
        {
            var a = nodes[mstEdges[e].from];
            var b = nodes[mstEdges[e].to];
            chains.Add(BuildWobblyPolyline(a, b, seed + e * 19.17f, pathSegmentStepMeters, pathWobbleAmplitude));
        }

        return chains;
    }

    static List<(int from, int to)> PrimMstEdges(IReadOnlyList<Vector2> nodes)
    {
        int n = nodes.Count;
        var inTree = new bool[n];
        var minDistSq = new float[n];
        var nearest = new int[n];
        for (int i = 0; i < n; i++)
        {
            minDistSq[i] = float.PositiveInfinity;
            nearest[i] = -1;
        }

        inTree[0] = true;
        for (int j = 1; j < n; j++)
        {
            var d = (nodes[j] - nodes[0]).sqrMagnitude;
            minDistSq[j] = d;
            nearest[j] = 0;
        }

        var edges = new List<(int, int)>(n - 1);
        for (int iter = 1; iter < n; iter++)
        {
            int best = -1;
            float bestD = float.PositiveInfinity;
            for (int i = 1; i < n; i++)
            {
                if (inTree[i])
                    continue;
                if (minDistSq[i] < bestD)
                {
                    bestD = minDistSq[i];
                    best = i;
                }
            }

            if (best < 0)
                break;

            inTree[best] = true;
            edges.Add((best, nearest[best]));

            for (int j = 1; j < n; j++)
            {
                if (inTree[j])
                    continue;
                var d = (nodes[j] - nodes[best]).sqrMagnitude;
                if (d < minDistSq[j])
                {
                    minDistSq[j] = d;
                    nearest[j] = best;
                }
            }
        }

        return edges;
    }

    static List<Vector2> BuildWobblyPolyline(
        Vector2 a,
        Vector2 b,
        float noiseSeed,
        float pathSegmentStepMeters,
        float pathWobbleAmplitude)
    {
        float len = Vector2.Distance(a, b);
        int steps = Mathf.Max(8, Mathf.CeilToInt(len / Mathf.Max(0.35f, pathSegmentStepMeters)));
        var pts = new List<Vector2>(steps + 1);
        Vector2 ab = b - a;
        Vector2 dir = len > 1e-5f ? ab / len : Vector2.right;
        Vector2 perp = new Vector2(-dir.y, dir.x);

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 basePoint = Vector2.Lerp(a, b, t);
            float envelope = Mathf.Sin(t * Mathf.PI);
            float n1 = (Mathf.PerlinNoise(t * 4.2f + noiseSeed, noiseSeed * 0.37f) - 0.5f) * 2f;
            float n2 = (Mathf.PerlinNoise(noiseSeed * 0.73f, t * 5.8f + noiseSeed * 1.1f) - 0.5f) * 2f;
            basePoint += perp * (n1 * pathWobbleAmplitude * envelope);
            basePoint += dir * (n2 * pathWobbleAmplitude * 0.4f * envelope);
            pts.Add(basePoint);
        }

        return pts;
    }
}
