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
    bool _pending;
    Vector3 _leaderWorldPosition;

    void OnEnable()
    {
        PlayerController.PlayerStartPositionApplied += OnPlayerStartPositionApplied;
    }

    void OnDisable()
    {
        PlayerController.PlayerStartPositionApplied -= OnPlayerStartPositionApplied;
    }

    void Update()
    {
        if (_pending)
            TrySpawnFollowers();
    }

    void OnPlayerStartPositionApplied(Vector3 leaderWorldPosition)
    {
        _leaderWorldPosition = leaderWorldPosition;
        _pending = true;
        TrySpawnFollowers();
    }

    void TrySpawnFollowers()
    {
        if (_spawned)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        // Terrain regenerates synchronously in WorldGenerationCoordinator.Start; PrefabSubScene
        // often has not finished loading yet. Wait for the baked registry before committing.
        if (!NpcSpawnApi.IsPrefabRegistryReady())
            return;

        _spawned = true;
        _pending = false;

        for (int i = 0; i < followerCount; i++)
        {
            float angle = URandom.Range(0f, Mathf.PI * 2f);
            float rad = URandom.Range(spawnRadiusMin, spawnRadiusMax);
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * rad;
            Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(_leaderWorldPosition + offset);

            var wc = NpcSpawnApi.WeaponClassForHalfMeleeHalfRangedSplit(i, followerCount);
            var e = NpcSpawnApi.SpawnFollower(pos, quaternion.identity, uniformScale: 1f, explicitWeaponClass: wc);
            if (e == Unity.Entities.Entity.Null)
            {
                Debug.LogWarning(
                    "FollowerSpawner: NpcSpawnApi.SpawnFollower failed (is NpcPrefabRegistryAuthoring in a loaded subscene with Follower prefab assigned?).");
                continue;
            }

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            NpcMovementApi.SetAnchorPosition(em, e, new float3(_leaderWorldPosition.x, _leaderWorldPosition.y, _leaderWorldPosition.z));
        }
    }
}
