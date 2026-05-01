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
    /// Main-thread helper for GameObject systems (e.g. watchtowers) to acquire hostile DOTS NPC targets.
    /// </summary>
    public static class NpcWatchTowerBanditQuery
    {
        /// <param name="towerFactionId"><see cref="Affiliation.FactionId"/> for the tower; if &lt; 0, only entities with <see cref="WellKnownFactionIds.Bandit"/> are considered.</param>
        public static bool TryFindNearestHostileDotsNpcForTower(
            int towerFactionId,
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
            using EntityQuery query = NpcCombatCandidateQuery.CreateEntityQuery(em);

            if (query.IsEmpty)
                return false;

            FactionManager? fm = FactionManager.Instance;

            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob);
            using NativeArray<LocalTransform> transforms = query.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            using NativeArray<NpcFactionId> factions = query.ToComponentDataArray<NpcFactionId>(Allocator.TempJob);
            using NativeArray<NpcCharacterCombatState> combats =
                query.ToComponentDataArray<NpcCharacterCombatState>(Allocator.TempJob);

            float rangeSq = maxRange * maxRange;
            float bestSq = float.MaxValue;
            int bestIdx = -1;

            for (int i = 0; i < entities.Length; i++)
            {
                int npcFaction = factions[i].Value;
                if (towerFactionId >= 0)
                {
                    if (fm == null || fm.GetRelationship(towerFactionId, npcFaction) != Relationship.Enemy)
                        continue;
                }
                else if (npcFaction != WellKnownFactionIds.Bandit)
                    continue;

                NpcCharacterCombatState combat = combats[i];
                if (combat.IsDead != 0 || combat.CurrentHealth <= 0f)
                    continue;

                float3 p = transforms[i].Position;
                float sq = NpcMath.DistanceSqXZ(towerFeetWorld.x, towerFeetWorld.z, p.x, p.z);
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
