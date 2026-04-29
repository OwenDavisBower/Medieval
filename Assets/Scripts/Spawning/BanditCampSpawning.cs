using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plans bandit camp centers per logical terrain chunk (configurable per chunk), with separation from all planned settlement centers
/// and from every accepted camp so spacing holds whether chunks are loaded. Burns the placement mask after each camp; prefabs stream in <see cref="WorldGenerationCoordinator"/>.
/// </summary>
public class BanditCampSpawning
{
    /// <summary>Distinct from <see cref="SettlementSpawning"/> chunk seeds so camp randomness is independent.</summary>
    static int ChunkRngSeed(int worldSeed, int chunkX, int chunkZ, int slot) =>
        unchecked(worldSeed * 101200933 ^ chunkX * 19349663 ^ chunkZ * 83492791 ^ slot * 50331653 ^ unchecked((int)0xC4B4D000));

    public void Reset() { }

    public void PlanCamps(
        BanditCampSpawnConfig config,
        ProceduralPlacementMask placementMask,
        IReadOnlyList<Vector3> plannedSettlementCenters,
        int worldSeed,
        List<Vector3> outCenters)
    {
        outCenters.Clear();
        if (config == null || config.BanditCampPrefab == null)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady || placementMask == null)
            return;

        int perChunk = config.CampsPerLogicalChunk;
        if (perChunk <= 0)
            return;

        float minSettleSq = config.MinDistanceFromSettlements * config.MinDistanceFromSettlements;
        float minCampSq = config.MinDistanceFromOtherCamps * config.MinDistanceFromOtherCamps;

        var settlementCenters = plannedSettlementCenters != null
            ? new List<Vector3>(plannedSettlementCenters)
            : CollectSettlementCenters();
        var placedCamps = new List<Vector3>();
        SeedExistingBanditCampPositions(placedCamps);

        float burnR = Mathf.Max(0.5f, config.OccupationFootprintRadius + config.OccupationBurnPadding);
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
                        if (!TryPickCampPositionInChunk(
                                gen, config, cx, cz, settlementCenters, placedCamps, minSettleSq, minCampSq, placementMask, out Vector3 pos))
                            continue;

                        placedCamps.Add(pos);
                        outCenters.Add(pos);
                        placementMask.BurnDiskWorldXZ(pos.x, pos.z, burnR);
                    }
                }
            }
        }
        finally
        {
            Random.state = prev;
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

    static bool TryPickCampPositionInChunk(
        TerrainGenerator terrain,
        BanditCampSpawnConfig config,
        int chunkX,
        int chunkZ,
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
            if (!SpawnPlacementUtility.TryRandomUniformWorldXZInTerrainChunk(
                    terrain, chunkX, chunkZ, config.TerrainEdgeMargin, out Vector3 xz))
            {
                pos = default;
                return false;
            }

            Vector3 candidate = TerrainSpawnUtility.GetWorldPositionOnTerrain(xz);
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
