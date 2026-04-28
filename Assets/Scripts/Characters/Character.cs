using UnityEngine;

/// <summary>Health for player, bandits, and followers.</summary>
public class Character : MonoBehaviour
{
    [Header("Stat rolls (randomized in Awake)")]
    [SerializeField] float minHealth = 70f;
    [SerializeField] float maxHealth = 130f;
    [SerializeField] float minStrength = 5f;
    [SerializeField] float maxStrength = 15f;
    [SerializeField] float minDexterity = 5f;
    [SerializeField] float maxDexterity = 15f;
    [SerializeField] float minFocus = 5f;
    [SerializeField] float maxFocus = 15f;
    [SerializeField] float minBravery = 5f;
    [SerializeField] float maxBravery = 15f;
    [Tooltip("Flee when health fraction is at or below this when bravery is at its minimum.")]
    [SerializeField] float fleeHealthFractionAtLowBravery = 0.48f;
    [Tooltip("Flee when health fraction is at or below this when bravery is at its maximum.")]
    [SerializeField] float fleeHealthFractionAtHighBravery = 0.06f;

    float _current;
    float _rolledMaxHealth;
    float _strength;
    float _dexterity;
    float _focus;
    float _bravery;
    float _attackStunUntil;

    CharacterHealthBar _healthBar;

    float _meleeDamageMultiplier;
    float _movementSpeedMultiplier;
    float _rangedAimErrorMultiplier;

    public float CurrentHealth => _current;
    public float MaxHealth => _rolledMaxHealth;
    public float Strength => _strength;
    public float Dexterity => _dexterity;
    public float Focus => _focus;
    public float Bravery => _bravery;
    public bool IsDead => _current <= 0f;

    /// <summary>Higher strength increases melee damage (see <see cref="MeleeCombat"/>).</summary>
    public float MeleeDamageMultiplier => _meleeDamageMultiplier;

    /// <summary>Higher dexterity increases movement speed (see <see cref="TargetSteeringMotor"/> / <see cref="PlayerController"/>).</summary>
    public float MovementSpeedMultiplier => _movementSpeedMultiplier;

    /// <summary>Higher focus tightens bow aim spread (values &lt; 1 reduce error).</summary>
    public float RangedAimErrorMultiplier => _rangedAimErrorMultiplier;

    /// <summary>True when health is low enough that this character should stop engaging and retreat (NPC combat).</summary>
    public bool ShouldFleeFromCombatThreat
    {
        get
        {
            if (_rolledMaxHealth <= 0.01f || IsDead)
                return false;
            float t = StatT(_bravery, minBravery, maxBravery);
            float fleeBelow = Mathf.Lerp(fleeHealthFractionAtLowBravery, fleeHealthFractionAtHighBravery, t);
            return (_current / _rolledMaxHealth) <= fleeBelow;
        }
    }

    /// <summary>False while stunned from a recent melee hit; blocks melee and ranged attacks.</summary>
    public bool CanAttack => Time.time >= _attackStunUntil;

    public void ApplyAttackStun(float duration)
    {
        if (duration <= 0f || IsDead)
            return;
        float end = Time.time + duration;
        if (end > _attackStunUntil)
            _attackStunUntil = end;
    }

    void Awake()
    {
        HierarchyLayers.SetRecursiveByLayerName(transform, "Character");

        RollStats();
        _meleeDamageMultiplier = StatMultiplier(_strength, minStrength, maxStrength, 0.78f, 1.22f);
        _movementSpeedMultiplier = StatMultiplier(_dexterity, minDexterity, maxDexterity, 0.86f, 1.14f);
        _rangedAimErrorMultiplier = StatMultiplier(_focus, minFocus, maxFocus, 1.28f, 0.62f);
        _current = _rolledMaxHealth;
        _healthBar = GetComponent<CharacterHealthBar>();
    }

    void RollStats()
    {
        _rolledMaxHealth = Random.Range(minHealth, maxHealth);
        _strength = Random.Range(minStrength, maxStrength);
        _dexterity = Random.Range(minDexterity, maxDexterity);
        _focus = Random.Range(minFocus, maxFocus);
        _bravery = Random.Range(minBravery, maxBravery);
    }

    static float StatT(float value, float min, float max)
    {
        if (max <= min + 0.001f)
            return 0.5f;
        return Mathf.Clamp01((value - min) / (max - min));
    }

    static float StatMultiplier(float value, float min, float max, float atMin, float atMax)
    {
        return Mathf.Lerp(atMin, atMax, StatT(value, min, max));
    }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f || IsDead)
            return;

        _current = Mathf.Max(0f, _current - amount);
        _healthBar ??= GetComponent<CharacterHealthBar>();
        _healthBar?.OnCharacterHealthChanged(_current, _rolledMaxHealth);

        if (_current <= 0f)
            Destroy(gameObject);
    }
}
