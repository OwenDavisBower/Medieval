using Medieval.Projectiles;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Shared combat tick: ranged dodge response, flee, optional pre-seek gate, seek override, ranged/melee.
/// Subclasses implement <see cref="FindCombatTarget"/> and may override <see cref="BeforeSeekCombat"/>.
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

    protected virtual void FixedUpdate()
    {
        TryScheduleRangedDodge();

        if (Character != null && Character.ShouldFleeFromCombatThreat)
        {
            Motor.SeekOverride = null;
            Motor.ClearOverrideFacing();
            Ranged?.CancelMovementLock();
            Motor.SetRangedMovementLock(false);
            return;
        }

        if (!BeforeSeekCombat())
        {
            Motor.SeekOverride = null;
            Motor.ClearOverrideFacing();
            Ranged?.CancelMovementLock();
            Motor.SetRangedMovementLock(false);
            return;
        }

        Transform target = FindCombatTarget();
        Motor.SeekOverride = target;

        if (target == null)
        {
            Motor.ClearOverrideFacing();
            Motor.SetRangedMovementLock(Ranged != null && Ranged.IsMovementLocked);
            return;
        }

        bool inRangedStandoff = IsRanged && Ranged != null && Ranged.enabled &&
            SpatialMath.FlatSqrDistance(transform.position, target.position) <= combatRange * combatRange;

        if (inRangedStandoff)
            Motor.SetOverrideFacingTowardWorldPoint(target.position);
        else
            Motor.ClearOverrideFacing();

        Motor.SetRangedMovementLock(Ranged != null && Ranged.IsMovementLocked);

        TryExecuteCombatAgainst(target);
    }

    /// <summary>Return false to clear seek (e.g. follower leash to leader).</summary>
    protected virtual bool BeforeSeekCombat() => true;

    protected abstract Transform FindCombatTarget();

    void TryScheduleRangedDodge()
    {
        if (Motor.CanScheduleRangedDodge && !Motor.HasPendingRangedDodge &&
            ProjectileDodgeBridge.TryGetIncomingDodgeReference(transform.root, out Vector3 dodgeRef))
            Motor.ScheduleRangedDodgeImpulse(dodgeRef);
    }

    void TryExecuteCombatAgainst(Transform target)
    {
        if (IsRanged && Ranged != null && Ranged.enabled &&
            SpatialMath.FlatSqrDistance(transform.position, target.position) <= combatRange * combatRange)
            Ranged.TryFireAt(target);
        else if (!IsRanged && Melee != null && Melee.enabled)
            Melee.TryAttack(target);
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
