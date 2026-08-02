using System;
using System.Collections.Generic;
using Medieval.NpcMovement;
using Medieval.Npcs;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Settlement + camp registry: reputation, ownership, local markets, passive income.
/// Bootstraps at play start; populated from world generation plans.
/// </summary>
public sealed class SettlementService : MonoBehaviour
{
    public static SettlementService Instance { get; private set; }

    public const int MaxReputation = 100;
    public const int MinReputation = -100;
    public const int ClaimReputationRequired = 35;
    public const int ClaimGoldCost = 40;
    /// <summary>Standing applied at the attacked village when declaring war.</summary>
    public const int AttackSettlementReputationHit = -80;
    public const float VillageInteractRadius = 18f;
    /// <summary>Must exceed bandit camp min distance from settlements (~120m) so camps link to villages.</summary>
    public const float CampLinkRadius = 180f;
    /// <summary>Approximate village extent from center (matches outermost structure layer).</summary>
    public const float SettlementPerimeterRadius = 30f;
    /// <summary>Bandit death within this distance of a village perimeter can grant standing.</summary>
    public const float BanditKillRepPerimeterMargin = 20f;
    /// <summary>Max distance from village center for per-kill standing (perimeter + margin).</summary>
    public static float BanditKillRepRadius => SettlementPerimeterRadius + BanditKillRepPerimeterMargin;
    const float CampKillCreditRadius = 55f;
    const int BanditKillReputation = 2;

    const float StockRegenInterval = 18f;
    const int StockRegenWood = 3;
    const int StockRegenFood = 2;
    const float TaxInterval = 25f;
    const int TaxGoldPerOwned = 4;

    public const int BuyWoodPrice = 4;
    public const int SellWoodPrice = 2;
    public const int BuyFoodPrice = 5;
    public const int SellFoodPrice = 2;
    /// <summary>Gold to fully restore one character at standing 0 (scales down with missing HP / standing / ownership).</summary>
    public const int HealFullCost = 20;

    readonly List<SettlementRecord> _settlements = new List<SettlementRecord>();
    readonly List<CampRecord> _camps = new List<CampRecord>();
    readonly Dictionary<int, SettlementRecord> _byId = new Dictionary<int, SettlementRecord>();
    readonly Dictionary<int, CampRecord> _campById = new Dictionary<int, CampRecord>();
    readonly List<Entity> _healFollowersScratch = new List<Entity>(16);
    readonly List<string> _nameScratch = new List<string>(32);

    public IReadOnlyList<SettlementRecord> Settlements => _settlements;
    public IReadOnlyList<CampRecord> Camps => _camps;

    public event Action Changed;

    void Start()
    {
        // World gen may have planned before this component enabled.
        var coordinator = FindAnyObjectByType<WorldGenerationCoordinator>();
        if (coordinator != null && coordinator.PlannedSettlementCenters.Count > 0 && _settlements.Count == 0)
            RebuildFromPlans(coordinator.PlannedSettlementCenters, coordinator.PlannedBanditCenters);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<SettlementService>() != null)
            return;

        var go = new GameObject("SettlementService");
        go.AddComponent<SettlementService>();
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

    void OnEnable()
    {
        WorldGenerationCoordinator.WorldContentPlanned += OnWorldContentPlanned;
        GameplayEvents.EnemyKilled += OnEnemyKilled;
    }

    void OnDisable()
    {
        WorldGenerationCoordinator.WorldContentPlanned -= OnWorldContentPlanned;
        GameplayEvents.EnemyKilled -= OnEnemyKilled;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (_settlements.Count == 0)
            return;

        float dt = Time.deltaTime;
        bool dirty = false;
        for (int i = 0; i < _settlements.Count; i++)
        {
            SettlementRecord s = _settlements[i];
            s.StockRegenTimer += dt;
            if (s.StockRegenTimer >= StockRegenInterval)
            {
                s.StockRegenTimer = 0f;
                s.WoodStock = Mathf.Min(200, s.WoodStock + StockRegenWood);
                s.FoodStock = Mathf.Min(160, s.FoodStock + StockRegenFood);
                dirty = true;
            }

            if (!s.OwnedByPlayer)
                continue;

            s.TaxTimer += dt;
            if (s.TaxTimer >= TaxInterval)
            {
                s.TaxTimer = 0f;
                var wallet = PlayerWallet.Instance;
                if (wallet != null)
                    wallet.Add(TaxGoldPerOwned);
            }
        }

        if (dirty)
            Changed?.Invoke();
    }

