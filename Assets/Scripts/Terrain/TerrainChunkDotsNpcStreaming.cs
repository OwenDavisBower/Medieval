#nullable enable
using System.Collections.Generic;
using Medieval.NpcMovement;
using Medieval.Npcs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// When logical terrain chunks leave the streaming window, disables DOTS NPC roots (and linked entities)
/// for bandits and villagers in that chunk; re-enables when the chunk returns. Matches
/// <see cref="TerrainChunkCharacterStreaming"/> (followers are not culled).
/// </summary>
public static class TerrainChunkDotsNpcStreaming
{
    static readonly Dictionary<Vector2Int, HashSet<Entity>> DisabledNpcRootsByChunk = new();

    public static void DisableNpcsInChunk(Vector2Int chunk, TerrainGenerator terrain)
    {
        World? world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || terrain == null)
            return;

        EntityManager em = world.EntityManager;
        Vector3 worldOrigin = terrain.transform.position;
        float worldSize = terrain.worldSize;
        int axis = Mathf.Max(1, terrain.chunkCount);

        using var query = em.CreateEntityQuery(
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<NpcProfile>(),
            ComponentType.ReadOnly<NpcMovementTag>());
        using var entities = query.ToEntityArray(Allocator.Temp);
        using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        using var profiles = query.ToComponentDataArray<NpcProfile>(Allocator.Temp);

        if (!DisabledNpcRootsByChunk.TryGetValue(chunk, out HashSet<Entity>? bucket))
        {
            bucket = new HashSet<Entity>();
            DisabledNpcRootsByChunk[chunk] = bucket;
        }

        for (int i = 0; i < entities.Length; i++)
        {
            NpcRole role = profiles[i].Role;
            if (role != NpcRole.Bandit && role != NpcRole.Villager)
                continue;

            float3 p = transforms[i].Position;
            Vector2Int home = TerrainLogicalChunkWindow.WorldXZToChunk(worldOrigin, worldSize, axis, p.x, p.z);
            if (home.x != chunk.x || home.y != chunk.y)
                continue;

            Entity root = entities[i];
            if (!em.Exists(root))
                continue;

            SetNpcTreeEnabled(em, root, false);
            bucket.Add(root);
        }
    }

    public static void EnableNpcsForChunk(Vector2Int chunk)
    {
        World? world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (!DisabledNpcRootsByChunk.TryGetValue(chunk, out HashSet<Entity>? bucket))
            return;

        EntityManager em = world.EntityManager;
        foreach (Entity root in bucket)
        {
            if (em.Exists(root))
                SetNpcTreeEnabled(em, root, true);
        }

        DisabledNpcRootsByChunk.Remove(chunk);
    }

    static void SetNpcTreeEnabled(EntityManager em, Entity root, bool enabled)
    {
        if (!em.Exists(root))
            return;

        if (em.HasBuffer<LinkedEntityGroup>(root))
        {
            var buffer = em.GetBuffer<LinkedEntityGroup>(root);
            for (int i = 0; i < buffer.Length; i++)
            {
                Entity e = buffer[i].Value;
                if (em.Exists(e))
                    em.SetEnabled(e, enabled);
            }

            return;
        }

        em.SetEnabled(root, enabled);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        DisabledNpcRootsByChunk.Clear();
    }
}
