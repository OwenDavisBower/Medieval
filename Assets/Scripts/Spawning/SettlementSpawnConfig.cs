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
}

[CreateAssetMenu(fileName = "SettlementSpawnConfig", menuName = "Medieval/Spawning/Settlement Spawn Config")]
public class SettlementSpawnConfig : ScriptableObject
{
    [SerializeField] SettlementBuildingSpawnEntry[] buildings;
    [SerializeField] GameObject villagerPrefab;
    [SerializeField] int settlementCount = 3;
    [SerializeField] float spawnRadius = 200f;
    [SerializeField] Vector3 spawnOrigin;
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

    public IReadOnlyList<SettlementBuildingSpawnEntry> Buildings => buildings;
    public GameObject VillagerPrefab => villagerPrefab;
    public int SettlementCount => settlementCount;
    public float SpawnRadius => spawnRadius;
    public Vector3 SpawnOrigin => spawnOrigin;
    public float TerrainEdgeMargin => terrainEdgeMargin;
    public float MinSettlementSeparation => minSettlementSeparation;
    public int MaxSpawnAttemptsPerSettlement => maxSpawnAttemptsPerSettlement;
    public float SettlementCenterFootprintRadius => settlementCenterFootprintRadius;
    /// <summary>Lower bound for path-distance ring; falls back if unset in older assets.</summary>
    public float MinDistanceFromPathMeters => minDistanceFromPathMeters > 0f ? minDistanceFromPathMeters : 12f;
    /// <summary>Upper bound for path-distance ring; falls back if unset in older assets.</summary>
    public float MaxDistanceFromPathMeters => maxDistanceFromPathMeters > 0f ? maxDistanceFromPathMeters : 56f;
}
