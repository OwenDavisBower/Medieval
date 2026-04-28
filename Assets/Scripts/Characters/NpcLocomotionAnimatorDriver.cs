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

        Vector3 v = _rigidbody.linearVelocity;
        float horizontalSpeed = new Vector3(v.x, 0f, v.z).magnitude;
        float maxSpeed = _motor.EffectiveMoveSpeed;

        if (horizontalSpeed < stopSpeedThreshold || maxSpeed < 0.01f)
        {
            animator.speed = 0f;
            return;
        }

        float normalized = Mathf.Clamp01(horizontalSpeed / maxSpeed);
        animator.speed = normalized * animationSpeedScale;
    }
}

