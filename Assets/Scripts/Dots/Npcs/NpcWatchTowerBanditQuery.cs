#nullable enable
using Medieval.NpcMovement;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.Npcs
{
    /// <summary>
    /// Main-thread helper for GameObject systems (e.g. watchtowers) to acquire DOTS bandit targets.
    /// </summary>
    public static class NpcWatchTowerBanditQuery
    {
        public static bool TryFindNearestBanditForTower(
            Vector3 towerFeetWorld,
            float maxRange,
            float eyeHeight,
            float targetAimHeight,
            LayerMask obstacleLayers,
            Transform? ignoreHitsUnderHierarchy,
            out Vector3 targetFeetWorld,
            out Vector3 targetHorizontalVelocity)
        {
            targetFeetWorld = default;
            targetHorizontalVelocity = default;

            World? world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            EntityManager em = world.EntityManager;
            using EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<NpcProfile>(),
                ComponentType.ReadOnly<NpcCharacterCombatState>(),
                ComponentType.ReadOnly<NpcMovementTag>());

            if (query.IsEmpty)
                return false;

            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob);
            using NativeArray<LocalTransform> transforms = query.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            using NativeArray<NpcProfile> profiles = query.ToComponentDataArray<NpcProfile>(Allocator.TempJob);
            using NativeArray<NpcCharacterCombatState> combats =
                query.ToComponentDataArray<NpcCharacterCombatState>(Allocator.TempJob);

            float rangeSq = maxRange * maxRange;
            float bestSq = float.MaxValue;
            int bestIdx = -1;

            float tx = towerFeetWorld.x;
            float tz = towerFeetWorld.z;

            for (int i = 0; i < entities.Length; i++)
            {
                if (profiles[i].Role != NpcRole.Bandit)
                    continue;

                NpcCharacterCombatState combat = combats[i];
                if (combat.IsDead != 0 || combat.CurrentHealth <= 0f)
                    continue;

                float3 p = transforms[i].Position;
                float dx = p.x - tx;
                float dz = p.z - tz;
                float sq = dx * dx + dz * dz;
                if (sq > rangeSq || sq >= bestSq)
                    continue;

                var targetFeet = new Vector3(p.x, p.y, p.z);
                if (!LineOfSightUtility.HasClearLineOfSightWorldPoints(
                        towerFeetWorld,
                        targetFeet,
                        eyeHeight,
                        targetAimHeight,
                        obstacleLayers,
                        ignoreHitsUnderHierarchy))
                    continue;

                bestSq = sq;
                bestIdx = i;
            }

            if (bestIdx < 0)
                return false;

            float3 bestPos = transforms[bestIdx].Position;
            targetFeetWorld = new Vector3(bestPos.x, bestPos.y, bestPos.z);

            Entity bestEntity = entities[bestIdx];
            if (em.HasComponent<NpcMovementState>(bestEntity))
            {
                float3 v = em.GetComponentData<NpcMovementState>(bestEntity).CurrentHorizontalVelocity;
                targetHorizontalVelocity = new Vector3(v.x, 0f, v.z);
            }

            return true;
        }
    }
}
