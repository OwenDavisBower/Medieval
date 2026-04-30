using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Medieval.NpcMovement;

namespace Medieval.Npcs
{
    /// <summary>Spawns DOTS NPCs using Entities Graphics prefabs.</summary>
    public static class NpcSpawnApi
    {
        public static Entity SpawnFollower(Vector3 worldPosition, quaternion worldRotation, float uniformScale = 1f)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return Entity.Null;

            EntityManager em = world.EntityManager;
            if (!TryGetPrefab(em, NpcPrefabKind.Follower, out Entity prefab))
                return Entity.Null;

            float3 pos = new float3(worldPosition.x, worldPosition.y, worldPosition.z);

            Entity e = em.Instantiate(prefab);
#if UNITY_EDITOR
            em.SetName(e, "FollowerNpc");
#endif
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(pos, worldRotation, uniformScale));
            EnsureMovementComponents(em, e, NpcPrefabKind.Follower);
            return e;
        }

        public static Entity SpawnBandit(Vector3 worldPosition, quaternion worldRotation, float uniformScale = 1f)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return Entity.Null;

            EntityManager em = world.EntityManager;
            if (!TryGetPrefab(em, NpcPrefabKind.Bandit, out Entity prefab))
                return Entity.Null;

            float3 pos = new float3(worldPosition.x, worldPosition.y, worldPosition.z);

            Entity e = em.Instantiate(prefab);
#if UNITY_EDITOR
            em.SetName(e, "BanditNpc");
#endif
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(pos, worldRotation, uniformScale));
            EnsureMovementComponents(em, e, NpcPrefabKind.Bandit);
            return e;
        }

        public static Entity SpawnVillager(Vector3 worldPosition, quaternion worldRotation, float uniformScale = 1f)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return Entity.Null;

            EntityManager em = world.EntityManager;
            if (!TryGetPrefab(em, NpcPrefabKind.Villager, out Entity prefab))
                return Entity.Null;

            float3 pos = new float3(worldPosition.x, worldPosition.y, worldPosition.z);

            Entity e = em.Instantiate(prefab);
#if UNITY_EDITOR
            em.SetName(e, "VillagerNpc");
