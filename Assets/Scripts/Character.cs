using UnityEngine;
using UnityEngine.UI;

/// <summary>Health for player, bandits, and followers. Shows a world-space bar after taking damage.</summary>
public class Character : MonoBehaviour
{
    [SerializeField] float maxHealth = 100f;
    [SerializeField] float healthBarHeightOffset = 2.15f;
    [SerializeField] float healthBarScale = 0.008f;

    float _current;
    Canvas _canvas;
    RectTransform _fillRect;
    Image _fillImage;
    Transform _billboardRoot;
    static Sprite _whiteSprite;

    public float CurrentHealth => _current;
    public float MaxHealth => maxHealth;
    public bool IsDead => _current <= 0f;

    void Awake()
    {
        _current = maxHealth;
        BuildHealthBar();
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (_billboardRoot == null || !_billboardRoot.gameObject.activeInHierarchy)
            return;
        var cam = Camera.main;
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

        if (_canvas != null && _current > 0f && _current < maxHealth)
            _canvas.gameObject.SetActive(true);

        if (_current <= 0f)
            Destroy(gameObject);
    }

    void RefreshBarVisual()
    {
        if (_fillRect == null)
            return;
        float t = maxHealth > 0.01f ? Mathf.Clamp01(_current / maxHealth) : 0f;
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
