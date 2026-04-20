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

    public GameObject CabinPrefab => cabinPrefab;
    public GameObject FarmPrefab => farmPrefab;
    public GameObject VillagerPrefab => villagerPrefab;
    public int SettlementCount => settlementCount;
    public float SpawnRadius => spawnRadius;
    public Vector3 SpawnOrigin => spawnOrigin;
    public float TerrainEdgeMargin => terrainEdgeMargin;
    public float MinSettlementSeparation => minSettlementSeparation;
    public int MaxSpawnAttemptsPerSettlement => maxSpawnAttemptsPerSettlement;
}
