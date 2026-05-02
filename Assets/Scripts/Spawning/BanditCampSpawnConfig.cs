using UnityEngine;

[CreateAssetMenu(fileName = "BanditCampSpawnConfig", menuName = "Medieval/Spawning/Bandit Camp Spawn Config")]
public class BanditCampSpawnConfig : ScriptableObject
{
    [SerializeField] BanditCamp banditCampPrefab;
    [Tooltip("How many bandit camps to try placing in each logical terrain chunk. 0 disables camp planning.")]
    [SerializeField, Min(0)] int campsPerLogicalChunk = 1;
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

    [Header("Camp paths (splat R)")]
    [Tooltip("Path ring runs this far outside each structure's horizontal bounds (world meters).")]
    [SerializeField] float pathRingOutsideFootprint = 1.25f;
    [Tooltip("Approximate spacing between samples along connecting paths (world meters).")]
    [SerializeField] float pathSegmentStepMeters = 1.4f;
    [Tooltip("Max lateral wobble for organic corridors (world meters).")]
    [SerializeField] float pathWobbleAmplitude = 1.1f;

    public BanditCamp BanditCampPrefab => banditCampPrefab;
    public int CampsPerLogicalChunk => campsPerLogicalChunk;
    public float TerrainEdgeMargin => terrainEdgeMargin;
    public float MinDistanceFromSettlements => minDistanceFromSettlements;
    public float MinDistanceFromOtherCamps => minDistanceFromOtherCamps;
    public int MaxSpawnAttemptsPerCamp => maxSpawnAttemptsPerCamp;
    public float OccupationFootprintRadius => occupationFootprintRadius;
    public float OccupationBurnPadding => occupationBurnPadding;

    public float PathRingOutsideFootprint => pathRingOutsideFootprint > 0f ? pathRingOutsideFootprint : 1.25f;
    public float PathSegmentStepMeters => pathSegmentStepMeters > 0f ? pathSegmentStepMeters : 1.4f;
    public float PathWobbleAmplitude => pathWobbleAmplitude >= 0f ? pathWobbleAmplitude : 1.1f;

    void OnValidate()
    {
        terrainEdgeMargin = Mathf.Max(0f, terrainEdgeMargin);
    }
}
