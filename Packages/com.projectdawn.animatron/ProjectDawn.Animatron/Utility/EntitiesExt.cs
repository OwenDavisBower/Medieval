using Unity.Entities;

namespace ProjectDawn.Animation
{
    public static class EntitiesExt
    {
        public static bool TryGetComponentObject<T>(this EntityManager entityManager, Entity entity, out T obj)
        {
            if (!entityManager.HasComponent<T>(entity))
            {
                obj = default;
                return false;
            }

            obj = entityManager.GetComponentObject<T>(entity);
            return true;
        }

        public static bool TryGetComponent<T>(this EntityManager entityManager, Entity entity, out T obj) where T : unmanaged, IComponentData
        {
            if (!entityManager.HasComponent<T>(entity))
            {
                obj = default;
                return false;
            }

            obj = entityManager.GetComponentData<T>(entity);
            return true;
        }

        public static bool TryGetSharedComponentManaged<T>(this EntityManager entityManager, Entity entity, out T obj) where T : struct, ISharedComponentData
        {
            if (!entityManager.HasComponent<T>(entity))
            {
                obj = default;
                return false;
            }

            obj = entityManager.GetSharedComponentManaged<T>(entity);
            return true;
        }

        public static void CreateEntityWithComponent<T>(this EntityCommandBuffer ecb, T component) where T : unmanaged, IComponentData
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, component);
        }

        public static void CreateEntityWithComponent<T1, T2>(this EntityCommandBuffer ecb, T1 component1, T2 component2)
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, component1);
            ecb.AddComponent(entity, component2);
        }

        public static void CreateEntityWithComponentManaged<T>(this EntityCommandBuffer ecb, T component) where T : class
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, component);
        }

        public static Entity CreateEntityWithComponentManaged<T>(this EntityManager ecb, T component) where T : class, IComponentData, new()
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponentData(entity, component);
            return entity;
        }

        public static void CreateEntityWithComponent<T>(this EntityManager ecb, T component) where T : unmanaged, IComponentData
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponentData(entity, component);
        }

        public static T CreateEntityWithComponentObject<T>(this EntityManager ecb, T component) where T : UnityEngine.Object
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponentObject(entity, component);
            return component;
        }

        public static T GetSingleton<T>(this EntityManager ecb) where T : unmanaged, IComponentData
        {
            return ecb.GetComponentData<T>(ecb.CreateEntityQuery(typeof(T)).GetSingletonEntity());
        }

        public static T GetSingletonManaged<T>(this EntityManager ecb) where T : class, IComponentData, new()
        {
            return ecb.GetComponentData<T>(ecb.CreateEntityQuery(typeof(T)).GetSingletonEntity());
        }

        public static bool TryGetSingletonManaged<T>(this EntityManager ecb, out T result) where T : class, IComponentData, new()
        {
            var query = ecb.CreateEntityQuery(typeof(T));
            if (!query.HasSingleton<T>())
            {
                result = null;
                return false;
            }
            result = ecb.GetComponentData<T>(query.GetSingletonEntity());
            return true;
        }

        public static T GetSingletonObject<T>(this EntityManager ecb)
        {
            return ecb.GetComponentObject<T>(ecb.CreateEntityQuery(typeof(T)).GetSingletonEntity());
        }
    }
}