using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class TreeSpawning
{
    public void Reset() { }

    /// <summary>
    /// Plans trees for one logical chunk; appends to <paramref name="appendPlanned"/> and <paramref name="acceptedSeparation"/>.
    /// Uses Burst <see cref="TreeSpawnBurstJobs.PlanTreesForChunkPositionsBurstJob"/> plus
    /// <see cref="TreeSpawnBurstJobs.FinalizeTreeInstancesParallelJob"/> for placement and instance data.
    /// </summary>
    public void PlanTreesForChunk(
        TreeSpawnConfig config,
        TerrainGenerator gen,
        ProceduralPlacementMask placementMask,
        int worldSeed,
        int chunkX,
        int chunkZ,
        List<Vector3> acceptedSeparation,
        List<TreeInstanceData> appendPlanned)
    {
        if (config == null || !config.HasSpawnableTreePrefab())
            return;
        if (gen == null || !gen.IsTerrainReady || placementMask == null || acceptedSeparation == null || appendPlanned == null)
            return;

        int vCount = config.GetBurstVariantCount();
        if (vCount < 1)
            return;

        int wordCount = placementMask.OccupancyWordCount;
        if (wordCount < 1)
            return;

        int hr = gen.worldResolution;
        int heightLen = hr * hr;
        if (heightLen < 1)
            return;

        float minPathClearance = config.PathClearance >= 0f
            ? config.PathClearance
            : gen.flatRadius + 2f;
        float treeBurnR = Mathf.Max(0.1f, config.OccupationFootprintRadius);
        float minSepSq = config.MinSeparation * config.MinSeparation;

        var globalSnap = new NativeArray<float3>(acceptedSeparation.Count, Allocator.TempJob);
        for (int i = 0; i < acceptedSeparation.Count; i++)
        {
            Vector3 p = acceptedSeparation[i];
            globalSnap[i] = new float3(p.x, p.y, p.z);
        }

        var occ = new NativeArray<uint>(wordCount, Allocator.TempJob);
        if (!placementMask.TryCopyCombinedOccupancyWords(occ))
        {
            globalSnap.Dispose();
            occ.Dispose();
            return;
        }

        var heights = new NativeArray<float>(heightLen, Allocator.TempJob);
        if (!gen.TryCopyHeightmap(heights))
        {
            globalSnap.Dispose();
            occ.Dispose();
            heights.Dispose();
            return;
        }

        var weights = new NativeArray<float>(vCount, Allocator.TempJob);
        config.CopyBurstVariantWeights(weights);

        var newAccepted = new NativeList<float3>(config.TreeCount, Allocator.TempJob);

        float3 origin = new float3(
            placementMask.WorldOrigin.x,
            placementMask.WorldOrigin.y,
            placementMask.WorldOrigin.z);

        var planJob = new TreeSpawnBurstJobs.PlanTreesForChunkPositionsBurstJob
        {
            GlobalAcceptedSnapshot = globalSnap,
            OccupancyWords = occ,
            OccupancyWordCount = wordCount,
            OccResolution = placementMask.Resolution,
            LogicalChunkAxis = Mathf.Max(1, gen.chunkCount),
            WorldSize = placementMask.WorldSize,
            WorldOrigin = origin,
            Heightmap = heights,
            HeightmapResolution = hr,
            TerrainHeightOffset = config.TerrainHeightOffset,
            WorldSeed = worldSeed,
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            TreesTarget = config.TreeCount,
            MaxAttemptsPerTree = config.MaxAttemptsPerTree,
            TerrainEdgeMargin = config.TerrainEdgeMargin,
            MinPathClearance = minPathClearance,
            MinSeparationSq = minSepSq,
            NewAcceptedWorld = newAccepted
        };

        planJob.Schedule().Complete();

        if (newAccepted.Length > 0)
        {
            var positions = new NativeArray<float3>(newAccepted.Length, Allocator.TempJob);
            for (int i = 0; i < newAccepted.Length; i++)
                positions[i] = newAccepted[i];

            var outChunk = new NativeArray<TreeInstanceData>(newAccepted.Length, Allocator.TempJob);
            var finalizeJob = new TreeSpawnBurstJobs.FinalizeTreeInstancesParallelJob
            {
                Positions = positions,
                Out = outChunk,
                WorldSeed = worldSeed,
                ChunkX = chunkX,
                ChunkZ = chunkZ,
                VariantCount = vCount,
                VariantWeights = weights,
                ScaleMin = config.InstanceScaleMin,
                ScaleMax = config.InstanceScaleMax
            };
            finalizeJob.Schedule(newAccepted.Length, 32).Complete();

            for (int i = 0; i < outChunk.Length; i++)
                appendPlanned.Add(outChunk[i]);

            outChunk.Dispose();
            positions.Dispose();
        }

        for (int i = 0; i < newAccepted.Length; i++)
        {
            float3 p = newAccepted[i];
            acceptedSeparation.Add(new Vector3(p.x, p.y, p.z));
        }

        if (newAccepted.Length > 0)
        {
            var burnScratch = new List<Vector3>(newAccepted.Length);
            for (int i = 0; i < newAccepted.Length; i++)
            {
                float3 p = newAccepted[i];
                burnScratch.Add(new Vector3(p.x, p.y, p.z));
            }

            placementMask.BurnDisksWorldXZ(burnScratch, treeBurnR);
        }

        newAccepted.Dispose();
        weights.Dispose();
        heights.Dispose();
        occ.Dispose();
        globalSnap.Dispose();
    }
}
