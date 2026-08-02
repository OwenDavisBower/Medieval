using System;
using UnityEngine;

/// <summary>Cross-system gameplay signals (loot, quests, settlements).</summary>
public static class GameplayEvents
{
    public static event Action<Vector3, int> EnemyKilled;
    public static event Action<int> SettlementClaimed;
    public static event Action<int, int> ReputationChanged;
    public static event Action<string> Toast;

    public static void RaiseEnemyKilled(Vector3 worldPosition, int factionOrRoleHint = 0) =>
        EnemyKilled?.Invoke(worldPosition, factionOrRoleHint);

    public static void RaiseSettlementClaimed(int settlementId) =>
        SettlementClaimed?.Invoke(settlementId);

    public static void RaiseReputationChanged(int settlementId, int newReputation) =>
        ReputationChanged?.Invoke(settlementId, newReputation);

    public static void RaiseToast(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;
        Toast?.Invoke(message);
    }
}
