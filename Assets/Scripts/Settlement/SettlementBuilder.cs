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

    [Header("Procedural placement mask")]
    [SerializeField] float centerFootprintRadius = 10f;
    [SerializeField] float cabinPlacementRadius = 5f;
    [SerializeField] float farmPlacementRadius = 7f;
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

    /// <summary>Used when prefabs are assigned at runtime (e.g. from <see cref="SettlementSpawning"/>).</summary>
    public void InitializeAndBuild(GameObject cabin, GameObject farm, GameObject villager = null, ProceduralPlacementMask placementMask = null)
    {
        cabinPrefab = cabin;
        farmPrefab = farm;
        villagerPrefab = villager;
        _placementMask = placementMask;
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
        var structureRoots = new List<GameObject>(cabinCount + farmCount);
        float minSepSq = minSeparation * minSeparation;
        float baseH = gen.baseHeight;

        for (int i = 0; i < cabinCount; i++)
        {
            if (!TryPlaceStructure(gen, baseH, placed, minSepSq, cabinRadiusMin, cabinRadiusMax, isFarm: false, out Vector3 pos))
                continue;

            placed.Add(pos);
            float yaw = Random.Range(0f, 360f);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            GameObject cabin = Instantiate(cabinPrefab, pos, rot, transform);
            structureRoots.Add(cabin);
            if (_placementMask != null)
                _placementMask.BurnFromRendererBoundsXZ(CombineRendererBounds(cabin), burnBoundsPadding);
            SpawnVillagersForCabin(cabin.transform, pos);
        }

        for (int i = 0; i < farmCount; i++)
        {
            if (!TryPlaceStructure(gen, baseH, placed, minSepSq, farmRadiusMin, farmRadiusMax, isFarm: true, out Vector3 pos))
                continue;

            placed.Add(pos);
            GameObject farmGo = SpawnPrefab(farmPrefab, pos);
            if (farmGo != null)
                structureRoots.Add(farmGo);
            if (_placementMask != null && farmGo != null)
                _placementMask.BurnFromRendererBoundsXZ(CombineRendererBounds(farmGo), burnBoundsPadding);
        }

        PaintSettlementPathsToSplatmap(gen, structureRoots);

        _built = true;
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
        bool isFarm,
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

            float maskR = isFarm ? farmPlacementRadius : cabinPlacementRadius;
            if (_placementMask != null && !_placementMask.IsDiskFreeWorldXZ(x, z, maskR))
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
        return Instantiate(prefab, worldPos, rot, transform);
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
