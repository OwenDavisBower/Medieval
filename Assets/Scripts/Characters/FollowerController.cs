using UnityEngine;

[RequireComponent(typeof(TargetSteeringMotor))]
[RequireComponent(typeof(Rigidbody))]
public class FollowerController : MonoBehaviour
{
    [Header("Formation")]
    [Tooltip("Horizontal distance from the leader beyond which followers stop chasing and move back. 0 = no limit.")]
    [SerializeField] float maxDistanceFromLeader = 25f;

    [Header("Combat")]
    [SerializeField] float banditAggroRadius = 50f;
    [SerializeField] float combatRange = 20f;
    [SerializeField] float eyeHeight = 1.5f;
    [SerializeField] float targetHeight = 1f;
    [SerializeField] LayerMask obstacleLayers = ~0;

    TargetSteeringMotor _motor;
    RangedCombat _ranged;
    MeleeCombat _melee;
    Character _character;
    bool _isRanged = true;
    BanditController[] _banditsCache;
    float _banditsCacheTime;

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

    /// <summary>Assigns a random orbit around the player; call once after spawn.</summary>
    public void Initialize()
    {
        if (_motor == null)
            _motor = GetComponent<TargetSteeringMotor>();
        _motor.InitializeOrbitRandom();
        TryAssignPlayerAnchor();
    }

    void Start()
    {
        TryAssignPlayerAnchor();
        _motor.SeekHoldDistance = _isRanged ? combatRange : 0f;
        if (_character != null)
            _motor.MoveSpeedScale = _character.MovementSpeedMultiplier;
    }

    void TryAssignPlayerAnchor()
    {
        var p = GameObject.Find("Player");
        if (p != null)
            _motor.AnchorTarget = p.transform;
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

        if (maxDistanceFromLeader > 0f)
        {
            Transform anchor = _motor.AnchorTarget;
            if (anchor != null &&
                SpatialMath.FlatSqrDistance(transform.position, anchor.position) >
                maxDistanceFromLeader * maxDistanceFromLeader)
            {
                _motor.SeekOverride = null;
                return;
            }
        }

        Transform bandit = FindBanditTarget();
        _motor.SeekOverride = bandit;

        if (bandit == null)
            return;

        if (_isRanged && _ranged != null && _ranged.enabled &&
            SpatialMath.FlatSqrDistance(transform.position, bandit.position) <= combatRange * combatRange)
            _ranged.TryFireAt(bandit);
        else if (!_isRanged && _melee != null && _melee.enabled)
            _melee.TryAttack(bandit);
    }

    Transform FindBanditTarget()
    {
        float aggroSq = banditAggroRadius * banditAggroRadius;
        Transform best = null;
        float bestSq = float.MaxValue;

        if (Time.time >= _banditsCacheTime)
        {
            _banditsCacheTime = Time.time + 0.15f;
            _banditsCache = FindObjectsByType<BanditController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        BanditController[] bandits = _banditsCache ?? System.Array.Empty<BanditController>();
        for (int i = 0; i < bandits.Length; i++)
        {
            BanditController b = bandits[i];
            if (b == null)
                continue;
            Transform bt = b.transform;
            float sq = SpatialMath.FlatSqrDistance(transform.position, bt.position);
            if (sq <= aggroSq && sq < bestSq && HasLineOfSightTo(bt))
            {
                best = bt;
                bestSq = sq;
            }
        }

        return best;
    }

    bool HasLineOfSightTo(Transform target) =>
        LineOfSightUtility.HasClearLineOfSight(transform.position, target, eyeHeight, targetHeight, obstacleLayers,
            transform.root);
}