    void OnWorldContentPlanned()
    {
        var coordinator = FindAnyObjectByType<WorldGenerationCoordinator>();
        if (coordinator == null)
            return;

        RebuildFromPlans(coordinator.PlannedSettlementCenters, coordinator.PlannedBanditCenters);
    }

    public void RebuildFromPlans(IReadOnlyList<Vector3> settlementCenters, IReadOnlyList<Vector3> banditCenters)
    {
        _settlements.Clear();
        _camps.Clear();
        _byId.Clear();
        _campById.Clear();

        if (settlementCenters != null)
        {
            int worldSeed = 42;
            var coordinator = FindAnyObjectByType<WorldGenerationCoordinator>();
            if (coordinator != null)
                worldSeed = coordinator.Seed;

            SettlementMedievalNameUtility.GenerateUnique(worldSeed, settlementCenters.Count, _nameScratch);

            for (int i = 0; i < settlementCenters.Count; i++)
            {
                int ownerFaction = AssignSettlementOwnerFaction(worldSeed, i, settlementCenters.Count);
                var rec = new SettlementRecord
                {
                    Id = i,
                    Name = _nameScratch[i],
                    PlannedCenter = settlementCenters[i],
                    WorldCenter = settlementCenters[i],
                    IsBuilt = false,
                    BuildFailed = false,
                    Reputation = 0,
                    OwnedByPlayer = false,
                    OwnerFactionId = ownerFaction,
                    WoodStock = 35 + (i * 3) % 20,
                    FoodStock = 20 + (i * 2) % 15
                };
                _settlements.Add(rec);
                _byId[i] = rec;
            }
        }

        if (banditCenters != null)
        {
            for (int i = 0; i < banditCenters.Count; i++)
            {
                Vector3 c = banditCenters[i];
                int linked = FindNearestSettlementId(c, CampLinkRadius);
                var camp = new CampRecord
                {
                    Id = i,
                    PlannedCenter = c,
                    WorldCenter = c,
                    LinkedSettlementId = linked
                };
                _camps.Add(camp);
                _campById[i] = camp;
            }
        }

        Changed?.Invoke();
    }

    public void NotifySettlementInstance(int settlementId, Vector3 worldCenter, bool active)
    {
        if (!_byId.TryGetValue(settlementId, out SettlementRecord rec))
            return;
        rec.HasLiveInstance = active;
        if (active)
        {
            rec.WorldCenter = worldCenter;
            rec.IsBuilt = true;
            rec.BuildFailed = false;
        }

        Changed?.Invoke();
    }

    /// <summary>Marks a planned settlement as unable to place structures so UI (e.g. minimap) can hide it.</summary>
    public void NotifySettlementBuildFailed(int settlementId)
    {
        if (!_byId.TryGetValue(settlementId, out SettlementRecord rec))
            return;

        // Keep a prior successful placement on the map if this spawn attempt failed.
        if (rec.IsBuilt)
            return;

        rec.BuildFailed = true;
        rec.HasLiveInstance = false;
        Changed?.Invoke();
    }

    public void NotifyCampInstance(int campId, Vector3 worldCenter, bool active, int spawnedBandits = -1)
    {
        if (!_campById.TryGetValue(campId, out CampRecord camp))
            return;
        camp.HasLiveInstance = active;
        if (active)
            camp.WorldCenter = worldCenter;
        if (spawnedBandits >= 0)
            camp.SpawnedBanditCount = spawnedBandits;
        Changed?.Invoke();
    }

    public bool TryGetSettlement(int id, out SettlementRecord record) => _byId.TryGetValue(id, out record);

    public bool TryGetCamp(int id, out CampRecord record) => _campById.TryGetValue(id, out record);

    public SettlementRecord FindNearestSettlement(Vector3 worldPos, float maxRadius = float.PositiveInfinity)
    {
        SettlementRecord best = null;
        float bestSq = maxRadius * maxRadius;
        for (int i = 0; i < _settlements.Count; i++)
        {
            SettlementRecord s = _settlements[i];
            float sq = HorizontalDistanceSq(worldPos, s.Center);
            if (sq <= bestSq)
            {
                bestSq = sq;
                best = s;
            }
        }

        return best;
    }

