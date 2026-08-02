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

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton(out NpcPlayerAnchor player) || player.HasPlayer == 0)
                return;

            float3 leader = player.Position;
            var navQuery = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.TempJob, 64);

            // SystemAPI.Query arity max is 7; seek/facing/combat target are WithAll + EntityManager.
            foreach (var (tfRW, cfgRO, mcfgRO, stateRW, pathRW, corners, entity) in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRO<NpcCombatSeekConfig>, RefRO<NpcMovementConfig>,
                             RefRW<NpcMovementState>, RefRW<NpcPathState>, DynamicBuffer<NpcPathCorner>>()
                         .WithAll<NpcMovementTag>()
                         .WithAll<NpcSeekOverride>()
                         .WithAll<NpcOverrideFacing>()
                         .WithAll<NpcCombatTarget>()
                         .WithEntityAccess())
            {
                var cfg = cfgRO.ValueRO;
                if (cfg.FollowerTeleportBackDistance <= 0f || cfg.FollowerTeleportBackTargetDistance < 0f)
                    continue;

                ref NpcMovementState move = ref stateRW.ValueRW;
                if (move.Group != NpcSeparationGroup.Followers)
                    continue;

                float3 self = tfRW.ValueRO.Position;
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

                var mcfg = mcfgRO.ValueRO;
                float3 placed = ResolveTeleportPosition(
                    navQuery, leader, away, self.y, cfg.FollowerTeleportBackTargetDistance, mcfg);

                tfRW.ValueRW.Position = placed;

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

                EntityManager.SetComponentData(entity, default(NpcSeekOverride));
                EntityManager.SetComponentData(entity, default(NpcOverrideFacing));
                EntityManager.SetComponentData(entity, default(NpcCombatTarget));

                corners.Clear();
                ref NpcPathState path = ref pathRW.ValueRW;
                path.PathValid = 0;
                path.CurrentCorner = 0;
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

            // Last resort: beside the leader, then raw ground if still off-mesh.
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
