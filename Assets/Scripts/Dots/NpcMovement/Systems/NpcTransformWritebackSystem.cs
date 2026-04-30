using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Main-thread sync that optionally raycasts down to snap Y to ground, writes
    /// <see cref="LocalTransform"/>, pushes the pose onto the companion <see cref="Rigidbody"/> (kinematic
    /// <c>MovePosition</c> / <c>MoveRotation</c>), and forwards simulation state to <see cref="INpcFacade"/>.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NpcTransformWritebackSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;

            foreach (var (companion, transformRW, cfgRO, stateRW, dodgeRO) in SystemAPI
                         .Query<NpcCompanion, RefRW<LocalTransform>, RefRO<NpcMovementConfig>,
                                RefRW<NpcMovementState>, RefRO<NpcPendingDodge>>()
                         .WithAll<NpcMovementTag>())
            {
                if (companion.Transform == null)
                    continue;

                float3 p = transformRW.ValueRO.Position;
                quaternion q = transformRW.ValueRO.Rotation;

                if (cfgRO.ValueRO.GroundSnapEnabled != 0)
                {
                    var cfg = cfgRO.ValueRO;
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
                        transformRW.ValueRW.Position = p;
                    }
                    else
                        stateRW.ValueRW.GroundSnapYVelocity = 0f;
                }
                else
                    stateRW.ValueRW.GroundSnapYVelocity = 0f;

                var worldPos = new Vector3(p.x, p.y, p.z);
                var worldRot = new Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);

                if (companion.Rigidbody != null && companion.Rigidbody.isKinematic)
                {
                    companion.Rigidbody.MovePosition(worldPos);
                    companion.Rigidbody.MoveRotation(worldRot);
                }
                else
                {
                    companion.Transform.SetPositionAndRotation(worldPos, worldRot);
                }

                companion.Facade?.OnMovementStateSynced(
                    stateRW.ValueRO.CurrentHorizontalVelocity,
                    stateRW.ValueRO.EffectiveMoveSpeed,
                    dodgeRO.ValueRO.HasPending != 0);
            }
        }
    }
}
