using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds world-state quest offers and concrete <see cref="QuestInstance"/> chains from templates.
/// </summary>
public static class QuestTemplateFactory
{
    public const int DefaultDeliverWood = 8;
    public const int LowWoodStockThreshold = 18;
    public const int HighWoodStockThreshold = 28;
    public const float MinEscortDestinationDistance = 40f;
    public const float CampSearchRadius = 160f;

    public static void BuildOffers(
        SettlementRecord settlement,
        QuestService quests,
        List<QuestOffer> into)
    {
        into.Clear();
        if (settlement == null || SettlementService.Instance == null)
            return;

        var settlements = SettlementService.Instance;

        CampRecord camp = null;
        int campRequired = 0;
        bool campSeedOpen = !quests.HasActiveTypeFrom(QuestType.ClearCamp, settlement.Id) &&
                            !quests.HasActiveTypeFrom(QuestType.RescueSurvivor, settlement.Id);
        if (campSeedOpen)
        {
            camp = settlements.FindUnclearedCampLinkedTo(settlement.Id)
                   ?? settlements.FindNearestUnclearedCamp(settlement.Center, CampSearchRadius);
            if (camp != null)
                campRequired = Mathf.Max(3, camp.SpawnedBanditCount > 0 ? camp.SpawnedBanditCount : 3);
        }

        // Prefer a diverse top-3: suppression, economy, travel, then rescue arc as filler.
        if (camp != null)
        {
            into.Add(new QuestOffer
            {
                Type = QuestType.ClearCamp,
                ButtonLabel = "Clear camp",
                Title = "Clear the Camp",
                Description = $"Defeat the bandits camping near {settlement.DisplayName}, then report back.",
                OriginSettlementId = settlement.Id,
                TargetCampId = camp.Id,
                TargetPosition = camp.Center,
                RequiredCount = campRequired,
                GoldReward = 35 + campRequired * 4,
                ReputationReward = 22,
                FoodReward = 1
            });
        }

        if (!quests.HasActiveTypeFrom(QuestType.DeliverWood, settlement.Id) &&
            settlement.WoodStock < LowWoodStockThreshold)
        {
            into.Add(new QuestOffer
            {
                Type = QuestType.DeliverWood,
                ButtonLabel = "Deliver wood",
                Title = "Deliver Wood",
                Description =
                    $"Bring {DefaultDeliverWood} wood to {settlement.DisplayName}. Buy it here or haul it from elsewhere.",
                OriginSettlementId = settlement.Id,
                TargetSettlementId = settlement.Id,
                TargetPosition = settlement.Center,
                RequiredCount = DefaultDeliverWood,
                GoldReward = 28,
                ReputationReward = 12,
                FoodReward = 1
            });
        }
        else if (!quests.HasActiveTypeFrom(QuestType.TradeRun, settlement.Id) &&
                 settlement.WoodStock >= HighWoodStockThreshold &&
                 FindNeedySettlement(settlement, out SettlementRecord needy))
        {
            into.Add(new QuestOffer
            {
                Type = QuestType.TradeRun,
                ButtonLabel = "Trade run",
                Title = "Trade Run",
                Description =
                    $"Haul {DefaultDeliverWood} wood to {needy.DisplayName}, then report back to {settlement.DisplayName}.",
                OriginSettlementId = settlement.Id,
                TargetSettlementId = needy.Id,
                TargetPosition = needy.Center,
                RequiredCount = DefaultDeliverWood,
                GoldReward = 42,
                ReputationReward = 18,
                FoodReward = 2
            });
        }

        if (!quests.HasActiveTypeFrom(QuestType.Escort, settlement.Id) &&
            FindEscortDestination(settlement, out SettlementRecord dest))
        {
            into.Add(new QuestOffer
            {
                Type = QuestType.Escort,
                ButtonLabel = "Escort",
                Title = "Escort Villager",
                Description = $"Safely bring the villager to {dest.DisplayName}.",
                OriginSettlementId = settlement.Id,
                TargetSettlementId = dest.Id,
                TargetPosition = dest.Center,
                GoldReward = 40,
                ReputationReward = 16,
                FoodReward = 1
            });
        }

        if (camp != null && into.Count < 3)
        {
            into.Add(new QuestOffer
            {
                Type = QuestType.RescueSurvivor,
                ButtonLabel = "Rescue survivor",
                Title = "Rescue Survivor",
                Description =
                    $"Clear the camp near {settlement.DisplayName}, then escort a survivor home.",
                OriginSettlementId = settlement.Id,
                TargetCampId = camp.Id,
                TargetSettlementId = settlement.Id,
                TargetPosition = camp.Center,
                RequiredCount = campRequired,
                GoldReward = 55 + campRequired * 5,
                ReputationReward = 28,
                FoodReward = 2
            });
        }

        if (into.Count > 3)
            into.RemoveRange(3, into.Count - 3);
    }

