using UnityEngine;

[System.Serializable]
public struct TreeSpawnWeightedPrefab
{
    [SerializeField] GameObject prefab;
    [SerializeField, Min(0f)] float weight;

    public GameObject Prefab => prefab;
    public float Weight => weight;
}

[CreateAssetMenu(fileName = "TreeSpawnConfig", menuName = "Medieval/Spawning/Tree Spawn Config")]
public class TreeSpawnConfig : ScriptableObject
{
    [Tooltip("Several tree prefabs with relative spawn weights. Leave empty to use Tree Prefab only.")]
    [SerializeField] TreeSpawnWeightedPrefab[] weightedTreePrefabs;
    [Tooltip("Used when Weighted Tree Prefabs is empty, or as fallback when no variant applies.")]
    [SerializeField] GameObject treePrefab;
    [SerializeField] int treeCount = 200;
    [Tooltip("Inset from procedural terrain edges when sampling tree positions (XZ).")]
    [SerializeField] float terrainEdgeMargin = 8f;
    [SerializeField] float minSeparation = 6f;
    [SerializeField] int maxAttemptsPerTree = 80;
    [Tooltip("-1 = use terrain flat corridor (TerrainGenerator.flatRadius + 2 world units). Otherwise minimum distance from the path spline centerline.")]
    [SerializeField] float pathClearance = -1f;
    [Tooltip("Radius burned into the procedural placement mask after each tree spawns (XZ).")]
    [SerializeField] float occupationFootprintRadius = 2.5f;

    public GameObject TreePrefab => treePrefab;

    /// <summary>Weighted entries; when empty or invalid, <see cref="TreePrefab"/> is used for every tree.</summary>
    public TreeSpawnWeightedPrefab[] WeightedTreePrefabs => weightedTreePrefabs;

    public bool HasSpawnableTreePrefab()
    {
        if (weightedTreePrefabs != null)
        {
            for (int i = 0; i < weightedTreePrefabs.Length; i++)
            {
                if (weightedTreePrefabs[i].Prefab != null)
                    return true;
            }
        }

        return treePrefab != null;
    }

    /// <summary>Picks a tree prefab using relative weights; if all weights are zero, picks uniformly among non-null prefabs; otherwise uses <see cref="TreePrefab"/>.</summary>
    public GameObject PickTreePrefab()
    {
        if (weightedTreePrefabs != null && weightedTreePrefabs.Length > 0)
        {
            float total = 0f;
            int nonNullCount = 0;
            for (int i = 0; i < weightedTreePrefabs.Length; i++)
            {
                var e = weightedTreePrefabs[i];
                if (e.Prefab == null)
                    continue;
                nonNullCount++;
                if (e.Weight > 0f)
                    total += e.Weight;
            }

            if (nonNullCount > 0 && total > 0f)
            {
                float r = Random.Range(0f, total);
                for (int i = 0; i < weightedTreePrefabs.Length; i++)
                {
                    var e = weightedTreePrefabs[i];
                    if (e.Prefab == null || e.Weight <= 0f)
                        continue;
                    r -= e.Weight;
                    if (r <= 0f)
                        return e.Prefab;
                }
            }
            else if (nonNullCount > 0)
            {
                int pick = Random.Range(0, nonNullCount);
                for (int i = 0; i < weightedTreePrefabs.Length; i++)
                {
                    if (weightedTreePrefabs[i].Prefab == null)
                        continue;
                    if (pick == 0)
                        return weightedTreePrefabs[i].Prefab;
                    pick--;
                }
            }
        }

        return treePrefab;
    }
    public int TreeCount => treeCount;
    public float TerrainEdgeMargin => terrainEdgeMargin;
    public float MinSeparation => minSeparation;
    public int MaxAttemptsPerTree => maxAttemptsPerTree;
    public float PathClearance => pathClearance;
    public float OccupationFootprintRadius => occupationFootprintRadius;

    void OnValidate()
    {
        terrainEdgeMargin = Mathf.Max(0f, terrainEdgeMargin);
    }
}