    public int FindNearestSettlementId(Vector3 worldPos, float maxRadius)
    {
        SettlementRecord s = FindNearestSettlement(worldPos, maxRadius);
        return s != null ? s.Id : -1;
    }

    public CampRecord FindNearestUnclearedCamp(Vector3 worldPos, float maxRadius = float.PositiveInfinity)
    {
        CampRecord best = null;
        float bestSq = maxRadius * maxRadius;
        for (int i = 0; i < _camps.Count; i++)
        {
            CampRecord c = _camps[i];
            if (c.Cleared)
                continue;
            float sq = HorizontalDistanceSq(worldPos, c.Center);
            if (sq <= bestSq)
            {
                bestSq = sq;
                best = c;
            }
        }

        return best;
    }

    public CampRecord FindUnclearedCampLinkedTo(int settlementId)
    {
        CampRecord best = null;
        float bestSq = float.MaxValue;
        if (!_byId.TryGetValue(settlementId, out SettlementRecord settlement))
            return null;

        for (int i = 0; i < _camps.Count; i++)
        {
            CampRecord c = _camps[i];
            if (c.Cleared)
                continue;
            if (c.LinkedSettlementId != settlementId)
            {
                // Fall back: any camp near this village
                if (HorizontalDistanceSq(c.Center, settlement.Center) > CampLinkRadius * CampLinkRadius)
                    continue;
            }

            float sq = HorizontalDistanceSq(c.Center, settlement.Center);
            if (sq < bestSq)
            {
                bestSq = sq;
                best = c;
            }
        }

        return best;
    }

    public bool AreLinkedCampsCleared(int settlementId)
    {
        if (!_byId.TryGetValue(settlementId, out SettlementRecord settlement))
            return false;

        bool any = false;
        for (int i = 0; i < _camps.Count; i++)
        {
            CampRecord c = _camps[i];
            float sq = HorizontalDistanceSq(c.Center, settlement.Center);
            if (sq > CampLinkRadius * CampLinkRadius)
                continue;
            any = true;
            if (!c.Cleared)
                return false;
        }

        return any;
    }

    public void AddReputation(int settlementId, int delta, string reason = null)
    {
        if (!_byId.TryGetValue(settlementId, out SettlementRecord s) || delta == 0)
            return;

        int prev = s.Reputation;
        s.Reputation = Mathf.Clamp(s.Reputation + delta, MinReputation, MaxReputation);
        if (s.Reputation == prev)
            return;

        GameplayEvents.RaiseReputationChanged(settlementId, s.Reputation);
        if (!string.IsNullOrEmpty(reason))
            GameplayEvents.RaiseToast($"{reason} ({(delta > 0 ? "+" : "")}{delta} standing)");
        Changed?.Invoke();
    }

    public int GetRecruitCost(SettlementRecord settlement)
    {
        int baseCost = PartyManager.BaseRecruitCost;
        if (settlement == null)
            return baseCost;
        if (settlement.OwnedByPlayer)
            return Mathf.Max(5, baseCost / 2);

        // Standing 0 → full price; 100 → ~40% off
        float discount = Mathf.Clamp01(settlement.Reputation / 100f) * 0.45f;
        return Mathf.Max(6, Mathf.RoundToInt(baseCost * (1f - discount)));
    }

    /// <summary>
    /// Gold the player pays to fully restore the party here (player + any still-damaged followers).
    /// Followers auto-spend their own gold on arrival; this covers whatever HP remains.
    /// 0 if already full / unavailable. Scales with missing HP; owned villages and high standing discount like recruit.
    /// </summary>
    public int GetHealCost(SettlementRecord settlement)
    {
        int fullCost = GetDiscountedHealFullCost(settlement);
        int total = 0;

        var character = PlayerReference.TryGetCharacter();
        if (character != null && !character.IsDead && character.MaxHealth > 0f)
            total += CostForMissingHp(fullCost, character.MaxHealth - character.CurrentHealth, character.MaxHealth);

        total += GetFollowerHealCost(fullCost);
        return total;
    }

