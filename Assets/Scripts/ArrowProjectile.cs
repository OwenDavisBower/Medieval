using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ArrowProjectile : MonoBehaviour
{
    [SerializeField] float maxLifetime = 12f;
    [SerializeField] float damage = 25f;

    float _spawnTime;
    Transform _shooterRoot;

    void Awake()
    {
        _spawnTime = Time.time;
    }

    /// <summary>Call after spawn so the arrow does not damage the shooter's Character.</summary>
    public void SetShooterRoot(Transform shooterRoot)
    {
        _shooterRoot = shooterRoot;
    }

    void Update()
    {
        if (Time.time - _spawnTime > maxLifetime)
            Destroy(gameObject);
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
