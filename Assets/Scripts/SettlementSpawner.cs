using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns several <see cref="SettlementBuilder"/> instances after procedural terrain is ready.
/// </summary>
public class SettlementSpawner : MonoBehaviour
{
    [SerializeField] GameObject cabinPrefab;
    [SerializeField] GameObject farmPrefab;
    [SerializeField] int settlementCount = 3;
    [SerializeField] float spawnRadius = 200f;
    [SerializeField] Vector3 spawnOrigin;
    [SerializeField] float terrainEdgeMargin = 64f;
    [Tooltip("Minimum distance between settlement centers (XZ).")]
    [SerializeField] float minSettlementSeparation = 30f;
    [SerializeField] int maxSpawnAttemptsPerSettlement = 120;

    bool _spawned;

    void OnEnable()
    {
        TerrainGenerator.TerrainGenerated += OnTerrainGenerated;
    }

    void OnDisable()
    {
        TerrainGenerator.TerrainGenerated -= OnTerrainGenerated;
    }

    void Start() => TrySpawnSettlements();

    void OnTerrainGenerated(TerrainGenerator _) => TrySpawnSettlements();

    void TrySpawnSettlements()
    {
        var gen = Object.FindFirstObjectByType<TerrainGenerator>();
        if (gen == null || !gen.IsTerrainReady)
            return;

        if (!_spawned && cabinPrefab != null && farmPrefab != null)
        {
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
                builder.InitializeAndBuild(cabinPrefab, farmPrefab);
            }
        }

        foreach (var bandit in FindObjectsByType<BanditCampSpawner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            bandit.SpawnCamps();

        foreach (var trees in FindObjectsByType<TreeSpawner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            trees.TrySpawnTrees();
    }

    bool TryPickSettlementPosition(
        TerrainGenerator gen,
        List<Vector3> placedCenters,
        float minSepSq,
        out Vector3 pos)
    {
        for (int attempt = 0; attempt < maxSpawnAttemptsPerSettlement; attempt++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = spawnRadius * Mathf.Sqrt(Random.value);
            Vector3 candidate = spawnOrigin + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
            candidate = ClampToTerrainXZ(candidate, gen, terrainEdgeMargin);

            if (IsFarEnoughXZ(candidate, placedCenters, minSepSq))
            {
                pos = candidate;
                return true;
            }
        }

        pos = default;
        return false;
    }

    static bool IsFarEnoughXZ(Vector3 candidate, List<Vector3> placedCenters, float minSepSq)
    {
        for (int i = 0; i < placedCenters.Count; i++)
        {
            float dx = candidate.x - placedCenters[i].x;
            float dz = candidate.z - placedCenters[i].z;
            if (dx * dx + dz * dz < minSepSq)
                return false;
        }

        return true;
    }

    static Vector3 ClampToTerrainXZ(Vector3 worldPos, TerrainGenerator gen, float margin)
    {
        float half = gen.worldSize * 0.5f - margin;
        if (half <= 0f)
            half = gen.worldSize * 0.25f;

        var o = gen.transform.position;
        float x = Mathf.Clamp(worldPos.x, o.x - half, o.x + half);
        float z = Mathf.Clamp(worldPos.z, o.z - half, o.z + half);
        return new Vector3(x, worldPos.y, z);
    }
}
