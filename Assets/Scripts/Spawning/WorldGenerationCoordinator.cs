using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Defers terrain <see cref="TerrainGenerator"/> startup, runs <see cref="TerrainGenerator.Regenerate"/>, then spawns
/// content after terrain is ready. Procedural spawn RNG uses <see cref="TerrainGenerator.seed"/>.
/// </summary>
[DefaultExecutionOrder(-100)]
public class WorldGenerationCoordinator : MonoBehaviour
{
    const string DefaultSettlementPath = "Assets/Data/Spawning/MainScene_SettlementSpawn.asset";
    const string DefaultTreePath = "Assets/Data/Spawning/MainScene_TreeSpawn.asset";
    const string DefaultBanditPath = "Assets/Data/Spawning/MainScene_BanditCampSpawn.asset";
    const string DefaultRockSpawnPath = "Assets/Data/Spawning/MainScene_RockSpawn.asset";

    [SerializeField] TerrainGenerator terrainGenerator;
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

    void Awake()
    {
        ResolveTerrain()?.DeferInitialPipeline();
    }

    void OnEnable()
    {
        TerrainGenerator.TerrainGenerated += OnTerrainGenerated;
    }

    void OnDisable()
    {
        TerrainGenerator.TerrainGenerated -= OnTerrainGenerated;
    }

    void Start()
    {
        if (!Application.isPlaying)
            return;

        var gen = ResolveTerrain();
        if (gen != null)
            gen.Regenerate();
    }

    void OnTerrainGenerated(TerrainGenerator _) => RunSpawnSequence();

    TerrainGenerator ResolveTerrain() =>
        terrainGenerator != null ? terrainGenerator : TerrainGenerator.GetActiveOrFind();

    void RunSpawnSequence()
    {
        var gen = ResolveTerrain();
        if (gen == null || !gen.IsTerrainReady)
            return;

        Random.InitState(gen.seed);

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
