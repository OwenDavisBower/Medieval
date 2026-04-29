using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plans settlement center positions and instantiates settlements for chunk streaming (see <see cref="WorldGenerationCoordinator"/>).
/// </summary>
public class SettlementSpawning
{
    public void PlanSettlementCenters(SettlementSpawnConfig config, ProceduralPlacementMask placementMask, List<Vector3> outCenters)
    {
        outCenters.Clear();
        if (config == null || !HasAnyBuildingPrefab(config) || placementMask == null)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        float minSepSq = config.MinSettlementSeparation * config.MinSettlementSeparation;
        var placedCenters = new List<Vector3>(config.SettlementCount);

        for (int i = 0; i < config.SettlementCount; i++)
        {
            if (!TryPickSettlementPosition(gen, config, placedCenters, minSepSq, placementMask, out Vector3 pos))
                continue;

            placedCenters.Add(pos);
            outCenters.Add(pos);
        }
    }

    public GameObject SpawnSettlementAt(
        SettlementSpawnConfig config,
        Vector3 nominalCenter,
        ProceduralPlacementMask placementMask,
        int planIndex)
    {
        if (config == null || !HasAnyBuildingPrefab(config) || placementMask == null)
            return null;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return null;

        var go = new GameObject($"Settlement_{planIndex}");
        go.transform.position = nominalCenter;
        var builder = go.AddComponent<SettlementBuilder>();
        builder.ConfigurePathOverlay(config.PathRingOutsideFootprint, config.PathSegmentStepMeters, config.PathWobbleAmplitude);
        builder.InitializeAndBuild(config.Buildings, config.VillagerPrefab, placementMask);
        return go;
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
