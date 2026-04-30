using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.Npcs
{
    /// <summary>Spawns DOTS NPCs using Entities Graphics prefabs.</summary>
    public static class NpcSpawnApi
    {
        public static bool SpawnFollower(Vector3 worldPosition, quaternion worldRotation, float uniformScale = 1f)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            EntityManager em = world.EntityManager;
            if (!TryGetPrefab(em, NpcPrefabKind.Follower, out Entity prefab))
                return false;

            float3 pos = new float3(worldPosition.x, worldPosition.y, worldPosition.z);

            Entity e = em.Instantiate(prefab);
#if UNITY_EDITOR
            em.SetName(e, "FollowerNpc");
#endif
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(pos, worldRotation, uniformScale));
            return true;
        }

        public static bool SpawnBandit(Vector3 worldPosition, quaternion worldRotation, float uniformScale = 1f)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            EntityManager em = world.EntityManager;
            if (!TryGetPrefab(em, NpcPrefabKind.Bandit, out Entity prefab))
                return false;

            float3 pos = new float3(worldPosition.x, worldPosition.y, worldPosition.z);

            Entity e = em.Instantiate(prefab);
#if UNITY_EDITOR
            em.SetName(e, "BanditNpc");
#endif
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(pos, worldRotation, uniformScale));
            return true;
        }

        public static bool SpawnVillager(Vector3 worldPosition, quaternion worldRotation, float uniformScale = 1f)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            EntityManager em = world.EntityManager;
            if (!TryGetPrefab(em, NpcPrefabKind.Villager, out Entity prefab))
                return false;

            float3 pos = new float3(worldPosition.x, worldPosition.y, worldPosition.z);

            Entity e = em.Instantiate(prefab);
#if UNITY_EDITOR
            em.SetName(e, "VillagerNpc");
#endif
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(pos, worldRotation, uniformScale));
            return true;
        }

        enum NpcPrefabKind : byte
        {
            Follower = 0,
            Bandit = 1,
            Villager = 2
        }

        static bool TryGetPrefab(EntityManager em, NpcPrefabKind kind, out Entity prefab)
        {
            prefab = Entity.Null;

            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<NpcPrefabRegistry>());
            if (q.CalculateEntityCount() == 0)
                return false;

            Entity reg = q.GetSingletonEntity();
            NpcPrefabRegistry data = em.GetComponentData<NpcPrefabRegistry>(reg);
            switch (kind)
            {
                case NpcPrefabKind.Follower:
                    prefab = data.FollowerPrefab;
                    break;
                case NpcPrefabKind.Bandit:
                    prefab = data.BanditPrefab;
                    break;
                case NpcPrefabKind.Villager:
                    prefab = data.VillagerPrefab;
                    break;
                default:
                    prefab = Entity.Null;
                    break;
            }

            return prefab != Entity.Null && em.Exists(prefab);
        }
    }
}

