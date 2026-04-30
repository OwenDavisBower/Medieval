using System.Collections.Generic;
using UnityEngine;
using Medieval.Npcs;
using Medieval.NpcMovement;
using Unity.Mathematics;
using Random = UnityEngine.Random;

/// <summary>
/// Places configured building prefabs on flat terrain (near <see cref="TerrainGenerator.baseHeight"/>).
/// </summary>
public class SettlementBuilder : MonoBehaviour
{
    const float PlayerSpawnDistance = 50f;
    const float PlayerSpawnDistanceSqr = PlayerSpawnDistance * PlayerSpawnDistance;

    static readonly HashSet<int> SpawnedSettlementIds = new HashSet<int>();
    static Transform _cachedPlayer;

    [SerializeField] SettlementBuildingSpawnEntry[] buildingEntries;
    [SerializeField] GameObject villagerPrefab;

    [Header("Water")]
    [Tooltip("Structures are not placed at or below this world Y (e.g. water surface).")]
    [SerializeField] float minSurfaceY = 0f;

    [Header("Flat ground")]
    [SerializeField] float flatHeightTolerance = 0.75f;
    [SerializeField] float maxSlope = 0.35f;
    [SerializeField] float slopeSampleOffset = 2.5f;

    [Header("Layout")]
    [SerializeField] float minSeparation = 6f;
    [SerializeField] int maxAttemptsPerStructure = 120;
    [Tooltip("Search this radius (XZ) around this transform for a flat settlement center.")]
    [SerializeField] float centerSearchRadius = 72f;
    [SerializeField] int maxCenterAttempts = 160;

    [Header("Procedural placement mask")]
    [SerializeField] float centerFootprintRadius = 10f;
    [SerializeField] float burnBoundsPadding = 0.75f;

    [Header("Settlement paths (splat R)")]
    [Tooltip("Path ring runs this far outside each structure's horizontal bounds (world meters).")]
    [SerializeField] float pathRingOutsideFootprint = 1.25f;
    [Tooltip("Approximate spacing between samples along connecting paths.")]
    [SerializeField] float pathSegmentStepMeters = 1.4f;
    [Tooltip("Max lateral wobble for organic corridors (world meters).")]
    [SerializeField] float pathWobbleAmplitude = 1.1f;

    ProceduralPlacementMask _placementMask;
    bool _built;
    bool _villagersSpawnedForThisInstance;
    int _settlementId = int.MinValue;
    SettlementBuildingSpawnEntry[] _runtimeBuildingEntries;
    readonly List<Bounds> _placementBurnBounds = new List<Bounds>();
    readonly List<Transform> _villagerSpawnAnchors = new List<Transform>();
    readonly List<Vector3> _villagerSpawnPositions = new List<Vector3>();

    public void SetSettlementId(int id) => _settlementId = id;

    void OnEnable()
    {
        TerrainGenerator.TerrainGenerated += OnTerrainGenerated;
    }

    void OnDisable()
    {
        TerrainGenerator.TerrainGenerated -= OnTerrainGenerated;
    }

    void Start() => TryBuildSettlement();

    void OnTerrainGenerated(TerrainGenerator _) => TryBuildSettlement();

    void Update()
    {
        if (!_built || _villagersSpawnedForThisInstance || HasSpawnedVillagersAlready())
            return;
        if (villagerPrefab == null || _villagerSpawnAnchors.Count == 0)
            return;

        Transform player = GetPlayerTransform();
        if (player == null)
            return;

        Vector3 delta = player.position - transform.position;
        if (delta.sqrMagnitude > PlayerSpawnDistanceSqr)
            return;

        SpawnDeferredVillagersNow();
    }

    /// <summary>Used when prefabs are assigned at runtime (e.g. from <see cref="SettlementSpawning"/>).</summary>
    public void InitializeAndBuild(IReadOnlyList<SettlementBuildingSpawnEntry> buildings, GameObject villager = null, ProceduralPlacementMask placementMask = null)
    {
        if (buildings == null || buildings.Count == 0)
            _runtimeBuildingEntries = null;
        else
        {
            _runtimeBuildingEntries = new SettlementBuildingSpawnEntry[buildings.Count];
            for (int i = 0; i < buildings.Count; i++)
                _runtimeBuildingEntries[i] = buildings[i];
        }

        villagerPrefab = villager;
        _placementMask = placementMask;
        TryBuildSettlement();
    }

