using Medieval.Npcs;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Chop-wood loop: path to nearest streamed tree, gather until capacity, path to drop-off, unload, repeat.
    /// Uses <see cref="NpcAnchorTarget"/> + <see cref="NpcMovementMode.MoveTowards"/> so villager seek is not
    /// cleared by combat seek (non-combat NPCs use anchor, not seek override).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(NpcPathfindingSystem))]
    public partial struct NpcChopWoodTaskSystem : ISystem
    {
        EntityQuery _streamingTreesSingletonQuery;

        public void OnCreate(ref SystemState state)
        {
            _streamingTreesSingletonQuery =
                state.GetEntityQuery(ComponentType.ReadOnly<WorldStreamingTreesSingletonTag>());
            state.RequireForUpdate<NpcChopWoodTaskTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_streamingTreesSingletonQuery.IsEmpty)
                return;

            Entity treeSingleton = _streamingTreesSingletonQuery.GetSingletonEntity();
            DynamicBuffer<StreamingTreePosition> trees =
                state.EntityManager.GetBuffer<StreamingTreePosition>(treeSingleton, isReadOnly: true);
            var treeArray = trees.AsNativeArray();
            float dt = SystemAPI.Time.DeltaTime;

            state.Dependency = new ChopWoodJob
            {
                TreePositions = treeArray,
                DeltaTime = dt
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(NpcChopWoodTaskTag), typeof(NpcMovementTag))]
        [WithNone(typeof(NpcDeadTag))]
        partial struct ChopWoodJob : IJobEntity
        {
            [ReadOnly] public NativeArray<StreamingTreePosition> TreePositions;
            public float DeltaTime;

            public void Execute(
                ref NpcTaskChopWoodState task,
                ref NpcMovementState move,
                ref NpcAnchorTarget anchor,
                ref NpcOverrideFacing facing,
                in NpcChopWoodConfig cfg,
                in NpcResourceDropOff dropOff,
                in LocalTransform tf)
            {
                if (dropOff.HasPosition == 0)
                    return;

                float3 self = tf.Position;
                float3 drop = dropOff.WorldPosition;

                switch (task.Phase)
                {
                    case NpcChopWoodPhase.WalkToTree:
                        facing = default;
                        if (!TryPickNearestTree(self, out float3 treePos))
                        {
                            anchor.HasAnchor = 0;
                            task.HasTargetTree = 0;
                            return;
                        }

                        task.TargetTreePosition = treePos;
                        task.HasTargetTree = 1;
                        move.Mode = NpcMovementMode.MoveTowards;
                        anchor.Position = treePos;
                        anchor.LinearVelocity = default;
                        anchor.HasAnchor = 1;

                        if (DistanceSqXZ(self, treePos) <= cfg.ChopInteractDistance * cfg.ChopInteractDistance)
                            task.Phase = NpcChopWoodPhase.Chopping;
                        break;

                    case NpcChopWoodPhase.Chopping:
                        if (task.HasTargetTree == 0 || !TryPickNearestTree(self, out float3 chopTree))
                        {
                            task.Phase = NpcChopWoodPhase.WalkToTree;
                            break;
                        }

                        task.TargetTreePosition = chopTree;
                        if (DistanceSqXZ(self, chopTree) > cfg.ChopInteractDistance * cfg.ChopInteractDistance)
                        {
                            task.Phase = NpcChopWoodPhase.WalkToTree;
                            break;
                        }

                        move.Mode = NpcMovementMode.MoveTowards;
                        anchor.Position = self;
                        anchor.LinearVelocity = default;
                        anchor.HasAnchor = 1;

                        float3 toTree = chopTree - self;
                        toTree.y = 0f;
                        if (math.lengthsq(toTree) > 1e-4f)
                        {
                            facing.FlatDirection = math.normalize(toTree);
                            facing.HasOverride = 1;
                        }
                        else
                            facing = default;

                        float room = cfg.CarryCapacity - task.WoodCarried;
                        if (room > 0f)
                        {
                            float add = cfg.WoodGatherPerSecond * DeltaTime;
                            task.WoodCarried = math.min(cfg.CarryCapacity, task.WoodCarried + add);
                        }

                        if (task.WoodCarried >= cfg.CarryCapacity - 1e-4f)
                        {
                            task.Phase = NpcChopWoodPhase.WalkToDropOff;
                            facing = default;
                        }

                        break;

                    case NpcChopWoodPhase.WalkToDropOff:
                        facing = default;
                        move.Mode = NpcMovementMode.MoveTowards;
                        anchor.Position = drop;
                        anchor.LinearVelocity = default;
                        anchor.HasAnchor = 1;

                        if (DistanceSqXZ(self, drop) <= cfg.DropArriveDistance * cfg.DropArriveDistance)
                        {
                            task.Phase = NpcChopWoodPhase.Dropping;
                            task.DropTimer = cfg.DropDurationSeconds;
                            anchor.Position = self;
                        }

                        break;

                    case NpcChopWoodPhase.Dropping:
                        move.Mode = NpcMovementMode.MoveTowards;
                        anchor.Position = self;
                        anchor.LinearVelocity = default;
                        anchor.HasAnchor = 1;
                        facing = default;

                        task.DropTimer -= DeltaTime;
                        if (task.DropTimer <= 0f)
                        {
                            task.WoodCarried = 0f;
                            task.Phase = NpcChopWoodPhase.WalkToTree;
                            task.HasTargetTree = 0;
                        }

                        break;
                }
            }

            bool TryPickNearestTree(float3 self, out float3 treePos)
            {
                treePos = default;
                if (!TreePositions.IsCreated || TreePositions.Length == 0)
                    return false;

                float best = float.MaxValue;
                bool found = false;
                for (int i = 0; i < TreePositions.Length; i++)
                {
                    float3 p = TreePositions[i].Position;
                    float sq = DistanceSqXZ(self, p);
                    if (sq < best)
                    {
                        best = sq;
                        treePos = p;
                        found = true;
                    }
                }

                return found;
            }

            static float DistanceSqXZ(float3 a, float3 b)
            {
                float dx = a.x - b.x;
                float dz = a.z - b.z;
                return dx * dx + dz * dz;
            }
        }
    }
}
