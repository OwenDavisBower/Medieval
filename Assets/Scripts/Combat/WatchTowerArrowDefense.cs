using UnityEngine;

/// <summary>
/// Fires physics arrows at the nearest bandit in range with clear line of sight (same projectile flow as <see cref="RangedCombat"/>).
/// </summary>
public class WatchTowerArrowDefense : MonoBehaviour
{
    [SerializeField] Rigidbody arrowPrefab;
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

    void Awake()
    {
        _ownerCollider = GetComponent<Collider>();
    }

    void Update()
    {
        if (arrowPrefab == null || Time.time < _nextFireTime)
            return;

        BanditController target = FindBanditToShoot();
        if (target == null)
            return;

        if (TryFireAt(target))
        {
            float min = Mathf.Min(fireIntervalMin, fireIntervalMax);
            float max = Mathf.Max(fireIntervalMin, fireIntervalMax);
            _nextFireTime = Time.time + Random.Range(min, max);
        }
    }

    BanditController FindBanditToShoot()
    {
        float rangeSq = combatRange * combatRange;
        BanditController best = null;
        float bestSq = float.MaxValue;

        BanditController[] bandits = CombatUnitRegistry.GetBandits();
        for (int i = 0; i < bandits.Length; i++)
        {
            BanditController b = bandits[i];
            if (b == null)
                continue;

            Character ch = b.GetComponent<Character>();
            if (ch != null && ch.IsDead)
                continue;

            float sq = SpatialMath.FlatSqrDistance(transform.position, b.transform.position);
            if (sq > rangeSq || sq >= bestSq)
                continue;

            if (!LineOfSightUtility.HasClearLineOfSight(transform.position, b.transform, eyeHeight, targetAimHeight,
                    obstacleLayers, transform.root))
                continue;

            best = b;
            bestSq = sq;
        }

        return best;
    }

    bool TryFireAt(BanditController target)
    {
        Vector2 spawnXz = spawnRadius > 0f ? Random.insideUnitCircle * spawnRadius : Vector2.zero;
        Vector3 towerBase = transform.position;
        Vector3 origin = towerBase + new Vector3(spawnXz.x, launchHeight, spawnXz.y);

        Vector3 aimBase = target.transform.position + Vector3.up * targetAimHeight;
        Vector3 vH = HorizontalVelocity(target);

        Vector3 aim = aimBase;
        for (int i = 0; i < 2; i++)
        {
            float t = LobbedFlightTime(origin, aim);
            aim = aimBase + vH * t;
        }

        Vector2 xz = Random.insideUnitCircle * horizontalAimError;
        aim += new Vector3(xz.x, Random.Range(-verticalAimError, verticalAimError), xz.y);

        Vector3 velocity = LobbedLaunchVelocity(origin, aim, out _);

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

        return true;
    }

    static Vector3 HorizontalVelocity(BanditController bandit)
    {
        if (bandit == null)
            return Vector3.zero;
        var rb = bandit.GetComponent<Rigidbody>();
        if (rb == null)
            return Vector3.zero;
        Vector3 v = rb.linearVelocity;
        return new Vector3(v.x, 0f, v.z);
    }

    static float LobbedFlightTime(Vector3 from, Vector3 to)
    {
        Vector3 displacement = to - from;
        Vector3 horizontal = new Vector3(displacement.x, 0f, displacement.z);
        float h = horizontal.magnitude;
        if (h < 0.05f)
            h = 0.05f;
        return Mathf.Clamp(h / 12f, 0.55f, 2.2f);
    }

    static Vector3 LobbedLaunchVelocity(Vector3 from, Vector3 to, out float flightTime)
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

        flightTime = Mathf.Clamp(h / 12f, 0.55f, 2.2f);
        float t = flightTime;
        float vy = (dh + 0.5f * g * t * t) / t;
        Vector3 vHoriz = horizontal.normalized * (h / t);
        return new Vector3(vHoriz.x, vy, vHoriz.z);
    }
}
