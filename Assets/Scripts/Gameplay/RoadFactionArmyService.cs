using Medieval.NpcMovement;
using Medieval.Npcs;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Spawns small neutral-kingdom patrols ahead of the player during open travel.
/// Troops are neutral to the player (will not aggro) and hostile to bandits.
/// </summary>
public sealed class RoadFactionArmyService : MonoBehaviour
{
    public static RoadFactionArmyService Instance { get; private set; }

    const int MinSoldiers = 2;
    const int MaxSoldiers = 4;
    const float SpawnDistanceAhead = 18f;
    const float SpawnLateralJitter = 3.5f;
    const float SoldierSpacing = 2.8f;
    const float OpenCooldownSeconds = 70f;
    const float OpenMinTravelMeters = 55f;
    const float DayPatrolChance = 0.28f;
    const float NightPatrolChance = 0.12f;
    const float MinMoveSpeedForOpen = 2f;
    const float SettlementExclusionRadius = 40f;
    const float CampExclusionRadius = 50f;

    Vector3 _lastPlayerPos;
    bool _hasLastPlayerPos;
    Vector3 _travelForward = Vector3.forward;
    float _lastPatrolTime = -999f;
    float _travelSinceCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<RoadFactionArmyService>() != null)
            return;

        var go = new GameObject("RoadFactionArmyService");
        go.AddComponent<RoadFactionArmyService>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        // Escort routes keep bandit ambushes; skip kingdom patrols during escorts.
        if (QuestService.Instance != null && QuestService.Instance.FindActiveEscortQuest() != null)
            return;

        Transform player = PlayerReference.TryGetTransform();
        if (player == null)
            return;

        Vector3 pos = player.position;
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        if (_hasLastPlayerPos)
        {
            Vector3 delta = pos - _lastPlayerPos;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist > 0.01f)
            {
                _travelForward = delta / dist;
                float speed = dist / dt;
                _travelSinceCheck += dist;
                if (speed >= MinMoveSpeedForOpen)
                    TryTriggerOpenPatrol(pos);
            }
        }

        _lastPlayerPos = pos;
        _hasLastPlayerPos = true;
    }

    void TryTriggerOpenPatrol(Vector3 playerPos)
    {
        if (Time.time - _lastPatrolTime < OpenCooldownSeconds)
            return;
        if (_travelSinceCheck < OpenMinTravelMeters)
            return;
        if (IsNearSettlement(playerPos, SettlementExclusionRadius))
            return;
        if (!CanSpawnAt(playerPos))
        {
            _travelSinceCheck = 0f;
            return;
        }

        _travelSinceCheck = 0f;
        float night = DayNightCycle.NightFactor;
        float chance = Mathf.Lerp(DayPatrolChance, NightPatrolChance, night);
        if (UnityEngine.Random.value > chance)
            return;

        int factionId = PickPatrolFaction(playerPos);
        if (!SpawnPatrol(playerPos, _travelForward, factionId, out string factionName))
            return;

        _lastPatrolTime = Time.time;
        GameplayEvents.RaiseToast(string.IsNullOrEmpty(factionName)
            ? "A patrol approaches."
            : $"{factionName} patrol ahead.");
    }

    static int PickPatrolFaction(Vector3 playerPos)
    {
        // Prefer a nearby kingdom that owns a settlement; otherwise roll any neutral kingdom.
        var settlements = SettlementService.Instance;
        if (settlements != null)
        {
            SettlementRecord nearestOwned = null;
            float bestSq = 220f * 220f;
            var list = settlements.Settlements;
            for (int i = 0; i < list.Count; i++)
            {
                SettlementRecord s = list[i];
                if (s == null || !s.IsOwnedByNeutralKingdom)
                    continue;
                float dx = playerPos.x - s.Center.x;
                float dz = playerPos.z - s.Center.z;
                float sq = dx * dx + dz * dz;
                if (sq >= bestSq)
                    continue;
                bestSq = sq;
                nearestOwned = s;
            }

            if (nearestOwned != null)
                return nearestOwned.OwnerFactionId;
        }

        int[] kingdoms = WellKnownFactionIds.NeutralKingdomIds;
        return kingdoms[UnityEngine.Random.Range(0, kingdoms.Length)];
    }

    bool CanSpawnAt(Vector3 playerPos)
    {
        if (!NpcSpawnApi.IsPrefabRegistryReady())
            return false;
        if (IsNearCamp(playerPos, CampExclusionRadius))
            return false;
        return true;
    }

    bool SpawnPatrol(Vector3 playerPos, Vector3 forward, int factionId, out string factionName)
    {
        factionName = null;
        if (FactionManager.Instance != null)
            FactionManager.Instance.TryGetFactionName(factionId, out factionName);

        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        int count = UnityEngine.Random.Range(MinSoldiers, MaxSoldiers + 1);
        EntityManager em = world.EntityManager;
        int spawned = 0;
        Vector3 anchor = playerPos + forward * SpawnDistanceAhead;
        anchor = TerrainSpawnUtility.GetWorldPositionOnTerrain(anchor);

        for (int i = 0; i < count; i++)
        {
            float side = count == 1
                ? 0f
                : Mathf.Lerp(-1f, 1f, i / (float)(count - 1));
            float lateral = side * SoldierSpacing * Mathf.Max(1f, (count - 1) * 0.5f);
            lateral += UnityEngine.Random.Range(-SpawnLateralJitter, SpawnLateralJitter);
            float ahead = SpawnDistanceAhead + UnityEngine.Random.Range(-1.2f, 1.2f);
            Vector3 offset = forward * ahead + right * lateral;
            Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(playerPos + offset);

            var wc = NpcSpawnApi.WeaponClassForHalfMeleeHalfRangedSplit(i, count);
            quaternion face = quaternion.LookRotationSafe(
                new float3(-forward.x, 0f, -forward.z),
                new float3(0f, 1f, 0f));
            Entity e = NpcSpawnApi.SpawnFactionSoldier(pos, face, factionId, 1f, wc);
            if (e == Entity.Null)
            {
                Debug.LogWarning(
                    "RoadFactionArmyService: SpawnFactionSoldier failed (Bandit prefab registry ready?).");
                continue;
            }

            NpcMovementApi.SetAnchorPosition(em, e, new float3(anchor.x, anchor.y, anchor.z));
            spawned++;
        }

        return spawned > 0;
    }

    static bool IsNearSettlement(Vector3 worldPos, float radius)
    {
        var settlements = SettlementService.Instance;
        if (settlements == null)
            return false;

        float rSq = radius * radius;
        var list = settlements.Settlements;
        for (int i = 0; i < list.Count; i++)
        {
            SettlementRecord s = list[i];
            if (s == null)
                continue;
            float dx = worldPos.x - s.Center.x;
            float dz = worldPos.z - s.Center.z;
            if (dx * dx + dz * dz <= rSq)
                return true;
        }

        return false;
    }

    static bool IsNearCamp(Vector3 worldPos, float radius)
    {
        var settlements = SettlementService.Instance;
        if (settlements == null)
            return false;

        float rSq = radius * radius;
        var list = settlements.Camps;
        for (int i = 0; i < list.Count; i++)
        {
            CampRecord c = list[i];
            if (c == null || c.Cleared)
                continue;
            float dx = worldPos.x - c.Center.x;
            float dz = worldPos.z - c.Center.z;
            if (dx * dx + dz * dz <= rSq)
                return true;
        }

        return false;
    }
}
