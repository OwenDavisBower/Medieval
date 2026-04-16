using UnityEngine;

/// <summary>Lobs physics-based arrows toward a target with intentional inaccuracy.</summary>
public class RangedCombat : MonoBehaviour
{
    [SerializeField] Rigidbody arrowPrefab;
    [SerializeField] float fireInterval = 1.15f;
    [SerializeField] float launchHeight = 1.45f;
    [SerializeField] float targetAimHeight = 1f;
    [SerializeField] float horizontalAimError = 1.8f;
    [SerializeField] float verticalAimError = 0.35f;

    Collider _ownerCollider;
    float _nextFireTime;

    void Awake()
    {
        _ownerCollider = GetComponentInChildren<Collider>();
    }

    public void TryFireAt(Transform target)
    {
        if (arrowPrefab == null || target == null)
            return;
        if (Time.time < _nextFireTime)
            return;

        Vector3 origin = transform.position + Vector3.up * launchHeight;
        Vector3 aim = target.position + Vector3.up * targetAimHeight;
        Vector2 xz = Random.insideUnitCircle * horizontalAimError;
        aim += new Vector3(xz.x, Random.Range(-verticalAimError, verticalAimError), xz.y);

        Vector3 velocity = LobbedLaunchVelocity(origin, aim);

        Rigidbody arrow = Instantiate(arrowPrefab, origin, Quaternion.identity);
        arrow.linearVelocity = velocity;

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

        _nextFireTime = Time.time + fireInterval;
    }

    static Vector3 LobbedLaunchVelocity(Vector3 from, Vector3 to)
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

        float t = Mathf.Clamp(h / 12f, 0.55f, 2.2f);
        float vy = (dh + 0.5f * g * t * t) / t;
        Vector3 vHoriz = horizontal.normalized * (h / t);
        return new Vector3(vHoriz.x, vy, vHoriz.z);
    }
}
