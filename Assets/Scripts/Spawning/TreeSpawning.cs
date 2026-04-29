using System.Collections.Generic;
using UnityEngine;

public struct PlannedTreeSpawn
{
    public Vector3 Position;
    public float YawDegrees;
    public GameObject Prefab;
}

public class TreeSpawning
{
    public void Reset() { }

    static int ChunkRngSeed(int worldSeed, int chunkX, int chunkZ) =>
        unchecked(worldSeed * 73856093 ^ chunkX * 19349663 ^ chunkZ * 83492791);

    /// <summary>
    /// Plans trees for one logical chunk; appends to <paramref name="appendPlanned"/> and <paramref name="acceptedSeparation"/>.
    /// Uses a per-chunk RNG derived from <paramref name="worldSeed"/> (restores global <see cref="Random.state"/>).
    /// Burns disks onto <paramref name="placementMask"/> for this chunk's trees.
    /// </summary>
    public void PlanTreesForChunk(
        TreeSpawnConfig config,
        TerrainGenerator gen,
        ProceduralPlacementMask placementMask,
        int worldSeed,
        int chunkX,
        int chunkZ,
        List<Vector3> acceptedSeparation,
        List<PlannedTreeSpawn> appendPlanned)
    {
        if (config == null || !config.HasSpawnableTreePrefab())
            return;
        if (gen == null || !gen.IsTerrainReady || placementMask == null || acceptedSeparation == null || appendPlanned == null)
            return;

        float minPathClearance = config.PathClearance >= 0f
            ? config.PathClearance
            : gen.flatRadius + 2f;
        float treeBurnR = Mathf.Max(0.1f, config.OccupationFootprintRadius);
        float minSepSq = config.MinSeparation * config.MinSeparation;
        int capPerChunk = config.TreeCount * config.MaxAttemptsPerTree;
        var newInChunk = new List<Vector3>(config.TreeCount);

        var prev = Random.state;
        Random.InitState(ChunkRngSeed(worldSeed, chunkX, chunkZ));
        try
        {
            int chunkAttempts = 0;
            int chunkAccepted = 0;
            while (chunkAccepted < config.TreeCount && chunkAttempts < capPerChunk)
            {
                chunkAttempts++;

                if (!SpawnPlacementUtility.TryRandomUniformWorldXZInTerrainChunk(gen, chunkX, chunkZ, config.TerrainEdgeMargin, out Vector3 xz))
                    break;

                Vector3 p = TerrainSpawnUtility.GetWorldPositionOnTerrain(xz);
                if (p.y < 0f)
                    continue;

                if (!placementMask.IsDiskFreeWorldXZ(p.x, p.z, minPathClearance))
                    continue;

                if (SpawnPlacementUtility.IsFarEnoughFromAllXZ(p, acceptedSeparation, minSepSq))
                {
                    acceptedSeparation.Add(p);
                    newInChunk.Add(p);
                    chunkAccepted++;
                }
            }

            for (int i = 0; i < newInChunk.Count; i++)
            {
                GameObject prefab = config.PickTreePrefab();
                if (prefab == null)
                    continue;

                float yaw = Random.Range(0f, 360f);
                appendPlanned.Add(new PlannedTreeSpawn
                {
                    Position = newInChunk[i],
                    YawDegrees = yaw,
                    Prefab = prefab
                });
            }
        }
        finally
        {
            Random.state = prev;
        }

        if (newInChunk.Count > 0)
            placementMask.BurnDisksWorldXZ(newInChunk, treeBurnR);
    }

    /// <summary>Computes tree layout and burns the placement mask; streaming instantiates from <paramref name="outPlanned"/>. <see cref="TreeSpawnConfig.TreeCount"/> is per logical terrain chunk.</summary>
    public void PlanTrees(TreeSpawnConfig config, TerrainGenerator gen, ProceduralPlacementMask placementMask, List<PlannedTreeSpawn> outPlanned)
    {
        outPlanned.Clear();
        if (config == null || !config.HasSpawnableTreePrefab())
            return;

        if (gen == null || !gen.IsTerrainReady || placementMask == null)
            return;

        float minPathClearance = config.PathClearance >= 0f
            ? config.PathClearance
            : gen.flatRadius + 2f;
        float treeBurnR = Mathf.Max(0.1f, config.OccupationFootprintRadius);
        float minSepSq = config.MinSeparation * config.MinSeparation;
        int axis = Mathf.Max(1, gen.chunkCount);
        int maxTrees = config.TreeCount * axis * axis;
        var accepted = new List<Vector3>(maxTrees);
        int capPerChunk = config.TreeCount * config.MaxAttemptsPerTree;

        for (int cz = 0; cz < axis; cz++)
        {
            for (int cx = 0; cx < axis; cx++)
            {
                int chunkAttempts = 0;
                int chunkAccepted = 0;
                while (chunkAccepted < config.TreeCount && chunkAttempts < capPerChunk)
                {
                    chunkAttempts++;

                    if (!SpawnPlacementUtility.TryRandomUniformWorldXZInTerrainChunk(gen, cx, cz, config.TerrainEdgeMargin, out Vector3 xz))
                        break;

                    Vector3 p = TerrainSpawnUtility.GetWorldPositionOnTerrain(xz);
                    if (p.y < 0f)
                        continue;

                    if (!placementMask.IsDiskFreeWorldXZ(p.x, p.z, minPathClearance))
                        continue;

                    if (SpawnPlacementUtility.IsFarEnoughFromAllXZ(p, accepted, minSepSq))
                    {
                        accepted.Add(p);
                        chunkAccepted++;
                    }
                }
            }
        }

        for (int i = 0; i < accepted.Count; i++)
        {
            GameObject prefab = config.PickTreePrefab();
            if (prefab == null)
                continue;

            float yaw = Random.Range(0f, 360f);
            outPlanned.Add(new PlannedTreeSpawn
            {
                Position = accepted[i],
                YawDegrees = yaw,
                Prefab = prefab
            });
        }

        placementMask.BurnDisksWorldXZ(accepted, treeBurnR);
    }

    public static GameObject SpawnTreeAt(PlannedTreeSpawn planned, Transform parent)
    {
        if (planned.Prefab == null)
            return null;
        var rot = Quaternion.Euler(0f, planned.YawDegrees, 0f);
        return Object.Instantiate(planned.Prefab, planned.Position, rot, parent);
    }
}
