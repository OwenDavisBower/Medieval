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

        var gen = Object.FindFirstObjectByType<TerrainGenerator>();
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

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Random.Range(0f, 1f)) * regionRadius;
            float x = Mathf.Cos(angle) * r;
            float z = Mathf.Sin(angle) * r;

            Vector3 p = TerrainSpawnUtility.GetWorldPositionOnTerrain(basePos + new Vector3(x, 0f, z));
            if (p.y < 0f)
                continue;

            bool ok = true;
            for (int i = 0; i < accepted.Count; i++)
            {
                float dx = p.x - accepted[i].x;
                float dz = p.z - accepted[i].z;
                if (dx * dx + dz * dz < minSepSq)
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
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
