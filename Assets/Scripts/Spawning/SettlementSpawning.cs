using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns several <see cref="SettlementBuilder"/> instances after procedural terrain is ready.
/// </summary>
public class SettlementSpawning
{
    bool _spawned;

    public void TrySpawnSettlements(SettlementSpawnConfig config, ProceduralPlacementMask placementMask)
    {
        if (config == null || _spawned || !HasAnyBuildingPrefab(config))
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
            if (!TryPickSettlementPosition(gen, config, placedCenters, minSepSq, placementMask, out Vector3 pos))
                continue;

            placedCenters.Add(pos);

            var go = new GameObject($"Settlement_{spawned}");
            spawned++;
            go.transform.position = pos;
            var builder = go.AddComponent<SettlementBuilder>();
            builder.InitializeAndBuild(config.Buildings, config.VillagerPrefab, placementMask);
        }
    }

    static bool HasAnyBuildingPrefab(SettlementSpawnConfig config)
    {
        var list = config.Buildings;
        if (list == null)
            return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].prefab != null)
                return true;
        }
        return false;
    }

    static bool TryPickSettlementPosition(
        TerrainGenerator gen,
        SettlementSpawnConfig config,
        List<Vector3> placedCenters,
        float minSepSq,
        ProceduralPlacementMask placementMask,
        out Vector3 pos)
    {
        float centerR = config.SettlementCenterFootprintRadius;
        for (int attempt = 0; attempt < config.MaxSpawnAttemptsPerSettlement; attempt++)
        {
            Vector3 candidate = config.SpawnOrigin + SpawnPlacementUtility.RandomUniformDiskOffsetXZ(config.SpawnRadius);
            candidate = SpawnPlacementUtility.ClampWorldXZToTerrain(gen, candidate, config.TerrainEdgeMargin);

            if (placementMask != null && !placementMask.IsDiskFreeWorldXZ(candidate.x, candidate.z, centerR))
                continue;

            float pathDist = gen.SamplePathDistanceWorldXZ(candidate.x, candidate.z);
            float pathMin = config.MinDistanceFromPathMeters;
            float pathMax = config.MaxDistanceFromPathMeters;
            if (pathMax <= pathMin)
                pathMax = pathMin + 20f;
            if (pathDist < pathMin || pathDist > pathMax)
                continue;

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
