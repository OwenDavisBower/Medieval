using System.Collections.Generic;
using UnityEngine;

public class BanditCampSpawning
{
    public void Reset() { }

    /// <summary>Picks camp locations and burns the mask (approximate disk); prefabs are instantiated when their chunk streams in.</summary>
    public void PlanCamps(
        BanditCampSpawnConfig config,
        ProceduralPlacementMask placementMask,
        IReadOnlyList<Vector3> plannedSettlementCenters,
        List<Vector3> outCenters)
    {
        outCenters.Clear();
        if (config == null || config.BanditCampPrefab == null)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady || placementMask == null)
            return;

        float minSettleSq = config.MinDistanceFromSettlements * config.MinDistanceFromSettlements;
        float minCampSq = config.MinDistanceFromOtherCamps * config.MinDistanceFromOtherCamps;

        var settlementCenters = plannedSettlementCenters != null
            ? new List<Vector3>(plannedSettlementCenters)
            : CollectSettlementCenters();
        var placedCamps = new List<Vector3>(config.CampCount);
        SeedExistingBanditCampPositions(placedCamps);

        float burnR = Mathf.Max(0.5f, config.OccupationFootprintRadius + config.OccupationBurnPadding);

        for (int i = 0; i < config.CampCount; i++)
        {
            if (!TryPickCampPosition(gen, config, settlementCenters, placedCamps, minSettleSq, minCampSq, placementMask, out Vector3 pos))
                continue;

            placedCamps.Add(pos);
            outCenters.Add(pos);
            placementMask.BurnDiskWorldXZ(pos.x, pos.z, burnR);
        }
    }

    public static GameObject SpawnCampAt(BanditCampSpawnConfig config, Vector3 pos)
    {
        if (config?.BanditCampPrefab == null)
            return null;
        var camp = Object.Instantiate(config.BanditCampPrefab, pos, Quaternion.identity);
        HierarchyLayers.SetRecursiveByLayerName(camp.transform, "Building");
        return camp.gameObject;
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
        IReadOnlyList<Vector3> settlementCenters,
        List<Vector3> placedCamps,
        float minSettleSq,
        float minCampSq,
        ProceduralPlacementMask placementMask,
        out Vector3 pos)
    {
        float campR = Mathf.Max(0.5f, config.OccupationFootprintRadius);
        for (int attempt = 0; attempt < config.MaxSpawnAttemptsPerCamp; attempt++)
        {
            Vector3 candidate = TerrainSpawnUtility.GetWorldPositionOnTerrain(
                config.SpawnOrigin + SpawnPlacementUtility.RandomUniformDiskOffsetXZ(config.SpawnRadius));
            if (candidate.y < 0f)
                continue;

            if (!placementMask.IsDiskFreeWorldXZ(candidate.x, candidate.z, campR))
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
