using Unity.Entities;
using UnityEngine;

namespace ProjectDawn.Animation.Hybrid
{
    public abstract class EntityBehaviour : MonoBehaviour
    {
        protected Entity m_Entity;

        public World World => World.DefaultGameObjectInjectionWorld;
        public bool IsCreated => m_Entity != Entity.Null;

        public Entity GetOrCreateEntity()
        {
            if (m_Entity != Entity.Null)
                return m_Entity;

            var world = World.DefaultGameObjectInjectionWorld;
            var manager = world.EntityManager;

            m_Entity = manager.CreateEntity();
            manager.AddComponentData(m_Entity, new EntityGuid(gameObject.GetInstanceID(), 0, 0, 0));
            manager.SetName(m_Entity, name);

            return m_Entity;
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
                world.EntityManager.DestroyEntity(m_Entity);

            m_Entity = Entity.Null;
        }

        protected void OnEnable()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
                return;

            var manager = world.EntityManager;
            manager.SetEnabled(m_Entity, true);
        }

        protected void OnDisable()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
                return;

            var manager = world.EntityManager;
            manager.SetEnabled(m_Entity, false);
        }
    }
}
