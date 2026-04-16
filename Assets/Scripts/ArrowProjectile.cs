using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ArrowProjectile : MonoBehaviour
{
    [SerializeField] float maxLifetime = 12f;

    float _spawnTime;

    void Awake()
    {
        _spawnTime = Time.time;
    }

    void Update()
    {
        if (Time.time - _spawnTime > maxLifetime)
            Destroy(gameObject);
    }

    void OnCollisionEnter(Collision _)
    {
        Destroy(gameObject);
    }
}
