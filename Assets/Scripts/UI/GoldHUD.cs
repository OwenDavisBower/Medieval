using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-center gold readout. Follows the runtime uGUI pattern of <see cref="MinimapUI"/>.
/// </summary>
public sealed class GoldHUD : MonoBehaviour
{
    [SerializeField] float marginPixels = 18f;
    [SerializeField] float panelWidth = 200f;
    [SerializeField] float panelHeight = 44f;
    [SerializeField] Color panelColor = new Color(0.06f, 0.05f, 0.04f, 0.62f);
    [SerializeField] Color accentColor = new Color(0.95f, 0.78f, 0.22f, 1f);
    [SerializeField] Color textColor = new Color(1f, 0.94f, 0.72f, 1f);

    Text _label;
    PlayerWallet _boundWallet;
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

    void OnDisable() => UnbindWallet();

    void OnDestroy() => UnbindWallet();

    void LateUpdate()
    {
        if (_boundWallet != null)
            return;
        TryBindWallet();
    }

    void TryBindWallet()
    {
        var wallet = PlayerWallet.Instance;
        if (wallet == null || wallet == _boundWallet)
            return;

        UnbindWallet();
        _boundWallet = wallet;
        _boundWallet.Changed += OnGoldChanged;
        Refresh(_boundWallet.Gold);
    }

    void UnbindWallet()
    {
        if (_boundWallet == null)
            return;
        _boundWallet.Changed -= OnGoldChanged;
        _boundWallet = null;
    }

    void OnGoldChanged(int gold) => Refresh(gold);

    void Refresh(int gold)
    {
        if (_label != null)
            _label.text = $"Gold  {gold}";
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("GoldCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 95;

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

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(panelRect, false);
        _label = labelGo.AddComponent<Text>();
        _label.font = BuiltinFont();
        _label.fontSize = 26;
        _label.fontStyle = FontStyle.Bold;
        _label.alignment = TextAnchor.MiddleCenter;
        _label.color = textColor;
        _label.raycastTarget = false;
        _label.horizontalOverflow = HorizontalWrapMode.Overflow;
        _label.verticalOverflow = VerticalWrapMode.Overflow;
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12f, 4f);
        labelRect.offsetMax = new Vector2(-8f, -4f);

        Refresh(PlayerWallet.Instance != null ? PlayerWallet.Instance.Gold : 0);
        TryBindWallet();
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
