using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// When a follower is farther than <see cref="NpcCombatSeekConfig.FollowerTeleportBackDistance"/> from the
    /// player in XZ, snaps it to <see cref="NpcCombatSeekConfig.FollowerTeleportBackTargetDistance"/> on the
    /// same radial line and re-grounds with the same raycast idea as <see cref="NpcGroundSnapSystem"/>.
    /// Mirrors legacy <c>FollowerController.TryTeleportBackTowardLeader</c>.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcPlayerAnchorSyncSystem))]
    [UpdateBefore(typeof(NpcSeparationSystem))]
    public partial class NpcFollowerTeleportBackSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton(out NpcPlayerAnchor player) || player.HasPlayer == 0)
                return;

            float3 leader = player.Position;

            foreach (var (tfRW, cfgRO, mcfgRO, stateRW, pathRW, corners) in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRO<NpcCombatSeekConfig>, RefRO<NpcMovementConfig>,
                             RefRW<NpcMovementState>, RefRW<NpcPathState>, DynamicBuffer<NpcPathCorner>>()
                         .WithAll<NpcMovementTag>())
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

                float3 raw = leader + away * cfg.FollowerTeleportBackTargetDistance;

                var mcfg = mcfgRO.ValueRO;
                float startH = math.max(0.05f, mcfg.GroundRaycastStartHeight);
                float maxDist = math.max(0.1f, mcfg.GroundRaycastMaxDistance);
                int mask = mcfg.GroundSnapLayerMask;
                if (mask == 0)
                    mask = ~0;

                float3 placed = raw;
                var origin = new Vector3(raw.x, self.y + startH, raw.z);
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, startH + maxDist, mask,
                        QueryTriggerInteraction.Ignore))
                    placed = new float3(raw.x, hit.point.y + mcfg.GroundSnapHeightOffset, raw.z);
                else
                    placed = new float3(raw.x, self.y, raw.z);

                tfRW.ValueRW.Position = placed;

                move.CurrentHorizontalVelocity = float3.zero;
                move.SeparationAccum = float3.zero;
                move.ObstacleDeflectDir = float3.zero;
                move.GroundSnapYVelocity = 0f;
                move.SmoothTarget = placed;
                move.SmoothTargetVel = float3.zero;

                corners.Clear();
                ref NpcPathState path = ref pathRW.ValueRW;
                path.PathValid = 0;
                path.CurrentCorner = 0;
            }
        }
    }
}
