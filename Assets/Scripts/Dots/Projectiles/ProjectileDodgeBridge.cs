using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Medieval.Projectiles
{
    /// <summary>GameObject-side access to ECS projectile state for ranged dodge (replaces <c>ArrowProjectile</c> static list).</summary>
    public static class ProjectileDodgeBridge
    {
        /// <summary>
        /// Picks a hostile arrow whose horizontal path passes near this character (incoming along velocity).
        /// Pass <paramref name="selfShooterRoot"/> when this character has an ECS combat root entity; otherwise use <see cref="Entity.Null"/>.
        /// </summary>
        public static bool TryGetIncomingDodgeReference(Transform characterRoot, Entity selfShooterRoot,
            out Vector3 dodgeReferencePosition,
            float maxRange = 28f, float maxLateral = 2.8f, float minHorizSpeed = 2.5f, float minAlong = 0.25f)
        {
            dodgeReferencePosition = default;
            if (characterRoot == null)
                return false;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            EntityManager em = world.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<ProjectileDodgeRegistryTag>());
            if (q.CalculateEntityCount() == 0)
                return false;

            Entity reg = q.GetSingletonEntity();
            var buffer = em.GetBuffer<ProjectileDodgeSnapshotElement>(reg);
            if (buffer.Length == 0)
                return false;

            Transform selfRoot = characterRoot.root;
            int selfLegacyId = selfRoot != null ? selfRoot.GetInstanceID() : 0;
            Vector3 selfFlat = new Vector3(characterRoot.position.x, 0f, characterRoot.position.z);

            float bestAlong = float.MaxValue;
            Vector3 bestDodgeRef = default;
            bool found = false;

            for (int i = 0; i < buffer.Length; i++)
            {
                ProjectileDodgeSnapshotElement snap = buffer[i];
                if (selfShooterRoot != Entity.Null && snap.ShooterRoot == selfShooterRoot)
                    continue;
                if (snap.LegacyShooterRootInstanceId != 0 && snap.LegacyShooterRootInstanceId == selfLegacyId)
                    continue;

                float3 vf = snap.VelocityFlat;
                float speed = math.length(vf);
                if (speed < minHorizSpeed)
                    continue;

                float3 velDir = vf / speed;
                float3 arrowFlat = snap.PositionFlat;
                float3 w = new float3(selfFlat.x - arrowFlat.x, 0f, selfFlat.z - arrowFlat.z);
                float along = math.dot(w, velDir);
                if (along < minAlong || along > maxRange)
                    continue;

                float3 perp = w - velDir * along;
                float maxLatSq = maxLateral * maxLateral;
                if (math.lengthsq(perp) > maxLatSq)
                    continue;

                if (along < bestAlong)
                {
                    bestAlong = along;
                    bestDodgeRef = characterRoot.position + new Vector3(velDir.x, 0f, velDir.z);
                    found = true;
                }
            }

            if (!found)
                return false;

            dodgeReferencePosition = bestDodgeRef;
            return true;
        }
    }
}
