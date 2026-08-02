using System;
using Medieval.Npcs;
using UnityEngine;

/// <summary>
/// Player combat XP / level-ups. Mirrors <see cref="NpcExperienceUtility"/> curves and bonuses
/// for the GameObject player (bow damage, move, aim, HP).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
public sealed class PlayerExperience : MonoBehaviour
{
    Character _character;
    RangedCombat _ranged;

    int _level = 1;
    float _currentXp;
    float _xpToNextLevel;

    public int Level => _level;
    public float CurrentXp => _currentXp;
    public float XpToNextLevel => _xpToNextLevel;
    public float XpFill01 =>
        _xpToNextLevel > 0.01f ? Mathf.Clamp01(_currentXp / _xpToNextLevel) : 0f;

    public event Action Changed;

    void Awake()
    {
        _character = GetComponent<Character>();
        _ranged = GetComponent<RangedCombat>();
        _xpToNextLevel = NpcExperienceUtility.XpRequiredForLevel(_level);
    }

    /// <summary>Combat XP from damage dealt (and kill bonus). No-op while dead.</summary>
    public void GrantDamageXp(float damageDealt, bool killed)
    {
        float xp = Mathf.Max(0f, damageDealt) * NpcExperienceUtility.XpPerDamagePoint;
        if (killed)
            xp += NpcExperienceUtility.KillBonusXp;
        AddXp(xp);
    }

    /// <summary>XP from damaging / destroying buildings.</summary>
    public void GrantBuildingDamageXp(float damageDealt, bool destroyed)
    {
        float xp = Mathf.Max(0f, damageDealt) * NpcExperienceUtility.XpPerDamagePoint;
        if (destroyed)
            xp += NpcExperienceUtility.BuildingDestroyBonusXp;
        AddXp(xp);
    }

    public void AddXp(float amount)
    {
        if (amount <= 0f || _character == null || _character.IsDead)
            return;

        _currentXp += amount;
        int levelsGained = 0;

        while (_level < NpcExperienceUtility.MaxLevel &&
               _xpToNextLevel > 0.01f &&
               _currentXp >= _xpToNextLevel)
        {
            _currentXp -= _xpToNextLevel;
            _level++;
            _xpToNextLevel = NpcExperienceUtility.XpRequiredForLevel(_level);
            levelsGained++;
            ApplyLevelBonuses();
        }

        if (_level >= NpcExperienceUtility.MaxLevel)
            _currentXp = 0f;

        if (levelsGained > 0)
            FloatingWorldText.Spawn(transform.position, _level);

        Changed?.Invoke();
    }

    void ApplyLevelBonuses()
    {
        _character?.ApplyLevelUpBonuses(
            NpcExperienceUtility.HealthGainPerLevel,
            NpcExperienceUtility.HealthRestoreFractionOnLevelUp,
            NpcExperienceUtility.MeleeDamageMultPerLevel,
            NpcExperienceUtility.MoveSpeedMultPerLevel,
            NpcExperienceUtility.AimErrorMultPerLevel,
            NpcExperienceUtility.BraveryGainPerLevel);

        _ranged?.ScaleArrowDamage(NpcExperienceUtility.RangedDamageMultPerLevel);
    }

    /// <summary>Safe grant from DOTS hit systems when the projectile belongs to the player.</summary>
    public static void TryGrantDamageXp(float damageDealt, bool killed)
    {
        var pe = PlayerReference.TryGetExperience();
        pe?.GrantDamageXp(damageDealt, killed);
    }

    /// <summary>Safe grant from DOTS hit systems for player shots against buildings.</summary>
    public static void TryGrantBuildingDamageXp(float damageDealt, bool destroyed)
    {
        var pe = PlayerReference.TryGetExperience();
        pe?.GrantBuildingDamageXp(damageDealt, destroyed);
    }
}
