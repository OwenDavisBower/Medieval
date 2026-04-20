using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Defers terrain <see cref="TerrainGenerator"/> startup, runs <see cref="TerrainGenerator.Regenerate"/>, then spawns
/// content after terrain is ready. <see cref="seed"/> drives terrain procedural noise and spawn <see cref="Random"/> state.
/// </summary>
[DefaultExecutionOrder(-100)]
public class WorldGenerationCoordinator : MonoBehaviour
{
    const string DefaultSettlementPath = "Assets/Data/Spawning/MainScene_SettlementSpawn.asset";
    const string DefaultTreePath = "Assets/Data/Spawning/MainScene_TreeSpawn.asset";
    const string DefaultBanditPath = "Assets/Data/Spawning/MainScene_BanditCampSpawn.asset";
    const string DefaultRockSpawnPath = "Assets/Data/Spawning/MainScene_RockSpawn.asset";

    [SerializeField] TerrainGenerator terrainGenerator;
    [Tooltip("Deterministic world seed: pushed to terrain before generate and used for spawn RNG.")]
    [SerializeField] int seed = 42;
    [SerializeField] SettlementSpawnConfig settlementSpawn;
    [SerializeField] TreeSpawnConfig treeSpawn;
    [SerializeField] BanditCampSpawnConfig banditCampSpawn;
    [SerializeField, FormerlySerializedAs("rockSpawn")]
    MeshSpawnConfig meshSpawn;
    [Tooltip("Optional parent for instantiated trees; may be null.")]
    [SerializeField] Transform treeSpawnParent;

    readonly SettlementSpawning _settlementSpawning = new SettlementSpawning();
    readonly TreeSpawning _treeSpawning = new TreeSpawning();
    readonly BanditCampSpawning _banditCampSpawning = new BanditCampSpawning();
    readonly MeshSpawning _meshSpawning = new MeshSpawning();

#if UNITY_EDITOR
    void OnValidate()
    {
        if (settlementSpawn == null)
            settlementSpawn = AssetDatabase.LoadAssetAtPath<SettlementSpawnConfig>(DefaultSettlementPath);
        if (treeSpawn == null)
            treeSpawn = AssetDatabase.LoadAssetAtPath<TreeSpawnConfig>(DefaultTreePath);
        if (banditCampSpawn == null)
            banditCampSpawn = AssetDatabase.LoadAssetAtPath<BanditCampSpawnConfig>(DefaultBanditPath);
        if (meshSpawn == null)
            meshSpawn = AssetDatabase.LoadAssetAtPath<MeshSpawnConfig>(DefaultRockSpawnPath);
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
        {
            gen.SetProceduralSeed(seed);
            gen.Regenerate();
        }
    }

    void OnTerrainGenerated(TerrainGenerator _) => RunSpawnSequence();

    TerrainGenerator ResolveTerrain() =>
        terrainGenerator != null ? terrainGenerator : TerrainGenerator.GetActiveOrFind();

    void RunSpawnSequence()
    {
        var gen = ResolveTerrain();
        if (gen == null || !gen.IsTerrainReady)
            return;

        Random.InitState(seed);

        _settlementSpawning.TrySpawnSettlements(settlementSpawn);
        _banditCampSpawning.SpawnCamps(banditCampSpawn);
        _treeSpawning.TrySpawnTrees(treeSpawn, treeSpawnParent);

        if (meshSpawn != null && meshSpawn.HasRenderableVariants)
        {
            var rockRenderer = GetComponent<RockIndirectRenderer>();
            if (rockRenderer == null)
                rockRenderer = gameObject.AddComponent<RockIndirectRenderer>();
            _meshSpawning.TrySpawnMeshes(meshSpawn, rockRenderer);
        }
    }
}
