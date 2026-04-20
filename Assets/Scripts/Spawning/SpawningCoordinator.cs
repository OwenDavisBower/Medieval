using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// After procedural terrain is ready, runs spawners in order: settlements, bandit camps, trees, GPU rocks.
/// </summary>
public class SpawningCoordinator : MonoBehaviour
{
    const string DefaultSettlementPath = "Assets/Data/Spawning/MainScene_SettlementSpawn.asset";
    const string DefaultTreePath = "Assets/Data/Spawning/MainScene_TreeSpawn.asset";
    const string DefaultBanditPath = "Assets/Data/Spawning/MainScene_BanditCampSpawn.asset";
    const string DefaultRockSpawnPath = "Assets/Data/Spawning/MainScene_RockSpawn.asset";

    [SerializeField] SettlementSpawnConfig settlementSpawn;
    [SerializeField] TreeSpawnConfig treeSpawn;
    [SerializeField] BanditCampSpawnConfig banditCampSpawn;
    [SerializeField] RockSpawnConfig rockSpawn;
    [Tooltip("Optional parent for instantiated trees; may be null.")]
    [SerializeField] Transform treeSpawnParent;

    readonly SettlementSpawning _settlementSpawning = new SettlementSpawning();
    readonly TreeSpawning _treeSpawning = new TreeSpawning();
    readonly BanditCampSpawning _banditCampSpawning = new BanditCampSpawning();
    readonly RockSpawning _rockSpawning = new RockSpawning();

#if UNITY_EDITOR
    void OnValidate()
    {
        if (settlementSpawn == null)
            settlementSpawn = AssetDatabase.LoadAssetAtPath<SettlementSpawnConfig>(DefaultSettlementPath);
        if (treeSpawn == null)
            treeSpawn = AssetDatabase.LoadAssetAtPath<TreeSpawnConfig>(DefaultTreePath);
        if (banditCampSpawn == null)
            banditCampSpawn = AssetDatabase.LoadAssetAtPath<BanditCampSpawnConfig>(DefaultBanditPath);
        if (rockSpawn == null)
            rockSpawn = AssetDatabase.LoadAssetAtPath<RockSpawnConfig>(DefaultRockSpawnPath);
    }
#endif

    void OnEnable()
    {
        TerrainGenerator.TerrainGenerated += OnTerrainGenerated;
    }

    void OnDisable()
    {
        TerrainGenerator.TerrainGenerated -= OnTerrainGenerated;
    }

    void Start() => RunSpawnSequence();

    void OnTerrainGenerated(TerrainGenerator _) => RunSpawnSequence();

    void RunSpawnSequence()
    {
        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        _settlementSpawning.TrySpawnSettlements(settlementSpawn);
        _banditCampSpawning.SpawnCamps(banditCampSpawn);
        _treeSpawning.TrySpawnTrees(treeSpawn, treeSpawnParent);

        if (rockSpawn != null && rockSpawn.RockMesh != null && rockSpawn.RockMaterial != null)
        {
            var rockRenderer = GetComponent<RockIndirectRenderer>();
            if (rockRenderer == null)
                rockRenderer = gameObject.AddComponent<RockIndirectRenderer>();
            _rockSpawning.TrySpawnRocks(rockSpawn, rockRenderer);
        }
    }
}
