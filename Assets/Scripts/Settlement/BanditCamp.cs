using UnityEngine;
using Medieval.Npcs;
using Unity.Mathematics;
using Random = UnityEngine.Random;

public class BanditCamp : MonoBehaviour
{
    const float PlayerSpawnDistance = 50f;
    const float PlayerSpawnDistanceSqr = PlayerSpawnDistance * PlayerSpawnDistance;

    static readonly System.Collections.Generic.HashSet<int> SpawnedCampIds = new();
    static Transform _cachedPlayer;

    [SerializeField] BanditController banditPrefab;
    [SerializeField] int banditCount = 3;
    [SerializeField] float spawnRadiusMin = 1f;
    [SerializeField] float spawnRadiusMax = 4f;
    [SerializeField] int campId = int.MinValue;

    bool _spawnAttempted;

    public void SetCampId(int id) => campId = id;

    void Start()
    {
        if (banditPrefab == null || banditCount <= 0)
        {
            _spawnAttempted = true;
            return;
        }

        if (HasSpawnedAlready())
        {
            _spawnAttempted = true;
            return;
        }
    }

    void Update()
    {
        if (_spawnAttempted || HasSpawnedAlready())
            return;

        Transform player = GetPlayerTransform();
        if (player == null)
            return;

        Vector3 delta = player.position - transform.position;
        if (delta.sqrMagnitude > PlayerSpawnDistanceSqr)
            return;

        SpawnBanditsNow();
    }

    bool HasSpawnedAlready()
    {
        return SpawnedCampIds.Contains(GetSpawnKey());
    }

    int GetSpawnKey()
    {
        if (campId != int.MinValue)
            return campId;

        Vector3 p = transform.position;
        int x = Mathf.RoundToInt(p.x * 10f);
        int z = Mathf.RoundToInt(p.z * 10f);
        return unchecked((x * 73856093) ^ (z * 19349663));
    }

    static Transform GetPlayerTransform()
    {
        if (_cachedPlayer != null)
            return _cachedPlayer;

        var player = FindFirstObjectByType<PlayerController>();
        if (player != null)
            _cachedPlayer = player.transform;

        return _cachedPlayer;
    }

    void SpawnBanditsNow()
    {
        _spawnAttempted = true;
        SpawnedCampIds.Add(GetSpawnKey());

        float minR = Mathf.Max(0f, spawnRadiusMin);
        float maxR = Mathf.Max(minR, spawnRadiusMax);
        for (int i = 0; i < banditCount; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float rad = Random.Range(minR, maxR);
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * rad;
            Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(transform.position + offset);

            // If a baked Entities Graphics bandit prefab is registered, prefer spawning DOTS bandits.
            if (NpcSpawnApi.SpawnBandit(pos, quaternion.identity))
                continue;

            BanditController bandit = Instantiate(banditPrefab, pos, Quaternion.identity);
            bandit.ApplyCombatRole(Random.value < 0.5f);
            bandit.Initialize(transform);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SpawnedCampIds.Clear();
        _cachedPlayer = null;
    }
}
