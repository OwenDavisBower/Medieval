using Unity.Entities;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Holds NavMesh clamping off during a cover-emerge leap, then restores baked movement settings.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(NpcNavMeshPositionClampSystem))]
    public partial struct NpcEmergeLeapSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NpcEmergeLeap>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float now = Time.time;
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            var facingLookup = SystemAPI.GetComponentLookup<NpcOverrideFacing>();

            foreach (var (emergeRW, cfgRW, mstateRW, entity) in SystemAPI
                         .Query<RefRW<NpcEmergeLeap>, RefRW<NpcMovementConfig>, RefRW<NpcMovementState>>()
                         .WithEntityAccess())
            {
                ref var emerge = ref emergeRW.ValueRW;
                ref var cfg = ref cfgRW.ValueRW;
                if (now < emerge.EndUnityTime)
                {
                    cfg.UseNavMeshWhenAvailable = 0;
                    continue;
                }

                cfg.UseNavMeshWhenAvailable = emerge.RestoreUseNavMesh;
                cfg.GroundSnapSmoothTime = emerge.RestoreGroundSnapSmoothTime;
                mstateRW.ValueRW.Mode = emerge.RestoreMode;
                if (facingLookup.HasComponent(entity))
                    facingLookup[entity] = default;
                ecb.RemoveComponent<NpcEmergeLeap>(entity);
            }

            ecb.Playback(state.EntityManager);
        }
    }
}
