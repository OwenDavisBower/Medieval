using Medieval.NpcMovement;
using Medieval.Npcs;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Night raids on player-owned settlements: spawns bandit raiders and steals stock
/// when the player is too far to defend.
/// </summary>
public sealed class SettlementRaidService : MonoBehaviour
{
    public static SettlementRaidService Instance { get; private set; }

    const float RaidCheckInterval = 22f;
    const float RaidChancePerCheck = 0.4f;
    const float RaidCooldownSeconds = 95f;
    const float MinNightFactor = 0.55f;
    const int MinRaiders = 3;
    const int MaxRaiders = 6;
    const float SpawnRingRadius = 26f;
    const float UndefendedDistance = 85f;
    const int UndefendedWoodLoss = 10;
    const int UndefendedFoodLoss = 6;
    const int UndefendedReputationPenalty = 4;
    const float RaiderSpacing = 2.2f;

    float _nextCheckTime;
    float _lastRaidTime = -999f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<SettlementRaidService>() != null)
            return;

        var go = new GameObject("SettlementRaidService");
        go.AddComponent<SettlementRaidService>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _nextCheckTime = Time.time + RaidCheckInterval;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (Time.time < _nextCheckTime)
            return;

        _nextCheckTime = Time.time + RaidCheckInterval;

        if (DayNightCycle.NightFactor < MinNightFactor)
            return;
        if (Time.time - _lastRaidTime < RaidCooldownSeconds)
            return;
        if (!NpcSpawnApi.IsPrefabRegistryReady())
            return;

        var settlements = SettlementService.Instance;
        if (settlements == null)
            return;

        if (UnityEngine.Random.value > RaidChancePerCheck)
            return;

        if (!TryPickOwnedSettlement(settlements, out SettlementRecord target))
            return;

        if (!TrySpawnRaid(target, out int spawned))
            return;

        _lastRaidTime = Time.time;

        Transform player = PlayerReference.TryGetTransform();
        float playerDist = player != null
            ? HorizontalDistance(player.position, target.Center)
            : float.MaxValue;

        if (playerDist > UndefendedDistance)
        {
            ApplyUndefendedLosses(settlements, target);
            GameplayEvents.RaiseToast(
                $"{target.DisplayName} was raided while you were away! ({spawned} bandits)");
        }
        else
        {
            GameplayEvents.RaiseToast($"{target.DisplayName} is under attack!");
        }
    }

    static bool TryPickOwnedSettlement(SettlementService settlements, out SettlementRecord target)
    {
        target = null;
        var list = settlements.Settlements;
        int ownedCount = 0;
        for (int i = 0; i < list.Count; i++)
        {
            SettlementRecord s = list[i];
            if (s != null && s.OwnedByPlayer && !s.BuildFailed)
                ownedCount++;
        }

        if (ownedCount == 0)
            return false;

        int pick = UnityEngine.Random.Range(0, ownedCount);
        for (int i = 0; i < list.Count; i++)
        {
            SettlementRecord s = list[i];
            if (s == null || !s.OwnedByPlayer || s.BuildFailed)
                continue;
            if (pick-- == 0)
            {
                target = s;
                return true;
            }
        }

        return false;
    }

    bool TrySpawnRaid(SettlementRecord settlement, out int spawned)
    {
        spawned = 0;
        Vector3 center = settlement.Center;
        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        Vector3 approach = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 right = Vector3.Cross(Vector3.up, approach).normalized;

        int count = UnityEngine.Random.Range(MinRaiders, MaxRaiders + 1);
        for (int i = 0; i < count; i++)
        {
            float lateral = (i - (count - 1) * 0.5f) * RaiderSpacing;
            Vector3 offset = approach * SpawnRingRadius + right * lateral;
            Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(center + offset);
            var wc = NpcSpawnApi.WeaponClassForHalfMeleeHalfRangedSplit(i, count);
            var e = NpcSpawnApi.SpawnBandit(pos, quaternion.LookRotationSafe(
                new float3(-approach.x, 0f, -approach.z), new float3(0f, 1f, 0f)), 1f, wc);
            if (e == Entity.Null)
                continue;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
                NpcMovementApi.SetAnchorPosition(world.EntityManager, e, new float3(center.x, center.y, center.z));

            spawned++;
        }

        return spawned > 0;
    }

    static void ApplyUndefendedLosses(SettlementService settlements, SettlementRecord settlement)
    {
        settlement.WoodStock = Mathf.Max(0, settlement.WoodStock - UndefendedWoodLoss);
        settlement.FoodStock = Mathf.Max(0, settlement.FoodStock - UndefendedFoodLoss);
        settlements.AddReputation(settlement.Id, -UndefendedReputationPenalty);
    }

    static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
