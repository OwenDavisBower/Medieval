#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds polylines in terrain local space by walking XZ with Perlin-noise-steered heading (noise worms).
/// </summary>
public static class NoiseWormSplineGenerator
{
    public struct Settings
    {
        public int Seed;
        public float WorldSize;
        /// <summary>Padding from terrain XZ edges (world units).</summary>
        public float BoundaryMargin;
        public int PathWormCount;
        public int RiverWormCount;
        public int SegmentCount;
        public float StepLength;
        public float NoiseScale;
        public float MaxTurnRadians;
    }

    /// <summary>Append path worms; each polyline has at least two points when SegmentCount >= 2. Points are local XZ only.</summary>
    public static void GeneratePaths(Transform terrainRoot, Settings settings, List<List<Vector2>> outSplines)
    {
        if (settings.PathWormCount <= 0 || settings.SegmentCount < 2 || settings.StepLength <= 0f)
            return;

        var rng = new System.Random(unchecked(settings.Seed * 1103515245 + 12345));
        var margin = Mathf.Max(0f, settings.BoundaryMargin);
        var extent = Mathf.Max(0.5f, settings.WorldSize * 0.5f - margin);
        var origin = terrainRoot.position;

        for (var w = 0; w < settings.PathWormCount; w++)
        {
            var poly = new List<Vector2>(settings.SegmentCount);
            var wx = RandomRange(rng, origin.x - extent, origin.x + extent);
            var wz = RandomRange(rng, origin.z - extent, origin.z + extent);
            var theta = (float)(rng.NextDouble() * (Math.PI * 2.0));

            for (var s = 0; s < settings.SegmentCount; s++)
            {
                var worldY = origin.y;
                var worldPos = new Vector3(wx, worldY, wz);
                var local = terrainRoot.InverseTransformPoint(worldPos);
                poly.Add(new Vector2(local.x, local.z));

                if (s == settings.SegmentCount - 1)
                    break;

                var n = Mathf.PerlinNoise(
                    wx * settings.NoiseScale + w * 19.17f,
                    wz * settings.NoiseScale + settings.Seed * 0.001f);
                theta += (n - 0.5f) * 2f * settings.MaxTurnRadians;

                wx += Mathf.Cos(theta) * settings.StepLength;
                wz += Mathf.Sin(theta) * settings.StepLength;

                wx = Mathf.Clamp(wx, origin.x - extent, origin.x + extent);
                wz = Mathf.Clamp(wz, origin.z - extent, origin.z + extent);
            }

            if (poly.Count >= 2)
                outSplines.Add(poly);
        }
    }

    /// <summary>Append river worms (XZ only); flow height is applied when sampling via River Local Y High/Low on TerrainGenerator.</summary>
    public static void GenerateRivers(Transform terrainRoot, Settings settings, List<List<Vector2>> outSplines)
    {
        if (settings.RiverWormCount <= 0 || settings.SegmentCount < 2 || settings.StepLength <= 0f)
            return;

        var rng = new System.Random(unchecked(settings.Seed * 362436069 + 789123));
        var margin = Mathf.Max(0f, settings.BoundaryMargin);
        var extent = Mathf.Max(0.5f, settings.WorldSize * 0.5f - margin);
        var origin = terrainRoot.position;

        for (var w = 0; w < settings.RiverWormCount; w++)
        {
            var poly = new List<Vector2>(settings.SegmentCount);
            var wx = RandomRange(rng, origin.x - extent, origin.x + extent);
            var wz = RandomRange(rng, origin.z - extent, origin.z + extent);
            var theta = (float)(rng.NextDouble() * (Math.PI * 2.0));

            for (var s = 0; s < settings.SegmentCount; s++)
            {
                var worldFlat = new Vector3(wx, origin.y, wz);
                var local = terrainRoot.InverseTransformPoint(worldFlat);
                poly.Add(new Vector2(local.x, local.z));

                if (s == settings.SegmentCount - 1)
                    break;

                var n = Mathf.PerlinNoise(
                    wx * settings.NoiseScale + w * 27.91f + 400f,
                    wz * settings.NoiseScale + settings.Seed * 0.002f + 200f);
                theta += (n - 0.5f) * 2f * settings.MaxTurnRadians;

                wx += Mathf.Cos(theta) * settings.StepLength;
                wz += Mathf.Sin(theta) * settings.StepLength;

                wx = Mathf.Clamp(wx, origin.x - extent, origin.x + extent);
                wz = Mathf.Clamp(wz, origin.z - extent, origin.z + extent);
            }

            if (poly.Count >= 2)
                outSplines.Add(poly);
        }
    }

    static float RandomRange(System.Random rng, float min, float max)
    {
        if (max <= min)
            return min;
        return min + (float)rng.NextDouble() * (max - min);
    }
}
