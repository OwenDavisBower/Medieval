using UnityEngine;

[RequireComponent(typeof(TargetSteeringMotor))]
[RequireComponent(typeof(Rigidbody))]
public class FollowerController : MonoBehaviour
{
    [Header("Combat")]
    [SerializeField] float banditAggroRadius = 50f;
    [SerializeField] float combatRange = 20f;
    [SerializeField] float eyeHeight = 1.5f;
    [SerializeField] float targetHeight = 1f;
    [SerializeField] LayerMask obstacleLayers = ~0;

    TargetSteeringMotor _motor;
    RangedCombat _ranged;
    BanditController[] _banditsCache;
    float _banditsCacheTime;

    void Awake()
    {
        _motor = GetComponent<TargetSteeringMotor>();
        _ranged = GetComponent<RangedCombat>();
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
        _motor.SeekHoldDistance = combatRange;
    }

    void TryAssignPlayerAnchor()
    {
        var p = GameObject.Find("Player");
        if (p != null)
            _motor.AnchorTarget = p.transform;
    }

    void FixedUpdate()
    {
        Transform bandit = FindBanditTarget();
        _motor.SeekOverride = bandit;

        if (bandit != null && _ranged != null)
        {
            Vector3 d = bandit.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude <= combatRange * combatRange)
            {
                if (_ranged.TryFireAt(bandit))
                    _motor.ScheduleRangedDodgeImpulse(bandit);
            }
        }
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
            Vector3 d = bt.position - transform.position;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq <= aggroSq && sq < bestSq && HasLineOfSight(bt))
            {
                best = bt;
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
