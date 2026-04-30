using Unity.Entities;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;
using static Unity.Entities.SystemAPI;
using Unity.Rendering;

namespace ProjectDawn.Animation.Hybrid
{
    //[DisableAutoCreation]
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
    public partial struct WriteRenderMeshArrayTransformSystem : ISystem
    {
        EntityQuery m_Query;
        ComponentLookup<LocalToWorld> m_TransformLookup;

        public void OnCreate(ref SystemState state)
        {
            m_Query = QueryBuilder()
                .WithAll<RenderMeshArray>()
                .WithAll<Transform>()
                .WithAllRW<LocalToWorld>()
                .WithNone<SkinRef>()
                .Build();
            m_TransformLookup = state.GetComponentLookup<LocalToWorld>();
        }

        public void OnDestroy(ref SystemState state) { }

        public void OnUpdate(ref SystemState state)
        {
            var entities = m_Query.ToEntityArray(Allocator.TempJob);
            var transformAcessArray = m_Query.GetTransformAccessArray();

            m_TransformLookup.Update(ref state);

            state.Dependency = new WriteRenderMeshArrayTransformJob
            {
                Entities = entities,
                TransformLookup = m_TransformLookup,
            }.Schedule(transformAcessArray, state.Dependency);
        }

        [BurstCompile]
        struct WriteRenderMeshArrayTransformJob : IJobParallelForTransform
        {
            [DeallocateOnJobCompletion]
            public NativeArray<Entity> Entities;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<LocalToWorld> TransformLookup;

            public void Execute(int index, [ReadOnly] TransformAccess transformAccess)
            {
                Entity entity = Entities[index];

                var transform = TransformLookup[entity];
                transform.Value = transformAccess.localToWorldMatrix;
                TransformLookup[entity] = transform;
            }
        }
    }
}
