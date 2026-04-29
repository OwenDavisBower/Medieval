using UnityEngine;
using UnityEngine.UI;

/// <summary>World-space health bar for any <see cref="IDamageableHealth"/> on this GameObject (e.g. <see cref="Character"/>, <see cref="Building"/>).</summary>
public class CharacterHealthBar : MonoBehaviour
{
    [SerializeField] float healthBarHeightOffset = 2.15f;
    [SerializeField] float healthBarScale = 0.011f;
    [SerializeField] [Tooltip("Max degrees from the camera toward a point-at-camera billboard at the viewport left/right edges; 0 at horizontal center (scaled smoothly by screen position).")] float maxHealthBarBillboardTiltDegrees = 15f;
    [SerializeField] [Tooltip("Use the same name as a user layer in Project Settings; assigned to the health bar so it can render on the URP overlay camera.")] string _healthBarLayer = "HealthBar";

    IDamageableHealth _health;
    Canvas _canvas;
    RectTransform _fillRect;
    Image _fillImage;
    Transform _billboardRoot;
    static Sprite _whiteSprite;
    static int s_billboardCamFrame = -1;
    static Camera s_billboardMainCam;

    void Start()
    {
        _health = GetComponent<IDamageableHealth>();
        if (_health == null)
        {
            enabled = false;
            return;
        }

        BuildHealthBar();
        OnHealthChanged(_health.CurrentHealth, _health.MaxHealth);
        if (_canvas != null && _health.CurrentHealth >= _health.MaxHealth - 0.001f)
            _canvas.gameObject.SetActive(false);
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
        Quaternion relCam = cam.transform.rotation;
        Quaternion faceCam = Quaternion.LookRotation(-toCam.normalized, Vector3.up);

        Vector3 vp = cam.WorldToViewportPoint(_billboardRoot.position);
        float tiltScale;
        if (vp.z <= 0f)
            tiltScale = 0f;
        else
        {
            float h = Mathf.Clamp01(Mathf.Abs(vp.x - 0.5f) * 2f);
            tiltScale = h;
        }

        float maxTilt = maxHealthBarBillboardTiltDegrees * tiltScale;
        _billboardRoot.rotation = Quaternion.RotateTowards(relCam, faceCam, maxTilt);
    }

    /// <summary>Call after <see cref="IDamageableHealth"/> health changes (e.g. from <see cref="Character.TakeDamage"/> or <see cref="Building.TakeDamage"/>).</summary>
    public void OnHealthChanged(float current, float maxHealth)
    {
        RefreshBarVisual(current, maxHealth);

        if (_canvas == null)
            return;
        if (current <= 0f)
            _canvas.gameObject.SetActive(false);
        else if (current < maxHealth)
            _canvas.gameObject.SetActive(true);
    }

    void RefreshBarVisual(float current, float maxHealth)
    {
        if (_fillRect == null)
            return;
        float t = maxHealth > 0.01f ? Mathf.Clamp01(current / maxHealth) : 0f;
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

        int hb = LayerMask.NameToLayer(_healthBarLayer);
        if (hb >= 0)
            HierarchyLayers.SetRecursive(barRoot.transform, hb);
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
