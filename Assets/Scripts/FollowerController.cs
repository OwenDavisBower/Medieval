using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class FollowerController : MonoBehaviour
{
    const int RingCount = 5;

    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float ringRadius = 4f;
    [SerializeField] float arriveThreshold = 0.15f;

    int _slotIndex;
    Rigidbody _rb;
    Transform _player;

    public void Initialize(int slotIndex)
    {
        _slotIndex = Mathf.Clamp(slotIndex, 0, RingCount - 1);
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
    }

    void Start()
    {
        var p = GameObject.Find("Player");
        if (p != null)
            _player = p.transform;
    }

    void FixedUpdate()
    {
        if (_player == null)
            return;

        float angle = _slotIndex * (Mathf.PI * 2f / RingCount);
        Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * ringRadius;
        Vector3 target = _player.position + offset;

        Vector3 flat = target - transform.position;
        flat.y = 0f;

        Vector3 velocity = _rb.linearVelocity;
        if (flat.sqrMagnitude > arriveThreshold * arriveThreshold)
        {
            Vector3 dir = flat.normalized;
            Vector3 targetHorizontal = dir * moveSpeed;
            velocity.x = targetHorizontal.x;
            velocity.z = targetHorizontal.z;
        }
        else
        {
            velocity.x = 0f;
            velocity.z = 0f;
        }

        _rb.linearVelocity = velocity;
    }
}
