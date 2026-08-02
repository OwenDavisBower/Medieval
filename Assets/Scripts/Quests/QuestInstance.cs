using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>Runtime quest with a linear list of composable objectives.</summary>
public sealed class QuestInstance
{
    public int Id;
    public QuestType Type;
    public QuestStatus Status = QuestStatus.Active;
    public int OriginSettlementId = -1;
    public int TargetSettlementId = -1;
    public int TargetCampId = -1;
    public string Title = string.Empty;
    public string Description = string.Empty;
    public int GoldReward;
    public int ReputationReward;
    public int FoodReward;
    public readonly List<QuestObjective> Objectives = new List<QuestObjective>(4);
    public int CurrentObjectiveIndex;

    public QuestObjective CurrentObjective
    {
        get
        {
            if (CurrentObjectiveIndex < 0 || CurrentObjectiveIndex >= Objectives.Count)
                return null;
            return Objectives[CurrentObjectiveIndex];
        }
    }

    /// <summary>Guidance / minimap target for the current step.</summary>
    public Vector3 TargetPosition
    {
        get
        {
            var step = CurrentObjective;
            return step != null ? step.TargetPosition : Vector3.zero;
        }
    }

    /// <summary>Escort entity on the current step, if any.</summary>
    public Entity EscortEntity
    {
        get
        {
            var step = CurrentObjective;
            return step != null && step.Kind == QuestObjectiveKind.EscortTo
                ? step.EscortEntity
                : Entity.Null;
        }
        set
        {
            var step = CurrentObjective;
            if (step != null && step.Kind == QuestObjectiveKind.EscortTo)
                step.EscortEntity = value;
        }
    }

    public float EscortRouteLength
    {
        get
        {
            var step = CurrentObjective;
            return step != null ? step.EscortRouteLength : 0f;
        }
        set
        {
            var step = CurrentObjective;
            if (step != null)
                step.EscortRouteLength = value;
        }
    }

    public bool AmbushTriggered
    {
        get
        {
            var step = CurrentObjective;
            return step != null && step.AmbushTriggered;
        }
        set
        {
            var step = CurrentObjective;
            if (step != null)
                step.AmbushTriggered = value;
        }
    }

    public string ProgressText
    {
        get
        {
            var step = CurrentObjective;
            if (step == null)
                return string.Empty;
            if (Objectives.Count <= 1)
                return step.ProgressText;
            return $"Step {CurrentObjectiveIndex + 1}/{Objectives.Count} — {step.ProgressText}";
        }
    }

    public bool TryGetActiveEscortObjective(out QuestObjective objective)
    {
        objective = CurrentObjective;
        return Status == QuestStatus.Active &&
               objective != null &&
               objective.Kind == QuestObjectiveKind.EscortTo &&
               objective.Status == QuestStatus.Active;
    }

    public bool NeedsTurnInAt(int settlementId)
    {
        if (Status != QuestStatus.Active || settlementId < 0)
            return false;
        var step = CurrentObjective;
        if (step == null || step.Status != QuestStatus.Active)
            return false;
        if (step.Kind == QuestObjectiveKind.DeliverItem || step.Kind == QuestObjectiveKind.ReportBack)
            return step.TargetSettlementId == settlementId;
        return false;
    }
}
