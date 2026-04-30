using System;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Legacy GameObject-side movement facade (deprecated).
///
/// The project now uses entity-authored <c>NpcMovementAuthoring</c> + pure DOTS steering systems that
/// update <c>LocalTransform</c> directly (no companion/writeback). This component remains only so older
/// MonoBehaviour gameplay scripts still compile when the project is in a transitional state.
/// </summary>
[Obsolete("Deprecated: use entity-authored NpcMovementAuthoring + DOTS systems (no companion/writeback).")]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class TargetSteeringMotor : MonoBehaviour
{
    [Header("Mode")]
    [SerializeField] TargetSteeringMovementMode mode = TargetSteeringMovementMode.Orbit;
    [SerializeField] Transform anchorTarget;
    [SerializeField] Transform seekOverride;
    [SerializeField] float seekHoldDistance;

    [Header("Motion")]
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float moveSpeedScale = 1f;

    [Header("Combat movement")]
    [SerializeField] float rangedDodgeCooldown = 0.42f;

    Rigidbody _rb;
    float _lastDodgeApplyTime = float.NegativeInfinity;
    bool _rangedMovementLocked;
    Vector3? _overrideFacingFlatDirection;

    public float EffectiveMoveSpeed => Mathf.Max(0f, moveSpeed) * Mathf.Max(0.05f, moveSpeedScale);

    public Vector3 CurrentHorizontalVelocity
    {
        get
        {
            if (_rb == null)
                return Vector3.zero;
            var v = _rb.linearVelocity;
            v.y = 0f;
            return v;
        }
    }

    public TargetSteeringSeparationGroup SeparationGroup => TargetSteeringSeparationGroup.None;

    public bool CanScheduleRangedDodge => Time.time >= _lastDodgeApplyTime + rangedDodgeCooldown;

    public bool HasPendingRangedDodge => false;

    public Transform AnchorTarget
    {
        get => anchorTarget;
        set => anchorTarget = value;
    }

    public Transform SeekOverride
    {
        get => seekOverride;
        set => seekOverride = value;
    }

    public float SeekHoldDistance
    {
        get => seekHoldDistance;
        set => seekHoldDistance = value;
    }

    public float MoveSpeedScale
    {
        get => moveSpeedScale;
        set => moveSpeedScale = Mathf.Max(0.05f, value);
    }

    public TargetSteeringMovementMode Mode
    {
        get => mode;
        set => mode = value;
    }

    public void SetRangedMovementLock(bool locked) => _rangedMovementLocked = locked;

    public void SetOverrideFacingTowardWorldPoint(Vector3 worldPosition)
    {
        Vector3 d = worldPosition - transform.position;
        d.y = 0f;
        _overrideFacingFlatDirection = d.sqrMagnitude > 1e-6f ? d.normalized : null;
    }

    public void ClearOverrideFacing() => _overrideFacingFlatDirection = null;

    public void ScheduleRangedDodgeImpulse(Vector3 _)
    {
        // Deprecated path: no-op, just advances cooldown bookkeeping so callers don't spam.
        _lastDodgeApplyTime = Time.time;
    }

    public void InitializeOrbitRandom()
    {
        // Deprecated path: no-op.
    }

    public void InitializeWanderAroundAnchor(Transform campAnchor, bool randomizeTimer = true)
    {
        anchorTarget = campAnchor;
        // Deprecated path: no-op.
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.None;
            _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY |
                              RigidbodyConstraints.FreezeRotationZ;
        }
    }
}

public enum TargetSteeringMovementMode
{
    Orbit,
    MoveTowards,
    WanderAroundTarget
}

public enum TargetSteeringSeparationGroup
{
    None,
    Followers,
    Bandits
}

