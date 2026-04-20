using System.Collections.Generic;
using UnityEngine;

public class BanditCampSpawning
{
    /// <summary>Matches <see cref="TerrainGenerator"/> splat path mask falloff (smoothstep 8→0 on path distance).</summary>
    const float MinPathDistanceFromSplatPaths = 8f;

    bool _spawned;

    public void SpawnCamps(BanditCampSpawnConfig config)
    {
        if (config == null || _spawned || config.BanditCampPrefab == null)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        _spawned = true;

        float minSettleSq = config.MinDistanceFromSettlements * config.MinDistanceFromSettlements;
        float minCampSq = config.MinDistanceFromOtherCamps * config.MinDistanceFromOtherCamps;

        List<Vector3> settlementCenters = CollectSettlementCenters();
        var placedCamps = new List<Vector3>(config.CampCount);
        SeedExistingBanditCampPositions(placedCamps);

        for (int i = 0; i < config.CampCount; i++)
        {
            if (!TryPickCampPosition(gen, config, settlementCenters, placedCamps, minSettleSq, minCampSq, out Vector3 pos))
                continue;

            placedCamps.Add(pos);
            Object.Instantiate(config.BanditCampPrefab, pos, Quaternion.identity);
        }
    }

    static List<Vector3> CollectSettlementCenters()
    {
        var builders = Object.FindObjectsByType<SettlementBuilder>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var list = new List<Vector3>(builders.Length);
        for (int i = 0; i < builders.Length; i++)
        {
            Vector3 p = builders[i].transform.position;
            list.Add(p);
        }

        return list;
    }

    static void SeedExistingBanditCampPositions(List<Vector3> placedCamps)
    {
        var existing = Object.FindObjectsByType<BanditCamp>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < existing.Length; i++)
            placedCamps.Add(existing[i].transform.position);
    }

    static bool TryPickCampPosition(
        TerrainGenerator terrain,
        BanditCampSpawnConfig config,
        List<Vector3> settlementCenters,
        List<Vector3> placedCamps,
        float minSettleSq,
        float minCampSq,
        out Vector3 pos)
    {
        for (int attempt = 0; attempt < config.MaxSpawnAttemptsPerCamp; attempt++)
        {
            Vector3 candidate = TerrainSpawnUtility.GetWorldPositionOnTerrain(
                config.SpawnOrigin + SpawnPlacementUtility.RandomUniformDiskOffsetXZ(config.SpawnRadius));
            if (candidate.y < 0f)
                continue;

            if (terrain.SamplePathDistanceWorldXZ(candidate.x, candidate.z) < MinPathDistanceFromSplatPaths)
                continue;

            if (!SpawnPlacementUtility.IsFarEnoughFromAllXZ(candidate, settlementCenters, minSettleSq))
                continue;
            if (!SpawnPlacementUtility.IsFarEnoughFromAllXZ(candidate, placedCamps, minCampSq))
                continue;

            pos = candidate;
            return true;
        }

        pos = default;
        return false;
    }
}
