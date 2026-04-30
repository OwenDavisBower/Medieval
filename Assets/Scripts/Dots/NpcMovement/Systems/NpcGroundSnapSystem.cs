using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Optional main-thread ground alignment: raycasts down and adjusts <see cref="LocalTransform.Position"/>
    /// Y, using <see cref="NpcMovementState.GroundSnapYVelocity"/> for SmoothDamp.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcIntegrationSystem))]
    public partial class NpcGroundSnapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;

            foreach (var (tfRW, cfgRO, stateRW) in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRO<NpcMovementConfig>, RefRW<NpcMovementState>>()
                         .WithAll<NpcMovementTag>())
            {
                var cfg = cfgRO.ValueRO;
                if (cfg.GroundSnapEnabled == 0)
                {
                    stateRW.ValueRW.GroundSnapYVelocity = 0f;
                    continue;
                }

                float3 p = tfRW.ValueRO.Position;
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
                    ref NpcMovementState mst = ref stateRW.ValueRW;

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
                    tfRW.ValueRW.Position = p;
                }
                else
                {
                    stateRW.ValueRW.GroundSnapYVelocity = 0f;
                }
            }
        }
    }
}

