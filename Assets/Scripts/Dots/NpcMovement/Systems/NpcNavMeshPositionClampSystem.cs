using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Experimental.AI;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// After horizontal integration, snaps <see cref="LocalTransform.Position"/> to the closest point on the
    /// NavMesh (same mapping rules as pathfinding). Prevents separation / steering from walking NPCs off
    /// small walkable islands such as tower tops.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcIntegrationSystem))]
    [UpdateBefore(typeof(NpcGroundSnapSystem))]
    public partial struct NpcNavMeshPositionClampSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NpcMovementTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var navQuery = new NavMeshQuery(NavMeshWorld.GetDefaultWorld(), Allocator.TempJob, 32);

            var workHandle = new ClampJob
            {
                NavQuery = navQuery
            }.Schedule(state.Dependency);

            workHandle.Complete();
            navQuery.Dispose();
            state.Dependency = workHandle;
        }

        [BurstCompile]
        [WithAll(typeof(NpcMovementTag))]
        partial struct ClampJob : IJobEntity
        {
            public NavMeshQuery NavQuery;

            public void Execute(ref LocalTransform tf, in NpcMovementConfig cfg, ref NpcMovementState mstate)
            {
                if (cfg.UseNavMeshWhenAvailable == 0)
                    return;

                float3 p = tf.Position;
                if (!math.all(math.isfinite(p)))
                    return;

                if (NpcNavMeshSampling.TryMapStartLocation(NavQuery, p, cfg.NavMeshSampleMaxDistance, out var loc))
                {
                    Vector3 mp = loc.position;
                    tf.Position = new float3(mp.x, mp.y, mp.z);
                    return;
                }

                mstate.CurrentHorizontalVelocity = float3.zero;
            }
        }
    }
}
