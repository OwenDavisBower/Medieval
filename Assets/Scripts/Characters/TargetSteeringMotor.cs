using Medieval.NpcMovement;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

public enum TargetSteeringMovementMode
{
    /// <summary>Loiter on an annulus around <see cref="TargetSteeringMotor.AnchorTarget"/> with organic noise.</summary>
    Orbit,
    /// <summary>Seek <see cref="TargetSteeringMotor.SeekOverride"/> if set, otherwise <see cref="TargetSteeringMotor.AnchorTarget"/>.</summary>
    MoveTowards,
    /// <summary>Random walk within a disk around <see cref="TargetSteeringMotor.AnchorTarget"/> (re-picks periodically).</summary>
    WanderAroundTarget
}

public enum TargetSteeringSeparationGroup
{
    None,
    Followers,
    Bandits
}

/// <summary>
/// GameObject-side facade over the DOTS NPC movement pipeline. All serialized tuning mirrors the
/// legacy motor; at <c>Start</c> a backing <see cref="Entity"/> is created and all runtime API calls
/// forward into it via <c>EntityManager</c>. The companion <see cref="Rigidbody"/> is driven kinematically
/// by the DOTS writeback system each frame.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Rigidbody))]
public class TargetSteeringMotor : MonoBehaviour, INpcFacade
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
    [Tooltip("Below this horizontal speed (m/s), facing is not updated. Avoids fast spin when nearly idle from velocity jitter.")]
    [SerializeField] float facingMinHorizontalSpeed = 1f;
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

    [Header("Orbit - trail behind moving anchor")]
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

    [Header("Path refresh")]
    [Tooltip("Seconds between pathfinding attempts when the goal has not moved significantly.")]
    [SerializeField] float repathInterval = 0.35f;
    [Tooltip("Distance the goal must move before forcing an early repath.")]
    [SerializeField] float repathGoalShiftDistance = 2f;

    Rigidbody _rb;
    Entity _entity = Entity.Null;
    bool _entityCreated;

    bool _orbitInitialized;
    bool _wanderInitialized;
    float _pendingBaseAngle;
    float _pendingBaseRadius;
    float _pendingNoiseA;
    float _pendingNoiseB;
    float _pendingNextWanderPickTime;

    Vector3 _cachedHorizontalVelocity;
    float _cachedEffectiveMoveSpeed;
    bool _cachedHasPendingDodge;
    float _lastDodgeApplyTime = float.NegativeInfinity;

    Vector3? _overrideFacingFlatDirection;

    /// <summary>Max configured horizontal speed this step (move speed x scale x water). Written by the DOTS writeback.</summary>
    public float EffectiveMoveSpeed => _cachedEffectiveMoveSpeed;

    /// <summary>Smoothed horizontal velocity from the DOTS integration. Consumed by <c>LocomotionAnimatorDriver</c>.</summary>
    public Vector3 CurrentHorizontalVelocity => _cachedHorizontalVelocity;

    public TargetSteeringSeparationGroup SeparationGroup => separationGroup;

    public bool CanScheduleRangedDodge => Time.time >= _lastDodgeApplyTime + rangedDodgeCooldown;

    public bool HasPendingRangedDodge => _cachedHasPendingDodge;

    public Transform AnchorTarget
    {
        get => anchorTarget;
        set => anchorTarget = value;
    }

    public Transform SeekOverride
    {
        get => seekOverride;
        set => seekOverride = value;
    }

    public float SeekHoldDistance
    {
        get => seekHoldDistance;
        set
        {
            seekHoldDistance = value;
            PushSeekOverride();
        }
    }

    public float MoveSpeedScale
    {
        get => moveSpeedScale;
        set
        {
            moveSpeedScale = Mathf.Max(0.05f, value);
            PushConfig();
        }
    }

    public TargetSteeringMovementMode Mode
    {
        get => mode;
        set
        {
            mode = value;
            PushModeAndGroup();
        }
    }

    public void SetRangedMovementLock(bool locked)
    {
        if (!TryGetEntityManager(out EntityManager em) || !em.HasComponent<NpcMovementState>(_entity))
            return;
        NpcMovementState s = em.GetComponentData<NpcMovementState>(_entity);
        s.RangedMovementLock = (byte)(locked ? 1 : 0);
        em.SetComponentData(_entity, s);
    }

    public void SetOverrideFacingTowardWorldPoint(Vector3 worldPosition)
    {
        Vector3 d = worldPosition - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > 1e-6f)
            _overrideFacingFlatDirection = d.normalized;
        else
            _overrideFacingFlatDirection = null;
        PushOverrideFacing();
    }

    public void ClearOverrideFacing()
    {
        _overrideFacingFlatDirection = null;
        PushOverrideFacing();
    }

    public void ScheduleRangedDodgeImpulse(Vector3 dodgeReferenceWorldPosition)
    {
        if (!TryGetEntityManager(out EntityManager em) || !em.HasComponent<NpcPendingDodge>(_entity))
            return;
        float delay = Mathf.Max(0f, postRangedDodgeDelay);
        em.SetComponentData(_entity, new NpcPendingDodge
        {
            ReferencePosition = new float3(dodgeReferenceWorldPosition.x, dodgeReferenceWorldPosition.y, dodgeReferenceWorldPosition.z),
            FireTime = Time.time + delay,
            HasPending = 1
        });
        _cachedHasPendingDodge = true;
    }

    public void InitializeOrbitRandom()
    {
        _pendingBaseAngle = Random.Range(0f, Mathf.PI * 2f);
        _pendingBaseRadius = Random.Range(minLoiterRadius, maxLoiterRadius);
        _pendingNoiseA = Random.Range(0f, 100f);
        _pendingNoiseB = Random.Range(0f, 100f);
        _orbitInitialized = true;
        ApplyInitializationToEntity();
    }

    public void InitializeWanderAroundAnchor(Transform campAnchor, bool randomizeTimer = true)
    {
        anchorTarget = campAnchor;
        _pendingBaseAngle = Random.Range(0f, Mathf.PI * 2f);
        _pendingBaseRadius = Random.Range(0f, wanderRadius);
        _pendingNoiseA = Random.Range(0f, 100f);
        _pendingNoiseB = Random.Range(0f, 100f);
        _pendingNextWanderPickTime = (Application.isPlaying ? Time.time : 0f)
                                     + (randomizeTimer ? Random.Range(0f, 1f) : 0f);
        _wanderInitialized = true;
        ApplyInitializationToEntity();
    }

    void ApplyInitializationToEntity()
    {
        if (!TryGetEntityManager(out EntityManager em) || !em.HasComponent<NpcMovementState>(_entity))
            return;
        NpcMovementState s = em.GetComponentData<NpcMovementState>(_entity);
        if (_orbitInitialized || _wanderInitialized)
        {
            s.BaseAngle = _pendingBaseAngle;
            s.BaseRadius = _pendingBaseRadius;
            s.NoiseA = _pendingNoiseA;
            s.NoiseB = _pendingNoiseB;
        }
        if (_wanderInitialized)
            s.NextWanderPickTime = _pendingNextWanderPickTime;
        em.SetComponentData(_entity, s);
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.interpolation = RigidbodyInterpolation.None;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
    }

    void Start()
    {
        EnsureEntity();

        if (mode == TargetSteeringMovementMode.Orbit && !_orbitInitialized)
            InitializeOrbitRandom();
        if (mode == TargetSteeringMovementMode.WanderAroundTarget && !_wanderInitialized && anchorTarget != null)
            InitializeWanderAroundAnchor(anchorTarget, randomizeTimer: true);
    }

    void OnDisable()
    {
        if (TryGetEntityManager(out EntityManager em) && em.HasComponent<NpcMovementState>(_entity))
        {
            NpcMovementState s = em.GetComponentData<NpcMovementState>(_entity);
            s.RangedMovementLock = 0;
            em.SetComponentData(_entity, s);
            em.SetComponentData(_entity, new NpcOverrideFacing());
            em.SetComponentData(_entity, new NpcPendingDodge());
        }
        _overrideFacingFlatDirection = null;
        _cachedHasPendingDodge = false;
    }

    void OnDestroy()
    {
        NpcMovementEntityFactory.Destroy(_entity);
        _entity = Entity.Null;
        _entityCreated = false;
    }

    void FixedUpdate()
    {
        if (!TryGetEntityManager(out EntityManager em))
            return;

        PushAnchor(em);
        PushSeekOverride(em);
    }

    void EnsureEntity()
    {
        if (_entityCreated)
            return;
        NpcMovementConfig cfg = BuildConfig();
        _entity = NpcMovementEntityFactory.Create(
            transform,
            _rb,
            this,
            cfg,
            (NpcMovementMode)mode,
            (NpcSeparationGroup)separationGroup);
        _entityCreated = _entity != Entity.Null;
        if (_entityCreated)
        {
            ApplyInitializationToEntity();
            PushOverrideFacing();
            PushAnchor();
            PushSeekOverride();
        }
    }

    NpcMovementConfig BuildConfig()
    {
        return new NpcMovementConfig
        {
            MoveSpeed = moveSpeed,
            MoveSpeedScale = Mathf.Max(0.05f, moveSpeedScale),
            ArriveThreshold = arriveThreshold,
            Acceleration = acceleration,
            FacingTurnSpeedDegreesPerSecond = facingTurnSpeedDegreesPerSecond,
            FacingMinHorizontalSpeed = facingMinHorizontalSpeed,
            PostRangedDodgeImpulse = postRangedDodgeImpulse,
            PostRangedDodgeRetreatRatio = postRangedDodgeRetreatRatio,
            PostRangedDodgeDelay = postRangedDodgeDelay,
            RangedDodgeCooldown = rangedDodgeCooldown,
            MinLoiterRadius = minLoiterRadius,
            MaxLoiterRadius = maxLoiterRadius,
            TrailBehindStrength = trailBehindStrength,
            MaxTrailOffset = maxTrailOffset,
            WanderRadius = wanderRadius,
            RepickWanderInterval = repickWanderInterval,
            TargetSmoothTime = targetSmoothTime,
            NoiseFrequency = noiseFrequency,
            AngleWobbleDegrees = angleWobbleDegrees,
            RadiusWobble = radiusWobble,
            UseNavMeshWhenAvailable = (byte)(useNavMeshWhenAvailable ? 1 : 0),
            NavMeshSampleMaxDistance = navMeshSampleMaxDistance,
            MinCornerAdvanceDistance = minCornerAdvanceDistance,
            SeparationRadius = separationRadius,
            SeparationStrength = separationStrength,
            ObstacleProbeRadius = obstacleProbeRadius,
            ObstacleProbeDistance = obstacleProbeDistance,
            RepathInterval = Mathf.Max(0.05f, repathInterval),
            RepathGoalShiftSqr = repathGoalShiftDistance * repathGoalShiftDistance
        };
    }

    bool TryGetEntityManager(out EntityManager em)
    {
        em = default;
        if (!_entityCreated)
            return false;
        World w = World.DefaultGameObjectInjectionWorld;
        if (w == null || !w.IsCreated)
            return false;
        em = w.EntityManager;
        return em.Exists(_entity);
    }

    void PushConfig()
    {
        if (!TryGetEntityManager(out EntityManager em) || !em.HasComponent<NpcMovementConfig>(_entity))
            return;
        em.SetComponentData(_entity, BuildConfig());
    }

    void PushModeAndGroup()
    {
        if (!TryGetEntityManager(out EntityManager em) || !em.HasComponent<NpcMovementState>(_entity))
            return;
        NpcMovementState s = em.GetComponentData<NpcMovementState>(_entity);
        s.Mode = (NpcMovementMode)mode;
        s.Group = (NpcSeparationGroup)separationGroup;
        em.SetComponentData(_entity, s);
    }

    void PushOverrideFacing()
    {
        if (!TryGetEntityManager(out EntityManager em) || !em.HasComponent<NpcOverrideFacing>(_entity))
            return;
        if (_overrideFacingFlatDirection.HasValue)
        {
            Vector3 d = _overrideFacingFlatDirection.Value;
            em.SetComponentData(_entity, new NpcOverrideFacing
            {
                FlatDirection = new float3(d.x, 0f, d.z),
                HasOverride = 1
            });
        }
        else
        {
            em.SetComponentData(_entity, new NpcOverrideFacing());
        }
    }

    void PushAnchor()
    {
        if (!TryGetEntityManager(out EntityManager em))
            return;
        PushAnchor(em);
    }

    void PushAnchor(EntityManager em)
    {
        if (!em.HasComponent<NpcAnchorTarget>(_entity))
            return;
        if (anchorTarget == null)
        {
            em.SetComponentData(_entity, new NpcAnchorTarget());
            return;
        }

        Vector3 p = anchorTarget.position;
        Vector3 v = Vector3.zero;
        var rb = anchorTarget.GetComponent<Rigidbody>();
        if (rb != null)
            v = rb.linearVelocity;
        em.SetComponentData(_entity, new NpcAnchorTarget
        {
            Position = new float3(p.x, p.y, p.z),
            LinearVelocity = new float3(v.x, v.y, v.z),
            HasAnchor = 1
        });
    }

    void PushSeekOverride()
    {
        if (!TryGetEntityManager(out EntityManager em))
            return;
        PushSeekOverride(em);
    }

    void PushSeekOverride(EntityManager em)
    {
        if (!em.HasComponent<NpcSeekOverride>(_entity))
            return;
        if (seekOverride == null)
        {
            em.SetComponentData(_entity, new NpcSeekOverride
            {
                Position = default,
                SeekHoldDistance = seekHoldDistance,
                HasOverride = 0
            });
            return;
        }
        Vector3 p = seekOverride.position;
        em.SetComponentData(_entity, new NpcSeekOverride
        {
            Position = new float3(p.x, p.y, p.z),
            SeekHoldDistance = seekHoldDistance,
            HasOverride = 1
        });
    }

    void INpcFacade.OnMovementStateSynced(float3 horizontalVelocity, float effectiveMoveSpeed, bool hasPendingDodge)
    {
        _cachedHorizontalVelocity = new Vector3(horizontalVelocity.x, horizontalVelocity.y, horizontalVelocity.z);
        _cachedEffectiveMoveSpeed = effectiveMoveSpeed;
        if (_cachedHasPendingDodge && !hasPendingDodge)
            _lastDodgeApplyTime = Time.time;
        _cachedHasPendingDodge = hasPendingDodge;
    }
}
