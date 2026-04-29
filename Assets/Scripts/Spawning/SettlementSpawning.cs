using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plans settlement centers per logical terrain chunk (configurable count per chunk), using the same path and mask rules as before,
/// and enforcing <see cref="SettlementSpawnConfig.MinSettlementSeparation"/> against every accepted center so spacing holds whether chunks are loaded or not.
/// <see cref="WorldGenerationCoordinator"/> streams instances when each center's chunk enters the window.
/// </summary>
public class SettlementSpawning
{
    static int ChunkRngSeed(int worldSeed, int chunkX, int chunkZ, int slot) =>
        unchecked(worldSeed * 73856093 ^ chunkX * 19349663 ^ chunkZ * 83492791 ^ slot * 50331653);

    public void PlanSettlementCenters(
        SettlementSpawnConfig config,
        ProceduralPlacementMask placementMask,
        int worldSeed,
        List<Vector3> outCenters)
    {
        outCenters.Clear();
        if (config == null || !HasAnyBuildingPrefab(config) || placementMask == null)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        int perChunk = config.SettlementsPerLogicalChunk;
        if (perChunk <= 0)
            return;

        float minSepSq = config.MinSettlementSeparation * config.MinSettlementSeparation;
        var placedCenters = new List<Vector3>();
        int axis = Mathf.Max(1, gen.chunkCount);

        var prev = Random.state;
        try
        {
            for (int cz = 0; cz < axis; cz++)
            {
                for (int cx = 0; cx < axis; cx++)
                {
                    for (int slot = 0; slot < perChunk; slot++)
                    {
                        Random.InitState(ChunkRngSeed(worldSeed, cx, cz, slot));
                        if (!TryPickSettlementPositionInChunk(gen, config, cx, cz, placedCenters, minSepSq, placementMask, out Vector3 pos))
                            continue;

                        placedCenters.Add(pos);
                        outCenters.Add(pos);
                    }
                }
            }
        }
        finally
        {
            Random.state = prev;
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

    static bool TryPickSettlementPositionInChunk(
        TerrainGenerator gen,
        SettlementSpawnConfig config,
        int chunkX,
        int chunkZ,
        List<Vector3> placedCenters,
        float minSepSq,
        ProceduralPlacementMask placementMask,
        out Vector3 pos)
    {
        float centerR = config.SettlementCenterFootprintRadius;
        for (int attempt = 0; attempt < config.MaxSpawnAttemptsPerSettlement; attempt++)
        {
            if (!SpawnPlacementUtility.TryRandomUniformWorldXZInTerrainChunk(
                    gen, chunkX, chunkZ, config.TerrainEdgeMargin, out Vector3 candidate))
            {
                pos = default;
                return false;
            }

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
