using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.Projectiles
{
    /// <summary>Spawns DOTS-driven projectiles with a classic mesh <see cref="Transform"/> visual.</summary>
    public static class ProjectileSpawnApi
    {
        const float DefaultHitRadius = 0.08f;

        /// <summary>
        /// Spawns a projectile. Disables physics colliders/rigidbodies on the visual so simulation uses ECS only.
        /// </summary>
        public static void Spawn(
            GameObject visualPrefab,
            Vector3 worldOrigin,
            Vector3 velocity,
            float damage,
            float maxLifetimeSeconds,
            Transform shooterRoot,
            Collider ownerCollider,
            float hitRadius = DefaultHitRadius)
        {
            if (visualPrefab == null)
                return;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            GameObject visual = Object.Instantiate(visualPrefab, worldOrigin, Quaternion.identity);
            DisablePhysicsOnVisual(visual);

            if (velocity.sqrMagnitude > 0.01f)
            {
                Vector3 forward = velocity.normalized;
                if (forward.sqrMagnitude > 0.99f)
                    visual.transform.rotation = Quaternion.LookRotation(forward);
            }

            Transform vt = visual.transform;
            Vector3 lossy = vt.lossyScale;
            float uniform = math.max(lossy.x, math.max(lossy.y, lossy.z));
            if (uniform < 1e-5f)
                uniform = 1f;

            quaternion rot = new quaternion(vt.rotation.x, vt.rotation.y, vt.rotation.z, vt.rotation.w);
            float3 pos = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z);

            EntityManager em = world.EntityManager;
            Entity e = em.CreateEntity();

#if UNITY_EDITOR
            em.SetName(e, "ProjectileArrow");
#endif

            em.AddComponent<ProjectileTag>(e);
            em.AddComponentData(e, LocalTransform.FromPositionRotationScale(pos, rot, uniform));
            em.AddComponentData(e, new ProjectileVelocity { Value = new float3(velocity.x, velocity.y, velocity.z) });
            em.AddComponentData(e, new ProjectileLifetime { SecondsRemaining = maxLifetimeSeconds });
            em.AddComponentData(e, new ProjectileDamage { Amount = damage });
            int shooterId = shooterRoot != null ? shooterRoot.root.GetInstanceID() : 0;
            em.AddComponentData(e, new ProjectileShooterId { RootInstanceId = shooterId });
            em.AddComponentData(e, new ProjectileHitSphere { Radius = hitRadius });
            em.AddComponentData(e, new ProjectileMotionState { PreviousPosition = pos });

            em.AddComponentObject(e, new ProjectileVisualCompanion
            {
                Visual = vt,
                ShooterRoot = shooterRoot != null ? shooterRoot.root : null,
                OwnerCollider = ownerCollider
            });
        }

        static void DisablePhysicsOnVisual(GameObject root)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>())
                c.enabled = false;

            foreach (var rb in root.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}
