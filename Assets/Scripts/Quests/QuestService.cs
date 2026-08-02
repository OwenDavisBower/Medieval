using System;
using System.Collections.Generic;
using Medieval.NpcMovement;
using Medieval.Npcs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Multi-quest journal with composable objectives. Seeds offers from settlement/camp world state.
/// </summary>
public sealed class QuestService : MonoBehaviour
{
    public const int MaxActiveQuests = 3;
    public const int MaxJournalEntries = 24;
    public const float KillNearRadius = 60f;
    public const float EscortArriveRadius = 16f;
    public const float PlayerArriveRadius = 20f;
    public const float EscortAbandonDistance = 55f;

    public static QuestService Instance { get; private set; }

    readonly List<QuestInstance> _active = new List<QuestInstance>(MaxActiveQuests);
    readonly List<QuestInstance> _journal = new List<QuestInstance>(MaxJournalEntries);
    readonly List<QuestOffer> _offerScratch = new List<QuestOffer>(4);
    int _nextId = 1;
    int _lastDeliverWoodSeen = -1;
    PlayerInventory _boundInventory;

    /// <summary>Currently tracked quest (guidance / HUD focus).</summary>
    public QuestInstance Tracked { get; private set; }

    /// <summary>Compatibility alias for tracked quest.</summary>
    public QuestInstance Active => Tracked;

    public IReadOnlyList<QuestInstance> ActiveQuests => _active;
    public IReadOnlyList<QuestInstance> Journal => _journal;

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

