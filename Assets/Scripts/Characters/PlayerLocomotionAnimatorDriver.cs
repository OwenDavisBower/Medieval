using UnityEngine;

/// <summary>
/// Drives <c>Locomotion</c> on <see cref="HumanAnimationController"/> from player Rigidbody velocity
/// (same idea as <see cref="NpcLocomotionAnimatorDriver"/> for <see cref="TargetSteeringMotor"/> NPCs).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerController))]
public sealed class PlayerLocomotionAnimatorDriver : MonoBehaviour
{
    [Header("Locomotion animation")]
    [Tooltip("Leave empty to use the first Animator under this object (e.g. mesh animator).")]
    [SerializeField] Animator animator;

    [Tooltip("Below this horizontal speed (m/s), animation playback is stopped.")]
    [SerializeField] float stopSpeedThreshold = 0.04f;

    [Tooltip("Scales walk animation vs. movement after speed is normalized.")]
    [SerializeField] float animationSpeedScale = 2f;

    Rigidbody _rigidbody;
    PlayerController _player;

    void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _player = GetComponent<PlayerController>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    void LateUpdate()
    {
        if (animator == null || _player == null || _rigidbody == null)
            return;

        float maxSpeed = _player.GetEffectiveMaxMoveSpeed();
        LocomotionAnimatorDriverUtil.Apply(animator, _rigidbody.linearVelocity, maxSpeed, stopSpeedThreshold,
            animationSpeedScale);
    }
}
