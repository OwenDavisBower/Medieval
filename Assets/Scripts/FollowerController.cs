using UnityEngine;

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

        Vector3 flat = _smoothTarget - transform.position;
        flat.y = 0f;

        Vector3 velocity = _rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

        if (flat.sqrMagnitude > arriveThreshold * arriveThreshold)
        {
            Vector3 desired = flat.normalized * moveSpeed;
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
}
