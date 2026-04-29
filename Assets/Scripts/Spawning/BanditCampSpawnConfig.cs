using UnityEngine;

[CreateAssetMenu(fileName = "BanditCampSpawnConfig", menuName = "Medieval/Spawning/Bandit Camp Spawn Config")]
public class BanditCampSpawnConfig : ScriptableObject
{
    [SerializeField] BanditCamp banditCampPrefab;
    [SerializeField] int campCount = 3;
    [Tooltip("Inset from procedural terrain edges when sampling camp positions (XZ).")]
    [SerializeField] float terrainEdgeMargin = 8f;

    [Header("Separation")]
    [Tooltip("Minimum XZ distance from settlement centers (SettlementBuilder transform).")]
    [SerializeField] float minDistanceFromSettlements = 30f;
    [Tooltip("Minimum XZ distance from other bandit camps.")]
    [SerializeField] float minDistanceFromOtherCamps = 20f;
    [SerializeField] int maxSpawnAttemptsPerCamp = 120;
    [Tooltip("XZ radius for mask queries and burning after a camp is placed.")]
    [SerializeField] float occupationFootprintRadius = 10f;
    [SerializeField] float occupationBurnPadding = 1f;

    public BanditCamp BanditCampPrefab => banditCampPrefab;
    public int CampCount => campCount;
    public float TerrainEdgeMargin => terrainEdgeMargin;
    public float MinDistanceFromSettlements => minDistanceFromSettlements;
    public float MinDistanceFromOtherCamps => minDistanceFromOtherCamps;
    public int MaxSpawnAttemptsPerCamp => maxSpawnAttemptsPerCamp;
    public float OccupationFootprintRadius => occupationFootprintRadius;
    public float OccupationBurnPadding => occupationBurnPadding;

    void OnValidate()
    {
        terrainEdgeMargin = Mathf.Max(0f, terrainEdgeMargin);
    }
}
