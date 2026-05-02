using System.Collections.Generic;
using Medieval.Npcs;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Mirrors procedural <see cref="TreeInstanceData"/> positions into ECS so NPC tasks can query nearest trees.
/// </summary>
public static class NpcWorldTreesDotsBridge
{
    public static void SyncStreamingTrees(List<TreeInstanceData> trees)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        EntityManager em = world.EntityManager;
        Entity singleton = GetOrCreateSingleton(em);
        DynamicBuffer<StreamingTreePosition> buf = em.GetBuffer<StreamingTreePosition>(singleton);
        buf.Clear();
        if (trees == null)
            return;

        for (int i = 0; i < trees.Count; i++)
            buf.Add(new StreamingTreePosition { Position = trees[i].Position });
    }

    static Entity GetOrCreateSingleton(EntityManager em)
    {
        using EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<WorldStreamingTreesSingletonTag>());
        if (!q.IsEmpty)
            return q.GetSingletonEntity();

        Entity e = em.CreateEntity();
        em.AddComponent<WorldStreamingTreesSingletonTag>(e);
        em.AddBuffer<StreamingTreePosition>(e);
#if UNITY_EDITOR
        em.SetName(e, "WorldStreamingTreesSingleton");
#endif
        return e;
    }
}
