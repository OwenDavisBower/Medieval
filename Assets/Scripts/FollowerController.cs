using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Rigidbody))]
public class FollowerController : MonoBehaviour
{
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float arriveThreshold = 0.15f;
    [SerializeField] float acceleration = 14f;

    [Header("Loiter region (each follower picks a random spot in this annulus)")]
    [SerializeField] float minLoiterRadius = 2.5f;
    [SerializeField] float maxLoiterRadius = 5.5f;

    [Header("Organic motion")]
    [SerializeField] float targetSmoothTime = 0.35f;
    [SerializeField] float noiseFrequency = 0.22f;
    [SerializeField] float angleWobbleDegrees = 40f;
    [SerializeField] float radiusWobble = 1.4f;
    [SerializeField] float trailBehindStrength = 0.35f;
    [SerializeField] float maxTrailOffset = 2f;

    [Header("Pathfinding & avoidance")]
    [Tooltip("When a NavMesh is baked, followers steer along NavMesh corners toward the loiter target.")]
    [SerializeField] bool useNavMeshWhenAvailable = true;
    [SerializeField] float navMeshSampleMaxDistance = 2f;
    [SerializeField] float minCornerAdvanceDistance = 0.35f;
    [SerializeField] float separationRadius = 1.1f;
    [SerializeField] float separationStrength = 4f;
    [SerializeField] float obstacleProbeRadius = 0.35f;
    [SerializeField] float obstacleProbeDistance = 1.25f;
    [SerializeField] LayerMask obstacleLayers = ~0;

    static readonly List<FollowerController> Instances = new List<FollowerController>();

    NavMeshPath _navPath;

    float _baseAngle;
    float _baseRadius;
    float _noiseA;
    float _noiseB;
    Rigidbody _rb;
    Transform _player;
    Rigidbody _playerRb;
    Vector3 _smoothTarget;
    Vector3 _smoothTargetVel;
    bool _hasSmoothTarget;
    bool _initialized;

    /// <summary>Assigns a random orbit around the player; call once after spawn.</summary>
    public void Initialize()
    {
        _baseAngle = Random.Range(0f, Mathf.PI * 2f);
        _baseRadius = Random.Range(minLoiterRadius, maxLoiterRadius);
        _noiseA = Random.Range(0f, 100f);
        _noiseB = Random.Range(0f, 100f);
        _initialized = true;
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
        _navPath = new NavMeshPath();
    }

    void OnEnable()
    {
        Instances.Add(this);
    }

    void OnDisable()
    {
        Instances.Remove(this);
    }

    void Start()
    {
        if (!_initialized)
            Initialize();

        var p = GameObject.Find("Player");
        if (p != null)
        {
            _player = p.transform;
            _playerRb = p.GetComponent<Rigidbody>();
        }
    }

    void FixedUpdate()
    {
        if (_player == null)
            return;

        float t = Time.time * noiseFrequency;
        float angleJitter = (Mathf.PerlinNoise(_noiseA, t) - 0.5f) * 2f * (angleWobbleDegrees * Mathf.Deg2Rad);
        float r = _baseRadius + (Mathf.PerlinNoise(t, _noiseB) - 0.5f) * 2f * radiusWobble;

        float angle = _baseAngle + angleJitter;
        Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * r;

        Vector3 trail = Vector3.zero;
        if (_playerRb != null && trailBehindStrength > 0f)
        {
            Vector3 pv = _playerRb.linearVelocity;
            pv.y = 0f;
            float mag = pv.magnitude;
            if (mag > 0.05f)
                trail = -pv.normalized * Mathf.Min(mag * trailBehindStrength, maxTrailOffset);
        }

        Vector3 rawTarget = _player.position + trail + offset;

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
            Vector3 desired = desiredDir * moveSpeed;
            desired += ComputeSeparation();
            float maxHorizSpeed = moveSpeed;
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

        _rb.linearVelocity = velocity;
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
        if (col.GetComponentInParent<FollowerController>() != null)
            return true;
        if (_player != null && (col.transform == _player || col.transform.IsChildOf(_player)))
            return true;
        return false;
    }

    Vector3 ComputeSeparation()
    {
        float r = separationRadius;
        float rSq = r * r;
        Vector3 sum = Vector3.zero;
        Vector3 p = transform.position;

        if (_player != null)
        {
            Vector3 d = p - _player.position;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq > 1e-6f && sq < rSq)
            {
                float dist = Mathf.Sqrt(sq);
                sum += d.normalized * (separationStrength * (1f - dist / r));
            }
        }

        for (int i = 0; i < Instances.Count; i++)
        {
            FollowerController other = Instances[i];
            if (other == null || other == this)
                continue;

            Vector3 d = p - other.transform.position;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq > 1e-6f && sq < rSq)
            {
                float dist = Mathf.Sqrt(sq);
                sum += d.normalized * (separationStrength * (1f - dist / r));
            }
        }

        return sum;
    }
}
