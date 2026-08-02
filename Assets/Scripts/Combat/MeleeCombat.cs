using System.Collections;
using Unity.Entities;
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
    [Tooltip("Seconds after the attack starts before damage/knockback apply (sync with SwordSlash impact).")]
    [SerializeField] float hitAnimationLeadSeconds = 0.45f;

    Rigidbody _selfRb;
    Transform _selfRoot;
    Character _selfCharacter;
    float _nextAttackTime;
    bool _hitInProgress;

    void Awake()
    {
        _selfRb = GetComponent<Rigidbody>();
        _selfRoot = transform.root;
        _selfCharacter = GetComponentInParent<Character>();
    }

    void OnDisable()
    {
        StopAllCoroutines();
        _hitInProgress = false;
    }

    /// <returns>True if an attack swing was attempted this frame (hit or miss).</returns>
    public bool TryAttack(Transform target)
    {
        if (target == null || !enabled)
            return false;
        var targetHealth = target.GetComponentInParent<IDamageableHealth>();
        if (targetHealth != null && targetHealth.IsDead)
            return false;
        if (_selfCharacter != null && !_selfCharacter.CanAttack)
            return false;
        if (Time.time < _nextAttackTime)
            return false;
        if (_hitInProgress)
            return false;

        if (SpatialMath.FlatSqrDistance(transform.position, target.position) > meleeRange * meleeRange)
            return false;

        _nextAttackTime = Time.time + attackInterval;

        if (Random.value > hitChance)
            return true;

        float lead = Mathf.Max(0f, hitAnimationLeadSeconds);
        if (lead <= 0f)
        {
            ApplyHit(target);
            return true;
        }

        _hitInProgress = true;
        StartCoroutine(ApplyHitAfterLead(target, lead));
        return true;
    }

    IEnumerator ApplyHitAfterLead(Transform target, float lead)
    {
        yield return new WaitForSeconds(lead);
        if (target != null && (_selfCharacter == null || _selfCharacter.CanAttack))
        {
            var h = target.GetComponentInParent<IDamageableHealth>();
            if (h == null || !h.IsDead)
                ApplyHit(target);
        }
        _hitInProgress = false;
    }

    void ApplyHit(Transform target)
    {
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
                if (victim is Character victimCharacter)
                {
                    victimCharacter.TakeDamage(dmg, d);
                    victimCharacter.ApplyAttackStun(hitMeleeStunDuration);
                }
                else
                {
                    victim.TakeDamage(dmg);
                }
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
    }

    Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
    }

    class MeleeCombatBaker : Baker<MeleeCombat>
    {
        public override void Bake(MeleeCombat authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
            AddComponent(entity, new Medieval.Npcs.NpcMeleeCombatConfig
            {
                AttackInterval = authoring.attackInterval,
                MeleeRange = authoring.meleeRange,
                HitChance = authoring.hitChance,
                Damage = authoring.damage,
                KnockbackImpulse = authoring.knockbackImpulse,
                HitMeleeStunDuration = authoring.hitMeleeStunDuration,
                HitAnimationLeadSeconds = authoring.hitAnimationLeadSeconds
            });
            AddComponent(entity, new Medieval.Npcs.NpcMeleeAttackState());
        }
    }
}