    public void ConfigurePathOverlay(float ringOutsideFootprintMeters, float segmentStepMeters, float wobbleAmplitudeMeters)
    {
        pathRingOutsideFootprint = Mathf.Max(0f, ringOutsideFootprintMeters);
        pathSegmentStepMeters = Mathf.Max(0.05f, segmentStepMeters);
        pathWobbleAmplitude = Mathf.Max(0f, wobbleAmplitudeMeters);
    }

    void TryBuildSettlement()
    {
        var entries = ActiveBuildingEntries();
        if (_built || entries == null || !HasAnyPrefab(entries))
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        if (!TryFindFlatCenter(gen, out Vector3 centerWorld))
            return;

        transform.position = new Vector3(centerWorld.x, transform.position.y, centerWorld.z);

        int capacity = 0;
        for (int e = 0; e < entries.Length; e++)
        {
            if (entries[e].prefab == null)
                continue;
            int hi = Mathf.Max(entries[e].minCount, entries[e].maxCount);
            if (hi > 0)
                capacity += hi;
        }

        var placed = new List<Vector3>(capacity);
        var structureRoots = new List<GameObject>(capacity);
        float minSepSq = minSeparation * minSeparation;
        float baseH = gen.baseHeight;
        _villagerSpawnAnchors.Clear();
        _villagerSpawnPositions.Clear();

        for (int e = 0; e < entries.Length; e++)
        {
            var entry = entries[e];
            if (entry.prefab == null)
                continue;

            int lo = Mathf.Min(entry.minCount, entry.maxCount);
            int hi = Mathf.Max(entry.minCount, entry.maxCount);
            int count = Random.Range(lo, hi + 1);
            float rMin = Mathf.Min(entry.radiusMin, entry.radiusMax);
            float rMax = Mathf.Max(entry.radiusMin, entry.radiusMax);

            for (int i = 0; i < count; i++)
            {
                if (!TryPlaceStructure(gen, baseH, placed, minSepSq, rMin, rMax, entry.placementRadius, out Vector3 pos))
                    continue;

                placed.Add(pos);
                GameObject structure = SpawnPrefab(entry.prefab, pos);
                if (structure == null)
                    continue;

                structureRoots.Add(structure);
                if (_placementMask != null)
                {
                    var b = CombineRendererBounds(structure);
                    _placementBurnBounds.Add(b);
                    _placementMask.BurnFromRendererBoundsXZ(b, burnBoundsPadding);
                }

                if (entry.spawnVillagersHere)
                {
                    _villagerSpawnAnchors.Add(structure.transform);
                    _villagerSpawnPositions.Add(pos);
                }
            }
        }

        if (structureRoots.Count > 0)
            PaintSettlementPathsToSplatmap(gen, structureRoots);

        _built = true;
        if (HasSpawnedVillagersAlready())
            _villagersSpawnedForThisInstance = true;
    }

    /// <summary>Releases structure burns from the mask (e.g. when streaming unloads this settlement). Path bits are unchanged.</summary>
    public void ClearPlacementBurnsFromMask(ProceduralPlacementMask mask)
    {
        if (mask == null || _placementBurnBounds.Count == 0)
            return;
        for (int i = 0; i < _placementBurnBounds.Count; i++)
            mask.UnburnFromRendererBoundsXZ(_placementBurnBounds[i], burnBoundsPadding);
        _placementBurnBounds.Clear();
    }

    SettlementBuildingSpawnEntry[] ActiveBuildingEntries() => _runtimeBuildingEntries ?? buildingEntries;