    public static QuestInstance CreateFromOffer(QuestOffer offer)
    {
        if (offer == null)
            return null;

        var quest = new QuestInstance
        {
            Type = offer.Type,
            OriginSettlementId = offer.OriginSettlementId,
            TargetSettlementId = offer.TargetSettlementId,
            TargetCampId = offer.TargetCampId,
            Title = offer.Title,
            Description = offer.Description,
            GoldReward = offer.GoldReward,
            ReputationReward = offer.ReputationReward,
            FoodReward = offer.FoodReward,
            Status = QuestStatus.Active,
            CurrentObjectiveIndex = 0
        };

        switch (offer.Type)
        {
            case QuestType.ClearCamp:
                BuildClearCamp(quest, offer);
                break;
            case QuestType.DeliverWood:
                BuildDeliverWood(quest, offer);
                break;
            case QuestType.Escort:
                BuildEscort(quest, offer);
                break;
            case QuestType.TradeRun:
                BuildTradeRun(quest, offer);
                break;
            case QuestType.RescueSurvivor:
                BuildRescueSurvivor(quest, offer);
                break;
            default:
                return null;
        }

        return quest.Objectives.Count > 0 ? quest : null;
    }

    static void BuildClearCamp(QuestInstance quest, QuestOffer offer)
    {
        int progress = 0;
        if (SettlementService.Instance != null &&
            SettlementService.Instance.TryGetCamp(offer.TargetCampId, out CampRecord camp) &&
            camp != null)
            progress = camp.KilledNearCamp;

        quest.Objectives.Add(new QuestObjective
        {
            Kind = QuestObjectiveKind.KillNear,
            Label = "Bandits slain near camp",
            TargetCampId = offer.TargetCampId,
            TargetPosition = offer.TargetPosition,
            RequiredCount = offer.RequiredCount,
            ProgressCount = progress
        });
        quest.Objectives.Add(new QuestObjective
        {
            Kind = QuestObjectiveKind.ReportBack,
            Label = "Report back",
            TargetSettlementId = offer.OriginSettlementId,
            TargetPosition = ResolveSettlementCenter(offer.OriginSettlementId, offer.TargetPosition)
        });
    }

    static void BuildDeliverWood(QuestInstance quest, QuestOffer offer)
    {
        quest.Objectives.Add(new QuestObjective
        {
            Kind = QuestObjectiveKind.DeliverItem,
            Label = "Deliver wood",
            TargetSettlementId = offer.TargetSettlementId >= 0 ? offer.TargetSettlementId : offer.OriginSettlementId,
            TargetPosition = offer.TargetPosition,
            RequiredCount = offer.RequiredCount > 0 ? offer.RequiredCount : DefaultDeliverWood
        });
    }

    static void BuildEscort(QuestInstance quest, QuestOffer offer)
    {
        quest.Objectives.Add(new QuestObjective
        {
            Kind = QuestObjectiveKind.EscortTo,
            Label = "Escort villager",
            TargetSettlementId = offer.TargetSettlementId,
            TargetPosition = offer.TargetPosition,
            EscortOriginSettlementId = offer.OriginSettlementId
        });
    }

