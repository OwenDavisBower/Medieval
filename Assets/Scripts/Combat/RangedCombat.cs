using UnityEngine;

/// <summary>Lobs physics-based arrows toward a target with intentional inaccuracy.</summary>
public class RangedCombat : MonoBehaviour
{
    static readonly int ShootArrowHash = Animator.StringToHash("ShootArrow");

    [SerializeField] Rigidbody arrowPrefab;
    [SerializeField] float fireInterval = 1.15f;
    [SerializeField] float launchHeight = 1.45f;
    [SerializeField] float targetAimHeight = 1f;
    [SerializeField] float horizontalAimError = 1.8f;
    [SerializeField] float verticalAimError = 0.35f;
    [Tooltip("No horizontal movement from steering while drawing/releasing (approx. shoot animation length).")]
    [SerializeField] float movementLockDuration = 0.85f;

    Collider _ownerCollider;
    Character _selfCharacter;
    Animator _animator;
    float _nextFireTime;
    float _movementLockUntilTime;

    public bool IsMovementLocked => Time.time < _movementLockUntilTime;

    public void CancelMovementLock() => _movementLockUntilTime = 0f;

    void Awake()
    {
        _ownerCollider = GetComponentInChildren<Collider>();
        _selfCharacter = GetComponentInParent<Character>();
        _animator = GetComponentInChildren<Animator>();
    }

    /// <returns>True if a shot was fired this call.</returns>
    public bool TryFireAt(Transform target)
    {
        if (arrowPrefab == null || target == null)
            return false;
        if (_selfCharacter != null && !_selfCharacter.CanAttack)
            return false;
        if (Time.time < _nextFireTime)
            return false;

        float aimScale = 1f;
        if (_selfCharacter != null)
            aimScale = _selfCharacter.RangedAimErrorMultiplier;

        Vector3 origin = transform.position + Vector3.up * launchHeight;
        Vector3 aim = target.position + Vector3.up * targetAimHeight;
        Vector2 xz = Random.insideUnitCircle * (horizontalAimError * aimScale);
        aim += new Vector3(xz.x, Random.Range(-verticalAimError, verticalAimError) * aimScale, xz.y);

        Vector3 velocity = LobbedLaunchVelocity(origin, aim);

        Rigidbody arrow = Instantiate(arrowPrefab, origin, Quaternion.identity);
        arrow.linearVelocity = velocity;

        var projectile = arrow.GetComponent<ArrowProjectile>();
        if (projectile != null)
            projectile.SetShooterRoot(transform.root);

        if (velocity.sqrMagnitude > 0.01f)
        {
            Vector3 forward = velocity.normalized;
            if (forward.sqrMagnitude > 0.99f)
                arrow.MoveRotation(Quaternion.LookRotation(forward));
        }

        if (_ownerCollider != null)
        {
            var ac = arrow.GetComponent<Collider>();
            if (ac != null)
                Physics.IgnoreCollision(_ownerCollider, ac, true);
        }

        if (_animator != null)
            _animator.SetTrigger(ShootArrowHash);

        _movementLockUntilTime = Time.time + movementLockDuration;
        _nextFireTime = Time.time + fireInterval;
        return true;
    }

    static Vector3 LobbedLaunchVelocity(Vector3 from, Vector3 to)
    {
        Vector3 displacement = to - from;
        Vector3 horizontal = new Vector3(displacement.x, 0f, displacement.z);
        float h = horizontal.magnitude;
        if (h < 0.05f)
            h = 0.05f;
        float dh = displacement.y;
        float g = -Physics.gravity.y;
        if (g < 0.01f)
            g = 9.81f;

        float t = Mathf.Clamp(h / 12f, 0.55f, 2.2f);
        float vy = (dh + 0.5f * g * t * t) / t;
        Vector3 vHoriz = horizontal.normalized * (h / t);
        return new Vector3(vHoriz.x, vy, vHoriz.z);
    }
}
