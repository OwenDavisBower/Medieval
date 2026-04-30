using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Defers terrain <see cref="TerrainGenerator"/> startup, runs <see cref="TerrainGenerator.Regenerate"/>, then spawns
/// content after terrain is ready. <see cref="seed"/> drives terrain procedural noise and spawn <see cref="Random"/> state.
/// Settlement centers and bandit camps are planned once per logical chunk (counts per chunk are configurable on their configs). Bandit planning runs after settlements burn the mask. Trees and mesh (rock) seeds are planned per logical chunk when that chunk first enters the streaming window (same 3×3 window as terrain meshes), so ongoing work scales with loaded area rather than the whole world at once.
/// </summary>
[DefaultExecutionOrder(-100)]
public class WorldGenerationCoordinator : MonoBehaviour
{
    const string DefaultSettlementPath = "Assets/Data/Spawning/MainScene_SettlementSpawn.asset";
    const string DefaultTreePath = "Assets/Data/Spawning/MainScene_TreeSpawn.asset";
    const string DefaultBanditPath = "Assets/Data/Spawning/MainScene_BanditCampSpawn.asset";
    const string DefaultRockSpawnPath = "Assets/Data/Spawning/MainScene_RockSpawn.asset";
    const string DefaultBridgePrefabPath = "Assets/Prefabs/Bridge.prefab";

    const float StreamingAnchorMoveEpsilonSqr = 0.25f;

    [SerializeField] TerrainGenerator terrainGenerator;
    [Tooltip("Deterministic world seed: pushed to terrain before generate and used for spawn RNG.")]
    [SerializeField] int seed = 42;
    [SerializeField] SettlementSpawnConfig settlementSpawn;
    [SerializeField] TreeSpawnConfig treeSpawn;
    [SerializeField] BanditCampSpawnConfig banditCampSpawn;
    [SerializeField, FormerlySerializedAs("rockSpawn")]
    MeshSpawnConfig meshSpawn;
    [Tooltip("When >= 0, path corridor stamped into the placement mask uses at least this many meters from the path centerline. When -1, uses the max of bandit/tree/mesh auto clearances.")]
    [SerializeField] float pathOccupancyStampMeters = -1f;
    [SerializeField] GameObject bridgePrefab;
    [Tooltip("Optional parent for bridge instances; defaults to this coordinator.")]
    [SerializeField] Transform bridgeSpawnParent;
    [SerializeField] float bridgeVerticalOffset;

    ProceduralPlacementMask _placementMask;
    readonly SettlementSpawning _settlementSpawning = new SettlementSpawning();
    readonly TreeSpawning _treeSpawning = new TreeSpawning();
    readonly BanditCampSpawning _banditCampSpawning = new BanditCampSpawning();
    readonly MeshSpawning _meshSpawning = new MeshSpawning();

    readonly List<Vector3> _plannedSettlementCenters = new List<Vector3>();
    readonly Dictionary<int, GameObject> _streamingSettlements = new Dictionary<int, GameObject>();

    readonly List<TreeInstanceData> _plannedTrees = new List<TreeInstanceData>();
    readonly List<TreeInstanceData> _streamingTreesScratch = new List<TreeInstanceData>();
    TreeIndirectRenderer _treeIndirectRenderer;
    TreeColliderPool _treeColliderPool;

    readonly List<Vector3> _plannedBanditCenters = new List<Vector3>();
    readonly Dictionary<int, GameObject> _streamingBandits = new Dictionary<int, GameObject>();

    readonly List<RockInstanceSeed> _plannedRockSeeds = new List<RockInstanceSeed>();
    readonly List<RockInstanceSeed> _streamingRocksScratch = new List<RockInstanceSeed>();
    RockIndirectRenderer _rockIndirectRenderer;
    int[] _meshVariantIndicesForStreaming;

    readonly List<Vector3> _treeAcceptedPositions = new List<Vector3>();
    readonly HashSet<Vector2Int> _treePlannedChunks = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> _rockPlannedChunks = new HashSet<Vector2Int>();
    readonly List<Vector2Int> _windowChunkSortScratch = new List<Vector2Int>();

    readonly List<PathRiverBridgeCrossing> _plannedBridges = new List<PathRiverBridgeCrossing>();
    readonly Dictionary<int, GameObject> _streamingBridges = new Dictionary<int, GameObject>();

