using UnityEngine;

/// <summary>Runtime state for one planned bandit camp.</summary>
public sealed class CampRecord
{
    public int Id;
    public Vector3 PlannedCenter;
    public Vector3 WorldCenter;
    public int LinkedSettlementId = -1;
    public bool Cleared;
    public bool HasLiveInstance;
    public int SpawnedBanditCount;
    public int KilledNearCamp;

    public Vector3 Center => HasLiveInstance ? WorldCenter : PlannedCenter;
}
