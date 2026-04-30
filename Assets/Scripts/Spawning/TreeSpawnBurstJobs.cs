using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Burst jobs and helpers for procedural tree placement (placement mask + heightmap sampling).
/// </summary>
public static class TreeSpawnBurstJobs
{
    public static int ChunkRngSeed(int worldSeed, int chunkX, int chunkZ) =>
        unchecked(worldSeed * 73856093 ^ chunkX * 19349663 ^ chunkZ * 83492791);

    /// <summary>Sequential acceptance: terrain height, placement mask, and separation (writes <see cref="NewAcceptedWorld"/>).</summary>
    [BurstCompile]
    public struct PlanTreesForChunkPositionsBurstJob : IJob
    {
        [ReadOnly] public NativeArray<float3> GlobalAcceptedSnapshot;
        [ReadOnly] public NativeArray<uint> OccupancyWords;
        public int OccupancyWordCount;
        public int OccResolution;
        public int LogicalChunkAxis;
        public float WorldSize;
        public float3 WorldOrigin;
        [ReadOnly] public NativeArray<float> Heightmap;
        public int HeightmapResolution;
        public float TerrainHeightOffset;
        public int WorldSeed;
        public int ChunkX;
        public int ChunkZ;
        public int TreesTarget;
        public int MaxAttemptsPerTree;
        public float TerrainEdgeMargin;
        public float MinPathClearance;
        public float MinSeparationSq;
        public NativeList<float3> NewAcceptedWorld;

        public void Execute()
        {
            NewAcceptedWorld.Clear();
            if (!OccupancyWords.IsCreated || OccupancyWords.Length < OccupancyWordCount
                || !Heightmap.IsCreated || Heightmap.Length < HeightmapResolution * HeightmapResolution)
                return;

            uint seed = (uint)math.max(1, ChunkRngSeed(WorldSeed, ChunkX, ChunkZ));
            var rng = Random.CreateFromIndex(seed);

            int chunkAttempts = 0;
            int cap = TreesTarget * MaxAttemptsPerTree;
            int chunkAccepted = 0;

            while (chunkAccepted < TreesTarget && chunkAttempts < cap)
            {
                chunkAttempts++;
                if (!TryRandomUniformWorldXZInTerrainChunk(
                        WorldOrigin, WorldSize, ChunkX, ChunkZ, LogicalChunkAxis, TerrainEdgeMargin, ref rng, out float3 xz))
                    break;

                float y = SampleHeightWorldBilinear(in Heightmap, HeightmapResolution, WorldSize, WorldOrigin, xz.x, xz.z);
                if (y < 0f)
                    continue;

                float3 p = new float3(xz.x, y + TerrainHeightOffset, xz.z);

                if (!IsDiskFreeWorldXZ(
                        in OccupancyWords, OccupancyWordCount, OccResolution, WorldSize, WorldOrigin,
                        p.x, p.z, MinPathClearance))
                    continue;

                if (!IsFarEnoughFromAllXZ(in GlobalAcceptedSnapshot, in NewAcceptedWorld, p, MinSeparationSq))
                    continue;

                NewAcceptedWorld.Add(p);
                chunkAccepted++;
            }
        }

        static bool IsFarEnoughFromAllXZ(
            in NativeArray<float3> globalSnap,
            in NativeList<float3> newInChunk,
            float3 candidate,
            float minSepSq)
        {
            float cx = candidate.x;
            float cz = candidate.z;
            for (int i = 0; i < globalSnap.Length; i++)
            {
                float3 o = globalSnap[i];
                float dx = cx - o.x;
                float dz = cz - o.z;
                if (dx * dx + dz * dz < minSepSq)
                    return false;
            }

            for (int i = 0; i < newInChunk.Length; i++)
            {
                float3 o = newInChunk[i];
                float dx = cx - o.x;
                float dz = cz - o.z;
                if (dx * dx + dz * dz < minSepSq)
                    return false;
            }

            return true;
        }

