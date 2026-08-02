using System;
using Medieval.NpcMovement;
using Medieval.Npcs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>Offers and tracks clear-camp, deliver-wood, and escort quests.</summary>
public sealed class QuestService : MonoBehaviour
{
    public static QuestService Instance { get; private set; }

    public ActiveQuest Active { get; private set; }

    public event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<QuestService>() != null)
            return;

        var go = new GameObject("QuestService");
        go.AddComponent<QuestService>();
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

    void OnEnable() => GameplayEvents.EnemyKilled += OnEnemyKilled;

    void OnDisable() => GameplayEvents.EnemyKilled -= OnEnemyKilled;

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (Active == null || Active.Status != QuestStatus.Active)
            return;

        switch (Active.Type)
        {
            case QuestType.ClearCamp:
                TickClearCamp();
                break;
            case QuestType.Escort:
                TickEscort();
                break;
        }
    }

    public bool HasActiveQuest => Active != null && Active.Status == QuestStatus.Active;

    public bool TryAcceptClearCamp(SettlementRecord settlement)
    {
        if (!BeginGuard(settlement))
            return false;

        var settlements = SettlementService.Instance;
        CampRecord camp = settlements.FindUnclearedCampLinkedTo(settlement.Id)
                          ?? settlements.FindNearestUnclearedCamp(settlement.Center, 160f);
        if (camp == null)
        {
            GameplayEvents.RaiseToast("No bandit camps threaten this village.");
            return false;
        }

        int required = Mathf.Max(3, camp.SpawnedBanditCount > 0 ? camp.SpawnedBanditCount : 3);
        Active = new ActiveQuest
        {
            Type = QuestType.ClearCamp,
            OriginSettlementId = settlement.Id,
            TargetCampId = camp.Id,
            TargetPosition = camp.Center,
            RequiredKills = required,
            ProgressKills = camp.KilledNearCamp,
            Title = "Clear the Camp",
            Description = $"Defeat the bandits camping near {settlement.DisplayName}.",
            GoldReward = 35 + required * 4,
            ReputationReward = 22
        };

        if (camp.Cleared || Active.ProgressKills >= Active.RequiredKills)
            CompleteActive("Camp already cleared!");
        else
        {
            GameplayEvents.RaiseToast("Quest: Clear the Camp");
            Changed?.Invoke();
        }

        return true;
    }

    public bool TryAcceptDeliverWood(SettlementRecord settlement)
    {
        if (!BeginGuard(settlement))
            return false;

        const int need = 8;
        Active = new ActiveQuest
        {
            Type = QuestType.DeliverWood,
            OriginSettlementId = settlement.Id,
            RequiredWood = need,
            TargetPosition = settlement.Center,
            Title = "Deliver Wood",
            Description = $"Bring {need} wood to {settlement.DisplayName}. Buy it here or haul it from elsewhere.",
            GoldReward = 28,
            ReputationReward = 12
        };

        GameplayEvents.RaiseToast("Quest: Deliver Wood");
        Changed?.Invoke();
        return true;
    }

    public bool TryAcceptEscort(SettlementRecord settlement)
    {
        if (!BeginGuard(settlement))
            return false;

        var settlements = SettlementService.Instance;
        SettlementRecord dest = null;
        float best = float.MaxValue;
        for (int i = 0; i < settlements.Settlements.Count; i++)
        {
            SettlementRecord s = settlements.Settlements[i];
            if (s.Id == settlement.Id)
                continue;
            float dx = s.Center.x - settlement.Center.x;
            float dz = s.Center.z - settlement.Center.z;
            float sq = dx * dx + dz * dz;
            if (sq < best && sq > 40f * 40f)
            {
                best = sq;
                dest = s;
            }
        }

        if (dest == null)
        {
            GameplayEvents.RaiseToast("No other village to escort to.");
            return false;
        }

        Transform player = PlayerReference.TryGetTransform();
        if (player == null || PartyManager.Instance == null)
            return false;

        Entity escort = PartyManager.Instance.SpawnEscortFollower(player.position);
        if (escort == Entity.Null)
        {
            GameplayEvents.RaiseToast("Could not find an escort.");
            return false;
        }

        float routeDx = dest.Center.x - settlement.Center.x;
        float routeDz = dest.Center.z - settlement.Center.z;
        float routeLength = Mathf.Sqrt(routeDx * routeDx + routeDz * routeDz);

        Active = new ActiveQuest
        {
            Type = QuestType.Escort,
            OriginSettlementId = settlement.Id,
            TargetSettlementId = dest.Id,
            TargetPosition = dest.Center,
            EscortEntity = escort,
            Title = "Escort Villager",
            Description = $"Safely bring the villager to {dest.DisplayName}.",
            GoldReward = 40,
            ReputationReward = 16,
            EscortRouteLength = routeLength,
            AmbushTriggered = false
        };

        GameplayEvents.RaiseToast($"Quest: Escort to {dest.DisplayName}");
        Changed?.Invoke();
        return true;
    }

    public bool TryTurnInDeliverWood()
    {
        if (Active == null || Active.Type != QuestType.DeliverWood || Active.Status != QuestStatus.Active)
            return false;

        var inv = PlayerInventory.Instance;
        if (inv == null || !inv.TrySpendWood(Active.RequiredWood))
        {
            GameplayEvents.RaiseToast($"Need {Active.RequiredWood} wood to deliver.");
            return false;
        }

        if (SettlementService.Instance != null &&
            SettlementService.Instance.TryGetSettlement(Active.OriginSettlementId, out SettlementRecord s))
            s.WoodStock += Active.RequiredWood;

        CompleteActive("Wood delivered!");
        return true;
    }

    public void Abandon()
    {
        if (Active == null)
            return;

        if (Active.Type == QuestType.Escort && Active.EscortEntity != Entity.Null)
            DestroyEscort(Active.EscortEntity);

        Active.Status = QuestStatus.Failed;
        GameplayEvents.RaiseToast("Quest abandoned.");
        Active = null;
        Changed?.Invoke();
    }

    bool BeginGuard(SettlementRecord settlement)
    {
        if (settlement == null)
        {
            GameplayEvents.RaiseToast("No village nearby.");
            return false;
        }

        if (HasActiveQuest)
        {
            GameplayEvents.RaiseToast("Finish or abandon your current quest first.");
            return false;
        }

        return true;
    }

    void OnEnemyKilled(Vector3 worldPosition, int _, bool byPlayerOrFollower)
    {
        if (!byPlayerOrFollower)
            return;
        if (Active == null || Active.Type != QuestType.ClearCamp || Active.Status != QuestStatus.Active)
            return;
        if (!SettlementService.Instance.TryGetCamp(Active.TargetCampId, out CampRecord camp))
            return;

        float dx = worldPosition.x - camp.Center.x;
        float dz = worldPosition.z - camp.Center.z;
        if (dx * dx + dz * dz > 60f * 60f)
            return;

        Active.ProgressKills++;
        Changed?.Invoke();
        if (Active.ProgressKills >= Active.RequiredKills || camp.Cleared)
            CompleteActive("Camp cleared!");
    }

    void TickClearCamp()
    {
        if (SettlementService.Instance != null &&
            SettlementService.Instance.TryGetCamp(Active.TargetCampId, out CampRecord camp) &&
            camp.Cleared)
            CompleteActive("Camp cleared!");
    }

    void TickEscort()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        EntityManager em = world.EntityManager;
        if (Active.EscortEntity == Entity.Null || !em.Exists(Active.EscortEntity) ||
            em.HasComponent<NpcDeadTag>(Active.EscortEntity))
        {
            GameplayEvents.RaiseToast("Escort died — quest failed.");
            Active.Status = QuestStatus.Failed;
            Active = null;
            Changed?.Invoke();
            return;
        }

        // Keep escort anchored near the player.
        Transform player = PlayerReference.TryGetTransform();
        if (player != null)
        {
            NpcMovementApi.SetAnchorPosition(em, Active.EscortEntity,
                new float3(player.position.x, player.position.y, player.position.z));
        }

        float3 escortPos = em.GetComponentData<LocalTransform>(Active.EscortEntity).Position;
        Vector3 target = Active.TargetPosition;
        float edx = escortPos.x - target.x;
        float edz = escortPos.z - target.z;
        if (edx * edx + edz * edz > 16f * 16f)
            return;

        if (player != null)
        {
            float pdx = player.position.x - target.x;
            float pdz = player.position.z - target.z;
            if (pdx * pdx + pdz * pdz > 20f * 20f)
                return;
        }

        DestroyEscort(Active.EscortEntity);
        Active.EscortEntity = Entity.Null;
        CompleteActive("Escort arrived safely!");
    }

    void CompleteActive(string toast)
    {
        if (Active == null)
            return;

        Active.Status = QuestStatus.Completed;
        var wallet = PlayerWallet.Instance;
        wallet?.Add(Active.GoldReward);

        if (SettlementService.Instance != null && Active.OriginSettlementId >= 0)
            SettlementService.Instance.AddReputation(Active.OriginSettlementId, Active.ReputationReward);

        // Escort also boosts destination standing a bit.
        if (Active.Type == QuestType.Escort && Active.TargetSettlementId >= 0 && SettlementService.Instance != null)
            SettlementService.Instance.AddReputation(Active.TargetSettlementId, Active.ReputationReward / 2);

        GameplayEvents.RaiseToast($"{toast} +{Active.GoldReward}g");
        Active = null;
        Changed?.Invoke();
    }

    static void DestroyEscort(Entity escort)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || escort == Entity.Null)
            return;
        var em = world.EntityManager;
        if (em.Exists(escort))
            NpcEntityDestroyUtility.DestroyNpcWithLinked(em, escort);
    }
}
