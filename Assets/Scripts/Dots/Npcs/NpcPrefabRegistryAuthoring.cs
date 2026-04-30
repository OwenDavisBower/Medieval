using Unity.Entities;
using UnityEngine;

namespace Medieval.Npcs
{
    public struct NpcPrefabRegistry : IComponentData
    {
        public Entity FollowerPrefab;
        public Entity BanditPrefab;
        public Entity VillagerPrefab;
    }

    /// <summary>
    /// Scene authoring that bakes referenced NPC GameObject prefabs into entity prefabs for Entities Graphics.
    /// Put this in a baked scene/subscene and assign the prototype prefabs.
    /// </summary>
    public sealed class NpcPrefabRegistryAuthoring : MonoBehaviour
    {
        [SerializeField] GameObject followerPrototypePrefab;
        [SerializeField] GameObject banditPrototypePrefab;
        [SerializeField] GameObject villagerPrototypePrefab;

        class Baker : Baker<NpcPrefabRegistryAuthoring>
        {
            public override void Bake(NpcPrefabRegistryAuthoring authoring)
            {
                Entity e = GetEntity(TransformUsageFlags.None);

                Entity follower = Entity.Null;
                if (authoring.followerPrototypePrefab != null)
                    follower = GetEntity(authoring.followerPrototypePrefab, TransformUsageFlags.Dynamic | TransformUsageFlags.Renderable);

                Entity bandit = Entity.Null;
                if (authoring.banditPrototypePrefab != null)
                    bandit = GetEntity(authoring.banditPrototypePrefab, TransformUsageFlags.Dynamic | TransformUsageFlags.Renderable);

                Entity villager = Entity.Null;
                if (authoring.villagerPrototypePrefab != null)
                    villager = GetEntity(authoring.villagerPrototypePrefab, TransformUsageFlags.Dynamic | TransformUsageFlags.Renderable);

                AddComponent(e, new NpcPrefabRegistry
                {
                    FollowerPrefab = follower,
                    BanditPrefab = bandit,
                    VillagerPrefab = villager
                });
            }
        }
    }
}

