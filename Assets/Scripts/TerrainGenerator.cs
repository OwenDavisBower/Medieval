using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// Procedural heightmap: mostly flat ground, gentle rolling hills, carved rivers and lake basins.
/// Fires <see cref="TerrainGenerationComplete"/> after heights, NavMesh bake, and player placement.
/// </summary>
[RequireComponent(typeof(Terrain))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public class TerrainGenerator : MonoBehaviour
{
    /// <summary>Invoked once per play session after this terrain's heightmap and NavMesh are ready.</summary>
    public static event Action TerrainGenerationComplete;

    [SerializeField] int _seed = 42;

    [Header("Plateau & hills")]
    [SerializeField] float _baseHeight = 0.44f;
    [SerializeField] float _hillStrength = 0.072f;
    [SerializeField] float _hillFrequency = 2.4f;
    [SerializeField] int _hillOctaves = 4;

    [Header("Rivers")]
    [SerializeField] int _riverCount = 2;
    /// <summary>Normalized height carve at river center. Keep small vs ~2-unit-tall player (terrain Y scale applies on top).</summary>
    [SerializeField] float _riverDepth = 0.014f;
    [SerializeField] float _riverHalfWidth = 0.009f;
    [SerializeField] int _riverSegments = 24;
    [SerializeField] float _riverEdgeMargin = 0.04f;

    [Header("Lakes")]
    [SerializeField] int _lakeCount = 5;
    [SerializeField] float _lakeDepth = 0.038f;
    [SerializeField] float _lakeRadiusMin = 0.035f;
    [SerializeField] float _lakeRadiusMax = 0.085f;

    [Header("After generation")]
    [SerializeField] float _playerHeightOffset = 0.05f;
    [Tooltip("When off, play mode uses the TerrainData heightmap already in the scene (no procedural pass). Use Regenerate Terrain in the inspector or via the Medieval menu.")]
    [SerializeField] bool _regenerateOnPlay = false;

    void Start()
    {
        if (_regenerateOnPlay)
            RegenerateTerrain();
        else
            CompleteTerrainSetup();
    }

    /// <summary>Rebuilds the heightmap from noise parameters, then NavMesh. Optionally places the player and fires <see cref="TerrainGenerationComplete"/> (play mode).</summary>
    /// <param name="placePlayerAndFireEvent">Set false when regenerating from the editor to avoid moving objects and firing gameplay events.</param>
    public void RegenerateTerrain(bool placePlayerAndFireEvent = true)
    {
        var terrain = GetComponent<Terrain>();
        TerrainData data = terrain.terrainData;
        int res = data.heightmapResolution;
        float[,] heights = data.GetHeights(0, 0, res, res);

        UnityEngine.Random.InitState(_seed);
        var rng = new System.Random(_seed);

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float nx = x / (float)(res - 1);
                float nz = z / (float)(res - 1);
                float h = _baseHeight + _hillStrength * FbmHills(nx * _hillFrequency, nz * _hillFrequency);
                heights[z, x] = h;
            }
        }

        var rivers = BuildRivers(_riverCount, _riverSegments, rng);
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float nx = x / (float)(res - 1);
                float nz = z / (float)(res - 1);
                var p = new Vector2(nx, nz);

                float carve = 0f;
                for (int r = 0; r < rivers.Count; r++)
                {
                    RiverSpec river = rivers[r];
                    float halfW = _riverHalfWidth * river.WidthMul;
                    float d = DistanceToPolyline(p, river.Points);
                    float t = 1f - Mathf.Clamp01(d / halfW);
                    t = t * t * (3f - 2f * t);
                    carve = Mathf.Max(carve, t * river.DepthMul);
                }

                heights[z, x] -= _riverDepth * carve;
            }
        }

        for (int i = 0; i < _lakeCount; i++)
        {
            float cx = (float)rng.NextDouble();
            float cz = (float)rng.NextDouble();
            float radius = Mathf.Lerp(_lakeRadiusMin, _lakeRadiusMax, (float)rng.NextDouble());
            float depth = _lakeDepth * Mathf.Lerp(0.65f, 1f, (float)rng.NextDouble());
            var center = new Vector2(cx, cz);

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float nx = x / (float)(res - 1);
                    float nz = z / (float)(res - 1);
                    float d = Vector2.Distance(new Vector2(nx, nz), center);
                    float t = 1f - Mathf.Clamp01(d / radius);
                    t = t * t * (3f - 2f * t);
                    heights[z, x] -= depth * t;
                }
            }
        }

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
                heights[z, x] = Mathf.Clamp(heights[z, x], 0.002f, 0.998f);
        }

        data.SetHeights(0, 0, heights);

        CompleteTerrainSetup(placePlayerAndFireEvent);
    }

    void CompleteTerrainSetup(bool placePlayerAndFireEvent = true)
    {
        var terrain = GetComponent<Terrain>();
        var navSurface = GetComponent<NavMeshSurface>();
        if (navSurface != null)
            navSurface.BuildNavMesh();

        if (!placePlayerAndFireEvent)
            return;

        PlacePlayerOnTerrain(terrain);

        TerrainGenerationComplete?.Invoke();
    }

    void PlacePlayerOnTerrain(Terrain terrain)
    {
        var player = FindFirstObjectByType<PlayerController>();
        if (player == null)
            return;

        TerrainData td = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        Vector3 worldXZ = origin + new Vector3(td.size.x * 0.5f, 0f, td.size.z * 0.5f);
        worldXZ.y = terrain.SampleHeight(worldXZ) + _playerHeightOffset;

        var rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MovePosition(worldXZ);
        }
        else
        {
            player.transform.position = worldXZ;
        }
    }

    float FbmHills(float x, float y)
    {
        float sum = 0f;
        float amp = 1f;
        float freq = 1f;
        for (int o = 0; o < _hillOctaves; o++)
        {
            float sample = Mathf.PerlinNoise(x * freq + 0.13f, y * freq + 0.71f);
            sum += (sample - 0.5f) * 2f * amp;
            amp *= 0.52f;
            freq *= 2.03f;
        }
        return Mathf.Clamp(sum * 0.55f, -1f, 1f);
    }

    struct RiverSpec
    {
        public List<Vector2> Points;
        public float DepthMul;
        public float WidthMul;
    }

    /// <summary>Rivers cross the heightmap in varied directions (edge-to-edge or diagonal), not locked to +Z.</summary>
    List<RiverSpec> BuildRivers(int count, int segments, System.Random rng)
    {
        float m = _riverEdgeMargin;
        var rivers = new List<RiverSpec>(count);

        for (int r = 0; r < count; r++)
        {
            Vector2 start = RandomBoundaryPoint(rng, m);
            Vector2 end = RandomBoundaryPoint(rng, m);
            for (int attempt = 0; attempt < 10 && Vector2.Distance(start, end) < 0.28f; attempt++)
                end = RandomBoundaryPoint(rng, m);

            float depthMul = Mathf.Lerp(0.55f, 1f, (float)rng.NextDouble());
            float widthMul = Mathf.Lerp(0.45f, 1.15f, (float)rng.NextDouble());
            float meander = Mathf.Lerp(0.04f, 0.14f, (float)rng.NextDouble());
            float alongFreq = Mathf.Lerp(2.1f, 5.5f, (float)rng.NextDouble());
            float noiseScale = 1.7f + (float)rng.NextDouble() * 2.4f;
            float phase = (float)(rng.NextDouble() * Mathf.PI * 2f);

            Vector2 delta = end - start;
            if (delta.sqrMagnitude < 1e-6f)
                delta = new Vector2(1f, 0.1f);
            Vector2 tangent = delta.normalized;
            Vector2 perp = new Vector2(-tangent.y, tangent.x);

            var pts = new List<Vector2>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector2 basePos = Vector2.Lerp(start, end, t);

                float n1 = Mathf.PerlinNoise(r * 9.3f + t * noiseScale, _seed * 0.017f + t * noiseScale * 0.73f);
                float n2 = Mathf.PerlinNoise(_seed * 0.031f + t * noiseScale * 1.1f, r * 6.1f + t * 4f);
                float wobble = (n1 - 0.5f) * 2f * meander + (n2 - 0.5f) * meander * 0.45f;
                wobble += meander * 0.35f * Mathf.Sin(t * Mathf.PI * alongFreq + phase + r);

                float fray = 0.02f * Mathf.Sin(t * Mathf.PI * (3f + r) + phase);
                basePos += perp * (wobble + fray) + tangent * (fray * 0.5f);
                pts.Add(basePos);
            }

            rivers.Add(new RiverSpec
            {
                Points = pts,
                DepthMul = depthMul,
                WidthMul = widthMul
            });
        }

        return rivers;
    }

    static Vector2 RandomBoundaryPoint(System.Random rng, float margin)
    {
        float u = (float)rng.NextDouble();
        int edge = rng.Next(4);
        float lo = margin;
        float hi = 1f - margin;
        return edge switch
        {
            0 => new Vector2(Mathf.Lerp(lo, hi, u), lo),
            1 => new Vector2(Mathf.Lerp(lo, hi, u), hi),
            2 => new Vector2(lo, Mathf.Lerp(lo, hi, u)),
            _ => new Vector2(hi, Mathf.Lerp(lo, hi, u)),
        };
    }


    static float DistanceToPolyline(Vector2 p, List<Vector2> poly)
    {
        if (poly == null || poly.Count < 2)
            return float.MaxValue;

        float min = float.MaxValue;
        for (int i = 0; i < poly.Count - 1; i++)
        {
            float d = DistancePointToSegment(p, poly[i], poly[i + 1]);
            if (d < min)
                min = d;
        }
        return min;
    }

    static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f);
        t = Mathf.Clamp01(t);
        Vector2 proj = a + t * ab;
        return Vector2.Distance(p, proj);
    }
}
