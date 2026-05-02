using System.Collections.Generic;
using UnityEngine;
using Medieval.Npcs;
using Medieval.NpcMovement;
using Unity.Mathematics;
using Random = UnityEngine.Random;

public class BanditCamp : MonoBehaviour
{
    const float PlayerSpawnDistance = 50f;
    const float PlayerSpawnDistanceSqr = PlayerSpawnDistance * PlayerSpawnDistance;

    static readonly HashSet<int> SpawnedCampIds = new();
    static Transform _cachedPlayer;

    [Header("Camp structures")]
    [Tooltip("Prefab entries placed around the camp center (counts, layer, placement radius; spawnVillagersHere is ignored).")]
    [SerializeField] SettlementBuildingSpawnEntry[] campStructures;
    [Tooltip("Annulus from camp center (XZ) for each layer used by campStructures.")]
    [SerializeField] SettlementLayerAnnulus[] campStructureLayers;

    [SerializeField] float minSurfaceY = 0f;
    [SerializeField] float flatHeightTolerance = 0.75f;
    [SerializeField] float maxSlope = 0.35f;
    [SerializeField] float slopeSampleOffset = 2.5f;
    [SerializeField] float minSeparation = 4f;
    [SerializeField] int maxAttemptsPerStructure = 120;

    [Header("Camp paths (splat R)")]
    [Tooltip("Path ring runs this far outside each structure's horizontal bounds (world meters).")]
    [SerializeField] float pathRingOutsideFootprint = 1.25f;
    [Tooltip("Approximate spacing between samples along connecting paths.")]
    [SerializeField] float pathSegmentStepMeters = 1.4f;
    [Tooltip("Max lateral wobble for organic corridors (world meters).")]
    [SerializeField] float pathWobbleAmplitude = 1.1f;

    [Header("Bandits (DOTS)")]
    [SerializeField] int banditCount = 3;
    [SerializeField] float spawnRadiusMin = 1f;
    [SerializeField] float spawnRadiusMax = 4f;
    [SerializeField] int campId = int.MinValue;

    bool _spawnAttempted;

    public void SetCampId(int id) => campId = id;

    public void ConfigurePathOverlay(float ringOutsideFootprintMeters, float segmentStepMeters, float wobbleAmplitudeMeters)
    {
        pathRingOutsideFootprint = Mathf.Max(0f, ringOutsideFootprintMeters);
        pathSegmentStepMeters = Mathf.Max(0.05f, segmentStepMeters);
        pathWobbleAmplitude = Mathf.Max(0f, wobbleAmplitudeMeters);
    }

    void Start()
    {
        if (!HasAnySpawnWork())
        {
            _spawnAttempted = true;
            return;
        }

        if (HasSpawnedAlready())
            _spawnAttempted = true;
    }

    void Update()
    {
        if (_spawnAttempted || HasSpawnedAlready())
            return;

        Transform player = GetPlayerTransform();
        if (player == null)
            return;

        Vector3 delta = player.position - transform.position;
        if (delta.sqrMagnitude > PlayerSpawnDistanceSqr)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (HasCampStructuresConfigured() && (gen == null || !gen.IsTerrainReady))
            return;

        SpawnCampContentNow();
    }

    bool HasAnySpawnWork() => banditCount > 0 || HasCampStructuresConfigured();

    static bool CampStructuresHasPrefab(SettlementBuildingSpawnEntry[] entries)
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

    bool HasCampStructuresConfigured() => CampStructuresHasPrefab(campStructures);

    bool HasSpawnedAlready() => SpawnedCampIds.Contains(GetSpawnKey());

