using UnityEngine;

public class FollowerSpawner : MonoBehaviour
{
    [SerializeField] FollowerController followerPrefab;
    [SerializeField] int followerCount = 5;
    [SerializeField] float spawnRadiusMin = 1.5f;
    [SerializeField] float spawnRadiusMax = 4f;

    bool _spawned;

    void OnEnable()
    {
        TerrainGenerator.TerrainGenerated += OnTerrainGenerated;
    }

    void OnDisable()
    {
        TerrainGenerator.TerrainGenerated -= OnTerrainGenerated;
    }

    void Start()
    {
        SpawnFollowers();
    }

    void OnTerrainGenerated(TerrainGenerator _) => SpawnFollowers();

    void SpawnFollowers()
    {
        if (_spawned || followerPrefab == null)
            return;

        var gen = Object.FindFirstObjectByType<TerrainGenerator>();
        if (gen == null || !gen.IsTerrainReady)
            return;

        _spawned = true;

        for (int i = 0; i < followerCount; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float rad = Random.Range(spawnRadiusMin, spawnRadiusMax);
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * rad;
            Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(transform.position + offset);

            FollowerController follower = Instantiate(followerPrefab, pos, Quaternion.identity);
            follower.ApplyCombatRole(Random.value < 0.5f);
            follower.Initialize();
        }
    }
}