        static bool TryRandomUniformWorldXZInTerrainChunk(
            float3 worldOrigin,
            float worldSize,
            int chunkX,
            int chunkZ,
            int logicalChunkAxis,
            float edgeMargin,
            ref Random rng,
            out float3 xz)
        {
            xz = default;
            int axis = math.max(1, logicalChunkAxis);
            chunkX = math.clamp(chunkX, 0, axis - 1);
            chunkZ = math.clamp(chunkZ, 0, axis - 1);

            float cw = worldSize / axis;
            float inset = math.max(0f, edgeMargin);

            float relMinX = chunkX * cw;
            float relMaxX = (chunkX + 1) * cw;
            float relMinZ = chunkZ * cw;
            float relMaxZ = (chunkZ + 1) * cw;

            relMinX = math.max(relMinX, inset);
            relMaxX = math.min(relMaxX, worldSize - inset);
            relMinZ = math.max(relMinZ, inset);
            relMaxZ = math.min(relMaxZ, worldSize - inset);

            if (relMinX >= relMaxX || relMinZ >= relMaxZ)
                return false;

            float rx = rng.NextFloat(relMinX, relMaxX);
            float rz = rng.NextFloat(relMinZ, relMaxZ);
            xz = new float3(worldOrigin.x - worldSize * 0.5f + rx, 0f, worldOrigin.z - worldSize * 0.5f + rz);
            return true;
        }

        static float SampleHeightWorldBilinear(
            in NativeArray<float> heightmap,
            int hr,
            float worldSize,
            float3 worldOrigin,
            float worldX,
            float worldZ)
        {
            if (hr < 2 || !heightmap.IsCreated || heightmap.Length < hr * hr)
                return -1f;

            float fx = (worldX - worldOrigin.x + worldSize * 0.5f) / worldSize * (hr - 1);
            float fz = (worldZ - worldOrigin.z + worldSize * 0.5f) / worldSize * (hr - 1);
            int ix = math.clamp((int)math.floor(fx), 0, hr - 2);
            int iz = math.clamp((int)math.floor(fz), 0, hr - 2);
            float tx = fx - ix;
            float tz = fz - iz;

            float h00 = heightmap[iz * hr + ix];
            float h10 = heightmap[iz * hr + (ix + 1)];
            float h01 = heightmap[(iz + 1) * hr + ix];
            float h11 = heightmap[(iz + 1) * hr + (ix + 1)];
            return math.lerp(math.lerp(h00, h10, tx), math.lerp(h01, h11, tx), tz);
        }

        static bool IsDiskFreeWorldXZ(
            in NativeArray<uint> occWords,
            int wordCount,
            int resolution,
            float worldSize,
            float3 worldOrigin,
            float worldX,
            float worldZ,
            float radiusWorld)
        {
            if (!occWords.IsCreated || radiusWorld < 0f || resolution < 1)
                return false;

            float cell = worldSize / resolution;
            float margin = cell * 0.501f;
            float r = radiusWorld + margin;
            float rSq = r * r;

            if (!TryGetCellRangeForWorldDisk(worldSize, worldOrigin, resolution, worldX, worldZ, r, out int x0, out int x1, out int z0, out int z1))
                return false;

            for (int z = z0; z <= z1; z++)
            {
                int row = z * resolution;
                for (int x = x0; x <= x1; x++)
                {
                    int linear = row + x;
                    if (IsCellBlockedByLinearIndex(in occWords, wordCount, linear))
                    {
                        float cx = CellCenterWorldX(worldOrigin, worldSize, resolution, x);
                        float cz = CellCenterWorldZ(worldOrigin, worldSize, resolution, z);
                        float dx = worldX - cx;
                        float dz = worldZ - cz;
                        if (dx * dx + dz * dz <= rSq)
                            return false;
                    }
                }
            }

            return true;
        }

        static float CellCenterWorldX(float3 worldOrigin, float worldSize, int resolution, int ix) =>
            worldOrigin.x + ((ix + 0.5f) / resolution - 0.5f) * worldSize;

        static float CellCenterWorldZ(float3 worldOrigin, float worldSize, int resolution, int iz) =>
            worldOrigin.z + ((iz + 0.5f) / resolution - 0.5f) * worldSize;

