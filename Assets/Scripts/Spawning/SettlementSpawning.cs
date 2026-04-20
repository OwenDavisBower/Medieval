using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns several <see cref="SettlementBuilder"/> instances after procedural terrain is ready.
/// </summary>
public class SettlementSpawning
{
    bool _spawned;

    public void TrySpawnSettlements(SettlementSpawnConfig config)
    {
        if (config == null || _spawned || config.CabinPrefab == null || config.FarmPrefab == null)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        _spawned = true;

        float minSepSq = config.MinSettlementSeparation * config.MinSettlementSeparation;
        var placedCenters = new List<Vector3>(config.SettlementCount);
        int spawned = 0;

        for (int i = 0; i < config.SettlementCount; i++)
        {
            if (!TryPickSettlementPosition(gen, config, placedCenters, minSepSq, out Vector3 pos))
                continue;

            placedCenters.Add(pos);

            var go = new GameObject($"Settlement_{spawned}");
            spawned++;
            go.transform.position = pos;
            var builder = go.AddComponent<SettlementBuilder>();
            builder.InitializeAndBuild(config.CabinPrefab, config.FarmPrefab, config.VillagerPrefab);
        }
    }

    static bool TryPickSettlementPosition(
        TerrainGenerator gen,
        SettlementSpawnConfig config,
        List<Vector3> placedCenters,
        float minSepSq,
        out Vector3 pos)
    {
        for (int attempt = 0; attempt < config.MaxSpawnAttemptsPerSettlement; attempt++)
        {
            Vector3 candidate = config.SpawnOrigin + SpawnPlacementUtility.RandomUniformDiskOffsetXZ(config.SpawnRadius);
            candidate = SpawnPlacementUtility.ClampWorldXZToTerrain(gen, candidate, config.TerrainEdgeMargin);

            if (SpawnPlacementUtility.IsFarEnoughFromAllXZ(candidate, placedCenters, minSepSq))
            {
                pos = candidate;
                return true;
            }
        }

        pos = default;
        return false;
    }
}
