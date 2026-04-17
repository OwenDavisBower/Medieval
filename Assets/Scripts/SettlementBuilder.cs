using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places cabins and farms on flat terrain (near <see cref="TerrainGenerator.baseHeight"/>), with farms on the outer ring.
/// </summary>
public class SettlementBuilder : MonoBehaviour
{
    [SerializeField] GameObject cabinPrefab;
    [SerializeField] GameObject farmPrefab;

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
    public void InitializeAndBuild(GameObject cabin, GameObject farm)
    {
        cabinPrefab = cabin;
        farmPrefab = farm;
        TryBuildSettlement();
    }

    void TryBuildSettlement()
    {
        if (_built || cabinPrefab == null || farmPrefab == null)
            return;

        var gen = Object.FindFirstObjectByType<TerrainGenerator>();
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
            SpawnPrefab(cabinPrefab, pos);
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
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float r = centerSearchRadius * Mathf.Sqrt(Random.value);
            float x = origin.x + Mathf.Cos(ang) * r;
            float z = origin.z + Mathf.Sin(ang) * r;

            if (!IsInsideTerrain(gen, x, z))
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
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float rad = Random.Range(rMin, rMax);
            float x = c.x + Mathf.Cos(ang) * rad;
            float z = c.z + Mathf.Sin(ang) * rad;

            if (!IsInsideTerrain(gen, x, z))
                continue;

            if (!IsFlatAt(gen, baseH, x, z, out float y))
                continue;

            var candidate = new Vector3(x, y, z);
            bool ok = true;
            for (int i = 0; i < placed.Count; i++)
            {
                float dx = candidate.x - placed[i].x;
                float dz = candidate.z - placed[i].z;
                if (dx * dx + dz * dz < minSepSq)
                {
                    ok = false;
                    break;
                }
            }

            if (!ok)
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

    static bool IsInsideTerrain(TerrainGenerator gen, float x, float z)
    {
        float half = gen.worldSize * 0.5f;
        var o = gen.transform.position;
        return x >= o.x - half && x <= o.x + half && z >= o.z - half && z <= o.z + half;
    }

    void SpawnPrefab(GameObject prefab, Vector3 worldPos)
    {
        float yaw = Random.Range(0f, 360f);
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        Instantiate(prefab, worldPos, rot, transform);
    }
}
