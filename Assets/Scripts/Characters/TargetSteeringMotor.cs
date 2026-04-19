using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum TargetSteeringMovementMode
{
    /// <summary>Loiter on an annulus around <see cref="TargetSteeringMotor.anchorTarget"/> with organic noise.</summary>
    Orbit,
    /// <summary>Seek <see cref="TargetSteeringMotor.seekOverride"/> if set, otherwise <see cref="TargetSteeringMotor.anchorTarget"/>.</summary>
    MoveTowards,
    /// <summary>Random walk within a disk around <see cref="TargetSteeringMotor.anchorTarget"/> (re-picks periodically).</summary>
    WanderAroundTarget
}

public enum TargetSteeringSeparationGroup
{
    None,
    Followers,
    Bandits
}

/// <summary>
/// Ground-plane steering toward a computed goal: orbit, seek, or wander around an anchor transform.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Rigidbody))]
public class TargetSteeringMotor : MonoBehaviour
{
    [Header("Mode")]
    [SerializeField] TargetSteeringMovementMode mode = TargetSteeringMovementMode.Orbit;
    [Tooltip("Center for orbit and wander; MoveTowards seeks this when seek override is unset.")]
    [SerializeField] Transform anchorTarget;
    [Tooltip("When set, steering seeks this transform (e.g. chase target) regardless of mode.")]
    [SerializeField] Transform seekOverride;
    [Tooltip("When > 0 and seek override is set, horizontal distance at or below this stops closing in (ranged standoff).")]
    [SerializeField] float seekHoldDistance;

    [Header("Motion")]
    [SerializeField] float moveSpeed = 5f;
    [Tooltip("Applied to moveSpeed (e.g. from Character dexterity).")]
    [SerializeField] float moveSpeedScale = 1f;
    [SerializeField] float arriveThreshold = 0.15f;
    [SerializeField] float acceleration = 14f;
    [Tooltip("Max degrees per second to rotate toward horizontal velocity.")]
    [SerializeField] float facingTurnSpeedDegreesPerSecond = 720f;
    [Tooltip("Horizontal speed added sideways right after a ranged shot (strafe dodge).")]
    [SerializeField] float postRangedDodgeImpulse = 3.6f;
    [Tooltip("Fraction of dodge impulse applied away from the target (retreat after shooting).")]
    [SerializeField] float postRangedDodgeRetreatRatio = 0.28f;
    [Tooltip("Reaction delay before applying a scheduled ranged dodge impulse.")]
    [SerializeField] float postRangedDodgeDelay = 0.14f;
    [Tooltip("Minimum time between ranged dodge impulses (incoming projectile dodges).")]
    [SerializeField] float rangedDodgeCooldown = 0.42f;

    [Header("Orbit (annulus around anchor)")]
    [SerializeField] float minLoiterRadius = 2.5f;
    [SerializeField] float maxLoiterRadius = 5.5f;

    [Header("Orbit — trail behind moving anchor")]
    [SerializeField] float trailBehindStrength = 0.35f;
    [SerializeField] float maxTrailOffset = 2f;

    [Header("Wander (disk around anchor)")]
    [SerializeField] float wanderRadius = 20f;
    [SerializeField] float repickWanderInterval = 4f;

    [Header("Organic motion (orbit & wander)")]
    [SerializeField] float targetSmoothTime = 0.35f;
    [SerializeField] float noiseFrequency = 0.2f;
    [SerializeField] float angleWobbleDegrees = 38f;
    [SerializeField] float radiusWobble = 2f;

    [Header("Pathfinding & avoidance")]
    [SerializeField] bool useNavMeshWhenAvailable = true;
    [SerializeField] float navMeshSampleMaxDistance = 2f;
    [SerializeField] float minCornerAdvanceDistance = 0.35f;
    [SerializeField] TargetSteeringSeparationGroup separationGroup = TargetSteeringSeparationGroup.None;
    [SerializeField] float separationRadius = 1.1f;
    [SerializeField] float separationStrength = 4f;
    [SerializeField] float obstacleProbeRadius = 0.35f;
    [SerializeField] float obstacleProbeDistance = 1.25f;
    [SerializeField] LayerMask obstacleLayers = ~0;
    [Tooltip("Bandits ignore follower colliders for obstacle probes (LOS/aggro stay on BanditController).")]
    [SerializeField] bool ignoreFollowerCollidersForObstacles;

