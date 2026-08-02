using System.Collections;
using UnityEngine;

/// <summary>
/// On player death: lose half gold/cargo, dismiss followers, respawn at a friendly village.
/// </summary>
public sealed class PlayerDeathRespawn : MonoBehaviour
{
    public static PlayerDeathRespawn Instance { get; private set; }

    [SerializeField] float deathHoldSeconds = 2.4f;
    [SerializeField] float respawnInvulnSeconds = 2.5f;

    bool _busy;
    Character _bound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<PlayerDeathRespawn>() != null)
            return;

        var go = new GameObject("PlayerDeathRespawn");
        go.AddComponent<PlayerDeathRespawn>();
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

    void LateUpdate()
    {
        if (_busy)
            return;

        var character = PlayerReference.TryGetCharacter();
        if (character == null)
            return;

        if (_bound != character)
            _bound = character;

        if (character.IsDead)
            StartCoroutine(RespawnRoutine(character));
    }

    IEnumerator RespawnRoutine(Character character)
    {
        _busy = true;
        GameplayEvents.RaiseToast("You have fallen...");

        yield return new WaitForSeconds(deathHoldSeconds);

        Transform player = PlayerReference.TryGetTransform();
        Vector3 from = player != null ? player.position : Vector3.zero;

        // Penalties
        var wallet = PlayerWallet.Instance;
        int goldLost = 0;
        if (wallet != null)
        {
            goldLost = wallet.Gold / 2;
            wallet.SetGold(wallet.Gold - goldLost);
        }

        int woodLost = 0, foodLost = 0;
        PlayerInventory.Instance?.ApplyDeathPenalty(out woodLost, out foodLost);

        int dismissed = PartyManager.Instance != null ? PartyManager.Instance.DisbandAll() : 0;

        SettlementRecord dest = SettlementService.Instance != null
            ? SettlementService.Instance.FindBestRespawnSettlement(from)
            : null;

        Vector3 respawnPos = dest != null ? dest.Center : from;
        respawnPos = TerrainSpawnUtility.GetWorldPositionOnTerrain(respawnPos);

        // Teleport
        var rb = PlayerReference.TryGetRigidbody();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.position = respawnPos;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else if (player != null)
        {
            player.position = respawnPos;
        }

        character.ReviveFull();

        // Brief god-mode blink so spawn camping is less punishing
        character.SetGodMode(true, Mathf.Max(character.MaxHealth, 200f));
        float invuln = respawnInvulnSeconds;
        while (invuln > 0f)
        {
            invuln -= Time.deltaTime;
            yield return null;
        }

        character.SetGodMode(false, 0f);

        string where = dest != null ? dest.DisplayName : "the wilds";
        GameplayEvents.RaiseToast(
            $"Respawned at {where}. Lost {goldLost}g, {woodLost} wood, {foodLost} food, {dismissed} troops.");

        _busy = false;
    }
}
