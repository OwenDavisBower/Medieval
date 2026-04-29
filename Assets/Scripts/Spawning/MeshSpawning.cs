using System.Collections.Generic;
using UnityEngine;

public class MeshSpawning
{
    public void Reset() { }

    /// <summary>Builds the full rock seed list for the world; <see cref="RockIndirectRenderer"/> is updated from a chunk-filtered subset during streaming.</summary>
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
        int planned = 0;
        for (int i = 0; i < variantIndices.Length; i++)
        {
            MeshSpawnVariant v = config.GetVariant(variantIndices[i]);
            if (v != null)
                planned += Mathf.Max(0, v.instanceCount);
        }

        if (planned > 0)
            seeds.Capacity = Mathf.Max(seeds.Capacity, planned);
        Vector3 origin = config.RegionCenter;
        float margin = config.TerrainEdgeMargin;

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
                Vector3 xz = origin + SpawnPlacementUtility.RandomUniformDiskOffsetXZ(config.RegionRadius);
                if (margin > 0f)
                    xz = SpawnPlacementUtility.ClampWorldXZToTerrain(gen, xz, margin);

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
