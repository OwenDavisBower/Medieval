using UnityEngine;

[RequireComponent(typeof(TargetSteeringMotor))]
[RequireComponent(typeof(Rigidbody))]
public class BanditController : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] float aggroRadius = 50f;
    [SerializeField] float eyeHeight = 1.5f;
    [SerializeField] float targetHeight = 1f;

    [SerializeField] LayerMask obstacleLayers = ~0;

    [Header("Combat")]
    [SerializeField] float combatRange = 20f;

    TargetSteeringMotor _motor;
    RangedCombat _ranged;
    MeleeCombat _melee;
    Character _character;
    bool _isRanged = true;
    Transform _player;
    bool _initialized;

    public void Initialize(Transform campAnchor)
    {
        if (_motor == null)
            _motor = GetComponent<TargetSteeringMotor>();
        _motor.Mode = TargetSteeringMovementMode.WanderAroundTarget;
        _motor.InitializeWanderAroundAnchor(campAnchor);
        _initialized = true;
    }

    void Awake()
    {
        _motor = GetComponent<TargetSteeringMotor>();
        _ranged = GetComponent<RangedCombat>();
        _melee = GetComponent<MeleeCombat>();
        _character = GetComponent<Character>();
    }

    /// <summary>Call once after spawn: ranged (bow) or melee, never both.</summary>
    public void ApplyCombatRole(bool ranged)
    {
        _isRanged = ranged;
        if (_ranged != null)
            _ranged.enabled = ranged;
        if (_melee != null)
            _melee.enabled = !ranged;
        CombatVisuals.SetRangedHatVisible(transform, ranged);
    }

    void Start()
    {
        if (!_initialized)
            _motor.InitializeWanderAroundAnchor(transform);

        var p = GameObject.Find("Player");
        if (p != null)
            _player = p.transform;

        _motor.SeekHoldDistance = _isRanged ? combatRange : 0f;
        if (_character != null)
            _motor.MoveSpeedScale = _character.MovementSpeedMultiplier;
    }

    void FixedUpdate()
    {
        if (_motor.CanScheduleRangedDodge && !_motor.HasPendingRangedDodge &&
            ArrowProjectile.TryGetIncomingDodgeReference(transform.root, out Vector3 dodgeRef))
            _motor.ScheduleRangedDodgeImpulse(dodgeRef);

        if (_character != null && _character.ShouldFleeFromCombatThreat)
        {
            _motor.SeekOverride = null;
            return;
        }

        Transform chase = FindChaseTarget();
        _motor.SeekOverride = chase;

        if (chase == null)
            return;

        if (_isRanged && _ranged != null && _ranged.enabled &&
            SpatialMath.FlatSqrDistance(transform.position, chase.position) <= combatRange * combatRange)
            _ranged.TryFireAt(chase);
        else if (!_isRanged && _melee != null && _melee.enabled)
            _melee.TryAttack(chase);
    }

    Transform FindChaseTarget()
    {
        float aggroSq = aggroRadius * aggroRadius;
        Transform best = null;
        float bestSq = float.MaxValue;

        if (_player != null)
        {
            float sq = SpatialMath.FlatSqrDistance(transform.position, _player.position);
            if (sq <= aggroSq && sq < bestSq && HasLineOfSightTo(_player))
            {
                best = _player;
                bestSq = sq;
            }
        }

        FollowerController[] followers = CombatUnitRegistry.GetFollowers();
        for (int i = 0; i < followers.Length; i++)
        {
            FollowerController f = followers[i];
            if (f == null)
                continue;
            Transform ft = f.transform;
            float sq = SpatialMath.FlatSqrDistance(transform.position, ft.position);
            if (sq <= aggroSq && sq < bestSq && HasLineOfSightTo(ft))
            {
                best = ft;
                bestSq = sq;
            }
        }

        VillagerController[] villagers = CombatUnitRegistry.GetVillagers();
        for (int i = 0; i < villagers.Length; i++)
        {
            VillagerController v = villagers[i];
            if (v == null)
                continue;
            Transform vt = v.transform;
            float sq = SpatialMath.FlatSqrDistance(transform.position, vt.position);
            if (sq <= aggroSq && sq < bestSq && HasLineOfSightTo(vt))
            {
                best = vt;
                bestSq = sq;
            }
        }

        return best;
    }

    bool HasLineOfSightTo(Transform target) =>
        LineOfSightUtility.HasClearLineOfSight(transform.position, target, eyeHeight, targetHeight, obstacleLayers,
            transform.root);
}