    /// <summary>
    /// Living followers spend their own wallet gold toward healing (proportional to what they can afford).
    /// Called automatically when the party enters a settlement.
    /// </summary>
    public bool TryAutoHealFollowers(SettlementRecord settlement)
    {
        if (settlement == null || IsOwnerHostileToPlayer(settlement))
            return false;

        int fullCost = GetDiscountedHealFullCost(settlement);
        int spent = HealLivingFollowersWithOwnGold(fullCost);
        if (spent <= 0)
            return false;

        GameplayEvents.RaiseToast($"Followers healed (−{spent}g)");
        return true;
    }

    public bool TryHealParty(SettlementRecord settlement)
    {
        if (settlement == null)
            return false;
        if (RefuseIfHostile(settlement))
            return false;

        var character = PlayerReference.TryGetCharacter();
        if (character == null || character.IsDead)
        {
            GameplayEvents.RaiseToast("Cannot heal right now.");
            return false;
        }

        int fullCost = GetDiscountedHealFullCost(settlement);
        int playerCost = CostForMissingHp(fullCost, character.MaxHealth - character.CurrentHealth, character.MaxHealth);
        int followerCost = GetFollowerHealCost(fullCost);
        int totalCost = playerCost + followerCost;

        if (totalCost <= 0)
        {
            GameplayEvents.RaiseToast("Party already at full health.");
            return false;
        }

        var wallet = PlayerWallet.Instance;
        if (wallet == null || !wallet.TrySpend(totalCost))
        {
            GameplayEvents.RaiseToast($"Need {totalCost}g to heal the party.");
            return false;
        }

        float playerMissing = character.MaxHealth - character.CurrentHealth;
        if (playerMissing > 0.01f)
            character.Heal(playerMissing);

        HealLivingFollowersFully();

        if (playerCost > 0 && followerCost > 0)
            GameplayEvents.RaiseToast($"Party healed (−{totalCost}g)");
        else if (playerCost > 0)
            GameplayEvents.RaiseToast($"Healed (−{totalCost}g)");
        else
            GameplayEvents.RaiseToast($"Followers healed (−{totalCost}g)");

        return true;
    }

    int GetDiscountedHealFullCost(SettlementRecord settlement)
    {
        int fullCost = HealFullCost;
        if (settlement != null && settlement.OwnedByPlayer)
            return Mathf.Max(5, fullCost / 2);
        if (settlement != null)
        {
            float discount = Mathf.Clamp01(settlement.Reputation / 100f) * 0.45f;
            return Mathf.Max(6, Mathf.RoundToInt(fullCost * (1f - discount)));
        }

        return fullCost;
    }

    static int CostForMissingHp(int fullCost, float missing, float maxHealth)
    {
        if (missing <= 0.01f || maxHealth <= 0f)
            return 0;
        float fraction = Mathf.Clamp01(missing / maxHealth);
        return Mathf.Max(1, Mathf.CeilToInt(fullCost * fraction));
    }

    bool TryGetFollowerEntityManager(out EntityManager em)
    {
        em = default;
        if (PartyManager.Instance == null)
            return false;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        PartyManager.Instance.CopyLivingFollowers(_healFollowersScratch);
        em = world.EntityManager;
        return true;
    }

    int GetFollowerHealCost(int fullCost)
    {
        if (fullCost <= 0 || !TryGetFollowerEntityManager(out EntityManager em))
            return 0;

        int total = 0;
        for (int i = 0; i < _healFollowersScratch.Count; i++)
        {
            Entity e = _healFollowersScratch[i];
            if (!em.HasComponent<NpcCharacterCombatState>(e))
                continue;

            var combat = em.GetComponentData<NpcCharacterCombatState>(e);
            if (combat.IsDead != 0 || combat.MaxHealth <= 0f)
                continue;

            total += CostForMissingHp(fullCost, combat.MaxHealth - combat.CurrentHealth, combat.MaxHealth);
        }

        return total;
    }

