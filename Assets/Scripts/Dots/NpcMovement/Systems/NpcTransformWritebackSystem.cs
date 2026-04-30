using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Main-thread sync that pushes the entity's <see cref="LocalTransform"/> back onto the companion
    /// <see cref="Rigidbody"/> (kinematic <c>MovePosition</c> + direct rotation assignment) and forwards
    /// the latest simulation state (horizontal velocity, effective move speed, pending dodge) to the
    /// <see cref="INpcFacade"/> so <c>LocomotionAnimatorDriver</c> and ranged cooldown tracking stay in
    /// sync.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NpcTransformWritebackSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var (companion, transformRO, stateRO, dodgeRO) in SystemAPI
                         .Query<NpcCompanion, RefRO<LocalTransform>, RefRO<NpcMovementState>,
                                RefRO<NpcPendingDodge>>()
                         .WithAll<NpcMovementTag>())
            {
                if (companion.Transform == null)
                    continue;

                float3 p = transformRO.ValueRO.Position;
                quaternion q = transformRO.ValueRO.Rotation;
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
                    stateRO.ValueRO.CurrentHorizontalVelocity,
                    stateRO.ValueRO.EffectiveMoveSpeed,
                    dodgeRO.ValueRO.HasPending != 0);
            }
        }
    }
}
