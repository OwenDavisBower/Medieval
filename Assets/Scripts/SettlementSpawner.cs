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
        if (_spawned || cabinPrefab == null || farmPrefab == null)
            return;

        var gen = Object.FindFirstObjectByType<TerrainGenerator>();
        if (gen == null || !gen.IsTerrainReady)
            return;

        _spawned = true;

        for (int i = 0; i < settlementCount; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = spawnRadius * Mathf.Sqrt(Random.value);
            Vector3 pos = spawnOrigin + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
            pos = ClampToTerrainXZ(pos, gen, terrainEdgeMargin);

            var go = new GameObject($"Settlement_{i}");
            go.transform.position = pos;
            var builder = go.AddComponent<SettlementBuilder>();
            builder.InitializeAndBuild(cabinPrefab, farmPrefab);
        }
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