    void OnDisable()
    {
        GameplayEvents.EnemyKilled -= OnEnemyKilled;
        UnbindInventory();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        TryBindInventory();
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            QuestInstance quest = _active[i];
            if (quest == null || quest.Status != QuestStatus.Active)
                continue;
            TickQuest(quest);
        }
    }

    void TryBindInventory()
    {
        if (_boundInventory != null || PlayerInventory.Instance == null)
            return;
        _boundInventory = PlayerInventory.Instance;
        _boundInventory.Changed += OnInventoryChanged;
        _lastDeliverWoodSeen = _boundInventory.Wood;
    }

    void UnbindInventory()
    {
        if (_boundInventory != null)
            _boundInventory.Changed -= OnInventoryChanged;
        _boundInventory = null;
    }

    public bool HasActiveQuest => _active.Count > 0;

    public int ActiveCount => _active.Count;

    public bool HasActiveTypeFrom(QuestType type, int originSettlementId)
    {
        for (int i = 0; i < _active.Count; i++)
        {
            QuestInstance q = _active[i];
            if (q != null && q.Status == QuestStatus.Active &&
                q.Type == type && q.OriginSettlementId == originSettlementId)
                return true;
        }

        return false;
    }

    public void GetOffers(SettlementRecord settlement, List<QuestOffer> into)
    {
        QuestTemplateFactory.BuildOffers(settlement, this, into);
    }

    public IReadOnlyList<QuestOffer> GetOffers(SettlementRecord settlement)
    {
        QuestTemplateFactory.BuildOffers(settlement, this, _offerScratch);
        return _offerScratch;
    }

    public bool TryAcceptOffer(QuestOffer offer)
    {
        if (offer == null)
            return false;
        if (!BeginGuard())
            return false;

        QuestInstance quest = QuestTemplateFactory.CreateFromOffer(offer);
        if (quest == null)
            return false;

        // Escort template needs a live follower at accept time.
        if (offer.Type == QuestType.Escort)
        {
            if (!TrySpawnEscortFor(quest, quest.CurrentObjective, atPlayer: true))
                return false;
        }

        return ActivateQuest(quest, $"Quest: {quest.Title}");
    }

    public bool TryAcceptClearCamp(SettlementRecord settlement) =>
        TryAcceptFirstOfType(settlement, QuestType.ClearCamp);

    public bool TryAcceptDeliverWood(SettlementRecord settlement) =>
        TryAcceptFirstOfType(settlement, QuestType.DeliverWood);

    public bool TryAcceptEscort(SettlementRecord settlement) =>
        TryAcceptFirstOfType(settlement, QuestType.Escort);

    public bool TryAcceptOfferAt(SettlementRecord settlement, int offerIndex)
    {
        if (settlement == null)
        {
            GameplayEvents.RaiseToast("No village nearby.");
            return false;
        }

        GetOffers(settlement, _offerScratch);
        if (offerIndex < 0 || offerIndex >= _offerScratch.Count)
        {
            GameplayEvents.RaiseToast("No quest available.");
            return false;
        }

        return TryAcceptOffer(_offerScratch[offerIndex]);
    }

    public bool TryTurnInAt(SettlementRecord settlement)
    {
        if (settlement == null)
            return false;

        for (int i = 0; i < _active.Count; i++)
        {
            QuestInstance quest = _active[i];
            if (quest == null || !quest.NeedsTurnInAt(settlement.Id))
                continue;

            var step = quest.CurrentObjective;
            if (step == null)
                continue;

            if (step.Kind == QuestObjectiveKind.DeliverItem)
            {
                var inv = PlayerInventory.Instance;
                if (inv == null || !inv.TrySpendWood(step.RequiredCount))
                {
                    GameplayEvents.RaiseToast($"Need {step.RequiredCount} wood to deliver.");
                    return false;
                }

                if (SettlementService.Instance != null &&
                    SettlementService.Instance.TryGetSettlement(step.TargetSettlementId, out SettlementRecord dest) &&
                    dest != null)
                    dest.WoodStock += step.RequiredCount;

                CompleteObjective(quest, step, "Wood delivered!");
                return true;
            }

            if (step.Kind == QuestObjectiveKind.ReportBack)
            {
                CompleteObjective(quest, step, "Reported in.");
                return true;
            }
        }

        return false;
    }

    /// <summary>Legacy entry used by village controller deliver/turn-in key.</summary>
    public bool TryTurnInDeliverWood()
    {
        var nearby = VillageInteractionController.Instance != null
            ? VillageInteractionController.Instance.NearbySettlement
            : null;
        return TryTurnInAt(nearby);
    }

    public bool HasTurnInAt(SettlementRecord settlement)
    {
        if (settlement == null)
            return false;
        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i] != null && _active[i].NeedsTurnInAt(settlement.Id))
                return true;
        }

        return false;
    }

    public void Abandon() => Abandon(Tracked);

    public void Abandon(QuestInstance quest)
    {
        if (quest == null || !_active.Contains(quest))
            return;

        CleanupEscort(quest);
        quest.Status = QuestStatus.Failed;
        PushJournal(quest);
        _active.Remove(quest);
        if (Tracked == quest)
            Tracked = _active.Count > 0 ? _active[0] : null;
        GameplayEvents.RaiseToast("Quest abandoned.");
        Changed?.Invoke();
    }

    public void CycleTracked()
    {
        if (_active.Count <= 1)
            return;
        int idx = Tracked != null ? _active.IndexOf(Tracked) : -1;
        idx = (idx + 1) % _active.Count;
        Tracked = _active[idx];
        GameplayEvents.RaiseToast($"Tracking: {Tracked.Title}");
        Changed?.Invoke();
    }

    public void SetTracked(QuestInstance quest)
    {
        if (quest == null || !_active.Contains(quest))
            return;
        Tracked = quest;
        Changed?.Invoke();
    }

    /// <summary>First active escort quest (for road ambush), preferring tracked.</summary>
    public QuestInstance FindActiveEscortQuest()
    {
        if (Tracked != null && Tracked.TryGetActiveEscortObjective(out _))
            return Tracked;
        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i] != null && _active[i].TryGetActiveEscortObjective(out _))
                return _active[i];
        }

        return null;
    }

    bool TryAcceptFirstOfType(SettlementRecord settlement, QuestType type)
    {
        if (settlement == null)
        {
            GameplayEvents.RaiseToast("No village nearby.");
            return false;
        }

        GetOffers(settlement, _offerScratch);
        for (int i = 0; i < _offerScratch.Count; i++)
        {
            if (_offerScratch[i].Type == type)
                return TryAcceptOffer(_offerScratch[i]);
        }

        GameplayEvents.RaiseToast("No quest available.");
        return false;
    }

    bool BeginGuard()
    {
        if (_active.Count >= MaxActiveQuests)
        {
            GameplayEvents.RaiseToast($"Quest log full ({MaxActiveQuests}). Finish or abandon one.");
            return false;
        }

        return true;
    }

    bool ActivateQuest(QuestInstance quest, string toast)
    {
        quest.Id = _nextId++;
        _active.Add(quest);
        Tracked = quest;

        // Clear-camp already satisfied (camp wiped before accept).
        var step = quest.CurrentObjective;
        if (step != null &&
            step.Kind == QuestObjectiveKind.KillNear &&
            (step.ProgressCount >= step.RequiredCount || IsCampCleared(step.TargetCampId)))
        {
            CompleteObjective(quest, step, "Camp already cleared!");
            return quest.Status == QuestStatus.Active || quest.Status == QuestStatus.Completed;
        }

        GameplayEvents.RaiseToast(toast);
        Changed?.Invoke();
        return true;
    }

    void TickQuest(QuestInstance quest)
    {
        var step = quest.CurrentObjective;
        if (step == null || step.Status != QuestStatus.Active)
            return;

        switch (step.Kind)
        {
            case QuestObjectiveKind.KillNear:
                if (IsCampCleared(step.TargetCampId))
                    CompleteObjective(quest, step, "Camp cleared!");
                break;
            case QuestObjectiveKind.EscortTo:
                TickEscort(quest, step);
                break;
            case QuestObjectiveKind.DeliverItem:
            case QuestObjectiveKind.ReportBack:
                // Manual turn-in via village interaction.
                break;
        }
    }

    void OnEnemyKilled(Vector3 worldPosition, int _)
    {
        bool any = false;
        for (int i = 0; i < _active.Count; i++)
        {
            QuestInstance quest = _active[i];
            if (quest == null || quest.Status != QuestStatus.Active)
                continue;
            var step = quest.CurrentObjective;
            if (step == null || step.Kind != QuestObjectiveKind.KillNear || step.Status != QuestStatus.Active)
                continue;
            if (!SettlementService.Instance.TryGetCamp(step.TargetCampId, out CampRecord camp))
                continue;

            float dx = worldPosition.x - camp.Center.x;
            float dz = worldPosition.z - camp.Center.z;
            if (dx * dx + dz * dz > KillNearRadius * KillNearRadius)
                continue;

            step.ProgressCount++;
            any = true;
            if (step.ProgressCount >= step.RequiredCount || camp.Cleared)
                CompleteObjective(quest, step, "Camp cleared!");
        }

        if (any)
            Changed?.Invoke();
    }

    void OnInventoryChanged()
    {
        int wood = PlayerInventory.Instance != null ? PlayerInventory.Instance.Wood : 0;
        int previous = _lastDeliverWoodSeen;
        if (wood == previous)
            return;
        _lastDeliverWoodSeen = wood;

        bool notified = false;
        for (int i = 0; i < _active.Count; i++)
        {
            QuestInstance quest = _active[i];
            var step = quest?.CurrentObjective;
            if (step == null || step.Kind != QuestObjectiveKind.DeliverItem)
                continue;
            if (!notified && previous < step.RequiredCount && wood >= step.RequiredCount)
            {
                GameplayEvents.RaiseToast($"Wood ready — turn in at the marked village ({step.RequiredCount}).");
                notified = true;
            }
        }

        Changed?.Invoke();
    }

    void TickEscort(QuestInstance quest, QuestObjective step)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        EntityManager em = world.EntityManager;
        if (step.EscortEntity == Entity.Null || !em.Exists(step.EscortEntity) ||
            em.HasComponent<NpcDeadTag>(step.EscortEntity))
        {
            FailQuest(quest, "Escort died — quest failed.");
            return;
        }

        Transform player = PlayerReference.TryGetTransform();
        if (player != null)
        {
            float3 escortPos = em.GetComponentData<LocalTransform>(step.EscortEntity).Position;
            float pdx = player.position.x - escortPos.x;
            float pdz = player.position.z - escortPos.z;
            if (pdx * pdx + pdz * pdz > EscortAbandonDistance * EscortAbandonDistance)
            {
                FailQuest(quest, "Escort left behind — quest failed.");
                return;
            }

            NpcMovementApi.SetAnchorPosition(em, step.EscortEntity,
                new float3(player.position.x, player.position.y, player.position.z));
        }

        float3 pos = em.GetComponentData<LocalTransform>(step.EscortEntity).Position;
        Vector3 target = step.TargetPosition;
        float edx = pos.x - target.x;
        float edz = pos.z - target.z;
        if (edx * edx + edz * edz > EscortArriveRadius * EscortArriveRadius)
            return;

        if (player != null)
        {
            float pdx = player.position.x - target.x;
            float pdz = player.position.z - target.z;
            if (pdx * pdx + pdz * pdz > PlayerArriveRadius * PlayerArriveRadius)
                return;
        }

        DestroyEscort(step.EscortEntity);
        step.EscortEntity = Entity.Null;
        CompleteObjective(quest, step, "Escort arrived safely!");
    }

    void CompleteObjective(QuestInstance quest, QuestObjective step, string toast)
    {
        if (quest == null || step == null || step.Status != QuestStatus.Active)
            return;

        step.Status = QuestStatus.Completed;

        if (step.SpawnEscortOnComplete)
        {
            TrySpawnEscortAfterKill(quest);
            if (quest.Status != QuestStatus.Active)
                return;
        }

        int next = quest.CurrentObjectiveIndex + 1;
        if (next < quest.Objectives.Count)
        {
            quest.CurrentObjectiveIndex = next;
            var nextStep = quest.CurrentObjective;
            if (nextStep != null)
                nextStep.Status = QuestStatus.Active;

            // Rescue: escort may have been spawned; ensure route length for ambush.
            if (nextStep != null &&
                nextStep.Kind == QuestObjectiveKind.EscortTo &&
                nextStep.EscortEntity != Entity.Null &&
                nextStep.EscortRouteLength <= 0f)
            {
                nextStep.EscortRouteLength = HorizontalDistance(nextStep.TargetPosition,
                    GetEscortWorldPosition(nextStep.EscortEntity));
                nextStep.EscortOriginSettlementId = -1;
            }

            GameplayEvents.RaiseToast($"{toast} Next: {nextStep?.Label ?? "continue"}");
            Changed?.Invoke();
            return;
        }

        CompleteQuest(quest, toast);
    }

    void CompleteQuest(QuestInstance quest, string toast)
    {
        if (quest == null)
            return;

        quest.Status = QuestStatus.Completed;
        CleanupEscort(quest);

        PlayerWallet.Instance?.Add(quest.GoldReward);
        if (quest.FoodReward > 0)
            PlayerInventory.Instance?.AddFood(quest.FoodReward);

        if (SettlementService.Instance != null && quest.OriginSettlementId >= 0)
            SettlementService.Instance.AddReputation(quest.OriginSettlementId, quest.ReputationReward);

        if ((quest.Type == QuestType.Escort || quest.Type == QuestType.TradeRun ||
             quest.Type == QuestType.RescueSurvivor) &&
            quest.TargetSettlementId >= 0 &&
            quest.TargetSettlementId != quest.OriginSettlementId &&
            SettlementService.Instance != null)
            SettlementService.Instance.AddReputation(quest.TargetSettlementId, quest.ReputationReward / 2);

        string foodBit = quest.FoodReward > 0 ? $" +{quest.FoodReward} food" : string.Empty;
        GameplayEvents.RaiseToast($"{toast} +{quest.GoldReward}g{foodBit}");

        PushJournal(quest);
        _active.Remove(quest);
        if (Tracked == quest)
            Tracked = _active.Count > 0 ? _active[0] : null;
        Changed?.Invoke();
    }

    void FailQuest(QuestInstance quest, string toast)
    {
        if (quest == null)
            return;

        CleanupEscort(quest);
        quest.Status = QuestStatus.Failed;
        PushJournal(quest);
        _active.Remove(quest);
        if (Tracked == quest)
            Tracked = _active.Count > 0 ? _active[0] : null;
        GameplayEvents.RaiseToast(toast);
        Changed?.Invoke();
    }

    void TrySpawnEscortAfterKill(QuestInstance quest)
    {
        for (int i = quest.CurrentObjectiveIndex + 1; i < quest.Objectives.Count; i++)
        {
            var step = quest.Objectives[i];
            if (step.Kind != QuestObjectiveKind.EscortTo)
                continue;
            if (!TrySpawnEscortFor(quest, step, atPlayer: true))
                FailQuest(quest, "Could not find a survivor — quest failed.");
            return;
        }
    }

    bool TrySpawnEscortFor(QuestInstance quest, QuestObjective step, bool atPlayer)
    {
        if (step == null)
            return false;

        Transform player = PlayerReference.TryGetTransform();
        if (player == null || PartyManager.Instance == null)
        {
            GameplayEvents.RaiseToast("Could not find an escort.");
            return false;
        }

        Vector3 spawnPos = atPlayer ? player.position : step.TargetPosition;
        Entity escort = PartyManager.Instance.SpawnEscortFollower(spawnPos);
        if (escort == Entity.Null)
        {
            GameplayEvents.RaiseToast("Could not find an escort.");
            return false;
        }

        step.EscortEntity = escort;
        Vector3 origin = spawnPos;
        if (step.EscortOriginSettlementId >= 0 &&
            SettlementService.Instance != null &&
            SettlementService.Instance.TryGetSettlement(step.EscortOriginSettlementId, out SettlementRecord originSettle) &&
            originSettle != null)
            origin = originSettle.Center;

        step.EscortRouteLength = HorizontalDistance(origin, step.TargetPosition);
        step.AmbushTriggered = false;
        return true;
    }

    void CleanupEscort(QuestInstance quest)
    {
        if (quest == null)
            return;
        for (int i = 0; i < quest.Objectives.Count; i++)
        {
            var step = quest.Objectives[i];
            if (step != null && step.EscortEntity != Entity.Null)
            {
                DestroyEscort(step.EscortEntity);
                step.EscortEntity = Entity.Null;
            }
        }
    }

    void PushJournal(QuestInstance quest)
    {
        _journal.Insert(0, quest);
        while (_journal.Count > MaxJournalEntries)
            _journal.RemoveAt(_journal.Count - 1);
    }

    static bool IsCampCleared(int campId)
    {
        return campId >= 0 &&
               SettlementService.Instance != null &&
               SettlementService.Instance.TryGetCamp(campId, out CampRecord camp) &&
               camp != null &&
               camp.Cleared;
    }

    static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static Vector3 GetEscortWorldPosition(Entity escort)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || escort == Entity.Null)
            return Vector3.zero;
        var em = world.EntityManager;
        if (!em.Exists(escort) || !em.HasComponent<LocalTransform>(escort))
            return Vector3.zero;
        float3 p = em.GetComponentData<LocalTransform>(escort).Position;
        return new Vector3(p.x, p.y, p.z);
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
