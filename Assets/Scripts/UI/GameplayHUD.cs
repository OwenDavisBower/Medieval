using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gameplay HUD: resources, party, quest tracker, village action panel, toasts.
/// Complements <see cref="GoldHUD"/> / <see cref="MinimapUI"/>.
/// </summary>
public sealed class GameplayHUD : MonoBehaviour
{
    static Font s_font;

    Text _resourcesLabel;
    Text _partyLabel;
    Text _standingLabel;
    Text _questTitle;
    Text _questBody;
    Text _toastLabel;
    RectTransform _villagePanel;
    Text _villageTitle;
    readonly List<Button> _actionButtons = new List<Button>();
    readonly List<Text> _actionLabels = new List<Text>();

    float _toastLife;
    SettlementRecord _boundSettlement;
    PlayerWallet _wallet;
    PlayerInventory _inventory;
    PartyManager _party;
    QuestService _quests;
    VillageInteractionController _village;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<GameplayHUD>() != null)
            return;

        var go = new GameObject("GameplayHUD");
        go.AddComponent<GameplayHUD>();
    }

    void Awake() => BuildUi();

    void OnEnable()
    {
        GameplayEvents.Toast += OnToast;
        TryBind();
    }

    void OnDisable()
    {
        GameplayEvents.Toast -= OnToast;
        Unbind();
    }

    void LateUpdate()
    {
        TryBind();
        RefreshResources();
        RefreshParty();
        RefreshQuest();
        RefreshVillagePanel();
        TickToast();
    }

    void TryBind()
    {
        if (_wallet == null && PlayerWallet.Instance != null)
        {
            _wallet = PlayerWallet.Instance;
            _wallet.Changed += OnWalletChanged;
        }

        if (_inventory == null && PlayerInventory.Instance != null)
        {
            _inventory = PlayerInventory.Instance;
            _inventory.Changed += RefreshResources;
        }

        if (_party == null && PartyManager.Instance != null)
        {
            _party = PartyManager.Instance;
            _party.Changed += RefreshParty;
        }

        if (_quests == null && QuestService.Instance != null)
        {
            _quests = QuestService.Instance;
            _quests.Changed += RefreshQuest;
        }

        if (_village == null && VillageInteractionController.Instance != null)
        {
            _village = VillageInteractionController.Instance;
            _village.NearbyChanged += RefreshVillagePanel;
        }
    }

    void Unbind()
    {
        if (_wallet != null)
            _wallet.Changed -= OnWalletChanged;
        if (_inventory != null)
            _inventory.Changed -= RefreshResources;
        if (_party != null)
            _party.Changed -= RefreshParty;
        if (_quests != null)
            _quests.Changed -= RefreshQuest;
        if (_village != null)
            _village.NearbyChanged -= RefreshVillagePanel;
        _wallet = null;
        _inventory = null;
        _party = null;
        _quests = null;
        _village = null;
    }

    void OnWalletChanged(int _) => RefreshResources();

    void RefreshResources()
    {
        if (_resourcesLabel == null)
            return;
        int wood = PlayerInventory.Instance != null ? PlayerInventory.Instance.Wood : 0;
        int food = PlayerInventory.Instance != null ? PlayerInventory.Instance.Food : 0;
        _resourcesLabel.text = $"Wood {wood}   Food {food}";
    }

    void RefreshParty()
    {
        if (_partyLabel == null)
            return;
        int count = PartyManager.Instance != null ? PartyManager.Instance.CountLivingFollowers() : 0;
        _partyLabel.text = $"Party {count}/{PartyManager.MaxPartySize}";
    }

    void RefreshQuest()
    {
        if (_questTitle == null || _questBody == null)
            return;

        var q = QuestService.Instance != null ? QuestService.Instance.Active : null;
        if (q == null || q.Status != QuestStatus.Active)
        {
            _questTitle.text = "No active quest";
            _questBody.text = "Visit a village (stand near houses).\nPress E for controls.";
            return;
        }

        _questTitle.text = q.Title;
        _questBody.text = $"{q.Description}\n{q.ProgressText}";
    }

    void RefreshVillagePanel()
    {
        var nearby = VillageInteractionController.Instance != null
            ? VillageInteractionController.Instance.NearbySettlement
            : null;

        if (_villagePanel != null)
            _villagePanel.gameObject.SetActive(nearby != null);

        if (nearby == null)
        {
            _boundSettlement = null;
            return;
        }

        _boundSettlement = nearby;
        if (_villageTitle != null)
            _villageTitle.text = nearby.DisplayName;

        if (_standingLabel != null)
        {
            string claim = nearby.OwnedByPlayer
                ? "Your land — taxes trickle in"
                : $"Standing {nearby.Reputation} ({nearby.StandingLabel})";
            _standingLabel.text = claim;
        }

        int recruitCost = SettlementService.Instance != null
            ? SettlementService.Instance.GetRecruitCost(nearby)
            : PartyManager.BaseRecruitCost;

        SetAction(0, $"1  Recruit ({recruitCost}g)", () => VillageInteractionController.Instance?.Recruit());
        SetAction(1, $"2  Buy wood ({SettlementService.BuyWoodPrice}g)", () => VillageInteractionController.Instance?.BuyWood());
        SetAction(2, $"3  Sell wood (+{SettlementService.SellWoodPrice}g)", () => VillageInteractionController.Instance?.SellWood());
        SetAction(3, $"4  Buy food ({SettlementService.BuyFoodPrice}g)", () => VillageInteractionController.Instance?.BuyFood());

        bool deliverTurnIn = QuestService.Instance != null &&
                             QuestService.Instance.Active != null &&
                             QuestService.Instance.Active.Type == QuestType.DeliverWood &&
                             QuestService.Instance.Active.OriginSettlementId == nearby.Id;
        SetAction(4, "5  Quest: Clear camp", () => VillageInteractionController.Instance?.QuestClearCamp());
        SetAction(5, deliverTurnIn ? "6  Turn in wood" : "6  Quest: Deliver wood",
            () => VillageInteractionController.Instance?.QuestDeliverOrTurnIn());
        SetAction(6, "7  Quest: Escort", () => VillageInteractionController.Instance?.QuestEscort());

        string claimLabel = nearby.OwnedByPlayer
            ? "8  Already owned"
            : $"8  Claim village ({SettlementService.ClaimGoldCost}g)";
        SetAction(7, claimLabel, () => VillageInteractionController.Instance?.Claim());
    }

    void SetAction(int index, string label, UnityEngine.Events.UnityAction onClick)
    {
        if (index < 0 || index >= _actionLabels.Count)
            return;
        _actionLabels[index].text = label;
        _actionButtons[index].onClick.RemoveAllListeners();
        _actionButtons[index].onClick.AddListener(onClick);
    }

    void OnToast(string message)
    {
        if (_toastLabel == null)
            return;
        _toastLabel.text = message;
        _toastLife = 3.2f;
        var c = _toastLabel.color;
        c.a = 1f;
        _toastLabel.color = c;
    }

    void TickToast()
    {
        if (_toastLabel == null || _toastLife <= 0f)
            return;
        _toastLife -= Time.deltaTime;
        if (_toastLife < 0.8f)
        {
            var c = _toastLabel.color;
            c.a = Mathf.Clamp01(_toastLife / 0.8f);
            _toastLabel.color = c;
        }

        if (_toastLife <= 0f)
            _toastLabel.text = string.Empty;
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("GameplayCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        var canvasRect = canvasGo.GetComponent<RectTransform>();
        StretchFull(canvasRect);

        // Top-left resource strip (gold HUD is top-center; this sits left)
        _resourcesLabel = MakeLabel(canvasRect, "Resources", new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(18f, -18f), new Vector2(420f, 36f), 22, TextAnchor.MiddleLeft,
            new Color(0.95f, 0.92f, 0.82f, 1f));

        _partyLabel = MakeLabel(canvasRect, "Party", new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(18f, -54f), new Vector2(280f, 30f), 20, TextAnchor.MiddleLeft,
            new Color(0.78f, 0.88f, 1f, 1f));

        // Quest panel top-left under party
        var questPanel = MakePanel(canvasRect, "QuestPanel", new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(18f, -96f), new Vector2(360f, 110f), new Color(0.05f, 0.06f, 0.07f, 0.62f));
        _questTitle = MakeLabel(questPanel, "QuestTitle", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(0f, 32f), 20, TextAnchor.MiddleLeft,
            new Color(1f, 0.86f, 0.45f, 1f));
        var qt = _questTitle.GetComponent<RectTransform>();
        qt.offsetMin = new Vector2(12f, -36f);
        qt.offsetMax = new Vector2(-12f, -6f);
        _questBody = MakeLabel(questPanel, "QuestBody", new Vector2(0f, 0f), new Vector2(1f, 1f),
            Vector2.zero, Vector2.zero, 16, TextAnchor.UpperLeft,
            new Color(0.9f, 0.9f, 0.88f, 1f));
        var qb = _questBody.GetComponent<RectTransform>();
        qb.offsetMin = new Vector2(12f, 8f);
        qb.offsetMax = new Vector2(-12f, -40f);
        _questBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        _questBody.verticalOverflow = VerticalWrapMode.Overflow;

        // Toast center-bottomish
        _toastLabel = MakeLabel(canvasRect, "Toast", new Vector2(0.5f, 0.22f), new Vector2(0.5f, 0.22f),
            Vector2.zero, new Vector2(900f, 48f), 26, TextAnchor.MiddleCenter,
            new Color(1f, 0.95f, 0.75f, 1f));
        _toastLabel.fontStyle = FontStyle.Bold;

        // Village action panel bottom-left
        _villagePanel = MakePanel(canvasRect, "VillagePanel", new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(18f, 18f), new Vector2(420f, 360f), new Color(0.05f, 0.05f, 0.04f, 0.72f));
        _villagePanel.pivot = new Vector2(0f, 0f);

        _villageTitle = MakeLabel(_villagePanel, "VillageTitle", new Vector2(0f, 1f), new Vector2(1f, 1f),
            Vector2.zero, new Vector2(0f, 34f), 22, TextAnchor.MiddleLeft,
            new Color(1f, 0.9f, 0.55f, 1f));
        var vt = _villageTitle.GetComponent<RectTransform>();
        vt.offsetMin = new Vector2(12f, -38f);
        vt.offsetMax = new Vector2(-12f, -6f);

        _standingLabel = MakeLabel(_villagePanel, "Standing", new Vector2(0f, 1f), new Vector2(1f, 1f),
            Vector2.zero, new Vector2(0f, 26f), 16, TextAnchor.MiddleLeft,
            new Color(0.75f, 0.9f, 0.78f, 1f));
        var st = _standingLabel.GetComponent<RectTransform>();
        st.offsetMin = new Vector2(12f, -62f);
        st.offsetMax = new Vector2(-12f, -38f);

        for (int i = 0; i < 8; i++)
        {
            float y = -70f - i * 34f;
            CreateActionButton(_villagePanel, i, y);
        }

        var dismiss = CreateTextButton(_villagePanel, "DismissBtn", "X  Dismiss follower",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 8f), new Vector2(0f, 32f),
            () => VillageInteractionController.Instance?.Disband());
        var db = dismiss.GetComponent<RectTransform>();
        db.offsetMin = new Vector2(10f, 40f);
        db.offsetMax = new Vector2(-10f, 72f);

        var sellFood = CreateTextButton(_villagePanel, "SellFoodBtn", $"C  Sell food (+{SettlementService.SellFoodPrice}g)",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 8f), new Vector2(0f, 32f),
            () => VillageInteractionController.Instance?.SellFood());
        var sf = sellFood.GetComponent<RectTransform>();
        sf.offsetMin = new Vector2(10f, 8f);
        sf.offsetMax = new Vector2(-10f, 40f);

        // Taller panel to fit sell-food row
        _villagePanel.sizeDelta = new Vector2(420f, 400f);

        _villagePanel.gameObject.SetActive(false);
        RefreshResources();
        RefreshParty();
        RefreshQuest();
    }

    void Start()
    {
        GameplayEvents.RaiseToast("Fight bandits for gold. Visit a village to recruit, trade, and take quests.");
    }

    void CreateActionButton(RectTransform parent, int index, float anchoredY)
    {
        var btn = CreateTextButton(parent, $"Action{index}", $"Action {index + 1}",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, anchoredY), new Vector2(0f, 30f), null);
        var rect = btn.GetComponent<RectTransform>();
        rect.offsetMin = new Vector2(10f, anchoredY - 30f);
        rect.offsetMax = new Vector2(-10f, anchoredY);
        _actionButtons.Add(btn);
        _actionLabels.Add(btn.GetComponentInChildren<Text>());
    }

    static Button CreateTextButton(RectTransform parent, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.16f, 0.14f, 0.1f, 0.85f);
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.32f, 0.28f, 0.18f, 1f);
        colors.pressedColor = new Color(0.45f, 0.38f, 0.2f, 1f);
        btn.colors = colors;
        if (onClick != null)
            btn.onClick.AddListener(onClick);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        var text = textGo.AddComponent<Text>();
        text.font = BuiltinFont();
        text.fontSize = 15;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = new Color(0.95f, 0.93f, 0.86f, 1f);
        text.text = label;
        text.raycastTarget = false;
        var tr = text.GetComponent<RectTransform>();
        StretchFull(tr);
        tr.offsetMin = new Vector2(10f, 0f);
        tr.offsetMax = new Vector2(-6f, 0f);
        return btn;
    }

    static RectTransform MakePanel(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPos, Vector2 size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        return rect;
    }

    static Text MakeLabel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPos, Vector2 size, int fontSize, TextAnchor align, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = BuiltinFont();
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = align;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(anchorMin.x, anchorMax.y);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        return text;
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
            return;

        var go = new GameObject("EventSystem");
        go.AddComponent<UnityEngine.EventSystems.EventSystem>();
        var module = go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        module.ActivateModule();
    }

    static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
