using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Defers terrain <see cref="TerrainGenerator"/> startup, runs <see cref="TerrainGenerator.Regenerate"/>, then spawns
/// content after terrain is ready. <see cref="seed"/> drives terrain procedural noise and spawn <see cref="Random"/> state.
/// Settlements stream with the same 3×3 logical chunk window as terrain meshes.
/// </summary>
[DefaultExecutionOrder(-100)]
public class WorldGenerationCoordinator : MonoBehaviour
{
    const string DefaultSettlementPath = "Assets/Data/Spawning/MainScene_SettlementSpawn.asset";
    const string DefaultTreePath = "Assets/Data/Spawning/MainScene_TreeSpawn.asset";
    const string DefaultBanditPath = "Assets/Data/Spawning/MainScene_BanditCampSpawn.asset";
    const string DefaultRockSpawnPath = "Assets/Data/Spawning/MainScene_RockSpawn.asset";

    const float StreamingAnchorMoveEpsilonSqr = 0.25f;

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

    readonly List<Vector3> _plannedSettlementCenters = new List<Vector3>();
    readonly Dictionary<int, GameObject> _streamingSettlements = new Dictionary<int, GameObject>();
    readonly HashSet<Vector2Int> _settlementChunksScratch = new HashSet<Vector2Int>();
    Vector3 _lastSettlementStreamAnchor = new Vector3(float.NaN, float.NaN, float.NaN);

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
        DestroyAllStreamingSettlements();
        _placementMask?.Dispose();
        _placementMask = null;
    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
            return;
        SyncStreamingSettlements();
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

    void DestroyAllStreamingSettlements()
    {
        foreach (var kv in _streamingSettlements)
        {
            if (kv.Value == null)
                continue;
            var b = kv.Value.GetComponent<SettlementBuilder>();
            b?.ClearPlacementBurnsFromMask(_placementMask);
            Destroy(kv.Value);
        }

        _streamingSettlements.Clear();
    }

    void SyncStreamingSettlements()
    {
        var gen = ResolveTerrain();
        if (gen == null || !gen.IsTerrainReady || _placementMask == null || settlementSpawn == null)
            return;
        if (_plannedSettlementCenters.Count == 0)
            return;

        var anchorTr = gen.StreamingAnchorOrCamera;
        if (anchorTr == null)
            return;

        Vector3 p = anchorTr.position;
        if (!float.IsNaN(_lastSettlementStreamAnchor.x))
        {
            float dx = p.x - _lastSettlementStreamAnchor.x;
            float dy = p.y - _lastSettlementStreamAnchor.y;
            float dz = p.z - _lastSettlementStreamAnchor.z;
            if (dx * dx + dy * dy + dz * dz < StreamingAnchorMoveEpsilonSqr)
                return;
        }

        _lastSettlementStreamAnchor = p;

        Vector3 origin = gen.transform.position;
        float ws = gen.worldSize;
        int axis = gen.chunkCount;
        int pool = TerrainLogicalChunkWindow.DefaultStreamingPoolSide;

        var win = TerrainLogicalChunkWindow.ComputeWindowOrigin(p, origin, ws, axis, pool);
        TerrainLogicalChunkWindow.CollectWindowChunks(win.x, win.y, pool, axis, _settlementChunksScratch);

        for (int i = 0; i < _plannedSettlementCenters.Count; i++)
        {
            Vector3 nominal = _plannedSettlementCenters[i];
            var home = TerrainLogicalChunkWindow.WorldXZToChunk(origin, ws, axis, nominal.x, nominal.z);
            bool inWin = _settlementChunksScratch.Contains(home);

            if (_streamingSettlements.TryGetValue(i, out var go) && go != null)
            {
                if (!inWin)
                {
                    var b = go.GetComponent<SettlementBuilder>();
                    b?.ClearPlacementBurnsFromMask(_placementMask);
                    Destroy(go);
                    _streamingSettlements.Remove(i);
                }
            }
            else if (inWin)
            {
                var spawned = _settlementSpawning.SpawnSettlementAt(settlementSpawn, nominal, _placementMask, i);
                if (spawned != null)
                    _streamingSettlements[i] = spawned;
            }
        }
    }

    void RunSpawnSequence()
    {
        var gen = ResolveTerrain();
        if (gen == null || !gen.IsTerrainReady)
            return;

        Random.InitState(seed);

        DestroyAllStreamingSettlements();

        _banditCampSpawning.Reset();
        _treeSpawning.Reset();
        _meshSpawning.Reset();

        _placementMask?.Dispose();
        _placementMask = new ProceduralPlacementMask();
        _placementMask.Allocate(gen);
        float pathStamp = ComputePathOccupancyStampMeters(gen);
        _placementMask.StampPathFromTerrain(gen, pathStamp);

        _plannedSettlementCenters.Clear();
        _settlementSpawning.PlanSettlementCenters(settlementSpawn, _placementMask, _plannedSettlementCenters);

        if (settlementSpawn != null)
        {
            float r = Mathf.Max(0.1f, settlementSpawn.SettlementCenterFootprintRadius);
            for (int i = 0; i < _plannedSettlementCenters.Count; i++)
            {
                Vector3 p = _plannedSettlementCenters[i];
                _placementMask.BurnDiskWorldXZ(p.x, p.z, r);
            }
        }

        _banditCampSpawning.SpawnCamps(banditCampSpawn, _placementMask, _plannedSettlementCenters);
        if (treeSpawn != null)
            _treeSpawning.TrySpawnTrees(treeSpawn, treeSpawnParent, gen, _placementMask);

        if (meshSpawn != null && meshSpawn.HasRenderableVariants)
        {
            var rockRenderer = GetComponent<RockIndirectRenderer>();
            if (rockRenderer == null)
                rockRenderer = gameObject.AddComponent<RockIndirectRenderer>();
            _meshSpawning.TrySpawnMeshes(meshSpawn, rockRenderer, gen, _placementMask);
        }

        _lastSettlementStreamAnchor = new Vector3(float.NaN, float.NaN, float.NaN);
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
