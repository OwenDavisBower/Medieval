using UnityEngine;

public class FollowerSpawner : MonoBehaviour
{
    [SerializeField] FollowerController followerPrefab;
    [SerializeField] int followerCount = 5;
    [SerializeField] float spawnRadiusMin = 1.5f;
    [SerializeField] float spawnRadiusMax = 4f;

    void Awake()
    {
        if (FindFirstObjectByType<TerrainGenerator>() != null)
            TerrainGenerator.TerrainGenerationComplete += SpawnFollowersOnce;
    }

    void Start()
    {
        if (FindFirstObjectByType<TerrainGenerator>() == null)
            SpawnFollowers();
    }

    void OnDestroy()
    {
        TerrainGenerator.TerrainGenerationComplete -= SpawnFollowersOnce;
    }

    void SpawnFollowersOnce()
    {
        TerrainGenerator.TerrainGenerationComplete -= SpawnFollowersOnce;
        SpawnFollowers();
    }

    void SpawnFollowers()
    {
        if (followerPrefab == null)
            return;

        for (int i = 0; i < followerCount; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float rad = Random.Range(spawnRadiusMin, spawnRadiusMax);
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * rad;
            Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(transform.position + offset);

            FollowerController follower = Instantiate(followerPrefab, pos, Quaternion.identity);
            follower.Initialize();
        }
    }
}
