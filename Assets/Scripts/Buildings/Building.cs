using UnityEngine;

/// <summary>Destructible placeable structure; damaged like <see cref="Character"/> via <see cref="IDamageableHealth"/>.</summary>
public class Building : MonoBehaviour, IDamageableHealth
{
    [SerializeField] float maxHealth = 400f;

    float _current;
    CharacterHealthBar _healthBar;

    public float CurrentHealth => _current;
    public float MaxHealth => maxHealth;
    public bool IsDead => _current <= 0f;

    void Awake()
    {
        _current = maxHealth;
        _healthBar = GetComponent<CharacterHealthBar>();
    }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f || IsDead)
            return;

        _current = Mathf.Max(0f, _current - amount);
        _healthBar ??= GetComponent<CharacterHealthBar>();
        _healthBar?.OnHealthChanged(_current, maxHealth);

        if (_current <= 0f)
            Destroy(gameObject);
    }
}
