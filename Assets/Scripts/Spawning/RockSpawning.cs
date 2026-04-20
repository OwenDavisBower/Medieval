using System.Collections.Generic;
using UnityEngine;

public class RockSpawning
{
    bool _spawned;

    public void TrySpawnRocks(RockSpawnConfig config, RockIndirectRenderer renderer)
    {
        if (_spawned || config == null || renderer == null)
            return;
        if (config.RockMesh == null || config.RockMaterial == null || config.RocksInstanceCompute == null)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        if (!TryCollectSeeds(config, gen, out var seeds) || seeds.Count == 0)
            return;

        _spawned = true;
        renderer.Initialize(config, seeds);
    }

    static bool TryCollectSeeds(RockSpawnConfig config, TerrainGenerator gen, out List<RockInstanceSeed> seeds)
    {
        seeds = new List<RockInstanceSeed>(config.RockCount);
        int cap = config.RockCount * config.MaxAttemptsPerRock;
        int attempts = 0;
        Vector3 origin = config.RegionCenter;
        float margin = config.TerrainEdgeMargin;

        while (seeds.Count < config.RockCount && attempts < cap)
        {
            attempts++;
            Vector3 xz = origin + SpawnPlacementUtility.RandomUniformDiskOffsetXZ(config.RegionRadius);
            if (margin > 0f)
                xz = SpawnPlacementUtility.ClampWorldXZToTerrain(gen, xz, margin);

            Vector3 p = TerrainSpawnUtility.GetWorldPositionOnTerrain(xz, config.TerrainHeightOffset);
            if (p.y < 0f)
                continue;

            float yaw = Random.Range(0f, Mathf.PI * 2f);
            float scale = Random.Range(config.MinScale, config.MaxScale);
            seeds.Add(new RockInstanceSeed
            {
                PositionAndYaw = new Vector4(p.x, p.y, p.z, yaw),
                ScaleAndPad = new Vector4(scale, 0f, 0f, 0f)
            });
        }

        return seeds.Count > 0;
    }
}
