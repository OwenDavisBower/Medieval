using UnityEngine;

[CreateAssetMenu(fileName = "TreeSpawnConfig", menuName = "Medieval/Spawning/Tree Spawn Config")]
public class TreeSpawnConfig : ScriptableObject
{
    [SerializeField] GameObject treePrefab;
    [SerializeField] int treeCount = 200;
    [SerializeField] float regionRadius = 100f;
    [SerializeField] float minSeparation = 6f;
    [SerializeField] int maxAttemptsPerTree = 80;
    [Tooltip("-1 = use terrain flat corridor (TerrainGenerator.flatRadius + 2 world units). Otherwise minimum distance from the path spline centerline.")]
    [SerializeField] float pathClearance = -1f;
    [Tooltip("Disk center for tree placement (XZ offset added to random samples). Y from terrain.")]
    [SerializeField] Vector3 regionCenter;

    public GameObject TreePrefab => treePrefab;
    public int TreeCount => treeCount;
    public float RegionRadius => regionRadius;
    public float MinSeparation => minSeparation;
    public int MaxAttemptsPerTree => maxAttemptsPerTree;
    public float PathClearance => pathClearance;
    public Vector3 RegionCenter => regionCenter;
}
