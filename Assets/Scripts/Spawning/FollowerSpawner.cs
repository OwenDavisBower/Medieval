using UnityEngine;
using Medieval.Npcs;
using Medieval.NpcMovement;
using Unity.Mathematics;
using URandom = UnityEngine.Random;

public class FollowerSpawner : MonoBehaviour
{
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
        if (_spawned)
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

            var e = NpcSpawnApi.SpawnFollower(pos, quaternion.identity);
            if (e == Unity.Entities.Entity.Null)
            {
                Debug.LogWarning(
                    "FollowerSpawner: NpcSpawnApi.SpawnFollower failed (is NpcPrefabRegistryAuthoring in a loaded subscene with Follower prefab assigned?).");
                continue;
            }

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            NpcMovementApi.SetAnchorPosition(em, e, new float3(leaderWorldPosition.x, leaderWorldPosition.y, leaderWorldPosition.z));
        }
    }
}
