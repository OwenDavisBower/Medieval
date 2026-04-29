using UnityEngine;

/// <summary>
/// Drives locomotion playback speed from horizontal movement speed.
/// Attach to any NPC root that has a Rigidbody + TargetSteeringMotor and an Animator in children.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(TargetSteeringMotor))]
public sealed class NpcLocomotionAnimatorDriver : MonoBehaviour
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

    void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _motor = GetComponent<TargetSteeringMotor>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    void LateUpdate()
    {
        if (animator == null || _motor == null || _rigidbody == null)
            return;

        float maxSpeed = _motor.EffectiveMoveSpeed;
        LocomotionAnimatorDriverUtil.Apply(animator, _rigidbody.linearVelocity, maxSpeed, stopSpeedThreshold,
            animationSpeedScale);
    }
}

