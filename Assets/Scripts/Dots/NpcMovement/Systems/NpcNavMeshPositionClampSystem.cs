using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Experimental.AI;

// Experimental.AI NavMeshQuery is obsolete without replacement on Unity 6000.4; still the job-safe API.
#pragma warning disable CS0618

namespace Medieval.NpcMovement
{
    /// <summary>
    /// After horizontal integration, snaps <see cref="LocalTransform.Position"/> to the closest point on the
    /// NavMesh (same mapping rules as pathfinding). Prevents separation / steering from walking NPCs off
    /// small walkable islands such as tower tops, without pulling bridge traffic down onto river mesh.
    /// Skipped entirely while fording (<see cref="NpcMath.NavMeshWaterTopY"/>) — Water-tagged slopes and
    /// ford edges otherwise magnetize agents ashore in a single frame.
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
        [WithNone(typeof(NpcEmergeLeap))]
        partial struct ClampJob : IJobEntity
        {
            public NavMeshQuery NavQuery;

            public void Execute(
                ref LocalTransform tf,
                in NpcMovementConfig cfg,
                ref NpcMovementState mstate,
                in NpcPathState pathState,
                DynamicBuffer<NpcPathCorner> corners)
            {
                if (cfg.UseNavMeshWhenAvailable == 0)
                    return;

                float3 p = tf.Position;
                if (!math.all(math.isfinite(p)))
                    return;

                // Fording: never snap onto navmesh. Water-tagged bank slopes and ford-edge polys
                // magnetize MapLocation toward shore and read as a teleport out of the river.
                if (p.y <= NpcMath.NavMeshWaterTopY)
                    return;

                bool preferWade = ShouldPreferWade(p, pathState, corners);
                if (NpcNavMeshSampling.TryMapNearHeight(
                        NavQuery, p, cfg.NavMeshSampleMaxDistance,
                        NpcNavMeshSampling.MaxVerticalDrop, NpcNavMeshSampling.MaxVerticalClimb, preferWade,
                        out var loc))
                {
                    Vector3 mp = loc.position;
                    tf.Position = new float3(mp.x, mp.y, mp.z);
                    return;
                }

                // Soft damp — avoid permanent freeze while off walkable mesh; never expand Y onto lower surfaces.
                float3 v = mstate.CurrentHorizontalVelocity;
                v.y = 0f;
                mstate.CurrentHorizontalVelocity = v * 0.85f;
            }

            /// <summary>
            /// True when any remaining path corner is underwater or well below the agent. Shoreline string-pull
            /// corners often sit at bank height, so we look ahead — otherwise clamp never allows a ford.
            /// </summary>
            static bool ShouldPreferWade(
                float3 selfPos, in NpcPathState pathState, DynamicBuffer<NpcPathCorner> corners)
            {
                if (pathState.PathValid == 0 || corners.Length == 0)
                    return false;

                int start = math.clamp(pathState.CurrentCorner, 0, corners.Length - 1);
                for (int i = start; i < corners.Length; i++)
                {
                    float3 cornerPos = corners[i].Value;
                    // Ford corners sit near NavMeshWaterTopY (above visual WaterSurfaceY).
                    if (cornerPos.y <= NpcMath.NavMeshWaterTopY)
                        return true;
                    if ((selfPos.y - cornerPos.y) > NpcNavMeshSampling.MaxVerticalDrop)
                        return true;
                }

                return false;
            }
        }
    }
}