    readonly HashSet<Vector2Int> _streamingWindowChunksScratch = new HashSet<Vector2Int>();
    Vector3 _lastStreamAnchor = new Vector3(float.NaN, float.NaN, float.NaN);
    Vector2Int _lastStreamingWindowOrigin = new Vector2Int(int.MinValue, int.MinValue);

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
        if (bridgePrefab == null)
            bridgePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultBridgePrefabPath);
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

        _streamingTreesScratch.Clear();
        _treeIndirectRenderer ??= GetComponent<TreeIndirectRenderer>();
        _treeIndirectRenderer?.Initialize(null, null);
        _treeColliderPool ??= GetComponent<TreeColliderPool>();
        _treeColliderPool?.ClearAll();

        foreach (var kv in _streamingBandits)
        {
            if (kv.Value != null)
                Destroy(kv.Value);
        }

        _streamingBandits.Clear();

        foreach (var kv in _streamingBridges)
        {
            if (kv.Value != null)
                Destroy(kv.Value);
        }

        _streamingBridges.Clear();

        _rockIndirectRenderer ??= GetComponent<RockIndirectRenderer>();
        _rockIndirectRenderer?.Initialize(null, null);
    }

    void SyncStreamingWorldContent()
    {
        var gen = ResolveTerrain();
        if (gen == null || !gen.IsTerrainReady)
            return;

        var anchorTr = gen.StreamingAnchorOrCamera;
        if (anchorTr == null)
            return;

        Vector3 p = anchorTr.position;
        Vector3 origin = gen.transform.position;
        float ws = gen.worldSize;
        int axis = gen.chunkCount;
        int pool = TerrainLogicalChunkWindow.DefaultStreamingPoolSide;

        var win = TerrainLogicalChunkWindow.ComputeWindowOrigin(p, origin, ws, axis, pool);
        Vector2Int prevStreamingWindowOrigin = _lastStreamingWindowOrigin;
        if (!float.IsNaN(_lastStreamAnchor.x))
        {
            float dx = p.x - _lastStreamAnchor.x;
            float dy = p.y - _lastStreamAnchor.y;
            float dz = p.z - _lastStreamAnchor.z;
            bool anchorMoved = dx * dx + dy * dy + dz * dz >= StreamingAnchorMoveEpsilonSqr;
            bool windowChanged = win.x != _lastStreamingWindowOrigin.x || win.y != _lastStreamingWindowOrigin.y;
            if (!anchorMoved && !windowChanged)
                return;
        }

        _lastStreamAnchor = p;
        _lastStreamingWindowOrigin = win;
        TerrainLogicalChunkWindow.CollectWindowChunks(win.x, win.y, pool, axis, _streamingWindowChunksScratch);

        bool streamingWindowOriginChanged = prevStreamingWindowOrigin.x != win.x || prevStreamingWindowOrigin.y != win.y;
        if (streamingWindowOriginChanged)
            TerrainChunkCharacterStreaming.OnTerrainStreamingWindowMoved(gen, prevStreamingWindowOrigin, win, pool);

        bool hasSpawnWork = settlementSpawn != null || treeSpawn != null || banditCampSpawn != null
            || (meshSpawn != null && meshSpawn.HasRenderableVariants)
            || bridgePrefab != null;
        if (!hasSpawnWork)
            return;

        EnsureChunkSpawnsPlannedForWindow(gen);

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
                    var spawned = _settlementSpawning.SpawnSettlementAt(settlementSpawn, nominal, _placementMask, seed, i);
                    if (spawned != null)
                        _streamingSettlements[i] = spawned;
                }
            }
        }

        _treeIndirectRenderer ??= GetComponent<TreeIndirectRenderer>();
        _treeColliderPool ??= GetComponent<TreeColliderPool>();
        if (treeSpawn != null && treeSpawn.HasSpawnableTreePrefab() && _plannedTrees.Count > 0)
        {
            _streamingTreesScratch.Clear();
            for (int i = 0; i < _plannedTrees.Count; i++)
            {
                TreeInstanceData t = _plannedTrees[i];
                var home = TerrainLogicalChunkWindow.WorldXZToChunk(origin, ws, axis, t.Position.x, t.Position.z);
                if (_streamingWindowChunksScratch.Contains(home))
                    _streamingTreesScratch.Add(t);
            }

            if (_treeIndirectRenderer != null)
                _treeIndirectRenderer.Initialize(treeSpawn, _streamingTreesScratch);
            _treeColliderPool?.Sync(treeSpawn, _streamingTreesScratch);
        }
        else
        {
            _treeIndirectRenderer?.Initialize(null, null);
            _treeColliderPool?.ClearAll();
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
                    {
                        BanditCamp camp = spawned.GetComponent<BanditCamp>();
                        if (camp != null)
                            camp.SetCampId(i);
                        _streamingBandits[i] = spawned;
                    }
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

        Transform bridgeParent = bridgeSpawnParent != null ? bridgeSpawnParent : transform;
        if (bridgePrefab != null && _plannedBridges.Count > 0)
        {
            for (int i = 0; i < _plannedBridges.Count; i++)
            {
                PathRiverBridgeCrossing planned = _plannedBridges[i];
                var home = TerrainLogicalChunkWindow.WorldXZToChunk(origin, ws, axis, planned.WorldPosition.x, planned.WorldPosition.z);
                bool inWin = _streamingWindowChunksScratch.Contains(home);

                if (_streamingBridges.TryGetValue(i, out var br) && br != null)
                {
                    if (!inWin)
                    {
                        Destroy(br);
                        _streamingBridges.Remove(i);
                    }
                }
                else if (inWin)
                {
                    Vector3 fwd = planned.PathForwardHorizontal;
                    Quaternion rot = fwd.sqrMagnitude > 1e-8f
                        ? Quaternion.LookRotation(fwd, Vector3.up)
                        : Quaternion.identity;
                    GameObject spawned = Instantiate(bridgePrefab, planned.WorldPosition, rot, bridgeParent);
                    if (spawned != null)
                        _streamingBridges[i] = spawned;
                }
            }
        }
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
        _plannedBridges.Clear();

        _placementMask?.Dispose();
        _placementMask = new ProceduralPlacementMask();
        _placementMask.Allocate(gen);
        float pathStamp = ComputePathOccupancyStampMeters(gen);
        _placementMask.StampPathFromTerrain(gen, pathStamp);

        if (bridgePrefab != null)
            gen.CollectPathRiverBridgeCrossings(_plannedBridges, bridgeVerticalOffset);

        _plannedSettlementCenters.Clear();
        _settlementSpawning.PlanSettlementCenters(settlementSpawn, _placementMask, seed, _plannedSettlementCenters);

        if (settlementSpawn != null)
        {
            float r = Mathf.Max(0.1f, settlementSpawn.SettlementCenterFootprintRadius);
            for (int i = 0; i < _plannedSettlementCenters.Count; i++)
            {
                Vector3 p = _plannedSettlementCenters[i];
                _placementMask.BurnDiskWorldXZ(p.x, p.z, r);
            }
        }

        _banditCampSpawning.PlanCamps(banditCampSpawn, _placementMask, _plannedSettlementCenters, seed, _plannedBanditCenters);

        _treeAcceptedPositions.Clear();
        _treePlannedChunks.Clear();
        _rockPlannedChunks.Clear();

        _rockIndirectRenderer = GetComponent<RockIndirectRenderer>();
        _treeIndirectRenderer = GetComponent<TreeIndirectRenderer>();
        _treeColliderPool = GetComponent<TreeColliderPool>();
        if (meshSpawn != null && meshSpawn.HasRenderableVariants)
        {
            if (_rockIndirectRenderer == null)
                _rockIndirectRenderer = gameObject.AddComponent<RockIndirectRenderer>();
            if (!_meshSpawning.TryGetRenderableVariantIndices(meshSpawn, out _meshVariantIndicesForStreaming))
                _meshVariantIndicesForStreaming = null;
        }
        else
        {
            _plannedRockSeeds.Clear();
            _meshVariantIndicesForStreaming = null;
            _rockIndirectRenderer?.Initialize(null, null);
        }

        if (treeSpawn != null && treeSpawn.HasSpawnableTreePrefab())
        {
            if (_treeIndirectRenderer == null)
                _treeIndirectRenderer = gameObject.AddComponent<TreeIndirectRenderer>();
            if (_treeColliderPool == null)
                _treeColliderPool = gameObject.AddComponent<TreeColliderPool>();
        }

        _lastStreamAnchor = new Vector3(float.NaN, float.NaN, float.NaN);
        _lastStreamingWindowOrigin = new Vector2Int(int.MinValue, int.MinValue);
    }

    void EnsureChunkSpawnsPlannedForWindow(TerrainGenerator gen)
    {
        if (_streamingWindowChunksScratch.Count == 0)
            return;

        _windowChunkSortScratch.Clear();
        foreach (Vector2Int c in _streamingWindowChunksScratch)
            _windowChunkSortScratch.Add(c);
        _windowChunkSortScratch.Sort((a, b) =>
        {
            int cmp = a.y.CompareTo(b.y);
            return cmp != 0 ? cmp : a.x.CompareTo(b.x);
        });

        for (int i = 0; i < _windowChunkSortScratch.Count; i++)
        {
            Vector2Int ch = _windowChunkSortScratch[i];
            if (treeSpawn != null && treeSpawn.HasSpawnableTreePrefab() && _placementMask != null
                && !_treePlannedChunks.Contains(ch))
            {
                _treeSpawning.PlanTreesForChunk(treeSpawn, gen, _placementMask, seed, ch.x, ch.y, _treeAcceptedPositions, _plannedTrees);
                _treePlannedChunks.Add(ch);
            }
        }

        for (int i = 0; i < _windowChunkSortScratch.Count; i++)
        {
            Vector2Int ch = _windowChunkSortScratch[i];
            if (meshSpawn != null && meshSpawn.HasRenderableVariants && _placementMask != null
                && _meshVariantIndicesForStreaming != null && _meshVariantIndicesForStreaming.Length > 0
                && !_rockPlannedChunks.Contains(ch))
            {
                _meshSpawning.TryCollectSeedsForChunk(
                    meshSpawn, gen, _placementMask, seed, ch.x, ch.y, _meshVariantIndicesForStreaming, _plannedRockSeeds);
                _rockPlannedChunks.Add(ch);
            }
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
