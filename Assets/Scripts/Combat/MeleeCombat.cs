using UnityEngine;

/// <summary>Close-range attacks with high hit chance; applies damage and horizontal knockback on hit.</summary>
public class MeleeCombat : MonoBehaviour
{
    [SerializeField] float attackInterval = 0.42f;
    [SerializeField] float meleeRange = 1.12f;
    [SerializeField] [Range(0f, 1f)] float hitChance = 0.88f;
    [SerializeField] float damage = 14f;
    [SerializeField] float knockbackImpulse = 4.2f;
    [SerializeField] float hitMeleeStunDuration = 0.28f;

    Rigidbody _selfRb;
    Transform _selfRoot;
    Character _selfCharacter;
    float _nextAttackTime;

    void Awake()
    {
        _selfRb = GetComponent<Rigidbody>();
        _selfRoot = transform.root;
        _selfCharacter = GetComponentInParent<Character>();
    }

    /// <returns>True if an attack swing was attempted this frame (hit or miss).</returns>
    public bool TryAttack(Transform target)
    {
        if (target == null || !enabled)
            return false;
        if (_selfCharacter != null && !_selfCharacter.CanAttack)
            return false;
        if (Time.time < _nextAttackTime)
            return false;

        if (SpatialMath.FlatSqrDistance(transform.position, target.position) > meleeRange * meleeRange)
            return false;

        _nextAttackTime = Time.time + attackInterval;

        if (Random.value > hitChance)
            return true;

        Vector3 d = target.position - transform.position;
        d.y = 0f;

        var victim = target.GetComponentInParent<IDamageableHealth>();
        if (victim != null && !victim.IsDead)
        {
            var victimMb = victim as MonoBehaviour;
            if (victimMb != null && victimMb.transform.root != _selfRoot)
            {
                float dmg = damage;
                if (_selfCharacter != null)
                    dmg *= _selfCharacter.MeleeDamageMultiplier;
                victim.TakeDamage(dmg);
                if (victim is Character victimCharacter)
                    victimCharacter.ApplyAttackStun(hitMeleeStunDuration);
            }
        }

        var victimRb = target.GetComponentInParent<Rigidbody>();
        if (victimRb != null && victimRb != _selfRb)
        {
            Vector3 push = d.sqrMagnitude > 1e-4f ? d.normalized : FlatForward();
            Vector3 deltaV = push * knockbackImpulse;
            Vector3 v = victimRb.linearVelocity;
            v.x += deltaV.x;
            v.z += deltaV.z;
            victimRb.linearVelocity = v;
        }

        return true;
    }

    Vector3 FlatForward()
    {
        Transform t = transform;
        var motor = GetComponent<TargetSteeringMotor>();
        if (motor != null)
            t = motor.FacingTransform;
        Vector3 f = t.forward;
        f.y = 0f;
        return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
    }
}
