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

    /// <summary>Computes tree layout and burns the placement mask; streaming instantiates from <paramref name="outPlanned"/>.</summary>
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
        var accepted = new List<Vector3>(config.TreeCount);
        int totalAttempts = 0;
        int cap = config.TreeCount * config.MaxAttemptsPerTree;
        while (accepted.Count < config.TreeCount && totalAttempts < cap)
        {
            totalAttempts++;

            Vector3 p = TerrainSpawnUtility.GetWorldPositionOnTerrain(
                SpawnPlacementUtility.RandomUniformWorldXZInTerrain(gen, config.TerrainEdgeMargin));
            if (p.y < 0f)
                continue;

            if (!placementMask.IsDiskFreeWorldXZ(p.x, p.z, minPathClearance))
                continue;

            if (SpawnPlacementUtility.IsFarEnoughFromAllXZ(p, accepted, minSepSq))
                accepted.Add(p);
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
