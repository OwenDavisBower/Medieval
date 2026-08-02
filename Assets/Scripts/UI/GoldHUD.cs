using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-center gold + player level/XP readout. Follows the runtime uGUI pattern of <see cref="MinimapUI"/>.
/// Click the panel to cheat +50 gold (debug).
/// </summary>
public sealed class GoldHUD : MonoBehaviour
{
    const int CheatGoldAmount = 50;

    [SerializeField] float marginPixels = 18f;
    [SerializeField] float panelWidth = 260f;
    [SerializeField] float panelHeight = 64f;
    [SerializeField] Color panelColor = new Color(0.06f, 0.05f, 0.04f, 0.62f);
    [SerializeField] Color accentColor = new Color(0.95f, 0.78f, 0.22f, 1f);
    [SerializeField] Color textColor = new Color(1f, 0.94f, 0.72f, 1f);
    [SerializeField] Color xpFillColor = new Color(0.35f, 0.65f, 1f, 1f);

    Text _goldLabel;
    Text _levelLabel;
    Image _xpFill;
    PlayerWallet _boundWallet;
    PlayerExperience _boundXp;
    static Font s_font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<GoldHUD>() != null)
            return;

        var go = new GameObject("GoldHUD");
        go.AddComponent<GoldHUD>();
    }

    void Awake() => BuildUi();

    void OnDisable()
    {
        UnbindWallet();
        UnbindXp();
    }

    void OnDestroy()
    {
        UnbindWallet();
        UnbindXp();
    }

    void LateUpdate()
    {
        if (_boundWallet == null)
            TryBindWallet();
        if (_boundXp == null)
            TryBindXp();
    }

    void TryBindWallet()
    {
        var wallet = PlayerWallet.Instance;
        if (wallet == null || wallet == _boundWallet)
            return;

        UnbindWallet();
        _boundWallet = wallet;
        _boundWallet.Changed += OnGoldChanged;
        RefreshGold(_boundWallet.Gold);
    }

    void UnbindWallet()
    {
        if (_boundWallet == null)
            return;
        _boundWallet.Changed -= OnGoldChanged;
        _boundWallet = null;
    }

    void TryBindXp()
    {
        var xp = PlayerReference.TryGetExperience();
        if (xp == null || xp == _boundXp)
            return;

        UnbindXp();
        _boundXp = xp;
        _boundXp.Changed += RefreshXp;
        RefreshXp();
    }

    void UnbindXp()
    {
        if (_boundXp == null)
            return;
        _boundXp.Changed -= RefreshXp;
        _boundXp = null;
    }

    void OnGoldChanged(int gold) => RefreshGold(gold);

    void OnCheatGoldClicked() => PlayerWallet.Instance?.Add(CheatGoldAmount);

    void RefreshGold(int gold)
    {
        if (_goldLabel != null)
            _goldLabel.text = $"Gold  {gold}";
    }

    void RefreshXp()
    {
        if (_boundXp == null)
            return;
        if (_levelLabel != null)
            _levelLabel.text = $"Lv {_boundXp.Level}";
        if (_xpFill != null)
            _xpFill.fillAmount = _boundXp.XpFill01;
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("GoldCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 95;
        canvasGo.AddComponent<GraphicRaycaster>();

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        var panelGo = new GameObject("GoldPanel");
        panelGo.transform.SetParent(canvasRect, false);
        var panelImage = panelGo.AddComponent<Image>();
        panelImage.color = panelColor;
        var panelBtn = panelGo.AddComponent<Button>();
        panelBtn.transition = Selectable.Transition.None;
        panelBtn.targetGraphic = panelImage;
        panelBtn.onClick.AddListener(OnCheatGoldClicked);
        var panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
        panelRect.anchoredPosition = new Vector2(0f, -marginPixels);

        var accentGo = new GameObject("Accent");
        accentGo.transform.SetParent(panelRect, false);
        var accentImage = accentGo.AddComponent<Image>();
        accentImage.color = accentColor;
        var accentRect = accentGo.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.sizeDelta = new Vector2(6f, 0f);
        accentRect.anchoredPosition = Vector2.zero;

        var goldGo = new GameObject("GoldLabel");
        goldGo.transform.SetParent(panelRect, false);
        _goldLabel = goldGo.AddComponent<Text>();
        _goldLabel.font = BuiltinFont();
        _goldLabel.fontSize = 24;
        _goldLabel.fontStyle = FontStyle.Bold;
        _goldLabel.alignment = TextAnchor.MiddleLeft;
        _goldLabel.color = textColor;
        _goldLabel.raycastTarget = false;
        _goldLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        _goldLabel.verticalOverflow = VerticalWrapMode.Overflow;
        var goldRect = goldGo.GetComponent<RectTransform>();
        goldRect.anchorMin = new Vector2(0f, 0.45f);
        goldRect.anchorMax = new Vector2(0.62f, 1f);
        goldRect.offsetMin = new Vector2(14f, 0f);
        goldRect.offsetMax = new Vector2(-4f, -2f);

        var levelGo = new GameObject("LevelLabel");
        levelGo.transform.SetParent(panelRect, false);
        _levelLabel = levelGo.AddComponent<Text>();
        _levelLabel.font = BuiltinFont();
        _levelLabel.fontSize = 22;
        _levelLabel.fontStyle = FontStyle.Bold;
        _levelLabel.alignment = TextAnchor.MiddleRight;
        _levelLabel.color = new Color(0.85f, 0.9f, 1f, 1f);
        _levelLabel.raycastTarget = false;
        _levelLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        var levelRect = levelGo.GetComponent<RectTransform>();
        levelRect.anchorMin = new Vector2(0.55f, 0.45f);
        levelRect.anchorMax = new Vector2(1f, 1f);
        levelRect.offsetMin = new Vector2(0f, 0f);
        levelRect.offsetMax = new Vector2(-10f, -2f);

        var xpTrackGo = new GameObject("XpTrack");
        xpTrackGo.transform.SetParent(panelRect, false);
        var xpTrack = xpTrackGo.AddComponent<Image>();
        xpTrack.sprite = WhiteSprite();
        xpTrack.color = new Color(0.18f, 0.18f, 0.22f, 0.9f);
        var xpTrackRect = xpTrackGo.GetComponent<RectTransform>();
        xpTrackRect.anchorMin = new Vector2(0f, 0f);
        xpTrackRect.anchorMax = new Vector2(1f, 0f);
        xpTrackRect.pivot = new Vector2(0.5f, 0f);
        xpTrackRect.sizeDelta = new Vector2(-20f, 10f);
        xpTrackRect.anchoredPosition = new Vector2(4f, 8f);

        var xpFillGo = new GameObject("XpFill");
        xpFillGo.transform.SetParent(xpTrackGo.transform, false);
        _xpFill = xpFillGo.AddComponent<Image>();
        _xpFill.sprite = WhiteSprite();
        _xpFill.color = xpFillColor;
        _xpFill.type = Image.Type.Filled;
        _xpFill.fillMethod = Image.FillMethod.Horizontal;
        _xpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _xpFill.fillAmount = 0f;
        _xpFill.raycastTarget = false;
        var xpFillRect = xpFillGo.GetComponent<RectTransform>();
        xpFillRect.anchorMin = Vector2.zero;
        xpFillRect.anchorMax = Vector2.one;
        xpFillRect.offsetMin = Vector2.zero;
        xpFillRect.offsetMax = Vector2.zero;

        RefreshGold(PlayerWallet.Instance != null ? PlayerWallet.Instance.Gold : 0);
        RefreshXp();
        TryBindWallet();
        TryBindXp();
    }

    static Sprite s_whiteSprite;

    static Sprite WhiteSprite()
    {
        if (s_whiteSprite != null)
            return s_whiteSprite;
        var tex = Texture2D.whiteTexture;
        s_whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        return s_whiteSprite;
    }

    static Font BuiltinFont()
    {
        if (s_font != null)
            return s_font;
        s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (s_font == null)
            s_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return s_font;
    }
}
