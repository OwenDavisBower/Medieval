using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Quest journal overlay (J). Lists active quests and recent completed/failed entries.</summary>
public sealed class QuestJournalUI : MonoBehaviour
{
    public static QuestJournalUI Instance { get; private set; }

    RectTransform _panel;
    Text _body;
    bool _open;
    readonly StringBuilder _sb = new StringBuilder(512);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<QuestJournalUI>() != null)
            return;

        var go = new GameObject("QuestJournalUI");
        go.AddComponent<QuestJournalUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildUi();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        if (kb.jKey.wasPressedThisFrame)
            Toggle();
        else if (_open && kb.escapeKey.wasPressedThisFrame)
            SetOpen(false);
        else if (kb.tKey.wasPressedThisFrame)
            QuestService.Instance?.CycleTracked();
        else if (kb.qKey.wasPressedThisFrame)
            QuestService.Instance?.Abandon();
    }

    void LateUpdate()
    {
        if (_open)
            Refresh();
    }

    public void Toggle() => SetOpen(!_open);

    public void SetOpen(bool open)
    {
        _open = open;
        if (_panel != null)
            _panel.gameObject.SetActive(open);
        if (open)
            Refresh();
    }

    void Refresh()
    {
        if (_body == null)
            return;

        var quests = QuestService.Instance;
        _sb.Clear();
        if (quests == null)
        {
            _body.text = "Quest service unavailable.";
            return;
        }

        _sb.AppendLine("ACTIVE");
        if (quests.ActiveQuests.Count == 0)
            _sb.AppendLine("  (none)");
        else
        {
            for (int i = 0; i < quests.ActiveQuests.Count; i++)
            {
                QuestInstance q = quests.ActiveQuests[i];
                if (q == null)
                    continue;
                string mark = q == quests.Tracked ? ">" : " ";
                _sb.Append(mark).Append(' ').Append(q.Title).Append(" — ").AppendLine(q.ProgressText);
            }
        }

        _sb.AppendLine();
        _sb.AppendLine("RECENT");
        int shown = 0;
        for (int i = 0; i < quests.Journal.Count && shown < 8; i++)
        {
            QuestInstance q = quests.Journal[i];
            if (q == null)
                continue;
            _sb.Append("  [").Append(q.Status).Append("] ").AppendLine(q.Title);
            shown++;
        }

        if (shown == 0)
            _sb.AppendLine("  (empty)");

        _sb.AppendLine();
        _sb.Append("T cycle track   Q abandon   J/Esc close");
        _body.text = _sb.ToString();
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("QuestJournalCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasRect, false);
        var image = panelGo.AddComponent<Image>();
        image.color = new Color(0.04f, 0.05f, 0.06f, 0.92f);
        _panel = panelGo.GetComponent<RectTransform>();
        _panel.anchorMin = new Vector2(0.5f, 0.5f);
        _panel.anchorMax = new Vector2(0.5f, 0.5f);
        _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(520f, 420f);

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(_panel, false);
        var title = titleGo.AddComponent<Text>();
        title.font = BuiltinFont();
        title.fontSize = 24;
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(1f, 0.86f, 0.45f, 1f);
        title.alignment = TextAnchor.MiddleLeft;
        title.text = "Quest Journal";
        title.raycastTarget = false;
        var titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -8f);
        titleRt.sizeDelta = new Vector2(-24f, 36f);

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(_panel, false);
        _body = bodyGo.AddComponent<Text>();
        _body.font = title.font;
        _body.fontSize = 16;
        _body.color = new Color(0.9f, 0.9f, 0.88f, 1f);
        _body.alignment = TextAnchor.UpperLeft;
        _body.horizontalOverflow = HorizontalWrapMode.Wrap;
        _body.verticalOverflow = VerticalWrapMode.Overflow;
        _body.raycastTarget = false;
        var bodyRt = _body.GetComponent<RectTransform>();
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = new Vector2(16f, 16f);
        bodyRt.offsetMax = new Vector2(-16f, -48f);

        _panel.gameObject.SetActive(false);
    }

    static Font BuiltinFont()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
