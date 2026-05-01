using Medieval.Npcs;
using Medieval.Projectiles;
using UnityEngine;

/// <summary>
/// Fires physics arrows at the nearest faction enemy in range with clear line of sight (same projectile flow as <see cref="RangedCombat"/>).
/// Uses <see cref="TargetFinder"/> when present; otherwise queries DOTS bandit entities via <see cref="NpcWatchTowerBanditQuery"/>.
/// </summary>
public class WatchTowerArrowDefense : MonoBehaviour
{
    [SerializeField] float arrowDamage = 25f;
    [SerializeField] float arrowMaxLifetime = 12f;
    [SerializeField] float arrowHitRadius = 0.08f;
    [SerializeField] float fireIntervalMin = 0.85f;
    [SerializeField] float fireIntervalMax = 1.25f;
    [SerializeField] float combatRange = 40f;
    [SerializeField] float launchHeight = 8.5f;
    [Tooltip("Arrows spawn at this height with XZ offset randomized inside spawnRadius.")]
    [SerializeField] float spawnRadius = 1.25f;
    [SerializeField] float targetAimHeight = 1f;
    [SerializeField] float horizontalAimError = 1.35f;
    [SerializeField] float verticalAimError = 0.28f;

    [Header("Line of sight")]
    [SerializeField] float eyeHeight = 8f;
    [SerializeField] LayerMask obstacleLayers = ~0;

    Collider _ownerCollider;
    float _nextFireTime;
    TargetFinder _targetFinder;

    void Awake()
    {
        _ownerCollider = GetComponent<Collider>();
        _targetFinder = GetComponent<TargetFinder>();
    }

    void Update()
    {
        if (Time.time < _nextFireTime)
            return;

        if (!TryGetShootTarget(out Transform transformTarget, out Vector3 feetWorld, out Vector3 horizVel))
            return;

        if (TryFireAt(transformTarget, feetWorld, horizVel))
        {
            float min = Mathf.Min(fireIntervalMin, fireIntervalMax);
            float max = Mathf.Max(fireIntervalMin, fireIntervalMax);
            _nextFireTime = Time.time + Random.Range(min, max);
        }
    }

    /// <param name="transformTarget">Non-null when aiming at a <see cref="TargetFinder"/> result; feetWorld is ignored for aim position.</param>
    /// <param name="feetWorld">DOTS bandit foot position when <paramref name="transformTarget"/> is null.</param>
    bool TryGetShootTarget(out Transform transformTarget, out Vector3 feetWorld, out Vector3 horizVel)
    {
        transformTarget = null;
        feetWorld = default;
        horizVel = default;

        float rangeSq = combatRange * combatRange;

        if (_targetFinder != null)
        {
            _targetFinder.ScanNow();
            Transform candidate = _targetFinder.CurrentEnemyTarget;
            if (candidate != null)
            {
                var health = candidate.GetComponentInParent<IDamageableHealth>();
                if (health == null || !health.IsDead)
                {
                    float sq = SpatialMath.FlatSqrDistance(transform.position, candidate.position);
                    if (sq <= rangeSq &&
                        LineOfSightUtility.HasClearLineOfSight(transform.position, candidate, eyeHeight, targetAimHeight,
                            obstacleLayers, transform.root))
                    {
                        transformTarget = candidate;
                        horizVel = HorizontalVelocity(candidate);
                        return true;
                    }
                }
            }
        }

        if (NpcWatchTowerBanditQuery.TryFindNearestBanditForTower(
                transform.position,
                combatRange,
                eyeHeight,
                targetAimHeight,
                obstacleLayers,
                transform.root,
                out feetWorld,
                out horizVel))
            return true;

        return false;
    }

    bool TryFireAt(Transform transformTarget, Vector3 feetWorld, Vector3 horizVel)
    {
        Vector2 spawnXz = spawnRadius > 0f ? Random.insideUnitCircle * spawnRadius : Vector2.zero;
        Vector3 towerBase = transform.position;
        Vector3 origin = towerBase + new Vector3(spawnXz.x, launchHeight, spawnXz.y);

        Vector3 aimBase = (transformTarget != null ? transformTarget.position : feetWorld) + Vector3.up * targetAimHeight;
        Vector3 vH = transformTarget != null ? HorizontalVelocity(transformTarget) : horizVel;

        Vector3 aim = aimBase;
        for (int i = 0; i < 2; i++)
        {
            float t = ProjectileBallistics.LobbedFlightTime(origin, aim);
            aim = aimBase + vH * t;
        }

        Vector2 xz = Random.insideUnitCircle * horizontalAimError;
        aim += new Vector3(xz.x, Random.Range(-verticalAimError, verticalAimError), xz.y);

        Vector3 velocity = ProjectileBallistics.LobbedLaunchVelocity(origin, aim, out _);

        ProjectileSpawnApi.Spawn(origin, velocity, arrowDamage, arrowMaxLifetime, transform.root, _ownerCollider, arrowHitRadius);

        return true;
    }

    static Vector3 HorizontalVelocity(Transform target)
    {
        if (target == null)
            return Vector3.zero;
        var rb = target.GetComponentInParent<Rigidbody>();
        if (rb == null)
            return Vector3.zero;
        Vector3 v = rb.linearVelocity;
        return new Vector3(v.x, 0f, v.z);
    }

}
