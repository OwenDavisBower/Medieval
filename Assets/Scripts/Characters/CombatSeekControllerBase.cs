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
    [Header("Locomotion animation")]
    [Tooltip("Leave empty to use the first Animator under this object (e.g. soldier mesh).")]
    [SerializeField] Animator locomotionAnimator;
    [Tooltip("Below this horizontal speed (m/s), animation playback is stopped.")]
    [SerializeField] float locomotionStopSpeedThreshold = 0.04f;
    [Tooltip("Scales walk animation vs. movement after speed is normalized. Raise to reduce foot sliding backward; lower if feet look too fast.")]
    [SerializeField] float locomotionAnimationSpeedScale = 2f;

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

    protected virtual void Awake()
    {
        CacheComponents();
        if (locomotionAnimator == null)
            locomotionAnimator = GetComponentInChildren<Animator>();
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

    protected virtual void LateUpdate()
    {
        UpdateLocomotionAnimationSpeed();
    }

    void UpdateLocomotionAnimationSpeed()
    {
        if (locomotionAnimator == null || Motor == null || _rigidbody == null)
            return;

        Vector3 v = _rigidbody.linearVelocity;
        float horizontalSpeed = new Vector3(v.x, 0f, v.z).magnitude;
        float maxSpeed = Motor.EffectiveMoveSpeed;
        if (horizontalSpeed < locomotionStopSpeedThreshold || maxSpeed < 0.01f)
        {
            locomotionAnimator.speed = 0f;
            return;
        }

        float normalized = Mathf.Clamp01(horizontalSpeed / maxSpeed);
        locomotionAnimator.speed = normalized * locomotionAnimationSpeedScale;
    }

    protected virtual void FixedUpdate()
    {
        TryScheduleRangedDodge();

        if (Character != null && Character.ShouldFleeFromCombatThreat)
        {
            Motor.SeekOverride = null;
            return;
        }

        if (!BeforeSeekCombat())
        {
            Motor.SeekOverride = null;
            return;
        }

        Transform target = FindCombatTarget();
        Motor.SeekOverride = target;

        if (target == null)
            return;

        TryExecuteCombatAgainst(target);
    }

    /// <summary>Return false to clear seek (e.g. follower leash to leader).</summary>
    protected virtual bool BeforeSeekCombat() => true;

    protected abstract Transform FindCombatTarget();

    void TryScheduleRangedDodge()
    {
        if (Motor.CanScheduleRangedDodge && !Motor.HasPendingRangedDodge &&
            ArrowProjectile.TryGetIncomingDodgeReference(transform.root, out Vector3 dodgeRef))
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

    protected float AggroRadiusSqr => aggroRadius * aggroRadius;
}