    static bool HasAnyPrefab(SettlementBuildingSpawnEntry[] entries)
    {
        if (entries == null)
            return false;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].prefab != null)
                return true;
        }
        return false;
    }

    void PaintSettlementPathsToSplatmap(TerrainGenerator gen, List<GameObject> structureRoots)
    {
        if (structureRoots.Count == 0)
            return;

        var ringCenters = new List<Vector2>(structureRoots.Count);
        var ringRadii = new List<float>(structureRoots.Count);
        for (int i = 0; i < structureRoots.Count; i++)
        {
            var b = CombineRendererBounds(structureRoots[i]);
            var xz = new Vector2(b.center.x, b.center.z);
            var halfW = Mathf.Max(b.extents.x, b.extents.z);
            ringCenters.Add(xz);
            ringRadii.Add(halfW + pathRingOutsideFootprint);
        }

        var nodes = new List<Vector2>(1 + structureRoots.Count);
        nodes.Add(new Vector2(transform.position.x, transform.position.z));
        for (int i = 0; i < structureRoots.Count; i++)
            nodes.Add(ringCenters[i]);

        var chains = BuildOrganicPathChains(nodes);
        gen.ApplySettlementPathSplatOverlay(ringCenters, ringRadii, chains);
    }

    List<List<Vector2>> BuildOrganicPathChains(IReadOnlyList<Vector2> nodes)
    {
        var chains = new List<List<Vector2>>();
        if (nodes.Count < 2)
            return chains;

        var mstEdges = PrimMstEdges(nodes);
        float seed = Random.value * 127.1f;
        for (int e = 0; e < mstEdges.Count; e++)
        {
            var a = nodes[mstEdges[e].from];
            var b = nodes[mstEdges[e].to];
            chains.Add(BuildWobblyPolyline(a, b, seed + e * 19.17f));
        }

        return chains;
    }

    static List<(int from, int to)> PrimMstEdges(IReadOnlyList<Vector2> nodes)
    {
        int n = nodes.Count;
        var inTree = new bool[n];
        var minDistSq = new float[n];
        var nearest = new int[n];
        for (int i = 0; i < n; i++)
        {
            minDistSq[i] = float.PositiveInfinity;
            nearest[i] = -1;
        }

        inTree[0] = true;
        for (int j = 1; j < n; j++)
        {
            var d = (nodes[j] - nodes[0]).sqrMagnitude;
            minDistSq[j] = d;
            nearest[j] = 0;
        }

        var edges = new List<(int, int)>(n - 1);
        for (int iter = 1; iter < n; iter++)
        {
            int best = -1;
            float bestD = float.PositiveInfinity;
            for (int i = 1; i < n; i++)
            {
                if (inTree[i])
                    continue;
                if (minDistSq[i] < bestD)
                {
                    bestD = minDistSq[i];
                    best = i;
                }
            }

            if (best < 0)
                break;

            inTree[best] = true;
            edges.Add((best, nearest[best]));

            for (int j = 1; j < n; j++)
            {
                if (inTree[j])
                    continue;
                var d = (nodes[j] - nodes[best]).sqrMagnitude;
                if (d < minDistSq[j])
                {
                    minDistSq[j] = d;
                    nearest[j] = best;
                }
            }
        }

        return edges;
    }

    List<Vector2> BuildWobblyPolyline(Vector2 a, Vector2 b, float noiseSeed)
    {
        float len = Vector2.Distance(a, b);
        int steps = Mathf.Max(8, Mathf.CeilToInt(len / Mathf.Max(0.35f, pathSegmentStepMeters)));
        var pts = new List<Vector2>(steps + 1);
        Vector2 ab = b - a;
        Vector2 dir = len > 1e-5f ? ab / len : Vector2.right;
        Vector2 perp = new Vector2(-dir.y, dir.x);

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 basePoint = Vector2.Lerp(a, b, t);
            float envelope = Mathf.Sin(t * Mathf.PI);
            float n1 = (Mathf.PerlinNoise(t * 4.2f + noiseSeed, noiseSeed * 0.37f) - 0.5f) * 2f;
            float n2 = (Mathf.PerlinNoise(noiseSeed * 0.73f, t * 5.8f + noiseSeed * 1.1f) - 0.5f) * 2f;
            basePoint += perp * (n1 * pathWobbleAmplitude * envelope);
            basePoint += dir * (n2 * pathWobbleAmplitude * 0.4f * envelope);
            pts.Add(basePoint);
        }

        return pts;
    }

    bool TryFindFlatCenter(TerrainGenerator gen, out Vector3 centerWorld)
    {
        centerWorld = default;
        var origin = transform.position;
        float baseH = gen.baseHeight;

        for (int a = 0; a < maxCenterAttempts; a++)
        {
            Vector3 disk = SpawnPlacementUtility.RandomUniformDiskOffsetXZ(centerSearchRadius);
            float x = origin.x + disk.x;
            float z = origin.z + disk.z;

            if (!SpawnPlacementUtility.IsWorldXZInsideTerrain(gen, x, z))
                continue;

            if (_placementMask != null && !_placementMask.IsDiskFreeWorldXZ(x, z, centerFootprintRadius))
                continue;

            if (IsFlatAt(gen, baseH, x, z, out float y))
            {
                centerWorld = new Vector3(x, y, z);
                return true;
            }
        }

        return false;
    }

    bool TryPlaceStructure(
        TerrainGenerator gen,
        float baseH,
        List<Vector3> placed,
        float minSepSq,
        float rMin,
        float rMax,
        float placementMaskRadius,
        out Vector3 worldPos)
    {
        worldPos = default;
        Vector3 c = transform.position;

        for (int attempt = 0; attempt < maxAttemptsPerStructure; attempt++)
        {
            Vector3 ring = SpawnPlacementUtility.RandomAnnulusOffsetXZ(rMin, rMax);
            float x = c.x + ring.x;
            float z = c.z + ring.z;

            if (!SpawnPlacementUtility.IsWorldXZInsideTerrain(gen, x, z))
                continue;

            if (!IsFlatAt(gen, baseH, x, z, out float y))
                continue;

            var candidate = new Vector3(x, y, z);
            if (!SpawnPlacementUtility.IsFarEnoughFromAllXZ(candidate, placed, minSepSq))
                continue;

            if (_placementMask != null && !_placementMask.IsDiskFreeWorldXZ(x, z, placementMaskRadius))
                continue;

            worldPos = TerrainSpawnUtility.GetWorldPositionOnTerrain(candidate);
            if (worldPos.y < minSurfaceY)
                continue;
            return true;
        }

        return false;
    }

    bool IsFlatAt(TerrainGenerator gen, float baseH, float x, float z, out float y)
    {
        y = gen.SampleHeightWorldXZ(x, z);
        if (y < minSurfaceY)
            return false;
        if (Mathf.Abs(y - baseH) > flatHeightTolerance)
            return false;

        float d = slopeSampleOffset;
        float h = y;
        float sx = gen.SampleHeightWorldXZ(x + d, z) - h;
        float sz = gen.SampleHeightWorldXZ(x, z + d) - h;
        float slope = Mathf.Sqrt(sx * sx + sz * sz);
        return slope <= maxSlope;
    }

    GameObject SpawnPrefab(GameObject prefab, Vector3 worldPos)
    {
        float yaw = Random.Range(0f, 360f);
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        GameObject instance = Instantiate(prefab, worldPos, rot, transform);
        HierarchyLayers.SetRecursiveByLayerName(instance.transform, "Building");
        return instance;
    }

    static Bounds CombineRendererBounds(GameObject root)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0)
            return new Bounds(root.transform.position, Vector3.one * 2f);

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);
        return b;
    }

    void SpawnVillagersNearBuilding(Transform anchor, Vector3 worldPos)
    {
        if (villagerPrefab == null)
            return;

        int count = Random.Range(2, 4);
        for (int v = 0; v < count; v++)
        {
            Vector3 offset = SpawnPlacementUtility.RandomAnnulusOffsetXZ(0.8f, 4f);
            Vector3 vpos = worldPos + offset;
            vpos = TerrainSpawnUtility.GetWorldPositionOnTerrain(vpos);
            if (vpos.y < minSurfaceY)
                continue;

            float vyaw = Random.Range(0f, 360f);
            // If a baked Entities Graphics villager prefab is registered, prefer spawning DOTS villagers.
            // (Minimal mode: no per-villager initialization/wander anchor yet.)
            var e = NpcSpawnApi.SpawnVillager(vpos, quaternion.Euler(0f, math.radians(vyaw), 0f));
            if (e != Unity.Entities.Entity.Null)
            {
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                var em = world.EntityManager;
                NpcMovementApi.SetAnchorPosition(em, e, new float3(anchor.position.x, anchor.position.y, anchor.position.z));
                continue;
            }

            GameObject villagerGo = Instantiate(villagerPrefab, vpos, Quaternion.Euler(0f, vyaw, 0f), transform);
            var villager = villagerGo.GetComponent<VillagerController>();
            if (villager != null)
                villager.Initialize(anchor);
        }
    }

    void SpawnDeferredVillagersNow()
    {
        _villagersSpawnedForThisInstance = true;
        SpawnedSettlementIds.Add(GetSettlementSpawnKey());
        for (int i = 0; i < _villagerSpawnAnchors.Count; i++)
            SpawnVillagersNearBuilding(_villagerSpawnAnchors[i], _villagerSpawnPositions[i]);
    }

    bool HasSpawnedVillagersAlready() => SpawnedSettlementIds.Contains(GetSettlementSpawnKey());

    int GetSettlementSpawnKey()
    {
        if (_settlementId != int.MinValue)
            return _settlementId;

        Vector3 p = transform.position;
        int x = Mathf.RoundToInt(p.x * 10f);
        int z = Mathf.RoundToInt(p.z * 10f);
        return unchecked((x * 73856093) ^ (z * 19349663));
    }

    static Transform GetPlayerTransform()
    {
        if (_cachedPlayer != null)
            return _cachedPlayer;

        var player = FindFirstObjectByType<PlayerController>();
        if (player != null)
            _cachedPlayer = player.transform;

        return _cachedPlayer;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SpawnedSettlementIds.Clear();
        _cachedPlayer = null;
    }
}
