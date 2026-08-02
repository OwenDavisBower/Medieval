using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Near-village actions: recruit, trade, heal party, quests, claim, disband.
/// Keyboard: E opens/advances hub focus; number keys 1–8 / H heal party / C sell food when in range.
/// </summary>
public sealed class VillageInteractionController : MonoBehaviour
{
    public static VillageInteractionController Instance { get; private set; }

    SettlementRecord _nearby;
    float _toastGate;

    public SettlementRecord NearbySettlement => _nearby;
    public bool IsInVillageRange => _nearby != null;

    public event System.Action NearbyChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<VillageInteractionController>() != null)
            return;

        var go = new GameObject("VillageInteraction");
        go.AddComponent<VillageInteractionController>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        RefreshNearby();
        HandleInput();
        _toastGate = Mathf.Max(0f, _toastGate - Time.deltaTime);
    }

    void RefreshNearby()
    {
        Transform player = PlayerReference.TryGetTransform();
        var character = PlayerReference.TryGetCharacter();
        if (player == null || character != null && character.IsDead || SettlementService.Instance == null)
        {
            SetNearby(null);
            return;
        }

        SettlementRecord nearest = SettlementService.Instance.FindNearestSettlement(
            player.position, SettlementService.VillageInteractRadius);
        SetNearby(nearest);
    }

    void SetNearby(SettlementRecord next)
    {
        if (ReferenceEquals(_nearby, next))
            return;
        if (_nearby != null && next != null && _nearby.Id == next.Id)
            return;
        _nearby = next;
        NearbyChanged?.Invoke();
    }

    void HandleInput()
    {
        if (_nearby == null)
            return;

        var kb = Keyboard.current;
        if (kb == null)
            return;

        if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame)
            Recruit();
        else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame)
            BuyWood();
        else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame)
            SellWood();
        else if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame)
            BuyFood();
        else if (kb.hKey.wasPressedThisFrame)
            Heal();
        else if (kb.cKey.wasPressedThisFrame)
            SellFood();
        else if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame)
            QuestClearCamp();
        else if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame)
            QuestDeliverOrTurnIn();
        else if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame)
            QuestEscort();
        else if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame)
            Claim();
        else if (kb.xKey.wasPressedThisFrame)
            Disband();
        else if (kb.eKey.wasPressedThisFrame)
            HintActions();
    }

    void HintActions()
    {
        if (_toastGate > 0f)
            return;
        _toastGate = 1.2f;
        GameplayEvents.RaiseToast("1 Recruit  2/3 Wood  4/C Food  H Heal party  5–7 Quests  8 Claim  X Dismiss");
    }

    public void Recruit() => PartyManager.Instance?.TryRecruit(_nearby);

    public void BuyWood() => SettlementService.Instance?.TryBuyWood(_nearby);

    public void SellWood() => SettlementService.Instance?.TrySellWood(_nearby);

    public void BuyFood() => SettlementService.Instance?.TryBuyFood(_nearby);

    public void SellFood() => SettlementService.Instance?.TrySellFood(_nearby);

    public void Heal() => SettlementService.Instance?.TryHealParty(_nearby);

    public void Disband() => PartyManager.Instance?.TryDisbandOne();

    public void Claim() => SettlementService.Instance?.TryClaim(_nearby);

    public void QuestClearCamp() => QuestService.Instance?.TryAcceptClearCamp(_nearby);

    public void QuestEscort() => QuestService.Instance?.TryAcceptEscort(_nearby);

    public void QuestDeliverOrTurnIn()
    {
        var quests = QuestService.Instance;
        if (quests == null)
            return;

        if (quests.Active != null &&
            quests.Active.Type == QuestType.DeliverWood &&
            quests.Active.Status == QuestStatus.Active &&
            quests.Active.OriginSettlementId == _nearby.Id)
        {
            quests.TryTurnInDeliverWood();
            return;
        }

        quests.TryAcceptDeliverWood(_nearby);
    }

    public void AbandonQuest() => QuestService.Instance?.Abandon();
}
