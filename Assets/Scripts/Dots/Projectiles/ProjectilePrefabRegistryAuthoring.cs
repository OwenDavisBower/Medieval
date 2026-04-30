using Unity.Entities;
using UnityEngine;

namespace Medieval.Projectiles
{
    public struct ProjectilePrefabRegistry : IComponentData
    {
        public Entity ArrowPrefab;
    }

    /// <summary>
    /// Scene authoring that bakes a referenced arrow GameObject prefab into an entity prefab for Entities Graphics.
    /// Put this in a baked scene/subscene and assign <see cref="arrowPrototypePrefab"/>.
    /// </summary>
    public sealed class ProjectilePrefabRegistryAuthoring : MonoBehaviour
    {
        [SerializeField] GameObject arrowPrototypePrefab;

        class Baker : Baker<ProjectilePrefabRegistryAuthoring>
        {
            public override void Bake(ProjectilePrefabRegistryAuthoring authoring)
            {
                Entity e = GetEntity(TransformUsageFlags.None);

                Entity arrow = Entity.Null;
                if (authoring.arrowPrototypePrefab != null)
                    arrow = GetEntity(authoring.arrowPrototypePrefab, TransformUsageFlags.Renderable);

                AddComponent(e, new ProjectilePrefabRegistry
                {
                    ArrowPrefab = arrow
                });
            }
        }
    }
}

