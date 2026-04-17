using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ArrowProjectile : MonoBehaviour
{
    static readonly List<ArrowProjectile> ActiveInstances = new List<ArrowProjectile>();

    [SerializeField] float maxLifetime = 12f;
    [SerializeField] float damage = 25f;

    Rigidbody _rb;
    Transform _shooterRoot;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        Destroy(gameObject, maxLifetime);
    }

    void OnEnable()
    {
        ActiveInstances.Add(this);
    }

    void OnDisable()
    {
        RemoveFromActiveInstances(this);
    }

    static void RemoveFromActiveInstances(ArrowProjectile ap)
    {
        int i = ActiveInstances.IndexOf(ap);
        if (i < 0)
            return;
        int last = ActiveInstances.Count - 1;
        if (i != last)
            ActiveInstances[i] = ActiveInstances[last];
        ActiveInstances.RemoveAt(last);
    }

    /// <summary>Call after spawn so the arrow does not damage the shooter's Character.</summary>
    public void SetShooterRoot(Transform shooterRoot)
    {
        _shooterRoot = shooterRoot;
    }

    /// <summary>
    /// Picks a hostile arrow whose horizontal path passes near this character (incoming along velocity).
    /// <paramref name="dodgeReferencePosition"/> is suitable for <see cref="TargetSteeringMotor.ApplyRangedDodgeImpulse"/>.
    /// </summary>
    public static bool TryGetIncomingDodgeReference(Transform characterRoot, out Vector3 dodgeReferencePosition,
        float maxRange = 28f, float maxLateral = 2.8f, float minHorizSpeed = 2.5f, float minAlong = 0.25f)
    {
        dodgeReferencePosition = default;
        if (characterRoot == null || ActiveInstances.Count == 0)
            return false;

        Transform selfRoot = characterRoot.root;
        Vector3 selfFlat = new Vector3(characterRoot.position.x, 0f, characterRoot.position.z);

        ArrowProjectile best = null;
        float bestAlong = float.MaxValue;
        Vector3 bestDodgeRef = default;

        for (int i = 0; i < ActiveInstances.Count; i++)
        {
            ArrowProjectile ap = ActiveInstances[i];
            if (ap == null)
                continue;
            if (!ap.TryEvaluateIncoming(characterRoot, selfRoot, selfFlat, maxRange, maxLateral, minHorizSpeed, minAlong,
                    out float along, out Vector3 dodgeRef))
                continue;

            if (along < bestAlong)
            {
                bestAlong = along;
                best = ap;
                bestDodgeRef = dodgeRef;
            }
        }

        if (best == null)
            return false;

        dodgeReferencePosition = bestDodgeRef;
        return true;
    }

    bool TryEvaluateIncoming(Transform characterRoot, Transform selfRoot, Vector3 selfFlat, float maxRange, float maxLateral,
        float minHorizSpeed, float minAlong, out float alongRay, out Vector3 dodgeReferencePosition)
    {
        alongRay = 0f;
        dodgeReferencePosition = default;

        if (_shooterRoot != null && selfRoot == _shooterRoot)
            return false;

        if (_rb == null)
            return false;

        Vector3 vel = _rb.linearVelocity;
        Vector3 velFlat = new Vector3(vel.x, 0f, vel.z);
        float speed = velFlat.magnitude;
        if (speed < minHorizSpeed)
            return false;

        Vector3 velDir = velFlat / speed;
        Vector3 arrowFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 w = selfFlat - arrowFlat;
        float along = Vector3.Dot(w, velDir);
        if (along < minAlong || along > maxRange)
            return false;

        Vector3 perp = w - velDir * along;
        float maxLatSq = maxLateral * maxLateral;
        if (perp.sqrMagnitude > maxLatSq)
            return false;

        alongRay = along;
        dodgeReferencePosition = characterRoot.position + new Vector3(velDir.x, 0f, velDir.z);
        return true;
    }

    void OnCollisionEnter(Collision collision)
    {
        var character = collision.collider.GetComponentInParent<Character>();
        if (character != null)
        {
            if (_shooterRoot != null)
            {
                var hitRoot = character.transform.root;
                if (hitRoot == _shooterRoot)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            character.TakeDamage(damage);
        }

        Destroy(gameObject);
    }
}
