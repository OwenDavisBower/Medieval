using UnityEngine;

/// <summary>
/// Drives locomotion playback speed from horizontal movement speed.
/// Attach to any character root that has a Rigidbody and either a <see cref="TargetSteeringMotor"/>
/// (NPCs) or a <see cref="PlayerController"/> (player).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class LocomotionAnimatorDriver : MonoBehaviour
{
    [Header("Locomotion animation")]
    [Tooltip("Leave empty to use the first Animator under this object (e.g. mesh animator).")]
    [SerializeField] Animator animator;

    [Tooltip("Below this horizontal speed (m/s), animation playback is stopped.")]
    [SerializeField] float stopSpeedThreshold = 0.04f;

    [Tooltip("Scales walk animation vs. movement after speed is normalized. Raise to reduce foot sliding backward; lower if feet look too fast.")]
    [SerializeField] float animationSpeedScale = 2f;

    Rigidbody _rigidbody;
    TargetSteeringMotor _motor;
    PlayerController _player;

    void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _motor = GetComponent<TargetSteeringMotor>();
        _player = GetComponent<PlayerController>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    void LateUpdate()
    {
        if (animator == null)
            return;

        float maxSpeed = ResolveEffectiveMaxSpeed();
        if (maxSpeed <= 0.001f)
            return;

        LocomotionAnimatorDriverUtil.Apply(
            animator,
            ResolveHorizontalVelocity(),
            maxSpeed,
            stopSpeedThreshold,
            animationSpeedScale
        );
    }

    float ResolveEffectiveMaxSpeed()
    {
        if (_motor != null)
            return _motor.EffectiveMoveSpeed;
        if (_player != null)
            return _player.GetEffectiveMaxMoveSpeed();
        return 0f;
    }

    Vector3 ResolveHorizontalVelocity()
    {
        if (_motor != null)
            return _motor.CurrentHorizontalVelocity;
        if (_rigidbody != null)
            return _rigidbody.linearVelocity;
        return Vector3.zero;
    }
}

