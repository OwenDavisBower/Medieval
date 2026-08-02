using Medieval.NpcMovement;
using Medieval.Npcs;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Spawns small roadside bandit ambushes during travel and once mid-route on escort quests.
/// Bandits emerge from nearby trees with an outward leap when trees are available.
/// </summary>
public sealed class RoadAmbushService : MonoBehaviour
{
    public static RoadAmbushService Instance { get; private set; }

    const int MinBanditsPerAmbush = 1;
    const int MaxBanditsPerAmbush = 4;
    const float SpawnDistanceAhead = 16f;
    const float SpawnLateralJitter = 4f;
    const float EscortRouteTriggerFraction = 0.4f;
    const float OpenCooldownSeconds = 45f;
    const float OpenMinTravelMeters = 40f;
    const float OpenAmbushChance = 0.7f;
    const float MinMoveSpeedForOpen = 2f;
    const float SettlementExclusionRadius = 35f;
    const float CampExclusionRadius = 60f;
    const float BanditSpacing = 2.5f;

    // Path occupancy stamps ~8m and tree placement needs a free disk (~4m), so trunks sit
    // ~12m+ off the road centerline. Search in that belt with a wide enough radius.
    const float TreeLineLateralOffset = 14f;
    const float TreeSearchRadius = 36f;
    const float TreeExitDistance = 3.6f;
    const float TreeSpawnHeight = 2.4f;
    const float LeapImpulseSpeed = 9.5f;
    const float LeapDurationSeconds = 0.55f;
    const float LeapGroundSnapSmoothTime = 0.4f;

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

                var quest = QuestService.Instance != null
                    ? QuestService.Instance.FindActiveEscortQuest()
                    : null;
                if (quest != null)
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

    void TryTriggerEscortAmbush(QuestInstance quest, Vector3 playerPos)
    {
        if (quest == null || !quest.TryGetActiveEscortObjective(out QuestObjective step))
            return;
        if (step.EscortRouteLength <= 1f)
            return;

        if (!HasReachedEscortAmbushPoint(quest, step, playerPos))
            return;

        if (!CanSpawnAt(playerPos))
            return;

        if (!SpawnAmbush(playerPos, _travelForward))
            return;

        step.AmbushTriggered = true;
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

        if (!SpawnAmbush(playerPos, _travelForward))
            return;

        _lastAmbushTime = Time.time;
        GameplayEvents.RaiseToast("Ambush!");
    }

