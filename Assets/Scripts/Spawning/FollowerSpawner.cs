using UnityEngine;
using Medieval.Npcs;
using Unity.Mathematics;
using URandom = UnityEngine.Random;

public class FollowerSpawner : MonoBehaviour
{
    [SerializeField] FollowerController followerPrefab;
    [SerializeField] int followerCount = 5;
    [SerializeField] float spawnRadiusMin = 1.5f;
    [SerializeField] float spawnRadiusMax = 4f;

    bool _spawned;

    void OnEnable()
    {
        PlayerController.PlayerStartPositionApplied += OnPlayerStartPositionApplied;
    }

    void OnDisable()
    {
        PlayerController.PlayerStartPositionApplied -= OnPlayerStartPositionApplied;
    }

    void OnPlayerStartPositionApplied(Vector3 leaderWorldPosition) => SpawnFollowers(leaderWorldPosition);

    void SpawnFollowers(Vector3 leaderWorldPosition)
    {
        if (_spawned || followerPrefab == null)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        _spawned = true;

        for (int i = 0; i < followerCount; i++)
        {
            float angle = URandom.Range(0f, Mathf.PI * 2f);
            float rad = URandom.Range(spawnRadiusMin, spawnRadiusMax);
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * rad;
            Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(leaderWorldPosition + offset);

            // If a baked Entities Graphics follower prefab is registered, prefer spawning DOTS followers.
            if (NpcSpawnApi.SpawnFollower(pos, quaternion.identity))
                continue;

            FollowerController follower = Instantiate(followerPrefab, pos, Quaternion.identity);
            follower.ApplyCombatRole(URandom.value < 0.5f);
            follower.Initialize();
        }
    }
}