    int GetSpawnKey()
    {
        if (campId != int.MinValue)
            return campId;

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

    void SpawnCampContentNow()
    {
        _spawnAttempted = true;
        SpawnedCampIds.Add(GetSpawnKey());

        var occupied = new List<Vector3>();
        PlaceCampStructures(occupied);
        SpawnBanditsAround(occupied);
    }

    void PlaceCampStructures(List<Vector3> occupied)
    {
        if (!HasCampStructuresConfigured())
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        float baseH = gen.baseHeight;
        float minSepSq = minSeparation * minSeparation;
        var structureRoots = new List<GameObject>();
        var layerAnnuli = new Dictionary<int, (float inner, float outer)>();
        var jobs = new List<SettlementBuildingSpawnEntry>();

        SettlementStructureSpawnLayout.MergeLayerAnnuliFromAuthoring(campStructureLayers, layerAnnuli);
        SettlementStructureSpawnLayout.BuildShuffledLayerJobQueue(campStructures, jobs);

        for (int j = 0; j < jobs.Count; j++)
        {
            var entry = jobs[j];
            if (!layerAnnuli.TryGetValue(entry.EffectiveLayer, out var band))
                continue;

            if (!TryPlaceStructure(gen, baseH, occupied, minSepSq, band.inner, band.outer, out Vector3 pos))
                continue;

            occupied.Add(pos);
            GameObject structure = SpawnStructurePrefab(
                entry.prefab,
                pos,
                entry.applyMinus90XRotation,
                entry.EffectiveUniformScale);
            if (structure == null)
                occupied.RemoveAt(occupied.Count - 1);
            else
                structureRoots.Add(structure);
        }

        if (structureRoots.Count > 0)
            SettlementPathSplatOverlay.ApplyToTerrain(gen, transform, structureRoots, pathRingOutsideFootprint, pathSegmentStepMeters, pathWobbleAmplitude);
    }

    bool TryPlaceStructure(
        TerrainGenerator gen,
        float baseH,
        List<Vector3> placed,
        float minSepSq,
        float rMin,
        float rMax,
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

    GameObject SpawnStructurePrefab(GameObject prefab, Vector3 worldPos, bool applyMinus90XRotation, float uniformScale)
    {
        float yaw = Random.Range(0f, 360f);
        Quaternion rot = applyMinus90XRotation
            ? Quaternion.Euler(-90f, yaw, 0f)
            : Quaternion.Euler(0f, yaw, 0f);
        GameObject instance = Instantiate(prefab, worldPos, rot, transform);
        Vector3 ls = instance.transform.localScale;
        instance.transform.localScale = ls * uniformScale;
        HierarchyLayers.SetRecursiveByLayerName(instance.transform, "Building");
        return instance;
    }

    void SpawnBanditsAround(List<Vector3> occupied)
    {
        if (banditCount <= 0)
            return;

        float minR = Mathf.Max(0f, spawnRadiusMin);
        float maxR = Mathf.Max(minR, spawnRadiusMax);
        const float banditClearanceSq = 2.25f;

        for (int i = 0; i < banditCount; i++)
        {
            Vector3 pos = default;
            bool placed = false;
            for (int attempt = 0; attempt < 48; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float rad = Random.Range(minR, maxR);
                Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * rad;
                pos = TerrainSpawnUtility.GetWorldPositionOnTerrain(transform.position + offset);
                if (pos.y < minSurfaceY)
                    continue;
                if (!SpawnPlacementUtility.IsFarEnoughFromAllXZ(pos, occupied, banditClearanceSq))
                    continue;
                placed = true;
                break;
            }

            if (!placed)
                continue;

            occupied.Add(pos);

            var wc = NpcSpawnApi.WeaponClassForHalfMeleeHalfRangedSplit(i, banditCount);
            var e = NpcSpawnApi.SpawnBandit(pos, quaternion.identity, 1f, wc);
            if (e == Unity.Entities.Entity.Null)
            {
                Debug.LogWarning(
                    "BanditCamp: NpcSpawnApi.SpawnBandit failed (is NpcPrefabRegistryAuthoring in a loaded subscene with Bandit prefab assigned?).");
                continue;
            }

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            NpcMovementApi.SetAnchorPosition(em, e, new float3(transform.position.x, transform.position.y, transform.position.z));
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SpawnedCampIds.Clear();
        _cachedPlayer = null;
    }
}
