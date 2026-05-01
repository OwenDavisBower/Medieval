using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Legacy MonoBehaviour shell for GameObject NPCs: combat role and component wiring.
/// Seek, path goals, and facing for baked DOTS NPCs are driven by <see cref="Medieval.Npcs.NpcCombatSeekSystem"/>.
/// </summary>
[RequireComponent(typeof(TargetSteeringMotor))]
[RequireComponent(typeof(Rigidbody))]
public abstract class CombatSeekControllerBase : MonoBehaviour
{
    [Header("Combat")]
    [SerializeField] protected float combatRange = 20f;
    [SerializeField] protected float eyeHeight = 1.5f;
    [SerializeField] protected float targetHeight = 1f;
    [SerializeField] protected LayerMask obstacleLayers = ~0;

    [Header("Detection")]
    [FormerlySerializedAs("banditAggroRadius")]
    [SerializeField] protected float aggroRadius = 50f;

    protected TargetSteeringMotor Motor { get; private set; }
    protected RangedCombat Ranged { get; private set; }
    protected MeleeCombat Melee { get; private set; }
    protected Character Character { get; private set; }
    protected bool IsRanged { get; private set; } = true;

    Rigidbody _rigidbody;
    TargetFinder _targetFinder;

    protected virtual void Awake()
    {
        CacheComponents();
        EnsureLocomotionAnimatorDriver();
    }

    protected void EnsureComponentsInitialized()
    {
        if (Motor == null)
            CacheComponents();
    }

    void CacheComponents()
    {
        Motor = GetComponent<TargetSteeringMotor>();
        _rigidbody = GetComponent<Rigidbody>();
        Ranged = GetComponent<RangedCombat>();
        Melee = GetComponent<MeleeCombat>();
        Character = GetComponent<Character>();
        _targetFinder = GetComponent<TargetFinder>();
    }

    /// <summary>
    /// When a <see cref="TargetFinder"/> is present, runs a faction scan and returns the closest enemy
    /// within aggro radius with line of sight. Otherwise returns null.
    /// </summary>
    protected Transform TrySelectEnemyViaFactionFinder()
    {
        if (_targetFinder == null)
            return null;

        _targetFinder.ScanNow();
        Transform candidate = _targetFinder.CurrentEnemyTarget;
        if (candidate == null)
            return null;

        var otherHealth = candidate.GetComponentInParent<IDamageableHealth>();
        if (otherHealth != null && otherHealth.IsDead)
            return null;

        if (SpatialMath.FlatSqrDistance(transform.position, candidate.position) > AggroRadiusSqr)
            return null;

        if (!HasLineOfSightTo(candidate))
            return null;

        return candidate;
    }

    /// <summary>Call once after spawn: ranged (bow) or melee, never both.</summary>
    public void ApplyCombatRole(bool ranged)
    {
        IsRanged = ranged;
        if (Ranged != null)
            Ranged.enabled = ranged;
        if (Melee != null)
            Melee.enabled = !ranged;
        CombatVisuals.SetRangedHatVisible(transform, ranged);
    }

    protected void ApplySeekHoldDistanceFromRole()
    {
        Motor.SeekHoldDistance = IsRanged ? combatRange : 0f;
    }

    protected void ApplyMotorSpeedFromCharacter()
    {
        CharacterMotorLink.ApplyMovementSpeed(Character, Motor);
    }

    void EnsureLocomotionAnimatorDriver()
    {
        if (GetComponent<LocomotionAnimatorDriver>() != null)
            return;

        gameObject.AddComponent<LocomotionAnimatorDriver>();
    }

    protected bool HasLineOfSightTo(Transform target) =>
        LineOfSightUtility.HasClearLineOfSight(transform.position, target, eyeHeight, targetHeight, obstacleLayers,
            transform.root);

    protected static bool IsAliveDamageableTarget(Transform t)
    {
        if (t == null)
            return false;
        var h = t.GetComponentInParent<IDamageableHealth>();
        return h == null || !h.IsDead;
    }

    protected float AggroRadiusSqr => aggroRadius * aggroRadius;
}
