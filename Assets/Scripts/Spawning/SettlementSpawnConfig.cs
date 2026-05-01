using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct SettlementBuildingSpawnEntry
{
    [Tooltip("Prefab instantiated for this building type.")]
    public GameObject prefab;
    [Min(0)]
    public int minCount;
    [Min(0)]
    public int maxCount;
    [Tooltip("Minimum horizontal distance from the settlement center (XZ).")]
    public float radiusMin;
    [Tooltip("Maximum horizontal distance from the settlement center (XZ).")]
    public float radiusMax;
    [Tooltip("Disk radius checked against the procedural placement mask at each candidate site.")]
    public float placementRadius;
    [Tooltip("If true, spawns villagers near each instance (same behavior as former cabin placement).")]
    public bool spawnVillagersHere;
    [Tooltip("Bandit camp only: use Euler (-90, yaw, 0) instead of (0, yaw, 0) when instantiating (common FBX axis fix). Ignored by settlement building spawn.")]
    public bool applyMinus90XRotation;
}

[CreateAssetMenu(fileName = "SettlementSpawnConfig", menuName = "Medieval/Spawning/Settlement Spawn Config")]
public class SettlementSpawnConfig : ScriptableObject
{
    [SerializeField] SettlementBuildingSpawnEntry[] buildings;
    [Tooltip("How many settlements to try placing in each logical terrain chunk (see TerrainGenerator.chunkCount). 0 disables settlement planning.")]
    [SerializeField, Min(0)] int settlementsPerLogicalChunk = 1;
    [Tooltip("Inset from procedural terrain edges when picking settlement centers (XZ).")]
    [SerializeField] float terrainEdgeMargin = 64f;
    [Tooltip("Minimum distance between settlement centers (XZ).")]
    [SerializeField] float minSettlementSeparation = 30f;
    [SerializeField] int maxSpawnAttemptsPerSettlement = 120;
    [Tooltip("XZ disk radius for procedural placement mask: settlement center must be free of path and prior burns.")]
    [SerializeField] float settlementCenterFootprintRadius = 14f;
    [Tooltip("Minimum horizontal distance from the nearest path centerline (meters). Keeps settlements off the path mesh; should be ≥ path occupancy stamp.")]
    [SerializeField] float minDistanceFromPathMeters = 12f;
    [Tooltip("Maximum horizontal distance from the nearest path (meters). Settlements must lie within this radius to count as near a path.")]
    [SerializeField] float maxDistanceFromPathMeters = 56f;

    [Header("Settlement paths (splat R)")]
    [Tooltip("Path ring runs this far outside each structure's horizontal bounds (world meters).")]
    [SerializeField] float pathRingOutsideFootprint = 1.25f;
    [Tooltip("Approximate spacing between samples along connecting paths (world meters).")]
    [SerializeField] float pathSegmentStepMeters = 1.4f;
    [Tooltip("Max lateral wobble for organic corridors (world meters).")]
    [SerializeField] float pathWobbleAmplitude = 1.1f;

    public IReadOnlyList<SettlementBuildingSpawnEntry> Buildings => buildings;
    public int SettlementsPerLogicalChunk => settlementsPerLogicalChunk;
    public float TerrainEdgeMargin => terrainEdgeMargin;
    public float MinSettlementSeparation => minSettlementSeparation;
    public int MaxSpawnAttemptsPerSettlement => maxSpawnAttemptsPerSettlement;
    public float SettlementCenterFootprintRadius => settlementCenterFootprintRadius;
    /// <summary>Lower bound for path-distance ring; falls back if unset in older assets.</summary>
    public float MinDistanceFromPathMeters => minDistanceFromPathMeters > 0f ? minDistanceFromPathMeters : 12f;
    /// <summary>Upper bound for path-distance ring; falls back if unset in older assets.</summary>
    public float MaxDistanceFromPathMeters => maxDistanceFromPathMeters > 0f ? maxDistanceFromPathMeters : 56f;

    public float PathRingOutsideFootprint => pathRingOutsideFootprint > 0f ? pathRingOutsideFootprint : 1.25f;
    public float PathSegmentStepMeters => pathSegmentStepMeters > 0f ? pathSegmentStepMeters : 1.4f;
    public float PathWobbleAmplitude => pathWobbleAmplitude >= 0f ? pathWobbleAmplitude : 1.1f;
}
