using System;
using UnityEngine;

/// <summary>
/// Shared caches for <see cref="FindObjectsByType"/> results used by AI controllers, so multiple units
/// do not each trigger a full scene scan on the same timer tick.
/// </summary>
public static class CombatUnitRegistry
{
    const float RefreshInterval = 0.15f;

    static FollowerController[] _followers = Array.Empty<FollowerController>();
    static VillagerController[] _villagers = Array.Empty<VillagerController>();
    static BanditController[] _bandits = Array.Empty<BanditController>();

    static float _followersNextRefresh = float.NegativeInfinity;
    static float _villagersNextRefresh = float.NegativeInfinity;
    static float _banditsNextRefresh = float.NegativeInfinity;

    public static FollowerController[] GetFollowers()
    {
        if (Time.time >= _followersNextRefresh)
        {
            _followersNextRefresh = Time.time + RefreshInterval;
            _followers = UnityEngine.Object.FindObjectsByType<FollowerController>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        }

        return _followers;
    }

    public static VillagerController[] GetVillagers()
    {
        if (Time.time >= _villagersNextRefresh)
        {
            _villagersNextRefresh = Time.time + RefreshInterval;
            _villagers = UnityEngine.Object.FindObjectsByType<VillagerController>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        }

        return _villagers;
    }

    public static BanditController[] GetBandits()
    {
        if (Time.time >= _banditsNextRefresh)
        {
            _banditsNextRefresh = Time.time + RefreshInterval;
            _bandits = UnityEngine.Object.FindObjectsByType<BanditController>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        }

        return _bandits;
    }
}
