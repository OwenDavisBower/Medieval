using Unity.Entities;
using UnityEngine;

/// <summary>One step in a quest chain. Progressed by <see cref="QuestService"/>.</summary>
public sealed class QuestObjective
{
    public QuestObjectiveKind Kind;
    public QuestStatus Status = QuestStatus.Active;
    public string Label = string.Empty;

    public Vector3 TargetPosition;
    public int TargetCampId = -1;
    public int TargetSettlementId = -1;

    /// <summary>Kills required, wood required, etc.</summary>
    public int RequiredCount;
    public int ProgressCount;

    public Entity EscortEntity;
    /// <summary>Horizontal origin→destination length when escort step began.</summary>
    public float EscortRouteLength;
    /// <summary>True after the mid-route road ambush has fired for this escort step.</summary>
    public bool AmbushTriggered;
    /// <summary>Settlement used as escort ambush origin (route start).</summary>
    public int EscortOriginSettlementId = -1;

    /// <summary>When true, completing KillNear auto-spawns an escort for the next EscortTo step.</summary>
    public bool SpawnEscortOnComplete;

    public string ProgressText
    {
        get
        {
            switch (Kind)
            {
                case QuestObjectiveKind.KillNear:
                    return $"{Label}: {ProgressCount}/{RequiredCount}";
                case QuestObjectiveKind.DeliverItem:
                {
                    int have = PlayerInventory.Instance != null ? PlayerInventory.Instance.Wood : 0;
                    return $"{Label}: {have}/{RequiredCount} wood";
                }
                case QuestObjectiveKind.EscortTo:
                {
                    string where = ResolveSettlementName(TargetSettlementId) ?? "the marked village";
                    return $"Escort the villager to {where}.";
                }
                case QuestObjectiveKind.ReportBack:
                {
                    string where = ResolveSettlementName(TargetSettlementId) ?? "the village";
                    return $"Report back at {where}.";
                }
                default:
                    return Label ?? string.Empty;
            }
        }
    }

    static string ResolveSettlementName(int settlementId)
    {
        if (settlementId < 0 || SettlementService.Instance == null)
            return null;
        if (!SettlementService.Instance.TryGetSettlement(settlementId, out SettlementRecord dest) || dest == null)
            return null;
        return dest.DisplayName;
    }
}
