using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns several <see cref="SettlementBuilder"/> instances after procedural terrain is ready.
/// </summary>
public class SettlementSpawner : MonoBehaviour
{
    [SerializeField] GameObject cabinPrefab;
    [SerializeField] GameObject farmPrefab;
    [SerializeField] GameObject villagerPrefab;
    [SerializeField] int settlementCount = 3;
    [SerializeField] float spawnRadius = 200f;
    [SerializeField] Vector3 spawnOrigin;
    [SerializeField] float terrainEdgeMargin = 64f;
    [Tooltip("Minimum distance between settlement centers (XZ).")]
    [SerializeField] float minSettlementSeparation = 30f;
    [SerializeField] int maxSpawnAttemptsPerSettlement = 120;

    bool _spawned;

    public void TrySpawnSettlements()
    {
        if (_spawned || cabinPrefab == null || farmPrefab == null)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        _spawned = true;

        float minSepSq = minSettlementSeparation * minSettlementSeparation;
        var placedCenters = new List<Vector3>(settlementCount);
        int spawned = 0;

        for (int i = 0; i < settlementCount; i++)
        {
            if (!TryPickSettlementPosition(gen, placedCenters, minSepSq, out Vector3 pos))
                continue;

            placedCenters.Add(pos);

            var go = new GameObject($"Settlement_{spawned}");
            spawned++;
            go.transform.position = pos;
            var builder = go.AddComponent<SettlementBuilder>();
            builder.InitializeAndBuild(cabinPrefab, farmPrefab, villagerPrefab);
        }
    }

    bool TryPickSettlementPosition(
        TerrainGenerator gen,
        List<Vector3> placedCenters,
        float minSepSq,
        out Vector3 pos)
    {
        for (int attempt = 0; attempt < maxSpawnAttemptsPerSettlement; attempt++)
        {
            Vector3 candidate = spawnOrigin + SpawnPlacementUtility.RandomUniformDiskOffsetXZ(spawnRadius);
            candidate = SpawnPlacementUtility.ClampWorldXZToTerrain(gen, candidate, terrainEdgeMargin);

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
