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
    [SerializeField] float _riverDepth = 0.052f;
    [SerializeField] float _riverHalfWidth = 0.028f;
    [SerializeField] int _riverSegments = 16;

    [Header("Lakes")]
    [SerializeField] int _lakeCount = 5;
    [SerializeField] float _lakeDepth = 0.038f;
    [SerializeField] float _lakeRadiusMin = 0.035f;
    [SerializeField] float _lakeRadiusMax = 0.085f;

    [Header("After generation")]
    [SerializeField] float _playerHeightOffset = 0.05f;

    void Start()
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
                    float d = DistanceToPolyline(p, rivers[r]);
                    float t = 1f - Mathf.Clamp01(d / _riverHalfWidth);
                    t = t * t * (3f - 2f * t);
                    carve = Mathf.Max(carve, t);
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

        var navSurface = GetComponent<NavMeshSurface>();
        if (navSurface != null)
            navSurface.BuildNavMesh();

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

    List<List<Vector2>> BuildRivers(int count, int segments, System.Random rng)
    {
        var rivers = new List<List<Vector2>>(count);
        for (int r = 0; r < count; r++)
        {
            float startSide = (float)rng.NextDouble();
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            var pts = new List<Vector2>(segments + 1);

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float nx = Mathf.Lerp(-0.15f, 1.15f, t);
                float nz = Mathf.Lerp(0.1f, 0.95f, t)
                    + Mathf.PerlinNoise(r * 7.17f + t * 4f, _seed * 0.01f + t * 3f) * 0.38f
                    + Mathf.PerlinNoise(_seed * 0.02f + t * 8f, r * 11f) * 0.15f;
                nz += 0.12f * Mathf.Sin(t * Mathf.PI * 2f + angle + r);
                nx += 0.08f * Mathf.Sin(t * Mathf.PI * 3f + startSide * 6f);
                pts.Add(new Vector2(nx, nz));
            }

            rivers.Add(pts);
        }

        return rivers;
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
