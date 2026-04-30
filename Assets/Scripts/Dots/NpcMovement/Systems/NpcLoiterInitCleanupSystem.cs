using Unity.Burst;
using Unity.Entities;

namespace Medieval.NpcMovement
{
    /// <summary>Removes <see cref="NpcLoiterInitTag"/> after <see cref="NpcLoiterInitSystem"/> runs.</summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(NpcLoiterInitSystem))]
    public partial struct NpcLoiterInitCleanupSystem : ISystem
    {
        EntityQuery _q;

        public void OnCreate(ref SystemState state)
        {
            _q = state.GetEntityQuery(ComponentType.ReadOnly<NpcLoiterInitTag>(), ComponentType.ReadOnly<NpcMovementTag>());
            state.RequireForUpdate(_q);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI.GetSingletonRW<BeginInitializationEntityCommandBufferSystem.Singleton>()
                .ValueRW.CreateCommandBuffer(state.WorldUnmanaged);
            ecb.RemoveComponent<NpcLoiterInitTag>(_q);
        }
    }
}

