using UnityEngine;

public class BanditCampSpawner : MonoBehaviour
{
    [SerializeField] BanditCamp banditCampPrefab;
    [SerializeField] int campCount = 3;
    [SerializeField] float spawnRadius = 100f;
    [SerializeField] Vector3 spawnOrigin = default;

    void Start()
    {
        SpawnCamps();
    }

    void SpawnCamps()
    {
        if (banditCampPrefab == null)
            return;

        for (int i = 0; i < campCount; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = spawnRadius * Mathf.Sqrt(Random.value);
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * r;
            Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(spawnOrigin + offset);

            Instantiate(banditCampPrefab, pos, Quaternion.identity);
        }
    }
}
