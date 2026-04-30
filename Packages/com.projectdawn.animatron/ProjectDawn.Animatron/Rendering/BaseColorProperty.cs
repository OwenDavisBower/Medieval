using ProjectDawn.Rendering;
using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

namespace ProjectDawn.Animation.Hybrid
{
    public class BaseColorProperty : MonoBehaviour
    {
        public Color Value;

        void Awake()
        {
            var entityBehaviour = GetComponent<RenderMeshArrayAuthoring>();
            var entity = entityBehaviour.GetOrCreateEntity();
            World.DefaultGameObjectInjectionWorld.EntityManager.AddComponentData(entity, new Animation.BaseColorProperty
            {
                Value = Value,
            });
        }

        void OnDestroy()
        {
            if (World.DefaultGameObjectInjectionWorld == null)
                return;
            var entityBehaviour = GetComponent<RenderMeshArrayAuthoring>();
            var entity = entityBehaviour.GetOrCreateEntity();
            World.DefaultGameObjectInjectionWorld.EntityManager.RemoveComponent<Animation.BaseColorProperty>(entity);
        }

        class BaseColorPropertyBaker : Unity.Entities.Baker<BaseColorProperty>
        {
            public override void Bake(BaseColorProperty authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(entity, new Animation.BaseColorProperty { Value = authoring.Value });
            }
        }
    }
}

namespace ProjectDawn.Animation
{
    [MaterialProperty("_BaseColor")]
    public struct BaseColorProperty : IComponentData
    {
        public Color Value;
    }
}