using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Optional main-thread ground alignment: raycasts down and adjusts <see cref="LocalTransform.Position"/>
    /// Y, using <see cref="NpcMovementState.GroundSnapYVelocity"/> for SmoothDamp.
    /// Runs for living NPCs (<see cref="NpcMovementTag"/>) and for corpses (<see cref="NpcDeadTag"/>): death
    /// strips <see cref="NpcMovementTag"/> but we still snap Y so bodies stay on the ground.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcIntegrationSystem))]
    public partial class NpcGroundSnapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Dependency.Complete();

            float dt = SystemAPI.Time.DeltaTime;

            foreach (var (tfRW, cfgRO, stateRW) in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRO<NpcMovementConfig>, RefRW<NpcMovementState>>()
                         .WithAll<NpcMovementTag>())
            {
                TrySnapToGround(ref tfRW.ValueRW, in cfgRO.ValueRO, ref stateRW.ValueRW, dt);
            }

            foreach (var (tfRW, cfgRO, stateRW) in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRO<NpcMovementConfig>, RefRW<NpcMovementState>>()
                         .WithAll<NpcDeadTag>()
                         .WithNone<NpcMovementTag>())
            {
                TrySnapToGround(ref tfRW.ValueRW, in cfgRO.ValueRO, ref stateRW.ValueRW, dt);
            }
        }

        static void TrySnapToGround(ref LocalTransform tf, in NpcMovementConfig cfg, ref NpcMovementState mst,
            float dt)
        {
            if (cfg.GroundSnapEnabled == 0)
            {
                mst.GroundSnapYVelocity = 0f;
                return;
            }

            float3 p = tf.Position;
            float startH = math.max(0.05f, cfg.GroundRaycastStartHeight);
            float maxDist = math.max(0.1f, cfg.GroundRaycastMaxDistance);

            var origin = new Vector3(p.x, p.y + startH, p.z);
            int mask = cfg.GroundSnapLayerMask;
            if (mask == 0)
                mask = ~0;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, startH + maxDist, mask,
                    QueryTriggerInteraction.Ignore))
            {
                float targetY = hit.point.y + cfg.GroundSnapHeightOffset;

                float newY;
                if (cfg.GroundSnapSmoothTime > 1e-4f)
                {
                    newY = Mathf.SmoothDamp(p.y, targetY, ref mst.GroundSnapYVelocity,
                        cfg.GroundSnapSmoothTime, Mathf.Infinity, dt);
                }
                else
                {
                    newY = targetY;
                    mst.GroundSnapYVelocity = 0f;
                }

                p = new float3(p.x, newY, p.z);
                tf.Position = p;
            }
            else
            {
                mst.GroundSnapYVelocity = 0f;
            }
        }
    }
}

