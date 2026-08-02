using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Builds a per-frame spatial hash of NPC positions keyed by (cellX, cellZ, group) and accumulates
    /// repulsion force into <see cref="NpcMovementState.SeparationAccum"/> for NPCs whose separation group
    /// is Followers or Bandits. Followers additionally repel away from their anchor (the player) when close.
    /// The steering system reads the accumulator next stage and clears it for the next frame.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct NpcSeparationSystem : ISystem
    {
        NativeParallelMultiHashMap<int3, float3> _hash;
        EntityQuery _dataQuery;

        public void OnCreate(ref SystemState state)
        {
            _hash = new NativeParallelMultiHashMap<int3, float3>(256, Allocator.Persistent);
            _dataQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<NpcMovementTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<NpcMovementState>(),
                ComponentType.ReadOnly<NpcMovementConfig>());
            state.RequireForUpdate(_dataQuery);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_hash.IsCreated)
                _hash.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            int capacity = _dataQuery.CalculateEntityCount();
            if (capacity <= 0)
                return;
            if (_hash.Capacity < capacity * 2)
                _hash.Capacity = math.max(capacity * 2, 256);
            _hash.Clear();

            // Cell size must cover max search radius so neighbors stay within a 3x3 block.
            // Allocation-free scan (configs are tiny); ranged boost uses r*1.45 so 1.5x is enough.
            float maxRadius = 0f;
            foreach (var cfg in SystemAPI.Query<RefRO<NpcMovementConfig>>().WithAll<NpcMovementTag>())
                maxRadius = math.max(maxRadius, cfg.ValueRO.SeparationRadius);
            float cellSize = math.max(0.25f, maxRadius * 1.5f);

            var buildJob = new BuildHashJob
            {
                Hash = _hash.AsParallelWriter(),
                CellSize = cellSize
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new AccumulateSeparationJob
            {
                Hash = _hash,
                CellSize = cellSize
            }.ScheduleParallel(buildJob);
        }

        [BurstCompile]
        [WithAll(typeof(NpcMovementTag))]
        partial struct BuildHashJob : IJobEntity
        {
            public NativeParallelMultiHashMap<int3, float3>.ParallelWriter Hash;
            public float CellSize;

            public void Execute(in LocalTransform tf, in NpcMovementState s)
            {
                if (s.Group == NpcSeparationGroup.None)
                    return;
                float3 p = tf.Position;
                int3 cell = new int3(
                    (int)math.floor(p.x / CellSize),
                    (int)s.Group,
                    (int)math.floor(p.z / CellSize));
                Hash.Add(cell, p);
            }
        }

        [BurstCompile]
        [WithAll(typeof(NpcMovementTag))]
        partial struct AccumulateSeparationJob : IJobEntity
        {
            [ReadOnly] public NativeParallelMultiHashMap<int3, float3> Hash;
            public float CellSize;

            public void Execute(
                in LocalTransform tf,
                in NpcMovementConfig cfg,
                in NpcAnchorTarget anchor,
                ref NpcMovementState state)
            {
                state.SeparationAccum = float3.zero;
                if (state.Group == NpcSeparationGroup.None)
                    return;

                float3 p = tf.Position;
                float r = cfg.SeparationRadius;
                float strength = cfg.SeparationStrength;
                if (state.RangedCombatSeparationBoost != 0)
                {
                    r *= 1.45f;
                    strength *= 1.9f;
                }

                float rSq = r * r;
                float3 sum = float3.zero;

                int cx = (int)math.floor(p.x / CellSize);
                int cz = (int)math.floor(p.z / CellSize);
                int groupKey = (int)state.Group;

                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int3 cell = new int3(cx + dx, groupKey, cz + dz);
                    if (!Hash.TryGetFirstValue(cell, out float3 other, out var it))
                        continue;
                    do
                    {
                        float3 d = p - other;
                        d.y = 0f;
                        float sq = math.lengthsq(d);
                        if (sq > 1e-6f && sq < rSq)
                        {
                            float dist = math.sqrt(sq);
                            sum += math.normalize(d) * (strength * (1f - dist / r));
                        }
                    } while (Hash.TryGetNextValue(out other, ref it));
                }

                if (state.Group == NpcSeparationGroup.Followers && anchor.HasAnchor != 0)
                {
                    float3 ad = p - anchor.Position;
                    ad.y = 0f;
                    float asq = math.lengthsq(ad);
                    if (asq > 1e-6f && asq < rSq)
                    {
                        float dist = math.sqrt(asq);
                        sum += math.normalize(ad) * (strength * (1f - dist / r));
                    }
                }

                state.SeparationAccum = sum;
            }
        }
    }
}
