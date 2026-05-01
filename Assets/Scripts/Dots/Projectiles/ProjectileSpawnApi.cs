using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.Projectiles
{
    /// <summary>Spawns DOTS-driven projectiles using an Entities Graphics prefab.</summary>
    public static class ProjectileSpawnApi
    {
        const float DefaultHitRadius = 0.08f;

        /// <summary>Spawns a projectile entity and sets initial state.</summary>
        public static void Spawn(
            Vector3 worldOrigin,
            Vector3 velocity,
            float damage,
            float maxLifetimeSeconds,
            Transform shooterRoot,
            Collider ownerCollider,
            float hitRadius = DefaultHitRadius)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            EntityManager em = world.EntityManager;
            if (!TryGetArrowPrototypePrefab(em, out Entity prefab))
                return;

            float3 pos = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z);
            quaternion rot = quaternion.identity;
            if (velocity.sqrMagnitude > 0.01f)
            {
                float3 v = new float3(velocity.x, velocity.y, velocity.z);
                rot = quaternion.LookRotationSafe(math.normalize(v), new float3(0f, 1f, 0f));
            }

            Entity e = em.Instantiate(prefab);

#if UNITY_EDITOR
            em.SetName(e, "ProjectileArrow");
#endif

            // The prefab carries render components; we only need to author simulation state.
            em.AddComponent<ProjectileTag>(e);
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(pos, rot, 1f));
            em.AddComponentData(e, new ProjectileVelocity { Value = new float3(velocity.x, velocity.y, velocity.z) });
            em.AddComponentData(e, new ProjectileLifetime { SecondsRemaining = maxLifetimeSeconds });
            em.AddComponentData(e, new ProjectileDamage { Amount = damage });
            int shooterId = shooterRoot != null ? shooterRoot.root.GetInstanceID() : 0;
            em.AddComponentData(e, new ProjectileShooterId { RootInstanceId = shooterId });
            int ownerColliderId = ownerCollider != null ? ownerCollider.GetInstanceID() : 0;
            em.AddComponentData(e, new ProjectileOwnerColliderId { ColliderInstanceId = ownerColliderId });
            em.AddComponentData(e, new ProjectileHitSphere { Radius = hitRadius });
            em.AddComponentData(e, new ProjectileMotionState { PreviousPosition = pos });
        }

        /// <summary>Spawns a projectile fired by a DOTS NPC root (no GameObject shooter); uses ECS hit exclusion.</summary>
        public static void SpawnFromDotsNpcShooter(
            Vector3 worldOrigin,
            Vector3 velocity,
            float damage,
            float maxLifetimeSeconds,
            Entity shooterNpcRoot,
            float hitRadius = DefaultHitRadius)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            EntityManager em = world.EntityManager;
            if (!TryGetArrowPrototypePrefab(em, out Entity prefab))
                return;

            float3 pos = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z);
            quaternion rot = quaternion.identity;
            if (velocity.sqrMagnitude > 0.01f)
            {
                float3 v = new float3(velocity.x, velocity.y, velocity.z);
                rot = quaternion.LookRotationSafe(math.normalize(v), new float3(0f, 1f, 0f));
            }

            Entity e = em.Instantiate(prefab);

#if UNITY_EDITOR
            em.SetName(e, "ProjectileArrow");
#endif

            em.AddComponent<ProjectileTag>(e);
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(pos, rot, 1f));
            em.AddComponentData(e, new ProjectileVelocity { Value = new float3(velocity.x, velocity.y, velocity.z) });
            em.AddComponentData(e, new ProjectileLifetime { SecondsRemaining = maxLifetimeSeconds });
            em.AddComponentData(e, new ProjectileDamage { Amount = damage });
            em.AddComponentData(e, new ProjectileShooterId { RootInstanceId = 0 });
            em.AddComponentData(e, new ProjectileOwnerColliderId { ColliderInstanceId = 0 });
            em.AddComponentData(e, new ProjectileShooterNpcRoot { Value = shooterNpcRoot });
            em.AddComponentData(e, new ProjectileHitSphere { Radius = hitRadius });
            em.AddComponentData(e, new ProjectileMotionState { PreviousPosition = pos });
        }

        /// <summary>
        /// Records projectile spawn on <paramref name="ecb"/>; safe during entity queries/iterators.
        /// Caller must <c>Playback</c> the buffer after iteration (same frame).
        /// </summary>
        public static void SpawnFromDotsNpcShooterDeferred(
            ref EntityCommandBuffer ecb,
            Vector3 worldOrigin,
            Vector3 velocity,
            float damage,
            float maxLifetimeSeconds,
            Entity shooterNpcRoot,
            float hitRadius = DefaultHitRadius)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            EntityManager em = world.EntityManager;
            if (!TryGetArrowPrototypePrefab(em, out Entity prefab))
                return;

            float3 pos = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z);
            quaternion rot = quaternion.identity;
            if (velocity.sqrMagnitude > 0.01f)
            {
                float3 v = new float3(velocity.x, velocity.y, velocity.z);
                rot = quaternion.LookRotationSafe(math.normalize(v), new float3(0f, 1f, 0f));
            }

            Entity e = ecb.Instantiate(prefab);
            ecb.AddComponent<ProjectileTag>(e);
            ecb.SetComponent(e, LocalTransform.FromPositionRotationScale(pos, rot, 1f));
            ecb.AddComponent(e, new ProjectileVelocity { Value = new float3(velocity.x, velocity.y, velocity.z) });
            ecb.AddComponent(e, new ProjectileLifetime { SecondsRemaining = maxLifetimeSeconds });
            ecb.AddComponent(e, new ProjectileDamage { Amount = damage });
            ecb.AddComponent(e, new ProjectileShooterId { RootInstanceId = 0 });
            ecb.AddComponent(e, new ProjectileOwnerColliderId { ColliderInstanceId = 0 });
            ecb.AddComponent(e, new ProjectileShooterNpcRoot { Value = shooterNpcRoot });
            ecb.AddComponent(e, new ProjectileHitSphere { Radius = hitRadius });
            ecb.AddComponent(e, new ProjectileMotionState { PreviousPosition = pos });
        }

        static bool TryGetArrowPrototypePrefab(EntityManager em, out Entity prefab)
        {
            prefab = Entity.Null;

            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<ProjectilePrefabRegistry>());
            if (q.CalculateEntityCount() == 0)
                return false;

            Entity reg = q.GetSingletonEntity();
            ProjectilePrefabRegistry data = em.GetComponentData<ProjectilePrefabRegistry>(reg);
            if (data.ArrowPrefab == Entity.Null || !em.Exists(data.ArrowPrefab))
                return false;

            prefab = data.ArrowPrefab;
            return true;
        }
    }
}
