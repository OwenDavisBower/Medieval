using System;
using System.Collections.Generic;
using Medieval.NpcMovement;
using Medieval.Npcs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Recruit, count, and disband player followers (DOTS).</summary>
public sealed class PartyManager : MonoBehaviour
{
    public static PartyManager Instance { get; private set; }

    public const int BaseRecruitCost = 15;
    public const int MaxPartySize = 14;
    public const int DisbandRefund = 8;

    const float RecruitBuildingSpawnHeight = 2.0f;
    const float RecruitBuildingExitMargin = 1.2f;
    const float RecruitLeapImpulseSpeed = 9.5f;
    const float RecruitLeapDurationSeconds = 0.55f;
    const float RecruitLeapGroundSnapSmoothTime = 0.4f;

    [SerializeField] float recruitSpawnRadiusMin = 1.4f;
    [SerializeField] float recruitSpawnRadiusMax = 3.6f;

    public event Action Changed;

    // Party food upkeep (consume rations / desertion) — off for now.
    static bool FoodUpkeepEnabled = false;
    float _foodUpkeepTimer;
    const float FoodUpkeepInterval = 40f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<PartyManager>() != null)
            return;

        var go = new GameObject("PartyManager");
        go.AddComponent<PartyManager>();
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

    void Update()
    {
        if (!FoodUpkeepEnabled)
            return;

        _foodUpkeepTimer += Time.deltaTime;
        if (_foodUpkeepTimer < FoodUpkeepInterval)
            return;
        _foodUpkeepTimer = 0f;

        int followers = CountLivingFollowers();
        if (followers <= 2)
            return;

        var inv = PlayerInventory.Instance;
        if (inv == null)
            return;

        int need = Mathf.Max(1, followers / 4);
        if (inv.TrySpendFood(need))
            return;

        // Starving party: dismiss one straggler.
        if (TryDisbandOneSilent())
            GameplayEvents.RaiseToast("Out of food — a follower deserted.");
    }

    bool TryDisbandOneSilent()
    {
        if (!TryFindFurthestFollower(out Entity follower, out EntityManager em))
            return false;
        NpcEntityDestroyUtility.DestroyNpcWithLinked(em, follower);
        Changed?.Invoke();
        return true;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public int CountLivingFollowers()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return 0;

        EntityManager em = world.EntityManager;
        using var q = em.CreateEntityQuery(
            ComponentType.ReadOnly<NpcProfile>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<NpcDeadTag>());

        using var entities = q.ToEntityArray(Unity.Collections.Allocator.Temp);
        int count = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            Entity e = entities[i];
            if (em.HasComponent<EscortNpcTag>(e))
                continue;
            if (em.GetComponentData<NpcProfile>(e).Role == NpcRole.Follower)
                count++;
        }

        return count;
    }

    /// <summary>Fills <paramref name="into"/> with living party followers (excludes escort NPCs).</summary>
    public void CopyLivingFollowers(List<Entity> into)
    {
        into.Clear();
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        EntityManager em = world.EntityManager;
        using var q = em.CreateEntityQuery(
            ComponentType.ReadOnly<NpcProfile>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<NpcDeadTag>());

        using var entities = q.ToEntityArray(Unity.Collections.Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            Entity e = entities[i];
            if (em.HasComponent<EscortNpcTag>(e))
                continue;
            if (em.GetComponentData<NpcProfile>(e).Role == NpcRole.Follower)
                into.Add(e);
        }
    }

    public static string GetDisplayName(EntityManager em, Entity npc)
    {
        if (em.Exists(npc) && em.HasComponent<NpcDisplayName>(npc))
        {
            var name = em.GetComponentData<NpcDisplayName>(npc).Value;
            if (name.Length > 0)
                return name.ToString();
        }

        return "Unknown";
    }

    public bool CanRecruit(SettlementRecord settlement, out string failReason)
    {
        failReason = null;
        if (!NpcSpawnApi.IsPrefabRegistryReady())
        {
            failReason = "Troops not ready yet.";
            return false;
        }

        if (CountLivingFollowers() >= MaxPartySize)
        {
            failReason = "Party is full.";
            return false;
        }

        int cost = SettlementService.Instance != null
            ? SettlementService.Instance.GetRecruitCost(settlement)
            : BaseRecruitCost;
        var wallet = PlayerWallet.Instance;
        if (wallet == null || wallet.Gold < cost)
        {
            failReason = $"Need {cost} gold to recruit.";
            return false;
        }

        return true;
    }

    public bool TryRecruit(SettlementRecord settlement)
    {
        if (!CanRecruit(settlement, out string fail))
        {
            GameplayEvents.RaiseToast(fail);
            return false;
        }

        int cost = SettlementService.Instance != null
            ? SettlementService.Instance.GetRecruitCost(settlement)
            : BaseRecruitCost;

        var wallet = PlayerWallet.Instance;
        if (wallet == null || !wallet.TrySpend(cost))
            return false;

        Transform player = PlayerReference.TryGetTransform();
        if (player == null)
        {
            wallet.Add(cost);
            return false;
        }

        Vector3 leader = player.position;
        if (!TryResolveRecruitSpawn(settlement, leader, out Vector3 spawnPos, out float3 landing, out float3 outward,
                out quaternion face, out bool emergeFromBuilding))
        {
            wallet.Add(cost);
            GameplayEvents.RaiseToast("Recruit failed.");
            return false;
        }

        NpcWeaponClass wc = UnityEngine.Random.value < 0.5f ? NpcWeaponClass.Melee : NpcWeaponClass.Ranged;
        Entity e = NpcSpawnApi.SpawnFollower(spawnPos, face, 1f, wc);
        if (e == Entity.Null)
        {
            wallet.Add(cost);
            GameplayEvents.RaiseToast("Recruit failed.");
            return false;
        }

        var world = World.DefaultGameObjectInjectionWorld;
        EntityManager em = world.EntityManager;
        if (emergeFromBuilding)
        {
            NpcMovementApi.StartEmergeLeap(
                em, e, landing, outward, RecruitLeapImpulseSpeed, RecruitLeapDurationSeconds,
                RecruitLeapGroundSnapSmoothTime);
        }
        else
        {
            NpcMovementApi.SetAnchorPosition(em, e, new float3(leader.x, leader.y, leader.z));
        }

        if (settlement != null && SettlementService.Instance != null)
            SettlementService.Instance.AddReputation(settlement.Id, 1);

        string recruitName = GetDisplayName(em, e);
        GameplayEvents.RaiseToast($"Recruited {recruitName} (−{cost}g)");
        Changed?.Invoke();
        return true;
    }

    bool TryResolveRecruitSpawn(
        SettlementRecord settlement,
        Vector3 leader,
        out Vector3 spawnPos,
        out float3 landing,
        out float3 outward,
        out quaternion face,
        out bool emergeFromBuilding)
    {
        spawnPos = default;
        landing = default;
        outward = default;
        face = quaternion.identity;
        emergeFromBuilding = false;

        if (settlement != null &&
            SettlementBuilder.TryFindBySettlementId(settlement.Id, out SettlementBuilder builder) &&
            builder.TryPickRecruitBuilding(leader, out Vector3 buildingPos, out float footprintRadius))
        {
            outward = new float3(leader.x - buildingPos.x, 0f, leader.z - buildingPos.z);
            if (math.lengthsq(outward) < 1e-4f)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                outward = new float3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            }

            outward = math.normalize(outward);
            float exitDistance = footprintRadius + RecruitBuildingExitMargin;
            landing = new float3(buildingPos.x, buildingPos.y, buildingPos.z) + outward * exitDistance;
            landing = new float3(
                landing.x,
                TerrainSpawnUtility.GetWorldPositionOnTerrain(new Vector3(landing.x, landing.y, landing.z)).y,
                landing.z);

            spawnPos = new Vector3(buildingPos.x, buildingPos.y + RecruitBuildingSpawnHeight, buildingPos.z);
            face = quaternion.LookRotationSafe(outward, new float3(0f, 1f, 0f));
            emergeFromBuilding = true;
            return true;
        }

        // Fallback when the village instance is unloaded or has no structures yet.
        float fallbackAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float rad = UnityEngine.Random.Range(recruitSpawnRadiusMin, recruitSpawnRadiusMax);
        Vector3 offset = new Vector3(Mathf.Sin(fallbackAngle), 0f, Mathf.Cos(fallbackAngle)) * rad;
        Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(leader + offset);
        spawnPos = pos;
        landing = new float3(pos.x, pos.y, pos.z);
        outward = new float3(Mathf.Sin(fallbackAngle), 0f, Mathf.Cos(fallbackAngle));
        return true;
    }

    public bool TryDisbandOne()
    {
        if (!TryFindFurthestFollower(out Entity follower, out EntityManager em))
        {
            GameplayEvents.RaiseToast("No followers to dismiss.");
            return false;
        }

        string dismissedName = GetDisplayName(em, follower);
        NpcEntityDestroyUtility.DestroyNpcWithLinked(em, follower);
        var wallet = PlayerWallet.Instance;
        wallet?.Add(DisbandRefund);
        GameplayEvents.RaiseToast($"Dismissed {dismissedName} (+{DisbandRefund}g)");
        Changed?.Invoke();
        return true;
    }

    public int DisbandAll()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return 0;

        EntityManager em = world.EntityManager;
        using var q = em.CreateEntityQuery(
            ComponentType.ReadOnly<NpcProfile>(),
            ComponentType.Exclude<NpcDeadTag>());

        using var entities = q.ToEntityArray(Unity.Collections.Allocator.Temp);
        var toDestroy = new List<Entity>(entities.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            Entity e = entities[i];
            if (em.HasComponent<EscortNpcTag>(e))
                continue;
            if (em.GetComponentData<NpcProfile>(e).Role == NpcRole.Follower)
                toDestroy.Add(e);
        }

        for (int i = 0; i < toDestroy.Count; i++)
            NpcEntityDestroyUtility.DestroyNpcWithLinked(em, toDestroy[i]);

        if (toDestroy.Count > 0)
            Changed?.Invoke();
        return toDestroy.Count;
    }

    /// <summary>
    /// Spawns a villager that orbits the player like a party follower for the escort quest.
    /// </summary>
    public Entity SpawnEscortFollower(Vector3 nearPlayer)
    {
        if (!NpcSpawnApi.IsPrefabRegistryReady())
            return Entity.Null;

        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float rad = UnityEngine.Random.Range(recruitSpawnRadiusMin, recruitSpawnRadiusMax);
        Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * rad;
        Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(nearPlayer + offset);
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            pos = hit.position;

        Entity e = NpcSpawnApi.SpawnVillager(pos, quaternion.identity, 1f);
        if (e == Entity.Null)
            return Entity.Null;

        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;

        if (em.HasComponent<NpcChopWoodTaskTag>(e))
            em.RemoveComponent<NpcChopWoodTaskTag>(e);

        NpcMovementApi.ConfigureAsPlayerFollower(em, e, seeksCombat: false);
        if (!em.HasComponent<EscortNpcTag>(e))
            em.AddComponent<EscortNpcTag>(e);

        // Travel with the player party for hostility / friendly-fire purposes.
        if (em.HasComponent<NpcFactionId>(e))
            em.SetComponentData(e, new NpcFactionId { Value = WellKnownFactionIds.Player });
        else
            em.AddComponentData(e, new NpcFactionId { Value = WellKnownFactionIds.Player });
        // Keep villager cloth tint even though combat faction is Player.
        NpcFactionClothingUtility.ApplyClothingColorForFaction(em, e, WellKnownFactionIds.Villager);

        NpcMovementApi.SetAnchorPosition(em, e, new float3(nearPlayer.x, nearPlayer.y, nearPlayer.z));
        return e;
    }

    bool TryFindFurthestFollower(out Entity follower, out EntityManager em)
    {
        follower = Entity.Null;
        em = default;
        Transform player = PlayerReference.TryGetTransform();
        World world = World.DefaultGameObjectInjectionWorld;
        if (player == null || world == null || !world.IsCreated)
            return false;

        em = world.EntityManager;
        using var q = em.CreateEntityQuery(
            ComponentType.ReadOnly<NpcProfile>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<NpcDeadTag>(),
            ComponentType.Exclude<EscortNpcTag>());

        using var entities = q.ToEntityArray(Unity.Collections.Allocator.Temp);
        float3 playerPos = new float3(player.position.x, player.position.y, player.position.z);
        float best = -1f;
        for (int i = 0; i < entities.Length; i++)
        {
            Entity e = entities[i];
            if (em.GetComponentData<NpcProfile>(e).Role != NpcRole.Follower)
                continue;
            float3 p = em.GetComponentData<LocalTransform>(e).Position;
            float dist = math.distancesq(p, playerPos);
            if (dist > best)
            {
                best = dist;
                follower = e;
            }
        }

        return follower != Entity.Null;
    }
}
