using System.Collections.Generic;
using UnityEngine;

public class MeshSpawning
{
    public void Reset() { }

    static int ChunkRngSeed(int worldSeed, int chunkX, int chunkZ, int variantIndex) =>
        unchecked(worldSeed * 73856093 ^ chunkX * 19349663 ^ chunkZ * 83492791 ^ variantIndex * 50331653);

    /// <summary>Appends rock seeds for one logical chunk. Per-chunk / variant RNG from <paramref name="worldSeed"/>; restores global <see cref="Random.state"/>.</summary>
    public void TryCollectSeedsForChunk(
        MeshSpawnConfig config,
        TerrainGenerator gen,
        ProceduralPlacementMask placementMask,
        int worldSeed,
        int chunkX,
        int chunkZ,
        int[] variantIndices,
        List<RockInstanceSeed> seeds)
    {
        if (config == null || gen == null || !gen.IsTerrainReady || placementMask == null || variantIndices == null || seeds == null)
            return;

        float margin = config.TerrainEdgeMargin;
        var prev = Random.state;
        try
        {
            for (int vi = 0; vi < variantIndices.Length; vi++)
            {
                int variantIndex = variantIndices[vi];
                MeshSpawnVariant variant = config.GetVariant(variantIndex);
                if (variant == null)
                    continue;

                int target = Mathf.Max(0, variant.instanceCount);
                if (target == 0)
                    continue;

                Random.InitState(ChunkRngSeed(worldSeed, chunkX, chunkZ, variantIndex));
                int cap = target * config.MaxAttemptsPerInstance;
                int attempts = 0;
                int spawned = 0;

                while (spawned < target && attempts < cap)
                {
                    attempts++;
                    if (!SpawnPlacementUtility.TryRandomUniformWorldXZInTerrainChunk(gen, chunkX, chunkZ, margin, out Vector3 xz))
                        break;
                    Vector3 p = TerrainSpawnUtility.GetWorldPositionOnTerrain(xz, config.TerrainHeightOffset);
                    if (p.y < 0f)
                        continue;
                    if (placementMask.SampleFree01WorldXZ(p.x, p.z) < 0.5f)
                        continue;

                    float yaw = Random.Range(0f, Mathf.PI * 2f);
                    float scale = Random.Range(variant.minScale, variant.maxScale);
                    seeds.Add(new RockInstanceSeed
                    {
                        PositionAndYaw = new Vector4(p.x, p.y, p.z, yaw),
                        ScaleAndPad = new Vector4(scale, variantIndex, 0f, 0f)
                    });
                    spawned++;
                }
            }
        }
        finally
        {
            Random.state = prev;
        }
    }

    /// <summary>Builds the full rock seed list for the world (each variant's <see cref="MeshSpawnVariant.instanceCount"/> is per logical terrain chunk); <see cref="RockIndirectRenderer"/> is updated from a chunk-filtered subset during streaming.</summary>
    public bool TryPlanRockSeeds(MeshSpawnConfig config, TerrainGenerator gen, ProceduralPlacementMask placementMask, List<RockInstanceSeed> into)
    {
        if (into == null)
            return false;
        into.Clear();
        if (config == null || !config.HasRenderableVariants)
            return false;

        if (gen == null || !gen.IsTerrainReady || placementMask == null)
            return false;

        if (!TryBuildValidVariantIndices(config, out var variantIndices))
            return false;

        TryCollectSeeds(config, gen, placementMask, variantIndices, into);
        return into.Count > 0;
    }

    public bool TryGetRenderableVariantIndices(MeshSpawnConfig config, out int[] indices) =>
        TryBuildValidVariantIndices(config, out indices);

    static bool TryBuildValidVariantIndices(MeshSpawnConfig config, out int[] indices)
    {
        var list = new List<int>();
        for (int i = 0; i < config.VariantCount; i++)
        {
            MeshSpawnVariant v = config.GetVariant(i);
            if (v != null && v.mesh != null && v.material != null)
                list.Add(i);
        }

        if (list.Count == 0)
        {
            indices = null;
            return false;
        }

        indices = list.ToArray();
        return true;
    }

    static void TryCollectSeeds(MeshSpawnConfig config, TerrainGenerator gen, ProceduralPlacementMask placementMask, int[] variantIndices, List<RockInstanceSeed> seeds)
    {
        seeds.Clear();
        int axis = Mathf.Max(1, gen.chunkCount);
        int chunks = axis * axis;
        int planned = 0;
        for (int i = 0; i < variantIndices.Length; i++)
        {
            MeshSpawnVariant v = config.GetVariant(variantIndices[i]);
            if (v != null)
                planned += Mathf.Max(0, v.instanceCount) * chunks;
        }

        if (planned > 0)
            seeds.Capacity = Mathf.Max(seeds.Capacity, planned);
        float margin = config.TerrainEdgeMargin;

        for (int cz = 0; cz < axis; cz++)
        {
            for (int cx = 0; cx < axis; cx++)
            {
                for (int vi = 0; vi < variantIndices.Length; vi++)
                {
                    int variantIndex = variantIndices[vi];
                    MeshSpawnVariant variant = config.GetVariant(variantIndex);
                    if (variant == null)
                        continue;

                    int target = Mathf.Max(0, variant.instanceCount);
                    if (target == 0)
                        continue;

                    int cap = target * config.MaxAttemptsPerInstance;
                    int attempts = 0;
                    int spawned = 0;

                    while (spawned < target && attempts < cap)
                    {
                        attempts++;
                        if (!SpawnPlacementUtility.TryRandomUniformWorldXZInTerrainChunk(gen, cx, cz, margin, out Vector3 xz))
                            break;
                        Vector3 p = TerrainSpawnUtility.GetWorldPositionOnTerrain(xz, config.TerrainHeightOffset);
                        if (p.y < 0f)
                            continue;
                        if (placementMask.SampleFree01WorldXZ(p.x, p.z) < 0.5f)
                            continue;

                        float yaw = Random.Range(0f, Mathf.PI * 2f);
                        float scale = Random.Range(variant.minScale, variant.maxScale);
                        seeds.Add(new RockInstanceSeed
                        {
                            PositionAndYaw = new Vector4(p.x, p.y, p.z, yaw),
                            ScaleAndPad = new Vector4(scale, variantIndex, 0f, 0f)
                        });
                        spawned++;
                    }
                }
            }
        }
    }
}
