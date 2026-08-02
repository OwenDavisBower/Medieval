using UnityEngine;

/// <summary>World-state quest seed offered at a settlement before acceptance.</summary>
public sealed class QuestOffer
{
    public QuestType Type;
    public string ButtonLabel = string.Empty;
    public string Title = string.Empty;
    public string Description = string.Empty;
    public int GoldReward;
    public int ReputationReward;
    public int FoodReward;

    public int OriginSettlementId = -1;
    public int TargetSettlementId = -1;
    public int TargetCampId = -1;
    public int RequiredCount;
    public Vector3 TargetPosition;
}