#endif
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(pos, worldRotation, uniformScale));
            EnsureMovementComponents(em, e, NpcPrefabKind.Villager);
            return e;
        }

        enum NpcPrefabKind : byte
        {
            Follower = 0,
            Bandit = 1,
            Villager = 2
        }

        static void EnsureMovementComponents(EntityManager em, Entity e, NpcPrefabKind kind)
        {
            if (!em.Exists(e))
                return;

            // If the prefab already has movement baked (preferred), do nothing.
            if (em.HasComponent<NpcMovementTag>(e))
                return;

            // Add a minimal movement stack so newly spawned Entities Graphics prefabs still move.
            // This is a safety net for when prototype prefabs are missing `NpcMovementAuthoring`.
            em.AddComponent<NpcMovementTag>(e);
            em.AddComponent<NpcLoiterInitTag>(e);

            if (!em.HasComponent<NpcMovementConfig>(e))
                em.AddComponentData(e, DefaultConfig());

            if (!em.HasComponent<NpcMovementState>(e))
                em.AddComponentData(e, DefaultState(kind, e));

            if (!em.HasComponent<NpcAnchorTarget>(e))
                em.AddComponent<NpcAnchorTarget>(e);
            if (!em.HasComponent<NpcSeekOverride>(e))
                em.AddComponent<NpcSeekOverride>(e);
            if (!em.HasComponent<NpcOverrideFacing>(e))
                em.AddComponent<NpcOverrideFacing>(e);
            if (!em.HasComponent<NpcPendingDodge>(e))
                em.AddComponent<NpcPendingDodge>(e);

            if (!em.HasComponent<NpcPathState>(e))
                em.AddComponent<NpcPathState>(e);
            if (!em.HasBuffer<NpcPathCorner>(e))
                em.AddBuffer<NpcPathCorner>(e);
        }

        static NpcMovementConfig DefaultConfig()
        {
            // Mirrors `NpcMovementAuthoring` defaults (kept here as a fallback only).
            return new NpcMovementConfig
            {
                MoveSpeed = 5f,
                MoveSpeedScale = 1f,
                ArriveThreshold = 0.15f,
                Acceleration = 14f,
                FacingTurnSpeedDegreesPerSecond = 720f,
                FacingMinHorizontalSpeed = 1f,
                PostRangedDodgeImpulse = 3.6f,
                PostRangedDodgeRetreatRatio = 0.28f,
                PostRangedDodgeDelay = 0.14f,
                RangedDodgeCooldown = 0.42f,
                MinLoiterRadius = 2.5f,
                MaxLoiterRadius = 5.5f,
                TrailBehindStrength = 0.35f,
                MaxTrailOffset = 2f,
                WanderRadius = 20f,
                RepickWanderInterval = 4f,
                TargetSmoothTime = 0.35f,
                NoiseFrequency = 0.2f,
                AngleWobbleDegrees = 38f,
                RadiusWobble = 2f,
                UseNavMeshWhenAvailable = 1,
                NavMeshSampleMaxDistance = 2f,
                MinCornerAdvanceDistance = 0.35f,
                SeparationRadius = 1.1f,
                SeparationStrength = 4f,
                ObstacleProbeRadius = 0.35f,
                ObstacleProbeDistance = 1.25f,
                RepathInterval = 0.35f,
                RepathGoalShiftSqr = 2f * 2f,
                GroundSnapEnabled = 1,
                GroundRaycastStartHeight = 1.25f,
                GroundRaycastMaxDistance = 5f,
                GroundSnapHeightOffset = 0f,
                GroundSnapSmoothTime = 0.1f,
                GroundSnapLayerMask = -1
            };
        }

        static NpcMovementState DefaultState(NpcPrefabKind kind, Entity e)
        {
            uint seed = (uint)e.Index ^ 0x9E3779B1u;
            if (seed == 0u)
                seed = 1u;

            var state = new NpcMovementState
            {
                Rng = new Unity.Mathematics.Random(seed)
            };

            switch (kind)
            {
                case NpcPrefabKind.Follower:
                    state.Mode = NpcMovementMode.Orbit;
                    state.Group = NpcSeparationGroup.Followers;
                    break;
                case NpcPrefabKind.Bandit:
                    state.Mode = NpcMovementMode.WanderAroundTarget;
                    state.Group = NpcSeparationGroup.Bandits;
                    break;
                case NpcPrefabKind.Villager:
                    state.Mode = NpcMovementMode.WanderAroundTarget;
                    state.Group = NpcSeparationGroup.None;
                    break;
                default:
                    state.Mode = NpcMovementMode.Orbit;
                    state.Group = NpcSeparationGroup.None;
                    break;
            }

            return state;
        }

        static bool TryGetPrefab(EntityManager em, NpcPrefabKind kind, out Entity prefab)
        {
            prefab = Entity.Null;

            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<NpcPrefabRegistry>());
            if (q.CalculateEntityCount() == 0)
                return false;

            Entity reg = q.GetSingletonEntity();
            NpcPrefabRegistry data = em.GetComponentData<NpcPrefabRegistry>(reg);
            switch (kind)
            {
                case NpcPrefabKind.Follower:
                    prefab = data.FollowerPrefab;
                    break;
                case NpcPrefabKind.Bandit:
                    prefab = data.BanditPrefab;
                    break;
                case NpcPrefabKind.Villager:
                    prefab = data.VillagerPrefab;
                    break;
                default:
                    prefab = Entity.Null;
                    break;
            }

            return prefab != Entity.Null && em.Exists(prefab);
        }
    }
}

