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
    static readonly int LocomotionParamId = Animator.StringToHash("Locomotion");

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

        Vector3 v = _rigidbody.linearVelocity;
        float horizontalSpeed = new Vector3(v.x, 0f, v.z).magnitude;
        float maxSpeed = _player.GetEffectiveMaxMoveSpeed();

        animator.SetFloat(LocomotionParamId, horizontalSpeed);

        if (horizontalSpeed < stopSpeedThreshold || maxSpeed < 0.01f)
        {
            animator.speed = 1f;
            return;
        }

        float normalized = Mathf.Clamp01(horizontalSpeed / maxSpeed);
        animator.speed = normalized * animationSpeedScale;
    }
}
