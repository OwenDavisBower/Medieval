using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Defers terrain <see cref="TerrainGenerator"/> startup, runs <see cref="TerrainGenerator.Regenerate"/>, then spawns
/// content after terrain is ready. <see cref="seed"/> drives terrain procedural noise and spawn <see cref="Random"/> state.
/// Settlements, trees, bandit camps, and rocks stream with the same 3×3 logical chunk window as terrain meshes.
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

    readonly List<PlannedTreeSpawn> _plannedTrees = new List<PlannedTreeSpawn>();
    readonly Dictionary<int, GameObject> _streamingTrees = new Dictionary<int, GameObject>();

    readonly List<Vector3> _plannedBanditCenters = new List<Vector3>();
    readonly Dictionary<int, GameObject> _streamingBandits = new Dictionary<int, GameObject>();

    readonly List<RockInstanceSeed> _plannedRockSeeds = new List<RockInstanceSeed>();
    readonly List<RockInstanceSeed> _streamingRocksScratch = new List<RockInstanceSeed>();
    RockIndirectRenderer _rockIndirectRenderer;

    readonly HashSet<Vector2Int> _streamingWindowChunksScratch = new HashSet<Vector2Int>();
    Vector3 _lastStreamAnchor = new Vector3(float.NaN, float.NaN, float.NaN);

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
        DestroyAllStreamingWorldContent();
        _placementMask?.Dispose();
        _placementMask = null;
    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
            return;
        SyncStreamingWorldContent();
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

    void DestroyAllStreamingWorldContent()
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

        foreach (var kv in _streamingTrees)
        {
            if (kv.Value != null)
                Destroy(kv.Value);
        }

        _streamingTrees.Clear();

        foreach (var kv in _streamingBandits)
        {
            if (kv.Value != null)
                Destroy(kv.Value);
        }

        _streamingBandits.Clear();

        _rockIndirectRenderer ??= GetComponent<RockIndirectRenderer>();
        _rockIndirectRenderer?.Initialize(null, null);
    }

    void SyncStreamingWorldContent()
    {
        var gen = ResolveTerrain();
        if (gen == null || !gen.IsTerrainReady)
            return;

        bool hasStreamingPlans = _plannedSettlementCenters.Count > 0
            || _plannedTrees.Count > 0
            || _plannedBanditCenters.Count > 0
            || _plannedRockSeeds.Count > 0;
        if (!hasStreamingPlans)
            return;

        var anchorTr = gen.StreamingAnchorOrCamera;
        if (anchorTr == null)
            return;

        Vector3 p = anchorTr.position;
        if (!float.IsNaN(_lastStreamAnchor.x))
        {
            float dx = p.x - _lastStreamAnchor.x;
            float dy = p.y - _lastStreamAnchor.y;
            float dz = p.z - _lastStreamAnchor.z;
            if (dx * dx + dy * dy + dz * dz < StreamingAnchorMoveEpsilonSqr)
                return;
        }

        _lastStreamAnchor = p;

        Vector3 origin = gen.transform.position;
        float ws = gen.worldSize;
        int axis = gen.chunkCount;
        int pool = TerrainLogicalChunkWindow.DefaultStreamingPoolSide;

        var win = TerrainLogicalChunkWindow.ComputeWindowOrigin(p, origin, ws, axis, pool);
        TerrainLogicalChunkWindow.CollectWindowChunks(win.x, win.y, pool, axis, _streamingWindowChunksScratch);

        if (settlementSpawn != null && _placementMask != null)
        {
            for (int i = 0; i < _plannedSettlementCenters.Count; i++)
            {
                Vector3 nominal = _plannedSettlementCenters[i];
                var home = TerrainLogicalChunkWindow.WorldXZToChunk(origin, ws, axis, nominal.x, nominal.z);
                bool inWin = _streamingWindowChunksScratch.Contains(home);

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

        for (int i = 0; i < _plannedTrees.Count; i++)
        {
            PlannedTreeSpawn planned = _plannedTrees[i];
            var home = TerrainLogicalChunkWindow.WorldXZToChunk(origin, ws, axis, planned.Position.x, planned.Position.z);
            bool inWin = _streamingWindowChunksScratch.Contains(home);

            if (_streamingTrees.TryGetValue(i, out var tgo) && tgo != null)
            {
                if (!inWin)
                {
                    Destroy(tgo);
                    _streamingTrees.Remove(i);
                }
            }
            else if (inWin)
            {
                GameObject spawned = TreeSpawning.SpawnTreeAt(planned, treeSpawnParent);
                if (spawned != null)
                    _streamingTrees[i] = spawned;
            }
        }

        if (banditCampSpawn != null)
        {
            for (int i = 0; i < _plannedBanditCenters.Count; i++)
            {
                Vector3 nominal = _plannedBanditCenters[i];
                var home = TerrainLogicalChunkWindow.WorldXZToChunk(origin, ws, axis, nominal.x, nominal.z);
                bool inWin = _streamingWindowChunksScratch.Contains(home);

                if (_streamingBandits.TryGetValue(i, out var bgo) && bgo != null)
                {
                    if (!inWin)
                    {
                        Destroy(bgo);
                        _streamingBandits.Remove(i);
                    }
                }
                else if (inWin)
                {
                    GameObject spawned = BanditCampSpawning.SpawnCampAt(banditCampSpawn, nominal);
                    if (spawned != null)
                        _streamingBandits[i] = spawned;
                }
            }
        }

        _rockIndirectRenderer ??= GetComponent<RockIndirectRenderer>();
        if (_rockIndirectRenderer != null && meshSpawn != null && meshSpawn.HasRenderableVariants && _plannedRockSeeds.Count > 0)
        {
            _streamingRocksScratch.Clear();
            for (int i = 0; i < _plannedRockSeeds.Count; i++)
            {
                RockInstanceSeed s = _plannedRockSeeds[i];
                var home = TerrainLogicalChunkWindow.WorldXZToChunk(origin, ws, axis, s.PositionAndYaw.x, s.PositionAndYaw.z);
                if (_streamingWindowChunksScratch.Contains(home))
                    _streamingRocksScratch.Add(s);
            }

            _rockIndirectRenderer.Initialize(meshSpawn, _streamingRocksScratch);
        }
        else if (_rockIndirectRenderer != null)
            _rockIndirectRenderer.Initialize(null, null);
    }

    void RunSpawnSequence()
    {
        var gen = ResolveTerrain();
        if (gen == null || !gen.IsTerrainReady)
            return;

        Random.InitState(seed);

        DestroyAllStreamingWorldContent();

        _banditCampSpawning.Reset();
        _treeSpawning.Reset();
        _meshSpawning.Reset();

        _plannedTrees.Clear();
        _plannedBanditCenters.Clear();
        _plannedRockSeeds.Clear();

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

        _banditCampSpawning.PlanCamps(banditCampSpawn, _placementMask, _plannedSettlementCenters, _plannedBanditCenters);
        if (treeSpawn != null)
            _treeSpawning.PlanTrees(treeSpawn, gen, _placementMask, _plannedTrees);

        _rockIndirectRenderer = GetComponent<RockIndirectRenderer>();
        if (meshSpawn != null && meshSpawn.HasRenderableVariants)
        {
            if (_rockIndirectRenderer == null)
                _rockIndirectRenderer = gameObject.AddComponent<RockIndirectRenderer>();
            _meshSpawning.TryPlanRockSeeds(meshSpawn, gen, _placementMask, _plannedRockSeeds);
        }
        else
        {
            _plannedRockSeeds.Clear();
            _rockIndirectRenderer?.Initialize(null, null);
        }

        _lastStreamAnchor = new Vector3(float.NaN, float.NaN, float.NaN);
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
