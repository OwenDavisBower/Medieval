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
    [Tooltip("When >= 0, path corridor stamped into the placement mask uses at least this many meters from the path centerline. When -1, uses the max of bandit/tree/mesh auto clearances.")]
    [SerializeField] float pathOccupancyStampMeters = -1f;

    ProceduralPlacementMask _placementMask;
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

    void OnDestroy()
    {
        _placementMask?.Dispose();
        _placementMask = null;
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

        _placementMask?.Dispose();
        _placementMask = new ProceduralPlacementMask();
        _placementMask.Allocate(gen);
        float pathStamp = ComputePathOccupancyStampMeters(gen);
        _placementMask.StampPathFromTerrain(gen, pathStamp);

        _settlementSpawning.TrySpawnSettlements(settlementSpawn, _placementMask);
        _banditCampSpawning.SpawnCamps(banditCampSpawn, _placementMask);
        if (treeSpawn != null)
            _treeSpawning.TrySpawnTrees(treeSpawn, treeSpawnParent, gen, _placementMask);

        if (meshSpawn != null && meshSpawn.HasRenderableVariants)
        {
            var rockRenderer = GetComponent<RockIndirectRenderer>();
            if (rockRenderer == null)
                rockRenderer = gameObject.AddComponent<RockIndirectRenderer>();
            _placementMask.SyncToTexture();
            _meshSpawning.TrySpawnMeshes(meshSpawn, rockRenderer, gen, _placementMask);
        }
    }

    float ComputePathOccupancyStampMeters(TerrainGenerator gen)
    {
        const float banditPathClearance = 8f;
        const float meshAutoPathClearance = 4f;
        float treeAuto = gen.flatRadius + 2f;
        float treeClear = treeSpawn != null && treeSpawn.PathClearance >= 0f
            ? treeSpawn.PathClearance
            : treeAuto;
        float meshClear = meshSpawn != null && meshSpawn.PathClearance >= 0f
            ? meshSpawn.PathClearance
            : meshAutoPathClearance;
        float auto = Mathf.Max(banditPathClearance, treeClear, meshClear);
        return pathOccupancyStampMeters >= 0f ? Mathf.Max(auto, pathOccupancyStampMeters) : auto;
    }
}
