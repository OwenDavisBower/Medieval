using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using static Unity.Entities.SystemAPI;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Maps <see cref="SkinMatrixBuffer"/> for writing.
    /// </summary>
    [UpdateBefore(typeof(PoseSystem))]
    [UpdateInGroup(typeof(AnimatronSystemGroup))]
    unsafe partial struct MapSkinMatrixSystem : ISystem
    {
        [BurstCompile]
        [WithNone(typeof(Culled))]
        partial struct UpdateSkinMatrixIndexJob : IJobEntity
        {
            [NativeDisableUnsafePtrRestriction]
            public int* Length;

            void Execute(in DynamicBuffer<SkinMatrix> skinMatrices, ref SkinMatrixBufferIndex index)
            {
                index.Value = *Length;
                *Length += skinMatrices.Length;
            }
        }

        void ISystem.OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SkinMatrixBuffer>();
            state.RequireForUpdate<SkinMatrixBufferIndex>();
        }

        void ISystem.OnUpdate(ref SystemState state)
        {
            var skinMatrixBuffer = ManagedAPI.GetSingleton<SkinMatrixBuffer>();

            // Reserve skin matrix buffer index 0 for invalid
            *skinMatrixBuffer.GetLenghtPtr() = InitializeSkinMatrixBufferSystem.MaxDummySkinMatrixCount;

            state.Dependency = new UpdateSkinMatrixIndexJob()
            {
                Length = skinMatrixBuffer.GetLenghtPtr(),
            }.Schedule(state.Dependency);

            skinMatrixBuffer.Handle = state.Dependency;
        }
    }

    /// <summary>
    /// Copies skin matrices from CPU to GPU <see cref="SkinMatrixBuffer"/>.
    /// </summary>
    [UpdateAfter(typeof(AnimatronSystemGroup))]
    unsafe partial struct UpdateSkinMatrixSystem : ISystem
    {
        [BurstCompile]
        [WithNone(typeof(Culled))]
        partial struct CopySkinMatricesJob : IJobEntity
        {
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<float3x4> Data;

            void Execute(in DynamicBuffer<SkinMatrix> skinMatrices, in SkinMatrixBufferIndex index)
            {
                // Skin matrix at index 0 is considered invalid.
                // Matrices from 0 to InitializeSkinMatrixBufferSystem.MaxDummySkinMatrixCount are invalid.
                // Here we simply clear the memory to 0 so nothing will be rendered.
                // TODO: This can be optimized by creating all buffers with the initial range already cleared.
                if (index.Value == 0)
                {
                    // Check that the armature does not exceed the dummy buffer size.
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                    if (skinMatrices.Length > InitializeSkinMatrixBufferSystem.MaxDummySkinMatrixCount)
                        throw new System.InvalidOperationException(
                            "One of the animatrons has an invalid skin matrix buffer index and exceeds the maximum joint count of 256 used for dummy rendering. " +
                            "It is recommended to either instantiate the entity before AnimatronSystemGroup or modify the InitializeSkinMatrixBufferSystem.MaxDummySkinMatrixCount field.");
#endif
                    return;
                }

                long length = (long)skinMatrices.Length * UnsafeUtility.SizeOf<float3x4>();
                UnsafeUtility.MemCpy(
                    (float3x4*)Data.GetUnsafePtr() + index.Value,
                    skinMatrices.GetUnsafeReadOnlyPtr(),
                    length
                );
            }
        }

        [BurstCompile]
        struct DummySkinMatricesJob : IJob
        {
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<float3x4> Data;
            public void Execute()
            {
                for (int i = 0; i < InitializeSkinMatrixBufferSystem.MaxDummySkinMatrixCount; i++)
                {
                    Data[i] = SkinMatrix.Default.Value;
                }
            }
        }


        void ISystem.OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SkinMatrixBuffer>();
            state.RequireForUpdate<SkinMatrixBufferIndex>();
        }

        void ISystem.OnUpdate(ref SystemState state)
        {
            var skinMatrixBuffer = ManagedAPI.GetSingleton<SkinMatrixBuffer>();

            // We have to wait for length value to be calculated
            state.Dependency.Complete();

            int count = *skinMatrixBuffer.GetLenghtPtr();
            if (count == 0)
                return;

            skinMatrixBuffer.ResizePassBufferIfRequired(count);

            var data = skinMatrixBuffer.Map(0, count);

            state.Dependency = JobHandle.CombineDependencies(
                new CopySkinMatricesJob()
                {
                    Data = data
                }.ScheduleParallel(state.Dependency),
                new DummySkinMatricesJob
                {
                    Data = data,
                }.Schedule(state.Dependency));

            skinMatrixBuffer.Handle = state.Dependency;
        }
    }

    /// <summary>
    /// Unmaps <see cref="SkinMatrixBuffer"/> for writing.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(EntitiesGraphicsSystem))]
    unsafe partial struct UnmapSkinMatrixSystem : ISystem
    {
        void ISystem.OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SkinMatrixBuffer>();
            state.RequireForUpdate<SkinMatrixBufferIndex>();
        }
        void ISystem.OnUpdate(ref SystemState state)
        {
            var skinMatrixBuffer = ManagedAPI.GetSingleton<SkinMatrixBuffer>();
            int count = *skinMatrixBuffer.GetLenghtPtr();
            if (count == 0)
                return;

            skinMatrixBuffer.Handle.Complete();
            skinMatrixBuffer.Unmap(*skinMatrixBuffer.GetLenghtPtr());
        }
    }
}
