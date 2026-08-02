using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Experimental.AI;

// Experimental.AI NavMeshQuery is obsolete without replacement on Unity 6000.4; still the job-safe API.
#pragma warning disable CS0618

namespace Medieval.NpcMovement
{
    /// <summary>
    /// When a follower is farther than <see cref="NpcCombatSeekConfig.FollowerTeleportBackDistance"/> from the
    /// player in XZ, snaps it to <see cref="NpcCombatSeekConfig.FollowerTeleportBackTargetDistance"/> on the
    /// same radial line, grounds with a raycast, then maps onto the NavMesh (walking inward toward the player
    /// if the preferred radius is off-mesh) so clamp systems do not freeze them off walkable area.
    /// Runs before follower anchors and combat seek so a snap does not fight the same-frame seek override.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcPlayerAnchorSyncSystem))]
    [UpdateBefore(typeof(NpcFollowersAnchorSystem))]
    public partial class NpcFollowerTeleportBackSystem : SystemBase
    {
        /// <summary>Larger than normal path sample so teleport can recover from rough terrain / cliffs.</summary>
        const float TeleportNavMeshSampleDistance = 12f;
        const float TeleportMinRadialDistance = 3f;
        const int TeleportRadialSteps = 10;

        EntityQuery _followersQuery;

        protected override void OnCreate()
        {
            _followersQuery = GetEntityQuery(
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadOnly<NpcCombatSeekConfig>(),
                ComponentType.ReadOnly<NpcMovementConfig>(),
                ComponentType.ReadWrite<NpcMovementState>(),
                ComponentType.ReadWrite<NpcPathState>(),
                ComponentType.ReadWrite<NpcPathCorner>(),
                ComponentType.ReadOnly<NpcMovementTag>(),
                ComponentType.ReadOnly<NpcSeekOverride>(),
                ComponentType.ReadOnly<NpcOverrideFacing>(),
                ComponentType.ReadOnly<NpcCombatTarget>(),
                ComponentType.Exclude<NpcEmergeLeap>());
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton(out NpcPlayerAnchor player) || player.HasPlayer == 0)
                return;

            if (_followersQuery.IsEmptyIgnoreFilter)
                return;

            float3 leader = player.Position;
            var navQuery = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.TempJob, 64);

            using var entities = _followersQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                var cfg = EntityManager.GetComponentData<NpcCombatSeekConfig>(entity);
                if (cfg.FollowerTeleportBackDistance <= 0f || cfg.FollowerTeleportBackTargetDistance < 0f)
                    continue;

                var move = EntityManager.GetComponentData<NpcMovementState>(entity);
                if (move.Group != NpcSeparationGroup.Followers)
                    continue;

                var tf = EntityManager.GetComponentData<LocalTransform>(entity);
                float3 self = tf.Position;
                float dx = self.x - leader.x;
                float dz = self.z - leader.z;
                float flatSq = dx * dx + dz * dz;
                float thr = cfg.FollowerTeleportBackDistance;
                if (flatSq <= thr * thr)
                    continue;

                float3 away = self - leader;
                away.y = 0f;
                if (math.lengthsq(away) < 1e-6f)
                    away = new float3(0f, 0f, -1f);
                else
                    away = math.normalize(away);

                var mcfg = EntityManager.GetComponentData<NpcMovementConfig>(entity);
                float3 placed = ResolveTeleportPosition(
                    navQuery, leader, away, self.y, cfg.FollowerTeleportBackTargetDistance, mcfg);

                tf.Position = placed;
                EntityManager.SetComponentData(entity, tf);

                move.CurrentHorizontalVelocity = float3.zero;
                move.SeparationAccum = float3.zero;
                move.ObstacleDeflectDir = float3.zero;
                move.GroundSnapYVelocity = 0f;
                move.SmoothTarget = placed;
                move.SmoothTargetVel = float3.zero;
                move.RangedMovementLock = 0;
                move.MeleeEngageMovementLock = 0;
                move.RangedCombatSeparationBoost = 0;
                move.CombatLeashBlocked = 0;
                move.HasSmoothTarget = 0;
                EntityManager.SetComponentData(entity, move);

                EntityManager.SetComponentData(entity, default(NpcSeekOverride));
                EntityManager.SetComponentData(entity, default(NpcOverrideFacing));
                EntityManager.SetComponentData(entity, default(NpcCombatTarget));

                var corners = EntityManager.GetBuffer<NpcPathCorner>(entity);
                corners.Clear();
                var path = EntityManager.GetComponentData<NpcPathState>(entity);
                path.PathValid = 0;
                path.CurrentCorner = 0;
                path.StuckTimer = 0f;
                path.ConsecutiveStuckRepaths = 0;
                path.HasRecoveryWaypoint = 0;
                path.ProgressInitialized = 0;
                path.LastProgressPosition = placed;
                EntityManager.SetComponentData(entity, path);
            }

            navQuery.Dispose();
        }

        static float3 ResolveTeleportPosition(
            NavMeshQuery navQuery,
            float3 leader,
            float3 away,
            float fallbackY,
            float preferredDistance,
            in NpcMovementConfig mcfg)
        {
            float sampleDist = math.max(mcfg.NavMeshSampleMaxDistance, TeleportNavMeshSampleDistance);
            float preferred = math.max(0f, preferredDistance);
            float minDist = math.min(TeleportMinRadialDistance, preferred);

            for (int i = 0; i <= TeleportRadialSteps; i++)
            {
                float t = i / (float)TeleportRadialSteps;
                float dist = math.lerp(preferred, minDist, t);
                float3 raw = leader + away * dist;
                float3 grounded = GroundAt(raw, fallbackY, mcfg);
                if (NpcNavMeshSampling.TryMapStartLocation(navQuery, grounded, sampleDist, out var loc))
                {
                    Vector3 mp = loc.position;
                    return new float3(mp.x, mp.y, mp.z);
                }
            }

            float3 nearLeader = leader + away * minDist;
            float3 nearGrounded = GroundAt(nearLeader, fallbackY, mcfg);
            if (NpcNavMeshSampling.TryMapStartLocation(navQuery, nearGrounded, sampleDist, out var nearLoc))
            {
                Vector3 mp = nearLoc.position;
                return new float3(mp.x, mp.y, mp.z);
            }

            if (NpcNavMeshSampling.TryMapStartLocation(navQuery, leader, sampleDist, out var leaderLoc))
            {
                Vector3 mp = leaderLoc.position;
                return new float3(mp.x, mp.y, mp.z);
            }

            return nearGrounded;
        }

        static float3 GroundAt(float3 raw, float fallbackY, in NpcMovementConfig mcfg)
        {
            float startH = math.max(0.05f, mcfg.GroundRaycastStartHeight);
            float maxDist = math.max(0.1f, mcfg.GroundRaycastMaxDistance);
            int mask = mcfg.GroundSnapLayerMask;
            if (mask == 0)
                mask = ~0;

            var origin = new Vector3(raw.x, fallbackY + startH, raw.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, startH + maxDist, mask,
                    QueryTriggerInteraction.Ignore))
                return new float3(raw.x, hit.point.y + mcfg.GroundSnapHeightOffset, raw.z);

            return new float3(raw.x, fallbackY, raw.z);
        }
    }
}
