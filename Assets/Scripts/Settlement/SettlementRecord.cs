using UnityEngine;

/// <summary>Runtime state for one planned settlement.</summary>
public sealed class SettlementRecord
{
    public int Id;
    public Vector3 PlannedCenter;
    public Vector3 WorldCenter;
    public int Reputation;
    public bool OwnedByPlayer;
    public int WoodStock = 40;
    public int FoodStock = 25;
    public float StockRegenTimer;
    public float TaxTimer;
    public bool HasLiveInstance;

    public Vector3 Center => HasLiveInstance ? WorldCenter : PlannedCenter;

    public string DisplayName => OwnedByPlayer ? $"Your Village #{Id + 1}" : $"Village #{Id + 1}";

    public string StandingLabel
    {
        get
        {
            if (OwnedByPlayer)
                return "Owned";
            if (Reputation >= 60)
                return "Allied";
            if (Reputation >= 25)
                return "Friendly";
            if (Reputation >= 5)
                return "Known";
            if (Reputation <= -40)
                return "Hostile";
            if (Reputation < 0)
                return "Wary";
            return "Neutral";
        }
    }
}
