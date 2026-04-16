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
    bool _isRanged = true;
    Transform _player;
    FollowerController[] _followersCache;
    float _followersCacheTime;
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
    }

    void FixedUpdate()
    {
        if (_motor.CanScheduleRangedDodge && !_motor.HasPendingRangedDodge &&
            ArrowProjectile.TryGetIncomingDodgeReference(transform.root, out Vector3 dodgeRef))
            _motor.ScheduleRangedDodgeImpulse(dodgeRef);

        Transform chase = FindChaseTarget();
        _motor.SeekOverride = chase;

        if (chase == null)
            return;

        Vector3 d = chase.position - transform.position;
        d.y = 0f;
        if (_isRanged && _ranged != null && _ranged.enabled && d.sqrMagnitude <= combatRange * combatRange)
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
            Vector3 d = _player.position - transform.position;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq <= aggroSq && sq < bestSq && HasLineOfSight(_player))
            {
                best = _player;
                bestSq = sq;
            }
        }

        if (Time.time >= _followersCacheTime)
        {
            _followersCacheTime = Time.time + 0.15f;
            _followersCache = FindObjectsByType<FollowerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        FollowerController[] followers = _followersCache ?? System.Array.Empty<FollowerController>();
        for (int i = 0; i < followers.Length; i++)
        {
            FollowerController f = followers[i];
            if (f == null)
                continue;
            Transform ft = f.transform;
            Vector3 d = ft.position - transform.position;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq <= aggroSq && sq < bestSq && HasLineOfSight(ft))
            {
                best = ft;
                bestSq = sq;
            }
        }

        return best;
    }

    bool HasLineOfSight(Transform target)
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 tgt = target.position + Vector3.up * targetHeight;
        Vector3 delta = tgt - eye;
        float dist = delta.magnitude;
        if (dist < 0.02f)
            return true;

        Vector3 dir = delta / dist;
        const float skin = 0.4f;
        Vector3 origin = eye + dir * skin;
        float remain = dist - skin;
        if (remain <= 0.01f)
            return true;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, remain, obstacleLayers, QueryTriggerInteraction.Ignore))
            return IsTargetOrChild(hit.collider.transform, target);

        return true;
    }

    static bool IsTargetOrChild(Transform hitTransform, Transform target)
    {
        for (Transform t = hitTransform; t != null; t = t.parent)
        {
            if (t == target)
                return true;
        }

        return false;
    }
}