        static bool TryGetCellRangeForWorldDisk(
            float worldSize,
            float3 worldOrigin,
            int resolution,
            float wx,
            float wz,
            float radius,
            out int x0,
            out int x1,
            out int z0,
            out int z1)
        {
            float minX = wx - radius;
            float maxX = wx + radius;
            float minZ = wz - radius;
            float maxZ = wz + radius;
            return WorldToCellInclusive(worldSize, worldOrigin, resolution, minX, minZ, maxX, maxZ, out x0, out z0, out x1, out z1);
        }

        static bool WorldToCellInclusive(
            float worldSize,
            float3 worldOrigin,
            int resolution,
            float minX,
            float minZ,
            float maxX,
            float maxZ,
            out int x0,
            out int z0,
            out int x1,
            out int z1)
        {
            float half = worldSize * 0.5f;
            float ox = worldOrigin.x;
            float oz = worldOrigin.z;

            float fx0 = (minX - ox + half) / worldSize * resolution - 0.5f;
            float fx1 = (maxX - ox + half) / worldSize * resolution - 0.5f;
            float fz0 = (minZ - oz + half) / worldSize * resolution - 0.5f;
            float fz1 = (maxZ - oz + half) / worldSize * resolution - 0.5f;

            x0 = math.clamp((int)math.floor(math.min(fx0, fx1)), 0, resolution - 1);
            x1 = math.clamp((int)math.ceil(math.max(fx0, fx1)) - 1, 0, resolution - 1);
            z0 = math.clamp((int)math.floor(math.min(fz0, fz1)), 0, resolution - 1);
            z1 = math.clamp((int)math.ceil(math.max(fz0, fz1)) - 1, 0, resolution - 1);
            return true;
        }

        static bool IsCellBlockedByLinearIndex(in NativeArray<uint> words, int wordCount, int linear)
        {
            int word = linear >> 5;
            int bit = linear & 31;
            if ((uint)word >= (uint)wordCount)
                return true;
            uint mask = 1u << bit;
            return (words[word] & mask) != 0u;
        }
    }

    /// <summary>
    /// Parallel phase: assigns variant, yaw, scale, and packs <see cref="TreeInstanceData"/> for accepted positions.
    /// </summary>
    [BurstCompile]
    public struct FinalizeTreeInstancesParallelJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        public NativeArray<TreeInstanceData> Out;
        public int WorldSeed;
        public int ChunkX;
        public int ChunkZ;
        public int VariantCount;
        [ReadOnly] public NativeArray<float> VariantWeights;
        public float ScaleMin;
        public float ScaleMax;

        public void Execute(int i)
        {
            if (!Positions.IsCreated || !Out.IsCreated || (uint)i >= (uint)Positions.Length || Out.Length != Positions.Length)
                return;

            uint seed = (uint)math.max(1, unchecked(ChunkRngSeed(WorldSeed, ChunkX, ChunkZ) + i * 374761393));
            var rng = Random.CreateFromIndex(seed);
            float3 pos = Positions[i];
            int variantId = PickVariantIndex(ref rng, in VariantWeights, VariantCount);
            float yawDeg = rng.NextFloat(360f);
            float scale = rng.NextFloat(ScaleMin, ScaleMax);
            quaternion rot = quaternion.RotateY(math.radians(yawDeg));
            Out[i] = new TreeInstanceData
            {
                Position = pos,
                Rotation = rot,
                Scale = scale,
                VariantId = variantId
            };
        }

        static int PickVariantIndex(ref Random rng, in NativeArray<float> weights, int count)
        {
            if (count < 1 || !weights.IsCreated || weights.Length < count)
                return 0;

            float total = 0f;
            for (int j = 0; j < count; j++)
                total += math.max(0f, weights[j]);

            if (total > 1e-6f)
            {
                float r = rng.NextFloat(total);
                for (int j = 0; j < count; j++)
                {
                    float w = math.max(0f, weights[j]);
                    r -= w;
                    if (r <= 0f)
                        return j;
                }

                return count - 1;
            }

            return (int)(rng.NextUInt() % (uint)count);
        }
    }
}