    static readonly List<TargetSteeringMotor> FollowerMotors = new List<TargetSteeringMotor>();
    static readonly List<TargetSteeringMotor> BanditMotors = new List<TargetSteeringMotor>();

    NavMeshPath _navPath;
    float _baseAngle;
    float _baseRadius;
    float _noiseA;
    float _noiseB;
    Rigidbody _rb;
    Rigidbody _anchorRb;
    Vector3 _smoothTarget;
    Vector3 _smoothTargetVel;
    bool _hasSmoothTarget;
    bool _orbitInitialized;
    bool _wanderInitialized;
    float _nextWanderPickTime;
    Vector3? _pendingDodgeReferencePosition;
    float _pendingDodgeTime;
    float _lastRangedDodgeApplyTime = float.NegativeInfinity;
    bool _dodgeImpulseThisFixed;
    float _effectiveMoveSpeedThisFixed;

    public Transform AnchorTarget
    {
        get => anchorTarget;
        set => anchorTarget = value;
    }

    /// <summary>When set, goal position follows this transform (cleared when null).</summary>
    public Transform SeekOverride
    {
        get => seekOverride;
        set => seekOverride = value;
    }

    public float SeekHoldDistance
    {
        get => seekHoldDistance;
        set => seekHoldDistance = value;
    }

    /// <summary>Scales base move speed (e.g. from Character dexterity). Clamped to a small positive minimum.</summary>
    public float MoveSpeedScale
    {
        get => moveSpeedScale;
        set => moveSpeedScale = Mathf.Max(0.05f, value);
    }

    public TargetSteeringMovementMode Mode
    {
        get => mode;
        set => mode = value;
    }

    public TargetSteeringSeparationGroup SeparationGroup => separationGroup;

    /// <summary>Whether another ranged dodge can be scheduled (cooldown after the last applied dodge).</summary>
    public bool CanScheduleRangedDodge => Time.time >= _lastRangedDodgeApplyTime + rangedDodgeCooldown;

    /// <summary>True while a dodge impulse is queued but not yet applied.</summary>
    public bool HasPendingRangedDodge => _pendingDodgeReferencePosition.HasValue;

    /// <summary>Queues a dodge after <see cref="postRangedDodgeDelay"/>; replaces any pending dodge.</summary>
    public void ScheduleRangedDodgeImpulse(Vector3 dodgeReferenceWorldPosition)
    {
        if (postRangedDodgeDelay <= 0f)
        {
            ApplyRangedDodgeImpulse(dodgeReferenceWorldPosition);
            return;
        }

        _pendingDodgeReferencePosition = dodgeReferenceWorldPosition;
        _pendingDodgeTime = Time.time + postRangedDodgeDelay;
    }

    /// <summary>Small random sideways nudge on the horizontal plane after firing at <paramref name="targetPosition"/>.</summary>
    public void ApplyRangedDodgeImpulse(Vector3 targetPosition)
    {
        if (postRangedDodgeImpulse <= 0f || _rb == null)
            return;

        _lastRangedDodgeApplyTime = Time.time;

        Vector3 flat = targetPosition - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1e-4f)
            return;
        flat.Normalize();
        Vector3 perp = Vector3.Cross(Vector3.up, flat);
        if (perp.sqrMagnitude < 1e-6f)
            return;
        perp.Normalize();
        if (Random.value < 0.5f)
            perp = -perp;

