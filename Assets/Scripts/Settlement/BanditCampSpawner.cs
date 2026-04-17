using System.Collections.Generic;
using UnityEngine;

public class BanditCampSpawner : MonoBehaviour
{
    [SerializeField] BanditCamp banditCampPrefab;
    [SerializeField] int campCount = 3;
    [SerializeField] float spawnRadius = 100f;
    [SerializeField] Vector3 spawnOrigin = default;

    [Header("Separation")]
    [Tooltip("Minimum XZ distance from settlement centers (SettlementBuilder transform).")]
    [SerializeField] float minDistanceFromSettlements = 30f;
    [Tooltip("Minimum XZ distance from other bandit camps.")]
    [SerializeField] float minDistanceFromOtherCamps = 20f;
    [SerializeField] int maxSpawnAttemptsPerCamp = 120;

    bool _spawned;

    public void SpawnCamps()
    {
        if (_spawned || banditCampPrefab == null)
            return;

        var gen = Object.FindFirstObjectByType<TerrainGenerator>();
        if (gen == null || !gen.IsTerrainReady)
            return;

        _spawned = true;

        float minSettleSq = minDistanceFromSettlements * minDistanceFromSettlements;
        float minCampSq = minDistanceFromOtherCamps * minDistanceFromOtherCamps;

        List<Vector3> settlementCenters = CollectSettlementCenters();
        var placedCamps = new List<Vector3>(campCount);
        SeedExistingBanditCampPositions(placedCamps);

        for (int i = 0; i < campCount; i++)
        {
            if (!TryPickCampPosition(settlementCenters, placedCamps, minSettleSq, minCampSq, out Vector3 pos))
                continue;

            placedCamps.Add(pos);
            Instantiate(banditCampPrefab, pos, Quaternion.identity);
        }
    }

    static List<Vector3> CollectSettlementCenters()
    {
        var builders = FindObjectsByType<SettlementBuilder>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var list = new List<Vector3>(builders.Length);
        for (int i = 0; i < builders.Length; i++)
        {
            Vector3 p = builders[i].transform.position;
            list.Add(p);
        }

        return list;
    }

    void SeedExistingBanditCampPositions(List<Vector3> placedCamps)
    {
        var existing = FindObjectsByType<BanditCamp>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < existing.Length; i++)
            placedCamps.Add(existing[i].transform.position);
    }

    bool TryPickCampPosition(
        List<Vector3> settlementCenters,
        List<Vector3> placedCamps,
        float minSettleSq,
        float minCampSq,
        out Vector3 pos)
    {
        for (int attempt = 0; attempt < maxSpawnAttemptsPerCamp; attempt++)
        {
            Vector3 candidate = TerrainSpawnUtility.GetWorldPositionOnTerrain(
                spawnOrigin + SpawnPlacementUtility.RandomUniformDiskOffsetXZ(spawnRadius));
            if (candidate.y < 0f)
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
