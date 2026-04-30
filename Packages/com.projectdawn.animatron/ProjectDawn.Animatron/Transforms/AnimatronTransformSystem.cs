using Unity.Entities;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;
using static Unity.Entities.SystemAPI;

namespace ProjectDawn.Animation.Hybrid
{
    //[DisableAutoCreation]
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
    public partial struct WriteAnimatronTransformSystem : ISystem
    {
        EntityQuery m_Query;
        ComponentLookup<LocalTransform> m_TransformLookup;

        public void OnCreate(ref SystemState state)
        {
            m_Query = QueryBuilder()
                .WithAll<ArmatureRef>()
                .WithAll<Transform>()
                .WithAllRW<LocalTransform>()
                .WithNone<Parent>()
                .Build();
            m_TransformLookup = state.GetComponentLookup<LocalTransform>();
        }

        public void OnDestroy(ref SystemState state) { }

        public void OnUpdate(ref SystemState state)
        {
            var entities = m_Query.ToEntityArray(Allocator.TempJob);
            var transformAcessArray = m_Query.GetTransformAccessArray();

            m_TransformLookup.Update(ref state);

            state.Dependency = new WriteAnimatronTransformJob
            {
                Entities = entities,
                TransformLookup = m_TransformLookup,
            }.Schedule(transformAcessArray, state.Dependency);
        }

        [BurstCompile]
        struct WriteAnimatronTransformJob : IJobParallelForTransform
        {
            [DeallocateOnJobCompletion]
            public NativeArray<Entity> Entities;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<LocalTransform> TransformLookup;

            public void Execute(int index, [ReadOnly] TransformAccess transformAccess)
            {
                Entity entity = Entities[index];

                var transform = TransformLookup[entity];
                transform.Position = transformAccess.position;
                transform.Rotation = transformAccess.rotation;
                TransformLookup[entity] = transform;
            }
        }
    }

    [DisableAutoCreation]
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
    public partial struct ReadAnimatronTransformSystem : ISystem
    {
        EntityQuery m_Query;
        ComponentLookup<LocalTransform> m_TransformLookup;

        public void OnCreate(ref SystemState state)
        {
            m_Query = QueryBuilder()
                .WithAll<ArmatureRef>()
                .WithAllRW<Transform>()
                .WithAll<LocalTransform>()
                .Build();
            m_TransformLookup = state.GetComponentLookup<LocalTransform>(true);
        }

        public void OnDestroy(ref SystemState state) { }

        public void OnUpdate(ref SystemState state)
        {
            var entities = m_Query.ToEntityArray(Allocator.TempJob);
            var transformAcessArray = m_Query.GetTransformAccessArray();

            m_TransformLookup.Update(ref state);

            state.Dependency = new ReadAnimatronTransformJob
            {
                Entities = entities,
                TransformLookup = m_TransformLookup,
            }.Schedule(transformAcessArray, state.Dependency);
        }

        [BurstCompile]
        struct ReadAnimatronTransformJob : IJobParallelForTransform
        {
            [DeallocateOnJobCompletion]
            public NativeArray<Entity> Entities;

            [ReadOnly]
            public ComponentLookup<LocalTransform> TransformLookup;

            public void Execute(int index, TransformAccess transformAccess)
            {
                Entity entity = Entities[index];

                var transform = TransformLookup[entity];
                transformAccess.position = transform.Position;
                transformAccess.rotation = transform.Rotation;
            }
        }
    }
}
