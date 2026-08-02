using System.Collections;
using Unity.Entities;
using UnityEngine;

using Medieval.Projectiles;

/// <summary>Lobs ECS-simulated arrows toward a target with intentional inaccuracy.</summary>
public class RangedCombat : MonoBehaviour
{
    static readonly int ShootArrowHash = Animator.StringToHash("ShootArrow");

    [SerializeField] float arrowDamage = 25f;
    [SerializeField] float arrowMaxLifetime = 12f;
    [SerializeField] float arrowHitRadius = 0.08f;
    [SerializeField] float fireInterval = 1.15f;
    [SerializeField] float launchHeight = 1.45f;
    [SerializeField] float targetAimHeight = 1f;
    [SerializeField] float horizontalAimError = 1.8f;
    [SerializeField] float verticalAimError = 0.35f;
    [Tooltip("Seconds after the shoot animation trigger before the arrow is spawned. Aim is recomputed at release.")]
    [SerializeField] float fireAnimationLeadSeconds = 0.12f;
    [Tooltip("No horizontal movement from steering while drawing/releasing (approx. shoot animation length).")]
    [SerializeField] float movementLockDuration = 0.85f;

    Collider _ownerCollider;
    Character _selfCharacter;
    Animator _animator;
    float _nextFireTime;
    float _movementLockUntilTime;
    bool _shotInProgress;

    public bool IsMovementLocked => Time.time < _movementLockUntilTime;

    public float TargetAimHeight => targetAimHeight;

    public void CancelMovementLock() => _movementLockUntilTime = 0f;

    void OnDisable()
    {
        StopAllCoroutines();
        _shotInProgress = false;
    }

    void Awake()
    {
        _ownerCollider = GetComponentInChildren<Collider>();
        _selfCharacter = GetComponentInParent<Character>();
        _animator = AnimatorUtil.ResolvePreferredAnimator(this);
    }

    /// <returns>True if a shot was started this call (animation + scheduled release).</returns>
    public bool TryFireAt(Transform target)
    {
        if (target == null)
            return false;
        var targetHealth = target.GetComponentInParent<IDamageableHealth>();
        if (targetHealth != null && targetHealth.IsDead)
            return false;

        return BeginShot(target, target.position);
    }

    /// <summary>Fire at a world feet position (e.g. DOTS NPC with no GameObject transform).</summary>
    public bool TryFireAtWorldFeet(Vector3 feetWorld) => BeginShot(null, feetWorld);

    bool BeginShot(Transform liveTarget, Vector3 aimFeetFallback)
    {
        if (_selfCharacter != null && !_selfCharacter.CanAttack)
            return false;
        if (Time.time < _nextFireTime)
            return false;
        if (_shotInProgress)
            return false;

        float aimScale = 1f;
        if (_selfCharacter != null)
            aimScale = _selfCharacter.RangedAimErrorMultiplier;

        _animator ??= AnimatorUtil.ResolvePreferredAnimator(this);
        if (_animator != null)
            _animator.SetTrigger(ShootArrowHash);

        // Apply delay whenever configured (do not tie to animator cache; Awake can run before child rig is ready).
        float lead = Mathf.Max(0f, fireAnimationLeadSeconds);
        _movementLockUntilTime = Time.time + movementLockDuration + lead;
        _nextFireTime = Time.time + fireInterval;
        _shotInProgress = true;

        if (lead <= 0f)
        {
            TrySpawnArrow(ResolveAimFeet(liveTarget, aimFeetFallback), aimScale);
            _shotInProgress = false;
        }
        else
            StartCoroutine(SpawnArrowAfterLead(liveTarget, aimFeetFallback, aimScale, lead));

        return true;
    }

    IEnumerator SpawnArrowAfterLead(Transform liveTarget, Vector3 aimFeetFallback, float aimScale, float lead)
    {
        yield return new WaitForSeconds(lead);
        if (_selfCharacter == null || _selfCharacter.CanAttack)
        {
            if (liveTarget != null)
            {
                var h = liveTarget.GetComponentInParent<IDamageableHealth>();
                if (h != null && h.IsDead)
                {
                    _shotInProgress = false;
                    yield break;
                }
            }

            TrySpawnArrow(ResolveAimFeet(liveTarget, aimFeetFallback), aimScale);
        }
        _shotInProgress = false;
    }

    static Vector3 ResolveAimFeet(Transform liveTarget, Vector3 aimFeetFallback) =>
        liveTarget != null ? liveTarget.position : aimFeetFallback;

    void TrySpawnArrow(Vector3 aimFeetWorld, float aimScale)
    {
        Vector3 origin = transform.position + Vector3.up * launchHeight;
        Vector3 aim = aimFeetWorld + Vector3.up * targetAimHeight;
        Vector2 xz = UnityEngine.Random.insideUnitCircle * (horizontalAimError * aimScale);
        aim += new Vector3(xz.x, UnityEngine.Random.Range(-verticalAimError, verticalAimError) * aimScale, xz.y);

        Vector3 velocity = ProjectileBallistics.LobbedLaunchVelocity(origin, aim);

        ProjectileSpawnApi.Spawn(origin, velocity, arrowDamage, arrowMaxLifetime, transform.root, _ownerCollider, arrowHitRadius);
    }

    class RangedCombatBaker : Baker<RangedCombat>
    {
        public override void Bake(RangedCombat authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
            // Prefabs serialized before the default was added can still carry 0; treat that as unset.
            float damage = authoring.arrowDamage > 0.01f ? authoring.arrowDamage : 25f;
            AddComponent(entity, new Medieval.Npcs.NpcRangedCombatConfig
            {
                ArrowDamage = damage,
                ArrowMaxLifetime = authoring.arrowMaxLifetime,
                ArrowHitRadius = authoring.arrowHitRadius,
                FireInterval = authoring.fireInterval,
                LaunchHeight = authoring.launchHeight,
                TargetAimHeight = authoring.targetAimHeight,
                HorizontalAimError = authoring.horizontalAimError,
                VerticalAimError = authoring.verticalAimError,
                FireAnimationLeadSeconds = authoring.fireAnimationLeadSeconds,
                MovementLockDuration = authoring.movementLockDuration
            });
            AddComponent(entity, new Medieval.Npcs.NpcRangedAttackState());
        }
    }
}
