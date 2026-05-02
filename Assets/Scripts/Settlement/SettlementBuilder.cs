using System.Collections.Generic;
using UnityEngine;
using Medieval.Npcs;
using Medieval.NpcMovement;
using Unity.Mathematics;
using Random = UnityEngine.Random;

/// <summary>
/// Places configured building prefabs on flat terrain (near <see cref="TerrainGenerator.baseHeight"/>).
/// Uses <see cref="SettlementStructureSpawnLayout"/> so lower <see cref="SettlementBuildingSpawnEntry.layer"/> values finish before outer layers; within a layer, building types are interleaved randomly.
/// </summary>
public class SettlementBuilder : MonoBehaviour
{
    const float PlayerSpawnDistance = 50f;
    const float PlayerSpawnDistanceSqr = PlayerSpawnDistance * PlayerSpawnDistance;

    static readonly HashSet<int> SpawnedSettlementIds = new HashSet<int>();
    static Transform _cachedPlayer;

    [SerializeField] SettlementBuildingSpawnEntry[] buildingEntries;

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
    readonly List<SettlementBuildingSpawnEntry> _placementJobsScratch = new List<SettlementBuildingSpawnEntry>();
    readonly Dictionary<int, (float inner, float outer)> _layerAnnulusScratch = new Dictionary<int, (float inner, float outer)>();

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
        if (_villagerSpawnAnchors.Count == 0)
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
    public void InitializeAndBuild(IReadOnlyList<SettlementBuildingSpawnEntry> buildings, ProceduralPlacementMask placementMask = null)
    {
        if (buildings == null || buildings.Count == 0)
            _runtimeBuildingEntries = null;
        else
        {
            _runtimeBuildingEntries = new SettlementBuildingSpawnEntry[buildings.Count];
            for (int i = 0; i < buildings.Count; i++)
                _runtimeBuildingEntries[i] = buildings[i];
        }

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

        SettlementStructureSpawnLayout.PrecomputeLayerAnnulusBounds(entries, _layerAnnulusScratch);
        SettlementStructureSpawnLayout.BuildShuffledLayerJobQueue(entries, _placementJobsScratch);

        for (int j = 0; j < _placementJobsScratch.Count; j++)
        {
            var entry = _placementJobsScratch[j];
            if (!_layerAnnulusScratch.TryGetValue(entry.EffectiveLayer, out var band))
                continue;

            SettlementStructureSpawnLayout.GetPlacementAnnulus(entry, band.inner, band.outer, out float rMin, out float rMax);

            if (!TryPlaceStructure(gen, baseH, placed, minSepSq, rMin, rMax, entry.placementRadius, out Vector3 pos))
                continue;

            placed.Add(pos);
            GameObject structure = SpawnPrefab(entry.prefab, pos, entry.EffectiveUniformScale);
            if (structure == null)
                continue;

            structureRoots.Add(structure);
            if (_placementMask != null)
            {
                var b = SettlementPathSplatOverlay.CombineRendererBounds(structure);
                _placementBurnBounds.Add(b);
                _placementMask.BurnFromRendererBoundsXZ(b, burnBoundsPadding);
            }

            if (entry.spawnVillagersHere)
            {
                _villagerSpawnAnchors.Add(structure.transform);
                _villagerSpawnPositions.Add(pos);
            }
        }

        if (structureRoots.Count > 0)
            SettlementPathSplatOverlay.ApplyToTerrain(gen, transform, structureRoots, pathRingOutsideFootprint, pathSegmentStepMeters, pathWobbleAmplitude);

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

    GameObject SpawnPrefab(GameObject prefab, Vector3 worldPos, float uniformScale)
    {
        float yaw = Random.Range(0f, 360f);
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        GameObject instance = Instantiate(prefab, worldPos, rot, transform);
        Vector3 ls = instance.transform.localScale;
        instance.transform.localScale = ls * uniformScale;
        HierarchyLayers.SetRecursiveByLayerName(instance.transform, "Building");
        return instance;
    }

    void SpawnVillagersNearBuilding(Transform anchor, Vector3 worldPos)
    {
        int count = Random.Range(2, 4);
        for (int v = 0; v < count; v++)
        {
            Vector3 offset = SpawnPlacementUtility.RandomAnnulusOffsetXZ(0.8f, 4f);
            Vector3 vpos = worldPos + offset;
            vpos = TerrainSpawnUtility.GetWorldPositionOnTerrain(vpos);
            if (vpos.y < minSurfaceY)
                continue;

            float vyaw = Random.Range(0f, 360f);
            var e = NpcSpawnApi.SpawnVillager(vpos, quaternion.Euler(0f, math.radians(vyaw), 0f));
            if (e == Unity.Entities.Entity.Null)
            {
                Debug.LogWarning(
                    "SettlementBuilder: NpcSpawnApi.SpawnVillager failed (is NpcPrefabRegistryAuthoring in a loaded subscene with Villager prefab assigned?).");
                continue;
            }

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            NpcMovementApi.SetAnchorPosition(em, e, new float3(anchor.position.x, anchor.position.y, anchor.position.z));
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
