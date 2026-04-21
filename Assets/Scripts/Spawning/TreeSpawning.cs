using System.Collections.Generic;
using UnityEngine;

public class TreeSpawning
{
    bool _spawned;

    public void TrySpawnTrees(TreeSpawnConfig config, Transform parent, TerrainGenerator gen, ProceduralPlacementMask placementMask)
    {
        if (config == null || _spawned || config.TreePrefab == null)
            return;

        if (gen == null || !gen.IsTerrainReady || placementMask == null)
            return;

        _spawned = true;

        float minPathClearance = config.PathClearance >= 0f
            ? config.PathClearance
            : gen.flatRadius + 2f;
        float treeBurnR = Mathf.Max(0.1f, config.OccupationFootprintRadius);
        float minSepSq = config.MinSeparation * config.MinSeparation;
        var accepted = new List<Vector3>(config.TreeCount);
        int totalAttempts = 0;
        int cap = config.TreeCount * config.MaxAttemptsPerTree;
        Vector3 basePos = config.RegionCenter;

        while (accepted.Count < config.TreeCount && totalAttempts < cap)
        {
            totalAttempts++;

            Vector3 p = TerrainSpawnUtility.GetWorldPositionOnTerrain(
                basePos + SpawnPlacementUtility.RandomUniformDiskOffsetXZ(config.RegionRadius));
            if (p.y < 0f)
                continue;

            if (!placementMask.IsDiskFreeWorldXZ(p.x, p.z, minPathClearance))
                continue;

            if (SpawnPlacementUtility.IsFarEnoughFromAllXZ(p, accepted, minSepSq))
                accepted.Add(p);
        }

        for (int i = 0; i < accepted.Count; i++)
        {
            float yaw = Random.Range(0f, 360f);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Object.Instantiate(config.TreePrefab, accepted[i], rot, parent);
        }

        placementMask.BurnDisksWorldXZ(accepted, treeBurnR);
    }
}