        Vector3 retreat = -flat * (postRangedDodgeImpulse * postRangedDodgeRetreatRatio);
        Vector3 add = perp * postRangedDodgeImpulse + retreat;
        Vector3 v = _rb.linearVelocity;
        v.x += add.x;
        v.z += add.z;
        Vector3 h = new Vector3(v.x, 0f, v.z);
        float cap = ComputeEffectiveMoveSpeed() * 2.05f;
        if (h.sqrMagnitude > cap * cap)
            h = h.normalized * cap;
        v.x = h.x;
        v.z = h.z;
        _rb.linearVelocity = v;
        _dodgeImpulseThisFixed = true;
    }

    public void InitializeOrbitRandom()
    {
        _baseAngle = Random.Range(0f, Mathf.PI * 2f);
        _baseRadius = Random.Range(minLoiterRadius, maxLoiterRadius);
        _noiseA = Random.Range(0f, 100f);
        _noiseB = Random.Range(0f, 100f);
        _orbitInitialized = true;
    }

    public void InitializeWanderAroundAnchor(Transform campAnchor, bool randomizeTimer = true)
    {
        anchorTarget = campAnchor;
        _baseAngle = Random.Range(0f, Mathf.PI * 2f);
        _baseRadius = Random.Range(0f, wanderRadius);
        _noiseA = Random.Range(0f, 100f);
        _noiseB = Random.Range(0f, 100f);
        _nextWanderPickTime = Time.time + (randomizeTimer ? Random.Range(0f, 1f) : 0f);
        _wanderInitialized = true;
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
        _navPath = new NavMeshPath();
    }

    void OnEnable()
    {
        switch (separationGroup)
        {
            case TargetSteeringSeparationGroup.Followers:
                FollowerMotors.Add(this);
                break;
            case TargetSteeringSeparationGroup.Bandits:
                BanditMotors.Add(this);
                break;
        }
    }

    void OnDisable()
    {
        _pendingDodgeReferencePosition = null;
        switch (separationGroup)
        {
            case TargetSteeringSeparationGroup.Followers:
                RemoveMotorSwap(FollowerMotors, this);
                break;
            case TargetSteeringSeparationGroup.Bandits:
                RemoveMotorSwap(BanditMotors, this);
                break;
        }
    }

    static void RemoveMotorSwap(List<TargetSteeringMotor> list, TargetSteeringMotor item)
    {
        int i = list.IndexOf(item);
        if (i < 0)
            return;
        int last = list.Count - 1;
        if (i != last)
            list[i] = list[last];
        list.RemoveAt(last);
    }

    void Start()
    {
        if (mode == TargetSteeringMovementMode.Orbit && !_orbitInitialized)
            InitializeOrbitRandom();
        if (mode == TargetSteeringMovementMode.WanderAroundTarget && !_wanderInitialized && anchorTarget != null)
            InitializeWanderAroundAnchor(anchorTarget, randomizeTimer: true);
    }

    void FixedUpdate()
    {
        _effectiveMoveSpeedThisFixed = ComputeEffectiveMoveSpeed();

        if (_pendingDodgeReferencePosition.HasValue && Time.time >= _pendingDodgeTime)
        {
            Vector3 dodgeRef = _pendingDodgeReferencePosition.Value;
            _pendingDodgeReferencePosition = null;
            ApplyRangedDodgeImpulse(dodgeRef);
        }

        if (seekOverride != null)
        {
            Vector3 goal = seekOverride.position;
            if (seekHoldDistance > 0f)
            {
                Vector3 flat = goal - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude <= seekHoldDistance * seekHoldDistance)
                {
                    ApplySteering(transform.position);
                    return;
                }
            }

            ApplySteering(goal);
            return;
        }

        if (anchorTarget == null)
        {
            _dodgeImpulseThisFixed = false;
            return;
        }

        CacheAnchorRigidbody();

        Vector3 rawTarget;
        switch (mode)
        {
            case TargetSteeringMovementMode.Orbit:
                rawTarget = ComputeOrbitTarget();
                break;
            case TargetSteeringMovementMode.MoveTowards:
                rawTarget = anchorTarget.position;
                break;
            case TargetSteeringMovementMode.WanderAroundTarget:
                rawTarget = ComputeWanderTarget();
                break;
            default:
                rawTarget = anchorTarget.position;
                break;
        }

        ApplySteering(rawTarget);
    }

    void CacheAnchorRigidbody()
    {
        if (_anchorRb == null && anchorTarget != null)
            _anchorRb = anchorTarget.GetComponent<Rigidbody>();
    }

    Vector3 ComputeOrbitTarget()
    {
        float t = Time.time * noiseFrequency;
        float angleJitter = (Mathf.PerlinNoise(_noiseA, t) - 0.5f) * 2f * (angleWobbleDegrees * Mathf.Deg2Rad);
        float r = _baseRadius + (Mathf.PerlinNoise(t, _noiseB) - 0.5f) * 2f * radiusWobble;
        float angle = _baseAngle + angleJitter;
        Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * r;

        Vector3 trail = Vector3.zero;
        if (_anchorRb != null && trailBehindStrength > 0f)
        {
            Vector3 pv = _anchorRb.linearVelocity;
            pv.y = 0f;
            float mag = pv.magnitude;
            if (mag > 0.05f)
                trail = -pv.normalized * Mathf.Min(mag * trailBehindStrength, maxTrailOffset);
        }

        return anchorTarget.position + trail + offset;
    }

    Vector3 ComputeWanderTarget()
    {
        if (Time.time >= _nextWanderPickTime)
        {
            _nextWanderPickTime = Time.time + repickWanderInterval * Random.Range(0.7f, 1.3f);
            Vector3 disk = SpawnPlacementUtility.RandomUniformDiskOffsetXZ_SinXCosZ(wanderRadius);
            _baseRadius = disk.magnitude;
            _baseAngle = _baseRadius > 1e-5f ? Mathf.Atan2(disk.x, disk.z) : 0f;
        }

        float t = Time.time * noiseFrequency;
        float angleJitter = (Mathf.PerlinNoise(_noiseA, t) - 0.5f) * 2f * (angleWobbleDegrees * Mathf.Deg2Rad);
        float rWobble = (Mathf.PerlinNoise(t, _noiseB) - 0.5f) * 2f * radiusWobble;
        float angle = _baseAngle + angleJitter;
        float r = Mathf.Clamp(_baseRadius + rWobble, 0f, wanderRadius);
        Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * r;
        return anchorTarget.position + offset;
    }

    void ApplySteering(Vector3 rawTarget)
    {
        if (!_hasSmoothTarget)
        {
            _smoothTarget = rawTarget;
            _hasSmoothTarget = true;
        }
        else
        {
            _smoothTarget = Vector3.SmoothDamp(_smoothTarget, rawTarget, ref _smoothTargetVel, targetSmoothTime);
        }

        Vector3 seekPoint = GetSeekPoint(_smoothTarget);

        Vector3 flat = seekPoint - transform.position;
        flat.y = 0f;

        Vector3 velocity = _rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

        if (flat.sqrMagnitude > arriveThreshold * arriveThreshold)
        {
            Vector3 desiredDir = flat.normalized;
            desiredDir = AdjustForObstacles(desiredDir);
            Vector3 desired = desiredDir * _effectiveMoveSpeedThisFixed;
            desired += ComputeSeparation();
            float maxHorizSpeed = _effectiveMoveSpeedThisFixed;
            if (desired.sqrMagnitude > maxHorizSpeed * maxHorizSpeed)
                desired = desired.normalized * maxHorizSpeed;

            float maxDelta = acceleration * Time.fixedDeltaTime;
            Vector3 newHorizontal = Vector3.MoveTowards(horizontal, desired, maxDelta);
            velocity.x = newHorizontal.x;
            velocity.z = newHorizontal.z;
        }
        else
        {
            Vector3 newHorizontal = Vector3.MoveTowards(horizontal, Vector3.zero, acceleration * Time.fixedDeltaTime);
            velocity.x = newHorizontal.x;
            velocity.z = newHorizontal.z;
        }

        ClampHorizontalWater(ref velocity);
        _rb.linearVelocity = velocity;
        ApplyFacingFromHorizontalVelocity();
    }

    void ApplyFacingFromHorizontalVelocity()
    {
        Vector3 h = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        if (h.sqrMagnitude < 1e-4f)
            return;
        Quaternion targetRot = Quaternion.LookRotation(h.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot,
            facingTurnSpeedDegreesPerSecond * Time.fixedDeltaTime);
    }

    float ComputeEffectiveMoveSpeed() =>
        moveSpeed * moveSpeedScale * WaterMovement.SpeedMultiplier(transform.position.y);

    void ClampHorizontalWater(ref Vector3 velocity)
    {
        if (WaterMovement.SpeedMultiplier(transform.position.y) >= 1f)
        {
            _dodgeImpulseThisFixed = false;
            return;
        }

        bool allowDodgeBurst = _dodgeImpulseThisFixed;
        _dodgeImpulseThisFixed = false;

        float cap = _effectiveMoveSpeedThisFixed;
        if (allowDodgeBurst)
            cap *= 2.05f;

        Vector3 h = new Vector3(velocity.x, 0f, velocity.z);
        if (h.sqrMagnitude > cap * cap)
            h = h.normalized * cap;
        velocity.x = h.x;
        velocity.z = h.z;
    }

    Vector3 GetSeekPoint(Vector3 goal)
    {
        if (!useNavMeshWhenAvailable)
            return goal;

        Vector3 origin = transform.position;
        if (!NavMesh.SamplePosition(origin, out NavMeshHit startHit, navMeshSampleMaxDistance, NavMesh.AllAreas))
            return goal;
        if (!NavMesh.SamplePosition(goal, out NavMeshHit goalHit, navMeshSampleMaxDistance, NavMesh.AllAreas))
            return goal;

        if (!NavMesh.CalculatePath(startHit.position, goalHit.position, NavMesh.AllAreas, _navPath))
            return goal;

        if (_navPath.status == NavMeshPathStatus.PathInvalid)
            return goal;

        if (_navPath.corners == null || _navPath.corners.Length < 2)
            return goal;

        for (int i = 1; i < _navPath.corners.Length; i++)
        {
            Vector3 c = _navPath.corners[i];
            c.y = origin.y;
            if ((c - origin).sqrMagnitude > minCornerAdvanceDistance * minCornerAdvanceDistance)
                return c;
        }

        return _navPath.corners[_navPath.corners.Length - 1];
    }

    Vector3 AdjustForObstacles(Vector3 desiredDir)
    {
        if (desiredDir.sqrMagnitude < 1e-6f)
            return desiredDir;

        Vector3 origin = transform.position + Vector3.up * 0.1f;
        if (Physics.SphereCast(origin, obstacleProbeRadius, desiredDir, out RaycastHit hit, obstacleProbeDistance,
                obstacleLayers, QueryTriggerInteraction.Ignore) && !IsAgentCollider(hit.collider))
        {
            Vector3 n = hit.normal;
            n.y = 0f;
            if (n.sqrMagnitude < 1e-6f)
                return desiredDir;
            n.Normalize();

            Vector3 tangent = Vector3.Cross(Vector3.up, n);
            if (tangent.sqrMagnitude < 1e-6f)
                return desiredDir;
            tangent.Normalize();
            if (Vector3.Dot(tangent, desiredDir) < 0f)
                tangent = -tangent;

            return (desiredDir * 0.35f + tangent * 0.65f).normalized;
        }

        return desiredDir;
    }

    bool IsAgentCollider(Collider col)
    {
        if (col == null)
            return false;

        var otherMotor = col.GetComponentInParent<TargetSteeringMotor>();
        if (otherMotor != null && otherMotor != this)
        {
            if (separationGroup == TargetSteeringSeparationGroup.Followers &&
                otherMotor.SeparationGroup == TargetSteeringSeparationGroup.Followers)
                return true;
            if (separationGroup == TargetSteeringSeparationGroup.Bandits &&
                otherMotor.SeparationGroup == TargetSteeringSeparationGroup.Bandits)
                return true;
        }

        if (ignoreFollowerCollidersForObstacles && col.GetComponentInParent<FollowerController>() != null)
            return true;

        if (anchorTarget != null && (col.transform == anchorTarget || col.transform.IsChildOf(anchorTarget)))
            return true;

        return false;
    }

    Vector3 ComputeSeparation()
    {
        float r = separationRadius;
        float rSq = r * r;
        Vector3 sum = Vector3.zero;
        Vector3 p = transform.position;

        if (separationGroup == TargetSteeringSeparationGroup.Followers && anchorTarget != null)
        {
            float sq = SpatialMath.FlatSqrDistance(p, anchorTarget.position);
            Vector3 d = p - anchorTarget.position;
            d.y = 0f;
            if (sq > 1e-6f && sq < rSq)
            {
                float dist = Mathf.Sqrt(sq);
                sum += d.normalized * (separationStrength * (1f - dist / r));
            }

            for (int i = 0; i < FollowerMotors.Count; i++)
            {
                TargetSteeringMotor other = FollowerMotors[i];
                if (other == null || other == this)
                    continue;

                float osq = SpatialMath.FlatSqrDistance(p, other.transform.position);
                Vector3 od = p - other.transform.position;
                od.y = 0f;
                if (osq > 1e-6f && osq < rSq)
                {
                    float dist = Mathf.Sqrt(osq);
                    sum += od.normalized * (separationStrength * (1f - dist / r));
                }
            }

            return sum;
        }

        if (separationGroup == TargetSteeringSeparationGroup.Bandits)
        {
            for (int i = 0; i < BanditMotors.Count; i++)
            {
                TargetSteeringMotor other = BanditMotors[i];
                if (other == null || other == this)
                    continue;

                float sq = SpatialMath.FlatSqrDistance(p, other.transform.position);
                Vector3 d = p - other.transform.position;
                d.y = 0f;
                if (sq > 1e-6f && sq < rSq)
                {
                    float dist = Mathf.Sqrt(sq);
                    sum += d.normalized * (separationStrength * (1f - dist / r));
                }
            }
        }

        return sum;
    }
}
