using UnityEngine;

[CreateAssetMenu(fileName = "SettlementSpawnConfig", menuName = "Medieval/Spawning/Settlement Spawn Config")]
public class SettlementSpawnConfig : ScriptableObject
{
    [SerializeField] GameObject cabinPrefab;
    [SerializeField] GameObject farmPrefab;
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

    public GameObject CabinPrefab => cabinPrefab;
    public GameObject FarmPrefab => farmPrefab;
    public GameObject VillagerPrefab => villagerPrefab;
    public int SettlementCount => settlementCount;
    public float SpawnRadius => spawnRadius;
    public Vector3 SpawnOrigin => spawnOrigin;
    public float TerrainEdgeMargin => terrainEdgeMargin;
    public float MinSettlementSeparation => minSettlementSeparation;
    public int MaxSpawnAttemptsPerSettlement => maxSpawnAttemptsPerSettlement;
    public float SettlementCenterFootprintRadius => settlementCenterFootprintRadius;
}