    static bool HasReachedEscortAmbushPoint(QuestInstance quest, QuestObjective step, Vector3 playerPos)
    {
        float routeLength = step.EscortRouteLength;
        float threshold = routeLength * EscortRouteTriggerFraction;
        int originId = step.EscortOriginSettlementId >= 0
            ? step.EscortOriginSettlementId
            : quest.OriginSettlementId;

        if (step.EscortOriginSettlementId >= 0 &&
            SettlementService.Instance != null &&
            SettlementService.Instance.TryGetSettlement(originId, out SettlementRecord origin) &&
            origin != null)
        {
            return HorizontalDistance(playerPos, origin.Center) >= threshold;
        }

        // Rescue / camp-origin escorts: measure progress toward the destination.
        float toDest = HorizontalDistance(playerPos, step.TargetPosition);
        float traveled = routeLength - toDest;
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

    bool SpawnAmbush(Vector3 playerPos, Vector3 forward)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        int banditCount = UnityEngine.Random.Range(MinBanditsPerAmbush, MaxBanditsPerAmbush + 1);
        EntityManager em = world.EntityManager;
        int spawned = 0;
        var usedTrees = new float3[banditCount];
        int usedTreeCount = 0;

        for (int i = 0; i < banditCount; i++)
        {
            float side = banditCount == 1
                ? (UnityEngine.Random.value < 0.5f ? -1f : 1f)
                : Mathf.Lerp(-1f, 1f, i / (float)(banditCount - 1));
            float roadLateral = side * BanditSpacing * Mathf.Max(1f, (banditCount - 1) * 0.5f);
            roadLateral += UnityEngine.Random.Range(-SpawnLateralJitter, SpawnLateralJitter);
            float treeLateral = side * TreeLineLateralOffset;
            treeLateral += UnityEngine.Random.Range(-SpawnLateralJitter, SpawnLateralJitter);
            float ahead = SpawnDistanceAhead + UnityEngine.Random.Range(-1.5f, 1.5f);
            Vector3 searchCenter = playerPos + forward * ahead + right * treeLateral;

            var wc = NpcSpawnApi.WeaponClassForHalfMeleeHalfRangedSplit(i, banditCount);

            if (TryPickAmbushTree(em, searchCenter, TreeSearchRadius, usedTrees, usedTreeCount, out float3 treePos))
            {
                if (usedTreeCount < usedTrees.Length)
                    usedTrees[usedTreeCount++] = treePos;

                Vector3 roadPoint = playerPos + forward * ahead;
                float3 outward = new float3(roadPoint.x - treePos.x, 0f, roadPoint.z - treePos.z);
                if (math.lengthsq(outward) < 1e-4f)
                    outward = new float3(right.x * -side, 0f, right.z * -side);
                outward = math.normalize(outward);

                float3 landing = treePos + outward * TreeExitDistance;
                landing = new float3(
                    landing.x,
                    TerrainSpawnUtility.GetWorldPositionOnTerrain(new Vector3(landing.x, landing.y, landing.z)).y,
                    landing.z);

                Vector3 spawnPos = new Vector3(treePos.x, treePos.y + TreeSpawnHeight, treePos.z);
                quaternion face = quaternion.LookRotationSafe(outward, new float3(0f, 1f, 0f));
                Entity e = NpcSpawnApi.SpawnBandit(spawnPos, face, 1f, wc);
                if (e == Entity.Null)
                {
                    Debug.LogWarning(
                        "RoadAmbushService: NpcSpawnApi.SpawnBandit failed (is NpcPrefabRegistryAuthoring ready with Bandit prefab?).");
                    continue;
                }

                NpcMovementApi.StartEmergeLeap(
                    em, e, landing, outward, LeapImpulseSpeed, LeapDurationSeconds, LeapGroundSnapSmoothTime);
                spawned++;
                continue;
            }

            // Fallback: open roadside spawn if no tree is in range.
            Vector3 offset = forward * ahead + right * roadLateral;
            Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(playerPos + offset);
            Entity open = NpcSpawnApi.SpawnBandit(pos, quaternion.identity, 1f, wc);
            if (open == Entity.Null)
            {
                Debug.LogWarning(
                    "RoadAmbushService: NpcSpawnApi.SpawnBandit failed (is NpcPrefabRegistryAuthoring ready with Bandit prefab?).");
                continue;
            }

            NpcMovementApi.SetAnchorPosition(em, open, new float3(pos.x, pos.y, pos.z));
            spawned++;
        }

        return spawned > 0;
    }

    static bool TryPickAmbushTree(
        EntityManager em,
        Vector3 searchCenter,
        float maxDistance,
        float3[] usedTrees,
        int usedTreeCount,
        out float3 treePos)
    {
        treePos = default;
        using var q = em.CreateEntityQuery(ComponentType.ReadOnly<WorldStreamingTreesSingletonTag>());
        if (q.IsEmpty)
            return false;

        Entity singleton = q.GetSingletonEntity();
        if (!em.HasBuffer<StreamingTreePosition>(singleton))
            return false;

        DynamicBuffer<StreamingTreePosition> trees = em.GetBuffer<StreamingTreePosition>(singleton, isReadOnly: true);
        if (trees.Length == 0)
            return false;

        float maxSq = maxDistance * maxDistance;
        float bestSq = maxSq;
        bool found = false;

        for (int i = 0; i < trees.Length; i++)
        {
            float3 p = trees[i].Position;
            if (IsTreeUsed(p, usedTrees, usedTreeCount))
                continue;

            float dx = p.x - searchCenter.x;
            float dz = p.z - searchCenter.z;
            float dSq = dx * dx + dz * dz;
            if (dSq > bestSq)
                continue;

            bestSq = dSq;
            treePos = p;
            found = true;
        }

        return found;
    }

    static bool IsTreeUsed(float3 treePos, float3[] usedTrees, int usedTreeCount)
    {
        for (int i = 0; i < usedTreeCount; i++)
        {
            float ux = treePos.x - usedTrees[i].x;
            float uz = treePos.z - usedTrees[i].z;
            if (ux * ux + uz * uz < 0.25f)
                return true;
        }

        return false;
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
