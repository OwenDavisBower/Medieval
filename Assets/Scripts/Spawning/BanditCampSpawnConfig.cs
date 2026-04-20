using UnityEngine;

[CreateAssetMenu(fileName = "BanditCampSpawnConfig", menuName = "Medieval/Spawning/Bandit Camp Spawn Config")]
public class BanditCampSpawnConfig : ScriptableObject
{
    [SerializeField] BanditCamp banditCampPrefab;
    [SerializeField] int campCount = 3;
    [SerializeField] float spawnRadius = 100f;
    [SerializeField] Vector3 spawnOrigin;

    [Header("Separation")]
    [Tooltip("Minimum XZ distance from settlement centers (SettlementBuilder transform).")]
    [SerializeField] float minDistanceFromSettlements = 30f;
    [Tooltip("Minimum XZ distance from other bandit camps.")]
    [SerializeField] float minDistanceFromOtherCamps = 20f;
    [SerializeField] int maxSpawnAttemptsPerCamp = 120;

    public BanditCamp BanditCampPrefab => banditCampPrefab;
    public int CampCount => campCount;
    public float SpawnRadius => spawnRadius;
    public Vector3 SpawnOrigin => spawnOrigin;
    public float MinDistanceFromSettlements => minDistanceFromSettlements;
    public float MinDistanceFromOtherCamps => minDistanceFromOtherCamps;
    public int MaxSpawnAttemptsPerCamp => maxSpawnAttemptsPerCamp;
}
