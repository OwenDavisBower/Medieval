using ProjectDawn.Animation;
using UnityEngine;

/// <summary>
/// Switches Animatron between idle and walk clips from horizontal speed (Rigidbody + optional <see cref="TargetSteeringMotor"/>).
/// For DOTS-only NPCs use <see cref="Medieval.NpcMovement.NpcAnimatronLocomotionSystem"/> instead.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class AnimatronLocomotionDriver : MonoBehaviour
{
    [SerializeField] string idleAnimationName = "Idle";
    [SerializeField] string walkingAnimationName = "Walking";
    [SerializeField] float stopSpeedThreshold = 0.04f;
    [SerializeField] float animationSpeedScale = 2f;

    AnimatronAuthoring _animatron;
    CrossFaderAuthoring _crossFader;
    TargetSteeringMotor _motor;
    Rigidbody _rigidbody;
    bool _lastMoving;

    void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _motor = GetComponent<TargetSteeringMotor>();
        _animatron = GetComponentInChildren<AnimatronAuthoring>();
        if (_animatron != null)
            _crossFader = _animatron.GetComponent<CrossFaderAuthoring>();
    }

    void Start()
    {
        if (_animatron == null)
            return;
        ApplyLocomotionClip(isMoving: false, force: true);
    }

    void LateUpdate()
    {
        if (_animatron == null)
            return;

        Vector3 v = ResolveHorizontalVelocity();
        float maxSpeed = ResolveEffectiveMaxSpeed();
        float horizontalSpeed = new Vector3(v.x, 0f, v.z).magnitude;
        bool moving = horizontalSpeed >= stopSpeedThreshold && maxSpeed >= 0.01f;

        if (moving)
        {
            float normalized = Mathf.Clamp01(horizontalSpeed / maxSpeed);
            _animatron.Speed = normalized * animationSpeedScale;
        }
        else
            _animatron.Speed = 1f;

        ApplyLocomotionClip(moving, force: false);
    }

    void ApplyLocomotionClip(bool isMoving, bool force)
    {
        if (!force && isMoving == _lastMoving)
            return;
        _lastMoving = isMoving;

        string name = isMoving ? walkingAnimationName : idleAnimationName;
        if (!_animatron.TryFindAnimationIndex(name, out var index))
            return;

        if (_crossFader != null)
            _crossFader.CrossFade(index);
        else
            _animatron.Play(index);
    }

    float ResolveEffectiveMaxSpeed()
    {
        if (_motor != null)
            return _motor.EffectiveMoveSpeed;
        return 1f;
    }

    Vector3 ResolveHorizontalVelocity()
    {
        if (_motor != null)
            return _motor.CurrentHorizontalVelocity;
        if (_rigidbody != null)
        {
            var v = _rigidbody.linearVelocity;
            v.y = 0f;
            return v;
        }
        return Vector3.zero;
    }
}
