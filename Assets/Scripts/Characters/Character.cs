using UnityEngine;
using UnityEngine.UI;

/// <summary>Health for player, bandits, and followers. Shows a world-space bar after taking damage.</summary>
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

    [SerializeField] float healthBarHeightOffset = 2.15f;
    [SerializeField] float healthBarScale = 0.011f;

    float _current;
    float _rolledMaxHealth;
    float _strength;
    float _dexterity;
    float _focus;
    float _bravery;
    float _attackStunUntil;
    Canvas _canvas;
    RectTransform _fillRect;
    Image _fillImage;
    Transform _billboardRoot;
    static Sprite _whiteSprite;
    static int s_billboardCamFrame = -1;
    static Camera s_billboardMainCam;

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
        RollStats();
        _meleeDamageMultiplier = StatMultiplier(_strength, minStrength, maxStrength, 0.78f, 1.22f);
        _movementSpeedMultiplier = StatMultiplier(_dexterity, minDexterity, maxDexterity, 0.86f, 1.14f);
        _rangedAimErrorMultiplier = StatMultiplier(_focus, minFocus, maxFocus, 1.28f, 0.62f);
        _current = _rolledMaxHealth;
        BuildHealthBar();
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
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

    void LateUpdate()
    {
        if (_billboardRoot == null || !_billboardRoot.gameObject.activeInHierarchy)
            return;
        if (Time.frameCount != s_billboardCamFrame)
        {
            s_billboardCamFrame = Time.frameCount;
            s_billboardMainCam = Camera.main;
        }

        var cam = s_billboardMainCam;
        if (cam == null)
            return;
        Vector3 toCam = cam.transform.position - _billboardRoot.position;
        if (toCam.sqrMagnitude < 0.0001f)
            return;
        _billboardRoot.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
    }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f || IsDead)
            return;

        _current = Mathf.Max(0f, _current - amount);
        RefreshBarVisual();

        if (_canvas != null && _current > 0f && _current < _rolledMaxHealth)
            _canvas.gameObject.SetActive(true);

        if (_current <= 0f)
            Destroy(gameObject);
    }

    void RefreshBarVisual()
    {
        if (_fillRect == null)
            return;
        float t = _rolledMaxHealth > 0.01f ? Mathf.Clamp01(_current / _rolledMaxHealth) : 0f;
        _fillRect.anchorMax = new Vector2(t, 1f);
        if (_fillImage != null)
            _fillImage.color = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.25f, 0.85f, 0.35f), t);
    }

    void BuildHealthBar()
    {
        var barRoot = new GameObject("HealthBar");
        barRoot.transform.SetParent(transform, false);
        barRoot.transform.localPosition = new Vector3(0f, healthBarHeightOffset, 0f);
        _billboardRoot = barRoot.transform;

        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(barRoot.transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 200;
        var rect = canvasGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(100f, 12f);
        canvasGo.transform.localScale = Vector3.one * healthBarScale;

        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bg = bgGo.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = new Color(0.12f, 0.12f, 0.12f, 0.92f);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(canvasGo.transform, false);
        _fillImage = fillGo.AddComponent<Image>();
        _fillImage.sprite = WhiteSprite();
        _fillImage.color = new Color(0.25f, 0.85f, 0.35f, 1f);
        _fillRect = _fillImage.GetComponent<RectTransform>();
        _fillRect.anchorMin = Vector2.zero;
        _fillRect.anchorMax = Vector2.one;
        _fillRect.pivot = new Vector2(0f, 0.5f);
        _fillRect.offsetMin = Vector2.zero;
        _fillRect.offsetMax = Vector2.zero;
    }

    static Sprite WhiteSprite()
    {
        if (_whiteSprite != null)
            return _whiteSprite;
        var tex = Texture2D.whiteTexture;
        _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        return _whiteSprite;
    }
}