    /// <summary>
    /// Each living follower spends their own wallet gold toward healing, restoring HP proportional to what they can afford.
    /// Returns total gold spent by followers.
    /// </summary>
    int HealLivingFollowersWithOwnGold(int fullCost)
    {
        if (fullCost <= 0 || !TryGetFollowerEntityManager(out EntityManager em))
            return 0;

        int totalSpent = 0;
        for (int i = 0; i < _healFollowersScratch.Count; i++)
        {
            Entity e = _healFollowersScratch[i];
            if (!em.HasComponent<NpcCharacterCombatState>(e))
                continue;

            var combat = em.GetComponentData<NpcCharacterCombatState>(e);
            if (combat.IsDead != 0 || combat.MaxHealth <= 0f)
                continue;

            float missing = combat.MaxHealth - combat.CurrentHealth;
            int cost = CostForMissingHp(fullCost, missing, combat.MaxHealth);
            if (cost <= 0)
                continue;

            if (!em.HasComponent<NpcWallet>(e))
                continue;

            var npcWallet = em.GetComponentData<NpcWallet>(e);
            if (npcWallet.Gold <= 0)
                continue;

            int spend = Mathf.Min(npcWallet.Gold, cost);
            float healFraction = spend / (float)cost;
            float healAmount = missing * healFraction;
            if (healAmount <= 0.01f)
                continue;

            npcWallet.Gold -= spend;
            em.SetComponentData(e, npcWallet);

            combat.CurrentHealth = Mathf.Min(combat.MaxHealth, combat.CurrentHealth + healAmount);
            em.SetComponentData(e, combat);
            totalSpent += spend;
        }

        return totalSpent;
    }

    /// <summary>Fully restores living followers (player already paid). Does not touch follower wallets.</summary>
    void HealLivingFollowersFully()
    {
        if (!TryGetFollowerEntityManager(out EntityManager em))
            return;

        for (int i = 0; i < _healFollowersScratch.Count; i++)
        {
            Entity e = _healFollowersScratch[i];
            if (!em.HasComponent<NpcCharacterCombatState>(e))
                continue;

            var combat = em.GetComponentData<NpcCharacterCombatState>(e);
            if (combat.IsDead != 0 || combat.MaxHealth <= 0f)
                continue;
            if (combat.CurrentHealth >= combat.MaxHealth - 0.01f)
                continue;

            combat.CurrentHealth = combat.MaxHealth;
            em.SetComponentData(e, combat);
        }
    }

    public bool IsOwnerHostileToPlayer(SettlementRecord settlement) =>
        settlement != null && settlement.IsAtWarWithPlayer;

    /// <summary>Toasts and returns true when peaceful village actions should be refused.</summary>
    public bool RefuseIfHostile(SettlementRecord settlement)
    {
        if (!IsOwnerHostileToPlayer(settlement))
            return false;
        GameplayEvents.RaiseToast("This village is at war with you.");
        return true;
    }

