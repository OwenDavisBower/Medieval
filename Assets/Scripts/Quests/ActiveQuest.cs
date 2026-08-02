using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Active quest payload.</summary>
public sealed class ActiveQuest
{
    public QuestType Type;
    public QuestStatus Status = QuestStatus.Active;
    public int OriginSettlementId = -1;
    public int TargetSettlementId = -1;
    public int TargetCampId = -1;
    public int RequiredWood;
    public int ProgressKills;
    public int RequiredKills = 3;
    public Vector3 TargetPosition;
    public Entity EscortEntity;
    public string Title;
    public string Description;
    public int GoldReward;
    public int ReputationReward;
    /// <summary>Horizontal origin→destination length when escort was accepted.</summary>
    public float EscortRouteLength;
    /// <summary>True after the mid-route road ambush has fired for this escort.</summary>
    public bool AmbushTriggered;

    public string ProgressText
    {
        get
        {
            switch (Type)
            {
                case QuestType.ClearCamp:
                    return $"Bandits slain near camp: {ProgressKills}/{RequiredKills}";
                case QuestType.DeliverWood:
                {
                    int have = PlayerInventory.Instance != null ? PlayerInventory.Instance.Wood : 0;
                    return $"Wood: {have}/{RequiredWood}";
                }
                case QuestType.Escort:
                {
                    string where = "the marked village";
                    if (TargetSettlementId >= 0 &&
                        SettlementService.Instance != null &&
                        SettlementService.Instance.TryGetSettlement(TargetSettlementId, out SettlementRecord dest) &&
                        dest != null)
                        where = dest.DisplayName;
                    return $"Escort the villager to {where}.";
                }
                default:
                    return string.Empty;
            }
        }
    }
}
