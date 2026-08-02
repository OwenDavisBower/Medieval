using System;
using System.Collections.Generic;
using Medieval.NpcMovement;
using Medieval.Npcs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>Recruit, count, and disband player followers (DOTS).</summary>
public sealed class PartyManager : MonoBehaviour
{
    public static PartyManager Instance { get; private set; }

    public const int BaseRecruitCost = 25;
    public const int MaxPartySize = 14;
    public const int DisbandRefund = 8;

    [SerializeField] float recruitSpawnRadiusMin = 1.4f;
    [SerializeField] float recruitSpawnRadiusMax = 3.6f;

    public event Action Changed;

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
            if (em.GetComponentData<NpcProfile>(entities[i]).Role == NpcRole.Follower)
                count++;
        }

        return count;
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
        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float rad = UnityEngine.Random.Range(recruitSpawnRadiusMin, recruitSpawnRadiusMax);
        Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * rad;
        Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(leader + offset);

        NpcWeaponClass wc = UnityEngine.Random.value < 0.5f ? NpcWeaponClass.Melee : NpcWeaponClass.Ranged;
        Entity e = NpcSpawnApi.SpawnFollower(pos, quaternion.identity, 1f, wc);
        if (e == Entity.Null)
        {
            wallet.Add(cost);
            GameplayEvents.RaiseToast("Recruit failed.");
            return false;
        }

        var world = World.DefaultGameObjectInjectionWorld;
        NpcMovementApi.SetAnchorPosition(world.EntityManager, e,
            new float3(leader.x, leader.y, leader.z));

        if (settlement != null && SettlementService.Instance != null)
            SettlementService.Instance.AddReputation(settlement.Id, 1);

        GameplayEvents.RaiseToast($"Recruited a fighter (−{cost}g)");
        Changed?.Invoke();
        return true;
    }

    public bool TryDisbandOne()
    {
        if (!TryFindFurthestFollower(out Entity follower, out EntityManager em))
        {
            GameplayEvents.RaiseToast("No followers to dismiss.");
            return false;
        }

        NpcEntityDestroyUtility.DestroyNpcWithLinked(em, follower);
        var wallet = PlayerWallet.Instance;
        wallet?.Add(DisbandRefund);
        GameplayEvents.RaiseToast($"Dismissed a follower (+{DisbandRefund}g)");
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
            if (em.GetComponentData<NpcProfile>(entities[i]).Role == NpcRole.Follower)
                toDestroy.Add(entities[i]);
        }

        for (int i = 0; i < toDestroy.Count; i++)
            NpcEntityDestroyUtility.DestroyNpcWithLinked(em, toDestroy[i]);

        if (toDestroy.Count > 0)
            Changed?.Invoke();
        return toDestroy.Count;
    }

    public Entity SpawnEscortFollower(Vector3 nearPlayer)
    {
        if (!NpcSpawnApi.IsPrefabRegistryReady())
            return Entity.Null;

        Vector3 pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(nearPlayer + UnityEngine.Random.insideUnitSphere * 2f);
        Entity e = NpcSpawnApi.SpawnFollower(pos, quaternion.identity, 1f, NpcWeaponClass.Melee);
        if (e == Entity.Null)
            return Entity.Null;

        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
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
            ComponentType.Exclude<NpcDeadTag>());

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
