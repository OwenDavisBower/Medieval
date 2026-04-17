using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places cabins and farms on flat terrain (near <see cref="TerrainGenerator.baseHeight"/>), with farms on the outer ring.
/// </summary>
public class SettlementBuilder : MonoBehaviour
{
    [SerializeField] GameObject cabinPrefab;
    [SerializeField] GameObject farmPrefab;
    [SerializeField] GameObject villagerPrefab;

    [Header("Water")]
    [Tooltip("Structures are not placed at or below this world Y (e.g. water surface).")]
    [SerializeField] float minSurfaceY = 0f;

    [Header("Flat ground")]
    [SerializeField] float flatHeightTolerance = 0.75f;
    [SerializeField] float maxSlope = 0.35f;
    [SerializeField] float slopeSampleOffset = 2.5f;

    [Header("Layout")]
    [Tooltip("Cabins spawn between these radii from the settlement center.")]
    [SerializeField] float cabinRadiusMin = 0f;
    [SerializeField] float cabinRadiusMax = 14f;
    [Tooltip("Farms spawn between these radii (should be outside cabin ring).")]
    [SerializeField] float farmRadiusMin = 15f;
    [SerializeField] float farmRadiusMax = 20f;
    [SerializeField] float minSeparation = 6f;
    [SerializeField] int maxAttemptsPerStructure = 120;
    [Tooltip("Search this radius (XZ) around this transform for a flat settlement center.")]
    [SerializeField] float centerSearchRadius = 72f;
    [SerializeField] int maxCenterAttempts = 160;

    bool _built;

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

    /// <summary>Used when prefabs are assigned at runtime (e.g. from <see cref="SettlementSpawner"/>).</summary>
    public void InitializeAndBuild(GameObject cabin, GameObject farm, GameObject villager = null)
    {
        cabinPrefab = cabin;
        farmPrefab = farm;
        villagerPrefab = villager;
        TryBuildSettlement();
    }

    void TryBuildSettlement()
    {
        if (_built || cabinPrefab == null || farmPrefab == null)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        if (!TryFindFlatCenter(gen, out Vector3 centerWorld))
            return;

        transform.position = new Vector3(centerWorld.x, transform.position.y, centerWorld.z);

        int cabinCount = Random.Range(2, 6);
        int farmCount = Random.Range(1, 7);

        var placed = new List<Vector3>(cabinCount + farmCount);
        float minSepSq = minSeparation * minSeparation;
        float baseH = gen.baseHeight;

        for (int i = 0; i < cabinCount; i++)
        {
            if (!TryPlaceStructure(gen, baseH, placed, minSepSq, cabinRadiusMin, cabinRadiusMax, out Vector3 pos))
                continue;

            placed.Add(pos);
            float yaw = Random.Range(0f, 360f);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            GameObject cabin = Instantiate(cabinPrefab, pos, rot, transform);
            SpawnVillagersForCabin(cabin.transform, pos);
        }

        for (int i = 0; i < farmCount; i++)
        {
            if (!TryPlaceStructure(gen, baseH, placed, minSepSq, farmRadiusMin, farmRadiusMax, out Vector3 pos))
                continue;

            placed.Add(pos);
            SpawnPrefab(farmPrefab, pos);
        }

        _built = true;
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

    void SpawnPrefab(GameObject prefab, Vector3 worldPos)
    {
        float yaw = Random.Range(0f, 360f);
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        Instantiate(prefab, worldPos, rot, transform);
    }

    void SpawnVillagersForCabin(Transform cabinAnchor, Vector3 cabinWorldPos)
    {
        if (villagerPrefab == null)
            return;

        int count = Random.Range(2, 4);
        for (int v = 0; v < count; v++)
        {
            Vector3 offset = SpawnPlacementUtility.RandomAnnulusOffsetXZ(0.8f, 4f);
            Vector3 vpos = cabinWorldPos + offset;
            vpos = TerrainSpawnUtility.GetWorldPositionOnTerrain(vpos);
            if (vpos.y < minSurfaceY)
                continue;

            float vyaw = Random.Range(0f, 360f);
            GameObject villagerGo = Instantiate(villagerPrefab, vpos, Quaternion.Euler(0f, vyaw, 0f), transform);
            var villager = villagerGo.GetComponent<VillagerController>();
            if (villager != null)
                villager.Initialize(cabinAnchor);
        }
    }
}