    static void BuildTradeRun(QuestInstance quest, QuestOffer offer)
    {
        quest.Objectives.Add(new QuestObjective
        {
            Kind = QuestObjectiveKind.DeliverItem,
            Label = "Deliver trade wood",
            TargetSettlementId = offer.TargetSettlementId,
            TargetPosition = offer.TargetPosition,
            RequiredCount = offer.RequiredCount > 0 ? offer.RequiredCount : DefaultDeliverWood
        });
        quest.Objectives.Add(new QuestObjective
        {
            Kind = QuestObjectiveKind.ReportBack,
            Label = "Collect payment",
            TargetSettlementId = offer.OriginSettlementId,
            TargetPosition = ResolveSettlementCenter(offer.OriginSettlementId, Vector3.zero)
        });
    }

    static void BuildRescueSurvivor(QuestInstance quest, QuestOffer offer)
    {
        int progress = 0;
        if (SettlementService.Instance != null &&
            SettlementService.Instance.TryGetCamp(offer.TargetCampId, out CampRecord camp) &&
            camp != null)
            progress = camp.KilledNearCamp;

        quest.Objectives.Add(new QuestObjective
        {
            Kind = QuestObjectiveKind.KillNear,
            Label = "Clear bandits",
            TargetCampId = offer.TargetCampId,
            TargetPosition = offer.TargetPosition,
            RequiredCount = offer.RequiredCount,
            ProgressCount = progress,
            SpawnEscortOnComplete = true
        });
        quest.Objectives.Add(new QuestObjective
        {
            Kind = QuestObjectiveKind.EscortTo,
            Label = "Escort survivor home",
            TargetSettlementId = offer.OriginSettlementId,
            TargetPosition = ResolveSettlementCenter(offer.OriginSettlementId, offer.TargetPosition),
            EscortOriginSettlementId = -1 // route length set when escort spawns at camp
        });
    }

    public static bool FindEscortDestination(SettlementRecord origin, out SettlementRecord dest)
    {
        dest = null;
        if (origin == null || SettlementService.Instance == null)
            return false;

        float best = float.MaxValue;
        var list = SettlementService.Instance.Settlements;
        for (int i = 0; i < list.Count; i++)
        {
            SettlementRecord s = list[i];
            if (s == null || s.Id == origin.Id || s.BuildFailed)
                continue;
            float dx = s.Center.x - origin.Center.x;
            float dz = s.Center.z - origin.Center.z;
            float sq = dx * dx + dz * dz;
            float minSq = MinEscortDestinationDistance * MinEscortDestinationDistance;
            if (sq < best && sq > minSq)
            {
                best = sq;
                dest = s;
            }
        }

        return dest != null;
    }

    static bool FindNeedySettlement(SettlementRecord origin, out SettlementRecord needy)
    {
        needy = null;
        if (origin == null || SettlementService.Instance == null)
            return false;

        float best = float.MaxValue;
        var list = SettlementService.Instance.Settlements;
        for (int i = 0; i < list.Count; i++)
        {
            SettlementRecord s = list[i];
            if (s == null || s.Id == origin.Id || s.BuildFailed)
                continue;
            if (s.WoodStock >= LowWoodStockThreshold)
                continue;
            float dx = s.Center.x - origin.Center.x;
            float dz = s.Center.z - origin.Center.z;
            float sq = dx * dx + dz * dz;
            if (sq < best && sq > MinEscortDestinationDistance * MinEscortDestinationDistance)
            {
                best = sq;
                needy = s;
            }
        }

        return needy != null;
    }

    static Vector3 ResolveSettlementCenter(int settlementId, Vector3 fallback)
    {
        if (settlementId >= 0 &&
            SettlementService.Instance != null &&
            SettlementService.Instance.TryGetSettlement(settlementId, out SettlementRecord s) &&
            s != null)
            return s.Center;
        return fallback;
    }
}
