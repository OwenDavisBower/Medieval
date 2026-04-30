using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Main-thread sync that copies the companion <see cref="UnityEngine.Transform"/> world position back
    /// into the entity's <see cref="LocalTransform"/> at the start of each frame. This is what lets external
    /// code (e.g. <c>FollowerController.TryTeleportBackTowardLeader</c>) teleport the NPC directly on its
    /// Rigidbody and have the DOTS pipeline pick up the new position.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class NpcInputSyncSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var (companion, transformRW) in SystemAPI
                         .Query<NpcCompanion, RefRW<LocalTransform>>()
                         .WithAll<NpcMovementTag>())
            {
                if (companion.Transform == null)
                    continue;
                // Prefer Rigidbody.position so teleports set via rb.position are picked up without
                // waiting for the next physics sync (kinematic bodies don't update transform until then).
                UnityEngine.Vector3 p = companion.Rigidbody != null
                    ? companion.Rigidbody.position
                    : companion.Transform.position;
                transformRW.ValueRW.Position = new float3(p.x, p.y, p.z);
            }
        }
    }
}
