using Medieval.NpcMovement;
using Medieval.Npcs;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Spawns small roadside bandit ambushes during travel and once mid-route on escort quests.
/// </summary>
public sealed class RoadAmbushService : MonoBehaviour
{
    public static RoadAmbushService Instance { get; private set; }

    const int BanditsPerAmbush = 2;
    const float SpawnDistanceAhead = 16f;
    const float SpawnLateralJitter = 4f;
    const float EscortRouteTriggerFraction = 0.4f;
    const float OpenCooldownSeconds = 100f;
    const float OpenMinTravelMeters = 80f;
    const float OpenAmbushChance = 0.45f;
    const float MinMoveSpeedForOpen = 2f;
    const float SettlementExclusionRadius = 35f;
    const float CampExclusionRadius = 60f;
    const float BanditSpacing = 2.5f;

    Vector3 _lastPlayerPos;
    bool _hasLastPlayerPos;
    Vector3 _travelForward = Vector3.forward;
    float _lastAmbushTime = -999f;
    float _travelSinceOpenCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<RoadAmbushService>() != null)
            return;

        var go = new GameObject("RoadAmbushService");
        go.AddComponent<RoadAmbushService>();
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
                _travelSinceOpenCheck += dist;

                var quest = QuestService.Instance != null ? QuestService.Instance.Active : null;
                bool escortActive = quest != null &&
                                    quest.Type == QuestType.Escort &&
                                    quest.Status == QuestStatus.Active;
                if (escortActive)
                {
                    if (!quest.AmbushTriggered)
                        TryTriggerEscortAmbush(quest, pos);
                }
                else if (speed >= MinMoveSpeedForOpen)
                {
                    TryTriggerOpenAmbush(pos);
                }
            }
        }

        _lastPlayerPos = pos;
        _hasLastPlayerPos = true;
    }

    void TryTriggerEscortAmbush(ActiveQuest quest, Vector3 playerPos)
    {
        if (quest.EscortRouteLength <= 1f)
            return;

        if (!HasReachedEscortAmbushPoint(quest, playerPos))
            return;

        if (!CanSpawnAt(playerPos))
            return;

        if (!SpawnAmbushPair(playerPos, _travelForward))
            return;

        quest.AmbushTriggered = true;
        _lastAmbushTime = Time.time;
        _travelSinceOpenCheck = 0f;
        GameplayEvents.RaiseToast("Ambush!");
    }

    void TryTriggerOpenAmbush(Vector3 playerPos)
    {
        if (Time.time - _lastAmbushTime < OpenCooldownSeconds)
            return;
        if (_travelSinceOpenCheck < OpenMinTravelMeters)
            return;
        if (IsNearSettlement(playerPos, SettlementExclusionRadius))
            return;
        if (!CanSpawnAt(playerPos))
        {
            // Burn travel so we re-check after more movement instead of every frame.
            _travelSinceOpenCheck = 0f;
            return;
        }

        _travelSinceOpenCheck = 0f;
        if (UnityEngine.Random.value > OpenAmbushChance)
            return;

        if (!SpawnAmbushPair(playerPos, _travelForward))
            return;

        _lastAmbushTime = Time.time;
        GameplayEvents.RaiseToast("Ambush!");
    }

    static bool HasReachedEscortAmbushPoint(ActiveQuest quest, Vector3 playerPos)
    {
        float threshold = quest.EscortRouteLength * EscortRouteTriggerFraction;
        if (SettlementService.Instance != null &&
            quest.OriginSettlementId >= 0 &&
            SettlementService.Instance.TryGetSettlement(quest.OriginSettlementId, out SettlementRecord origin) &&
            origin != null)
        {
            return HorizontalDistance(playerPos, origin.Center) >= threshold;
        }

        float toDest = HorizontalDistance(playerPos, quest.TargetPosition);
        float traveled = quest.EscortRouteLength - toDest;
        return traveled >= threshold;
    }

    bool CanSpawnAt(Vector3 playerPos)
    {
        if (!NpcSpawnApi.IsPrefabRegistryReady())
            return false;
        if (IsNearCamp(playerPos, CampExclusionRadius))
            return false;
        return true;
    }

    bool SpawnAmbushPair(Vector3 playerPos, Vector3 forward)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        EntityManager em = world.EntityManager;
        int spawned = 0;

        for (int i = 0; i < BanditsPerAmbush; i++)
        {
            float lateral = (i == 0 ? -1f : 1f) * (BanditSpacing * 0.5f);
            lateral += UnityEngine.Random.Range(-SpawnLateralJitter, SpawnLateralJitter);
            float ahead = SpawnDistanceAhead + UnityEngine.Random.Range(-1.5f, 1.5f);
            Vector3 offset = forward * ahead + right * lateral;
            Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(playerPos + offset);

            var wc = NpcSpawnApi.WeaponClassForHalfMeleeHalfRangedSplit(i, BanditsPerAmbush);
            Entity e = NpcSpawnApi.SpawnBandit(pos, quaternion.identity, 1f, wc);
            if (e == Entity.Null)
            {
                Debug.LogWarning(
                    "RoadAmbushService: NpcSpawnApi.SpawnBandit failed (is NpcPrefabRegistryAuthoring ready with Bandit prefab?).");
                continue;
            }

            NpcMovementApi.SetAnchorPosition(em, e, new float3(pos.x, pos.y, pos.z));
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

    static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
