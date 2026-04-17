using System.Collections.Generic;
using UnityEngine;

public class TreeSpawner : MonoBehaviour
{
    [SerializeField] GameObject treePrefab;
    [SerializeField] int treeCount = 120;
    [SerializeField] float regionRadius = 200f;
    [SerializeField] float minSeparation = 12f;
    [SerializeField] int maxAttemptsPerTree = 80;

    bool _spawned;

    public void TrySpawnTrees()
    {
        if (_spawned || treePrefab == null)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        _spawned = true;

        float minSepSq = minSeparation * minSeparation;
        var accepted = new List<Vector3>(treeCount);
        int totalAttempts = 0;
        int cap = treeCount * maxAttemptsPerTree;
        Vector3 basePos = transform.position;

        while (accepted.Count < treeCount && totalAttempts < cap)
        {
            totalAttempts++;

            Vector3 p = TerrainSpawnUtility.GetWorldPositionOnTerrain(
                basePos + SpawnPlacementUtility.RandomUniformDiskOffsetXZ(regionRadius));
            if (p.y < 0f)
                continue;

            if (SpawnPlacementUtility.IsFarEnoughFromAllXZ(p, accepted, minSepSq))
                accepted.Add(p);
        }

        for (int i = 0; i < accepted.Count; i++)
        {
            float yaw = Random.Range(0f, 360f);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Instantiate(treePrefab, accepted[i], rot, transform);
        }
    }
}