    public bool CanAttack(SettlementRecord settlement, out string failReason)
    {
        failReason = null;
        if (settlement == null)
        {
            failReason = "No village nearby.";
            return false;
        }

        if (settlement.OwnedByPlayer)
        {
            failReason = "You cannot attack your own village.";
            return false;
        }

        if (FactionManager.Instance == null)
        {
            failReason = "Factions not ready.";
            return false;
        }

        if (settlement.IsAtWarWithPlayer)
        {
            failReason = "Already at war with this faction.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Declares war on the settlement's owning faction. Garrison soldiers and other NPCs of that
    /// faction treat the player as Enemy and begin seeking combat.
    /// </summary>
    public bool TryAttack(SettlementRecord settlement)
    {
        if (!CanAttack(settlement, out string fail))
        {
            GameplayEvents.RaiseToast(fail);
            return false;
        }

        var fm = FactionManager.Instance;
        int enemyFaction = settlement.OwnerFactionId;
        fm.SetRelationship(WellKnownFactionIds.Player, enemyFaction, Relationship.Enemy);

        ApplyAttackStandingHit(settlement);

        string foeName = "this faction";
        if (fm.TryGetFactionName(enemyFaction, out string factionName))
            foeName = factionName;

        GameplayEvents.RaiseToast($"War with {foeName}! Their soldiers will attack you.");
        Changed?.Invoke();
        return true;
    }

    void ApplyAttackStandingHit(SettlementRecord attacked)
    {
        int prev = attacked.Reputation;
        attacked.Reputation = Mathf.Clamp(
            attacked.Reputation + AttackSettlementReputationHit, MinReputation, MaxReputation);
        if (attacked.Reputation != prev)
            GameplayEvents.RaiseReputationChanged(attacked.Id, attacked.Reputation);

        // Other villages of the same faction turn cold without extra toasts.
        for (int i = 0; i < _settlements.Count; i++)
        {
            SettlementRecord s = _settlements[i];
            if (s == null || s.Id == attacked.Id || s.OwnedByPlayer)
                continue;
            if (s.OwnerFactionId != attacked.OwnerFactionId)
                continue;

            int before = s.Reputation;
            s.Reputation = Mathf.Min(s.Reputation, -40);
            if (s.Reputation != before)
                GameplayEvents.RaiseReputationChanged(s.Id, s.Reputation);
        }
    }

    public bool CanClaim(SettlementRecord settlement, out string failReason)
    {
        failReason = null;
        if (settlement == null)
        {
            failReason = "No village nearby.";
            return false;
        }

        if (settlement.OwnedByPlayer)
        {
            failReason = "You already own this village.";
            return false;
        }

        if (settlement.IsAtWarWithPlayer)
        {
            failReason = "Cannot claim a village at war with you.";
            return false;
        }

        bool campsClear = AreLinkedCampsCleared(settlement.Id);
        bool standingOk = settlement.Reputation >= ClaimReputationRequired;
        if (!campsClear && !standingOk)
        {
            failReason = $"Need standing {ClaimReputationRequired}+ or clear nearby camps.";
            return false;
        }

        var wallet = PlayerWallet.Instance;
        if (wallet == null || wallet.Gold < ClaimGoldCost)
        {
            failReason = $"Need {ClaimGoldCost} gold to claim.";
            return false;
        }

        return true;
    }

    public bool TryClaim(SettlementRecord settlement)
    {
        if (!CanClaim(settlement, out string fail))
        {
            GameplayEvents.RaiseToast(fail);
            return false;
        }

        var wallet = PlayerWallet.Instance;
        if (wallet == null || !wallet.TrySpend(ClaimGoldCost))
            return false;

        settlement.OwnedByPlayer = true;
        settlement.OwnerFactionId = WellKnownFactionIds.Player;
        settlement.Reputation = Mathf.Max(settlement.Reputation, 50);
        GameplayEvents.RaiseSettlementClaimed(settlement.Id);
        GameplayEvents.RaiseToast($"Claimed {settlement.DisplayName}!");
        Changed?.Invoke();
        return true;
    }

    public bool TryBuyWood(SettlementRecord settlement, int amount = 1)
    {
        if (settlement == null || amount <= 0)
            return false;
        if (RefuseIfHostile(settlement))
            return false;
        int cost = BuyWoodPrice * amount;
        if (settlement.WoodStock < amount)
        {
            GameplayEvents.RaiseToast("Village has no wood.");
            return false;
        }

        var wallet = PlayerWallet.Instance;
        var inv = PlayerInventory.Instance;
        if (wallet == null || inv == null || !wallet.TrySpend(cost))
        {
            GameplayEvents.RaiseToast("Not enough gold.");
            return false;
        }

        settlement.WoodStock -= amount;
        inv.AddWood(amount);
        GameplayEvents.RaiseToast($"Bought {amount} wood (−{cost}g)");
        Changed?.Invoke();
        return true;
    }

    public bool TrySellWood(SettlementRecord settlement, int amount = 1)
    {
        if (settlement == null || amount <= 0)
            return false;
        if (RefuseIfHostile(settlement))
            return false;
        var inv = PlayerInventory.Instance;
        var wallet = PlayerWallet.Instance;
        if (inv == null || wallet == null || !inv.TrySpendWood(amount))
        {
            GameplayEvents.RaiseToast("No wood to sell.");
            return false;
        }

        int gain = SellWoodPrice * amount;
        settlement.WoodStock += amount;
        wallet.Add(gain);
        if (settlement.Reputation < 20)
            AddReputation(settlement.Id, 1);
        GameplayEvents.RaiseToast($"Sold {amount} wood (+{gain}g)");
        Changed?.Invoke();
        return true;
    }

    public bool TryBuyFood(SettlementRecord settlement, int amount = 1)
    {
        if (settlement == null || amount <= 0)
            return false;
        if (RefuseIfHostile(settlement))
            return false;
        int cost = BuyFoodPrice * amount;
        if (settlement.FoodStock < amount)
        {
            GameplayEvents.RaiseToast("Village has no food.");
            return false;
        }

        var wallet = PlayerWallet.Instance;
        var inv = PlayerInventory.Instance;
        if (wallet == null || inv == null || !wallet.TrySpend(cost))
        {
            GameplayEvents.RaiseToast("Not enough gold.");
            return false;
        }

        settlement.FoodStock -= amount;
        inv.AddFood(amount);
        GameplayEvents.RaiseToast($"Bought {amount} food (−{cost}g)");
        Changed?.Invoke();
        return true;
    }

    public bool TrySellFood(SettlementRecord settlement, int amount = 1)
    {
        if (settlement == null || amount <= 0)
            return false;
        if (RefuseIfHostile(settlement))
            return false;
        var inv = PlayerInventory.Instance;
        var wallet = PlayerWallet.Instance;
        if (inv == null || wallet == null || !inv.TrySpendFood(amount))
        {
            GameplayEvents.RaiseToast("No food to sell.");
            return false;
        }

        int gain = SellFoodPrice * amount;
        settlement.FoodStock += amount;
        wallet.Add(gain);
        GameplayEvents.RaiseToast($"Sold {amount} food (+{gain}g)");
        Changed?.Invoke();
        return true;
    }

    public void MarkCampCleared(int campId, bool grantReputation = true)
    {
        if (!_campById.TryGetValue(campId, out CampRecord camp) || camp.Cleared)
            return;

        camp.Cleared = true;
        if (grantReputation && camp.LinkedSettlementId >= 0)
            AddReputation(camp.LinkedSettlementId, 18, "Camp cleared");
        else
            GameplayEvents.RaiseToast("Bandit camp cleared!");
        Changed?.Invoke();
    }

    void OnEnemyKilled(Vector3 worldPosition, int _, bool byPlayerOrFollower)
    {
        CampRecord nearCamp = FindNearestUnclearedCamp(worldPosition, CampKillCreditRadius);

        // Player/follower kills within BanditKillRepPerimeterMargin of a village perimeter improve standing.
        if (byPlayerOrFollower)
        {
            SettlementRecord nearSettle = FindNearestSettlement(worldPosition, BanditKillRepRadius);
            if (nearSettle != null && !nearSettle.OwnedByPlayer)
                AddReputation(nearSettle.Id, BanditKillReputation, "Bandits defeated");
        }

        if (nearCamp == null)
            return;

        nearCamp.KilledNearCamp++;
        int needed = Mathf.Max(2, nearCamp.SpawnedBanditCount > 0 ? nearCamp.SpawnedBanditCount : 3);
        if (nearCamp.KilledNearCamp >= needed)
            MarkCampCleared(nearCamp.Id);
    }

    public SettlementRecord FindBestRespawnSettlement(Vector3 fromPos)
    {
        SettlementRecord bestOwned = null;
        float bestOwnedSq = float.MaxValue;
        SettlementRecord bestRep = null;
        int bestRepValue = int.MinValue;
        float bestRepSq = float.MaxValue;

        for (int i = 0; i < _settlements.Count; i++)
        {
            SettlementRecord s = _settlements[i];
            float sq = HorizontalDistanceSq(fromPos, s.Center);
            if (s.OwnedByPlayer && sq < bestOwnedSq)
            {
                bestOwnedSq = sq;
                bestOwned = s;
            }

            if (s.Reputation > bestRepValue || (s.Reputation == bestRepValue && sq < bestRepSq))
            {
                bestRepValue = s.Reputation;
                bestRepSq = sq;
                bestRep = s;
            }
        }

        if (bestOwned != null)
            return bestOwned;
        if (bestRep != null && bestRep.Reputation >= 0)
            return bestRep;
        return FindNearestSettlement(fromPos);
    }

    /// <summary>
    /// Deterministic ownership: ~2/3 of villages belong to a neutral kingdom, rest stay independent.
    /// </summary>
    static int AssignSettlementOwnerFaction(int worldSeed, int settlementIndex, int settlementCount)
    {
        if (settlementCount <= 0)
            return WellKnownFactionIds.Villager;

        uint seed = math.asuint(worldSeed) ^ ((uint)(settlementIndex + 1) * 2246822519u);
        var rng = new Unity.Mathematics.Random(math.max(1u, seed));

        // Keep at least one independent village when there are several.
        if (settlementCount >= 3 && settlementIndex == 0)
            return WellKnownFactionIds.Villager;

        if (rng.NextFloat() > 0.68f)
            return WellKnownFactionIds.Villager;

        int[] kingdoms = WellKnownFactionIds.NeutralKingdomIds;
        return kingdoms[rng.NextInt(0, kingdoms.Length)];
    }

    static float HorizontalDistanceSq(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
}
