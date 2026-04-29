#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Interleaved vertex layout used for terrain chunk MeshData uploads (matches vertex attribute stream 0).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TerrainMeshVertex
{
    /// <summary>Local-space position relative to the chunk transform.</summary>
    public float3 position;

    /// <summary>Object-space normal.</summary>
    public float3 normal;

    /// <summary>UV0 used as normalized world XZ for splat sampling.</summary>
    public float2 uv0;

    /// <summary>UV1.x = height01, UV1.y = slope magnitude.</summary>
    public float2 uv1;
}

/// <summary>
/// Orchestrates procedural heightmap generation, path splat mask, distance fields, and pooled terrain chunk meshes.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(TerrainNavMeshBuilder))]
public sealed class TerrainGenerator : MonoBehaviour
{
    #region TerrainGenerator

    /// <summary>Heightmap resolution in texels per axis.</summary>
    public int worldResolution = 512;

    /// <summary>World extent along X and Z in Unity units; terrain spans this transform's XZ position ± half.</summary>
    public float worldSize = 1024f;

    /// <summary>Baseline terrain height in world units.</summary>
    public float baseHeight = 0f;

    /// <summary>
    /// When height is below <see cref="baseHeight"/>, compresses how far below: effective height is
    /// <c>baseHeight - (baseHeight - y) / waterHeightAdjustmentAmount</c> (larger values = shallower valleys).
    /// </summary>
    [Tooltip("Divide depth below base height by this amount (e.g. 10 turns -4 into -0.4 when base height is 0).")]
    public float waterHeightAdjustmentAmount = 10f;

    /// <summary>Maximum fBm height contribution in world units (before bias).</summary>
    public float maxHeightVariation = 40f;

    /// <summary>Distance from path or river where terrain stays flat (noise masked out).</summary>
    public float flatRadius = 18f;

    /// <summary>Distance over which noise fades in after the flat radius.</summary>
    public float falloffDistance = 35f;

    [Header("Cliffs / height noise")]
    [Tooltip("When false, height noise uses billow only (no ridge blend or cliff steepening).")]
    public bool proceduralCliffsEnabled = true;
    [Tooltip("Billow noise above this value (0–1) is remapped toward 1 for sheer faces.")]
    public float cliffThreshold = 0.6f;
    [Tooltip("Cliff remap exponent on the normalized tail above the threshold. Larger = steeper cliffs.")]
    public float cliffSteepPower = 2.5f;
    [Tooltip("Frequency multiplier for ridge simplex relative to billow coordinates.")]
    public float ridgeNoiseFrequencyScale = 1.12f;
    [Tooltip("Ridge channel is saturate(abs(snoise) * this). Higher = sharper ridge peaks.")]
    public float ridgeSharpness = 2f;
    [Tooltip("smoothstep low edge: billow below this stays mostly billowy.")]
    public float ridgeBlendBillowLow = 0.22f;
    [Tooltip("smoothstep high edge: billow above this is mostly ridge-shaped.")]
    public float ridgeBlendBillowHigh = 0.92f;

    /// <summary>Logical chunks along each axis for sampling the heightmap (full world is chunkCount × chunkCount cells).</summary>
    public int chunkCount = 16;

    const int StreamingWindowSide = 3;

    /// <summary>Noise / worm seed for the current pipeline run; set via <see cref="SetProceduralSeed"/> (e.g. from <c>WorldGenerationCoordinator</c>). Defaults to 42 when unset.</summary>
    int _proceduralSeed = 42;

    [Header("Splines")]
    [SerializeField] int splineSampleCount = 400;

    /// <summary>Path splines as nested control-point lists; each <see cref="Vector2"/> is local XZ (relative to this transform).</summary>
    public List<List<Vector2>> pathSplines = new();

    /// <summary>River splines; local XZ only (world Y ignored for sampling).</summary>
    public List<List<Vector2>> riverSplines = new();

    [Tooltip("Optional Unity Splines authoring sources; merged with pathSplines after list-based splines.")]
    [SerializeField] List<SplineContainer>? authoringPathSplines;

    [Tooltip("Optional Unity Splines authoring sources; merged with riverSplines after list-based splines.")]
    [SerializeField] List<SplineContainer>? authoringRiverSplines;

    [Header("Procedural worm splines")]
    [Tooltip("When enabled, path polylines are appended from Perlin-noise worms before distance fields (merged with Path Splines).")]
    [SerializeField] bool generateWormPaths = true;
    [Tooltip("When enabled, river polylines are appended from Perlin-noise worms before distance fields (merged with River Splines).")]
    [SerializeField] bool generateWormRivers = true;
    [SerializeField] int pathWormCount = 4;
    [SerializeField] int riverWormCount = 2;
    [Tooltip("More segments = longer worms (each step advances Step Length).")]
    [SerializeField] int wormSegmentCount = 96;
    [Tooltip("World-units per worm step; larger steps also lengthen the polyline.")]
    [SerializeField] float wormStepLength = 12f;
    [SerializeField] float wormNoiseScale = 0.015f;
    [SerializeField] float wormMaxTurnRadians = 0.45f;
    [SerializeField] float wormBoundaryMargin = 24f;

    [Header("River carving")]
    [Tooltip("How far below the undisturbed terrain height the river bed is carved (world units); constant along the river.")]
    [SerializeField] float riverBedDepth = 6f;
    [Tooltip("Half-width of the carved channel: distance from the river centerline to the inner bank (world units).")]
    [SerializeField] float riverChannelHalfWidth = 2.2f;
    [Tooltip("Distance from the inner bank to the outer edge where carving fades to zero (world units).")]
    [SerializeField] float riverCarveBlendDistance = 3.8f;

    [Header("Rendering")]
    [SerializeField] Transform? cameraTransform;
    [Tooltip("Chunk streaming window is centered on this transform. If unset, uses Camera Transform.")]
    [SerializeField] Transform? streamingAnchor;
    [SerializeField] Material? terrainMaterial;
    [SerializeField] int chunkVertexDensity = 32;
    [SerializeField] float lod1Distance = 180f;
    [SerializeField] float lod2Distance = 360f;
    [SerializeField] float lodCameraMoveEpsilonSqr = 0.25f;

    [Header("Splat / rock")]
    [Tooltip("Slope magnitude (dh over 1 m horizontally) where rock splat begins to blend in.")]
    [SerializeField] float rockSlopeBlendStart = 0.35f;
    [Tooltip("Slope magnitude where rock splat is fully blended in.")]
    [SerializeField] float rockSlopeBlendEnd = 1.4f;

    [Header("Navigation")]
    [Tooltip("Optional; drives camera-follow NavMesh bakes. If unset, resolved via GetComponent at runtime.")]
    [SerializeField] TerrainNavMeshBuilder? terrainNavMeshBuilder;

    const int SplatmapResolution = 512;
    const string TerrainChunkGameObjectPrefix = "TerrainChunk";

    SplineSystem _splineSystem = new();
    DistanceFieldBaker _distanceFieldBaker = new();
    HeightmapGenerator _heightmapGenerator = new();
    SplatmapPainter _splatmapPainter = new();
    ChunkManager _chunkManager = new();

    NativeArray<float> _pathDistanceField;
    NativeArray<float> _riverDistanceField;
    NativeArray<float> _heightmap;
    /// <summary>Per heightmap texel: horizontal slope magnitude (dh/d_horizontal, ~1 m basis) for splat / shader.</summary>
    NativeArray<float> _heightmapSlope;
    NativeArray<float> _splatmapRgba;
    NativeArray<float2> _pathSamples;
    NativeArray<float2> _riverSamples;

    Texture2D? _splatmapTexture;

    bool _chunksBuilt;
    Vector3 _lastCameraPos = new(float.NaN, float.NaN, float.NaN);
    Vector3 _lastStreamingSourcePos = new(float.NaN, float.NaN, float.NaN);
    Vector2Int _lastStreamingWindowOrigin = new(int.MinValue, int.MinValue);

    /// <summary>When set before <see cref="Start"/>, automatic <see cref="RunPipeline"/> is skipped (e.g. <c>WorldGenerationCoordinator</c> calls <see cref="Regenerate"/>).</summary>
    bool _deferInitialPipeline;

    readonly List<Vector2> _gizmoPathPoints = new();
    readonly List<Vector2> _gizmoRiverPoints = new();

    readonly List<List<Vector2>> _generatedPathSplines = new();
    readonly List<List<Vector2>> _generatedRiverSplines = new();
    readonly List<List<Vector2>> _mergedPathsForBuild = new();
    readonly List<List<Vector2>> _mergedRiversForBuild = new();

    /// <summary>Fired in play mode after chunk meshes and colliders are built (end of <see cref="RunPipeline"/>).</summary>
    public static event Action<TerrainGenerator>? TerrainGenerated;

    /// <summary>Currently enabled terrain generator (last <see cref="OnEnable"/>); null if none.</summary>
    public static TerrainGenerator? Instance { get; private set; }

    /// <summary>Uses <see cref="Instance"/> when set; otherwise finds one in the scene (e.g. before first <see cref="OnEnable"/>).</summary>
    public static TerrainGenerator? GetActiveOrFind() =>
        Instance != null ? Instance : UnityEngine.Object.FindFirstObjectByType<TerrainGenerator>();

    /// <summary>True after a successful <see cref="RunPipeline"/> run with a valid heightmap.</summary>
    public bool IsTerrainReady => _chunksBuilt && _heightmap.IsCreated;

    /// <summary>Camera used for chunk LOD and shared with <see cref="TerrainNavMeshBuilder"/> for bake volume placement.</summary>
    public Transform? CameraTransform => cameraTransform;

    public Transform? StreamingAnchorOrCamera => streamingAnchor != null ? streamingAnchor : cameraTransform;

    /// <summary>Current streaming window origin (logical chunk indices); updated when chunk meshes stream.</summary>
    public Vector2Int StreamingWindowOrigin => new(_chunkManager.StreamingWindowOriginX, _chunkManager.StreamingWindowOriginZ);

    /// <summary>Call from <see cref="MonoBehaviour.Awake"/> before this component's <see cref="Start"/> so initial generation can be driven by <see cref="WorldGenerationCoordinator"/>.</summary>
    public void DeferInitialPipeline() => _deferInitialPipeline = true;

    /// <summary>Sets the deterministic seed used for height noise and worm splines on the next <see cref="RunPipeline"/> / <see cref="Regenerate"/>.</summary>
    public void SetProceduralSeed(int value) => _proceduralSeed = value;

    /// <summary>Splat mask (R = path, G = rock weight, B = linear slope magnitude, A unused); null until <see cref="Regenerate"/> completes successfully.</summary>
    public Texture2D? SplatmapTexture => _splatmapTexture;

    /// <summary>Bilinear sample of procedural height at world XZ (same space as chunk meshes).</summary>
    public float SampleHeightWorldXZ(float worldX, float worldZ)
    {
        if (!_heightmap.IsCreated)
            return baseHeight;

        var hr = worldResolution;
        var ox = transform.position.x;
        var oz = transform.position.z;
        var fx = (worldX - ox + worldSize * 0.5f) / worldSize * (hr - 1);
        var fz = (worldZ - oz + worldSize * 0.5f) / worldSize * (hr - 1);
        var ix = Mathf.Clamp((int)Mathf.Floor(fx), 0, hr - 2);
        var iz = Mathf.Clamp((int)Mathf.Floor(fz), 0, hr - 2);
        var tx = fx - ix;
        var tz = fz - iz;

        var h00 = _heightmap[iz * hr + ix];
        var h10 = _heightmap[iz * hr + (ix + 1)];
        var h01 = _heightmap[(iz + 1) * hr + ix];
        var h11 = _heightmap[(iz + 1) * hr + (ix + 1)];
        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    /// <summary>
    /// Bilinear sample of distance to the nearest path spline sample in world XZ (same space as <see cref="SampleHeightWorldXZ"/>).
    /// </summary>
    public float SamplePathDistanceWorldXZ(float worldX, float worldZ)
    {
        if (!_pathDistanceField.IsCreated)
            return float.MaxValue;

        return DistanceFieldBaker.SampleDf(
            _pathDistanceField,
            worldResolution,
            worldSize,
            transform.position,
            new Vector2(worldX, worldZ));
    }

    /// <summary>
    /// World position on the path spline sample closest to <paramref name="targetWorldXz"/> in the horizontal plane
    /// (<c>x</c> = world X, <c>y</c> = world Z). Height uses <see cref="SampleHeightWorldXZ"/> plus <paramref name="heightOffset"/>.
    /// </summary>
    public bool TryGetClosestPathPointWorldXZ(Vector2 targetWorldXz, float heightOffset, out Vector3 worldPosition)
    {
        worldPosition = default;
        if (!_pathSamples.IsCreated || _pathSamples.Length == 0)
            return false;

        float tx = targetWorldXz.x;
        float tz = targetWorldXz.y;
        float bestSq = float.MaxValue;
        float bestX = 0f;
        float bestZ = 0f;

        for (int i = 0; i < _pathSamples.Length; i++)
        {
            float2 p = _pathSamples[i];
            float dx = p.x - tx;
            float dz = p.y - tz;
            float sq = dx * dx + dz * dz;
            if (sq < bestSq)
            {
                bestSq = sq;
                bestX = p.x;
                bestZ = p.y;
            }
        }

        float y = SampleHeightWorldXZ(bestX, bestZ) + heightOffset;
        worldPosition = new Vector3(bestX, y, bestZ);
        return true;
    }

    /// <summary>
    /// Writes one byte per heightmap cell: 1 when path distance is under <paramref name="clearanceWorldMeters"/>, else 0.
    /// <paramref name="blocked"/> length must be at least <c>worldResolution * worldResolution</c>.
    /// </summary>
    public void WritePathBlockedBytes(NativeArray<byte> blocked, float clearanceWorldMeters)
    {
        if (!blocked.IsCreated || !_pathDistanceField.IsCreated)
            return;

        int n = worldResolution * worldResolution;
        if (blocked.Length < n)
            return;

        for (int i = 0; i < n; i++)
            blocked[i] = (byte)(_pathDistanceField[i] < clearanceWorldMeters ? 1 : 0);
    }

    /// <summary>
    /// Sets occupancy bits where path distance is under clearance. Packs one bit per heightmap cell into <paramref name="words"/> (32 cells per <see cref="uint"/>).
    /// <paramref name="words"/> length must be at least <c>(worldResolution * worldResolution + 31) / 32</c>; <paramref name="resolution"/> must match <see cref="worldResolution"/>.
    /// </summary>
    public bool TryStampPathOccupancyBits(NativeArray<uint> words, int resolution, float clearanceWorldMeters)
    {
        if (!words.IsCreated || !_pathDistanceField.IsCreated)
            return false;
        if (resolution != worldResolution)
            return false;

        int n = worldResolution * worldResolution;
        int expectedWords = (n + 31) / 32;
        if (words.Length < expectedWords)
            return false;

        for (int i = 0; i < n; i++)
        {
            if (_pathDistanceField[i] < clearanceWorldMeters)
            {
                int w = i >> 5;
                int b = i & 31;
                words[w] |= 1u << b;
            }
        }

        return true;
    }

    /// <summary>
    /// Blends village footpaths into splat R (same 8 m falloff as <see cref="SplatmapPainter"/>). Only updates splat texels inside the path bounds.
    /// Call after structures are placed; safe to call multiple times (max-combines with existing path).
    /// </summary>
    public void ApplySettlementPathSplatOverlay(
        IReadOnlyList<Vector2> ringCentersWorldXz,
        IReadOnlyList<float> ringRadiiWorld,
        IReadOnlyList<List<Vector2>> corridorPolylinesWorldXz)
    {
        if (!_splatmapRgba.IsCreated || _splatmapRgba.Length < SplatmapResolution * SplatmapResolution * 4)
            return;
        if (ringCentersWorldXz.Count != ringRadiiWorld.Count)
            return;

        const float pathFalloffWorld = 8f;

        var ox = transform.position.x;
        var oz = transform.position.z;
        var ws = worldSize;
        var res = SplatmapResolution;

        float wxMin = float.PositiveInfinity, wxMax = float.NegativeInfinity;
        float wzMin = float.PositiveInfinity, wzMax = float.NegativeInfinity;

        void GrowBounds(float x, float z, float pad)
        {
            wxMin = Mathf.Min(wxMin, x - pad);
            wxMax = Mathf.Max(wxMax, x + pad);
            wzMin = Mathf.Min(wzMin, z - pad);
            wzMax = Mathf.Max(wzMax, z + pad);
        }

        for (var i = 0; i < ringCentersWorldXz.Count; i++)
        {
            var c = ringCentersWorldXz[i];
            var r = ringRadiiWorld[i];
            GrowBounds(c.x, c.y, r + pathFalloffWorld);
        }

        for (var p = 0; p < corridorPolylinesWorldXz.Count; p++)
        {
            var chain = corridorPolylinesWorldXz[p];
            if (chain == null)
                continue;
            for (var k = 0; k < chain.Count; k++)
                GrowBounds(chain[k].x, chain[k].y, pathFalloffWorld);
        }

        if (float.IsInfinity(wxMin))
            return;

        float half = ws * 0.5f;
        wxMin = Mathf.Clamp(wxMin, ox - half, ox + half);
        wxMax = Mathf.Clamp(wxMax, ox - half, ox + half);
        wzMin = Mathf.Clamp(wzMin, oz - half, oz + half);
        wzMax = Mathf.Clamp(wzMax, oz - half, oz + half);

        float ToIx(float wx) => res * ((wx - ox) / ws + 0.5f) - 0.5f;
        float ToIz(float wz) => res * ((wz - oz) / ws + 0.5f) - 0.5f;

        var ix0 = Mathf.Clamp((int)Mathf.Floor(Mathf.Min(ToIx(wxMin), ToIx(wxMax))), 0, res - 1);
        var ix1 = Mathf.Clamp((int)Mathf.Ceil(Mathf.Max(ToIx(wxMin), ToIx(wxMax))), 0, res - 1);
        var iz0 = Mathf.Clamp((int)Mathf.Floor(Mathf.Min(ToIz(wzMin), ToIz(wzMax))), 0, res - 1);
        var iz1 = Mathf.Clamp((int)Mathf.Ceil(Mathf.Max(ToIz(wzMin), ToIz(wzMax))), 0, res - 1);

        for (var iz = iz0; iz <= iz1; iz++)
        {
            var wz = oz + ((iz + 0.5f) / res - 0.5f) * ws;
            for (var ix = ix0; ix <= ix1; ix++)
            {
                var wx = ox + ((ix + 0.5f) / res - 0.5f) * ws;
                var p = new Vector2(wx, wz);

                var minD = float.PositiveInfinity;

                for (var r = 0; r < ringCentersWorldXz.Count; r++)
                {
                    var c = ringCentersWorldXz[r];
                    var rad = ringRadiiWorld[r];
                    var ringD = Mathf.Abs(Vector2.Distance(p, c) - rad);
                    minD = Mathf.Min(minD, ringD);
                }

                for (var cIdx = 0; cIdx < corridorPolylinesWorldXz.Count; cIdx++)
                {
                    var chain = corridorPolylinesWorldXz[cIdx];
                    if (chain == null || chain.Count < 2)
                        continue;
                    for (var j = 0; j < chain.Count - 1; j++)
                        minD = Mathf.Min(minD, DistancePointSegmentXz(p, chain[j], chain[j + 1]));
                }

                if (float.IsInfinity(minD))
                    continue;

                // Match SplatmapPainter: pathWeight = smoothstep(8, 0, pathDist)
                var t = Mathf.InverseLerp(pathFalloffWorld, 0f, minD); // (8 - d) / 8 clamped to [0,1]
                var w = Mathf.SmoothStep(0f, 1f, t);
                if (w <= 1e-5f)
                    continue;

                var baseIndex = (iz * res + ix) * 4;
                var prev = _splatmapRgba[baseIndex];
                _splatmapRgba[baseIndex] = Mathf.Max(prev, w);
            }
        }

        if (_splatmapTexture != null)
        {
            _splatmapTexture.SetPixelData(_splatmapRgba, 0);
            _splatmapTexture.Apply(false, false);
        }

        if (terrainMaterial != null && _splatmapTexture != null)
            terrainMaterial.SetTexture("_SplatmapTex", _splatmapTexture);
    }

    static float DistancePointSegmentXz(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var denom = ab.sqrMagnitude;
        var t = denom > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom) : 0f;
        var closest = a + ab * t;
        return Vector2.Distance(p, closest);
    }

    void OnEnable()
    {
        Instance = this;
        if (terrainNavMeshBuilder == null)
            terrainNavMeshBuilder = GetComponent<TerrainNavMeshBuilder>();
        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
    }

    void OnDisable()
    {
        if (Instance == this)
            Instance = null;
        RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
    }

    void Start()
    {
        if (!Application.isPlaying && !IsEditorValidated())
            return;

        if (_deferInitialPipeline)
            return;

        RunPipeline();
    }

    static bool IsEditorValidated()
    {
#if UNITY_EDITOR
        return UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
#else
        return false;
#endif
    }

    void OnDestroy()
    {
        DisposeNativeBuffers();
        _chunkManager.Dispose();
        if (_splatmapTexture != null)
        {
            if (Application.isPlaying)
                Destroy(_splatmapTexture);
            else
                DestroyImmediate(_splatmapTexture);
            _splatmapTexture = null;
        }
    }

    void OnBeginContextRendering(ScriptableRenderContext _, List<Camera> __)
    {
        if (!_chunksBuilt)
            return;

        var lodCamera = cameraTransform;
        var streamSource = streamingAnchor != null ? streamingAnchor : lodCamera;
        if (streamSource == null)
            return;

        var anchorPos = streamSource.position;
        var streamDx = anchorPos.x - _lastStreamingSourcePos.x;
        var streamDy = anchorPos.y - _lastStreamingSourcePos.y;
        var streamDz = anchorPos.z - _lastStreamingSourcePos.z;
        var streamMoved = float.IsNaN(_lastStreamingSourcePos.x) ||
            streamDx * streamDx + streamDy * streamDy + streamDz * streamDz >= lodCameraMoveEpsilonSqr;

        var camMoved = false;
        if (lodCamera != null)
        {
            var p = lodCamera.position;
            var dx = p.x - _lastCameraPos.x;
            var dy = p.y - _lastCameraPos.y;
            var dz = p.z - _lastCameraPos.z;
            camMoved = float.IsNaN(_lastCameraPos.x) ||
                dx * dx + dy * dy + dz * dz >= lodCameraMoveEpsilonSqr;
        }

        if (!streamMoved && !camMoved)
            return;

        _lastStreamingSourcePos = anchorPos;
        if (lodCamera != null)
            _lastCameraPos = lodCamera.position;

        var notifyPos = lodCamera != null ? lodCamera.position : anchorPos;
        var (lodDirty, streamDirty) = _chunkManager.UpdateStreamingAndLod(
            anchorPos,
            lodCamera,
            lod1Distance,
            lod2Distance,
            _heightmap,
            worldResolution,
            worldSize,
            baseHeight,
            maxHeightVariation,
            transform,
            chunkCount,
            StreamingWindowSide,
            chunkVertexDensity,
            ref _lastStreamingWindowOrigin);

        terrainNavMeshBuilder?.NotifyCameraOrLodChange(notifyPos, lodDirty || streamDirty);
    }

    /// <summary>
    /// Disposes native buffers and rebuilds the full procedural pipeline (edit or play mode).
    /// </summary>
    [ContextMenu("Regenerate")]
    public void Regenerate()
    {
        _chunkManager.DestroyChunkObjects(transform, TerrainChunkGameObjectPrefix);
        _chunksBuilt = false;

        RunPipeline();
    }

    /// <summary>
    /// Destroys pooled chunk objects and disposes all native allocations for a clean Inspector reset.
    /// </summary>
    [ContextMenu("Clear")]
    public void ClearTerrain()
    {
        DisposeNativeBuffers();
        _chunkManager.DestroyChunkObjects(transform, TerrainChunkGameObjectPrefix);
        _chunksBuilt = false;
        if (_splatmapTexture != null)
        {
            if (Application.isPlaying)
                Destroy(_splatmapTexture);
            else
                DestroyImmediate(_splatmapTexture);
            _splatmapTexture = null;
        }
    }

    void RunPipeline()
    {
        if (worldResolution < 8 || chunkCount < 1 || worldSize <= 0f)
            return;

        _generatedPathSplines.Clear();
        _generatedRiverSplines.Clear();

        if (generateWormPaths)
            NoiseWormSplineGenerator.GeneratePaths(transform, BuildWormSettings(), _generatedPathSplines);

        if (generateWormRivers)
            NoiseWormSplineGenerator.GenerateRivers(transform, BuildWormSettings(), _generatedRiverSplines);

        MergeListBasedSplines(pathSplines, _generatedPathSplines, _mergedPathsForBuild);
        MergeListBasedSplines(riverSplines, _generatedRiverSplines, _mergedRiversForBuild);

        _splineSystem.BuildSamples(
            transform,
            _mergedPathsForBuild,
            _mergedRiversForBuild,
            authoringPathSplines,
            authoringRiverSplines,
            splineSampleCount,
            Allocator.Persistent,
            ref _pathSamples,
            ref _riverSamples);

        RebuildGizmoPoints();

        var count = worldResolution * worldResolution;
        var splatFloatCount = SplatmapResolution * SplatmapResolution * 4;
        EnsurePersistentFloatBuffer(ref _pathDistanceField, count);
        EnsurePersistentFloatBuffer(ref _riverDistanceField, count);
        EnsurePersistentFloatBuffer(ref _heightmap, count);
        EnsurePersistentFloatBuffer(ref _heightmapSlope, count);
        EnsurePersistentFloatBuffer(ref _splatmapRgba, splatFloatCount);

        _distanceFieldBaker.Bake(
            _pathSamples,
            _riverSamples,
            worldResolution,
            worldSize,
            transform.position,
            _pathDistanceField,
            _riverDistanceField);

        RidgeBlendClamped(ridgeBlendBillowLow, ridgeBlendBillowHigh, out var ridgeBlendLow, out var ridgeBlendHigh);
        _heightmapGenerator.Generate(
            _pathDistanceField,
            _riverDistanceField,
            worldResolution,
            worldSize,
            baseHeight,
            waterHeightAdjustmentAmount,
            maxHeightVariation,
            flatRadius,
            falloffDistance,
            _proceduralSeed,
            transform.position,
            math.max(1e-4f, riverBedDepth),
            math.max(1e-4f, riverChannelHalfWidth),
            math.max(1e-4f, riverCarveBlendDistance),
            proceduralCliffsEnabled,
            math.clamp(cliffThreshold, 0.05f, 0.95f),
            math.max(1.01f, cliffSteepPower),
            math.max(0.01f, ridgeNoiseFrequencyScale),
            math.max(0.01f, ridgeSharpness),
            ridgeBlendLow,
            ridgeBlendHigh,
            _heightmap,
            _heightmapSlope);

        _splatmapPainter.Paint(
            _pathDistanceField,
            _heightmapSlope,
            worldResolution,
            worldSize,
            SplatmapResolution,
            transform.position,
            rockSlopeBlendStart,
            rockSlopeBlendEnd,
            _splatmapRgba);

        EnsureSplatmapTexture(ref _splatmapTexture, _splatmapRgba);

        _chunkManager.InitializePool(transform, StreamingWindowSide, chunkCount, worldSize, TerrainChunkGameObjectPrefix);
        _chunksBuilt = true;

        var anchor = StreamingAnchorOrCamera;
        var anchorPos = anchor != null ? anchor.position : transform.position;
        _lastStreamingWindowOrigin = new Vector2Int(int.MinValue, int.MinValue);
        _chunkManager.GenerateAllChunkMeshes(
            anchorPos,
            _heightmap,
            worldResolution,
            worldSize,
            chunkCount,
            StreamingWindowSide,
            chunkVertexDensity,
            baseHeight,
            maxHeightVariation,
            transform.position,
            cameraTransform,
            lod1Distance,
            lod2Distance,
            ref _lastStreamingWindowOrigin);

        _chunkManager.AssignMaterial(terrainMaterial);
        if (terrainMaterial != null && _splatmapTexture != null)
            terrainMaterial.SetTexture("_SplatmapTex", _splatmapTexture);

        if (cameraTransform != null)
            _lastCameraPos = cameraTransform.position;
        else
            _lastCameraPos = new Vector3(float.NaN, float.NaN, float.NaN);

        _lastStreamingSourcePos = anchorPos;

        terrainNavMeshBuilder?.RebuildImmediatelyAfterTerrainPipeline();

        if (Application.isPlaying)
            TerrainGenerated?.Invoke(this);
    }

    void RebuildGizmoPoints()
    {
        _gizmoPathPoints.Clear();
        for (var i = 0; i < _pathSamples.Length; i++)
            _gizmoPathPoints.Add(_pathSamples[i]);

        _gizmoRiverPoints.Clear();
        for (var i = 0; i < _riverSamples.Length; i++)
            _gizmoRiverPoints.Add(_riverSamples[i]);
    }

    NoiseWormSplineGenerator.Settings BuildWormSettings()
    {
        return new NoiseWormSplineGenerator.Settings
        {
            Seed = _proceduralSeed,
            WorldSize = worldSize,
            BoundaryMargin = wormBoundaryMargin,
            PathWormCount = generateWormPaths ? pathWormCount : 0,
            RiverWormCount = generateWormRivers ? riverWormCount : 0,
            SegmentCount = wormSegmentCount,
            StepLength = wormStepLength,
            NoiseScale = wormNoiseScale,
            MaxTurnRadians = wormMaxTurnRadians
        };
    }

    static void MergeListBasedSplines(
        List<List<Vector2>> authored,
        List<List<Vector2>> generated,
        List<List<Vector2>> destination)
    {
        destination.Clear();
        foreach (var s in authored)
        {
            if (s != null && s.Count >= 2)
                destination.Add(s);
        }

        foreach (var s in generated)
        {
            if (s != null && s.Count >= 2)
                destination.Add(s);
        }
    }

    void DisposeNativeBuffers()
    {
        if (_pathDistanceField.IsCreated)
            _pathDistanceField.Dispose();
        if (_riverDistanceField.IsCreated)
            _riverDistanceField.Dispose();
        if (_heightmap.IsCreated)
            _heightmap.Dispose();
        if (_heightmapSlope.IsCreated)
            _heightmapSlope.Dispose();
        if (_splatmapRgba.IsCreated)
            _splatmapRgba.Dispose();
        if (_pathSamples.IsCreated)
            _pathSamples.Dispose();
        if (_riverSamples.IsCreated)
            _riverSamples.Dispose();
    }

    static void EnsurePersistentFloatBuffer(ref NativeArray<float> buffer, int length)
    {
        if (buffer.IsCreated && buffer.Length == length)
            return;

        if (buffer.IsCreated)
            buffer.Dispose();
        buffer = new NativeArray<float>(length, Allocator.Persistent);
    }

    static void RidgeBlendClamped(float billowLow, float billowHigh, out float clampedLow, out float clampedHigh)
    {
        clampedHigh = math.clamp(billowHigh, 0.02f, 1f);
        clampedLow = math.clamp(billowLow, 0f, clampedHigh - 0.01f);
    }

    static void EnsureSplatmapTexture(ref Texture2D? texture, NativeArray<float> rgbaData)
    {
        const int res = SplatmapResolution;
        // RGBAFloat: four floats per texel — matches _splatmapRgba (RGBA32 would be 8-bit and misreads float data).
        if (texture != null && texture.width == res && texture.height == res && texture.format == TextureFormat.RGBAFloat)
        {
            texture.SetPixelData(rgbaData, 0);
            texture.Apply(false, false);
            return;
        }

        if (texture != null)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }

        texture = new Texture2D(res, res, TextureFormat.RGBAFloat, false, true)
        {
            name = "ProceduralSplatmap",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        texture.SetPixelData(rgbaData, 0);
        texture.Apply(false, false);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        foreach (var spline in pathSplines)
        {
            if (spline == null || spline.Count < 2)
                continue;
            for (var i = 0; i < spline.Count - 1; i++)
            {
                var a = transform.TransformPoint(spline[i].x, 0f, spline[i].y);
                var b = transform.TransformPoint(spline[i + 1].x, 0f, spline[i + 1].y);
                Gizmos.DrawLine(a, b);
            }
        }

        Gizmos.color = Color.blue;
        foreach (var spline in riverSplines)
        {
            if (spline == null || spline.Count < 2)
                continue;
            for (var i = 0; i < spline.Count - 1; i++)
            {
                var a = transform.TransformPoint(spline[i].x, 0f, spline[i].y);
                var b = transform.TransformPoint(spline[i + 1].x, 0f, spline[i + 1].y);
                Gizmos.DrawLine(a, b);
            }
        }

        Gizmos.color = Color.cyan;
        foreach (var spline in _generatedPathSplines)
        {
            if (spline == null || spline.Count < 2)
                continue;
            for (var i = 0; i < spline.Count - 1; i++)
            {
                var a = transform.TransformPoint(spline[i].x, 0f, spline[i].y);
                var b = transform.TransformPoint(spline[i + 1].x, 0f, spline[i + 1].y);
                Gizmos.DrawLine(a, b);
            }
        }

        Gizmos.color = Color.magenta;
        foreach (var spline in _generatedRiverSplines)
        {
            if (spline == null || spline.Count < 2)
                continue;
            for (var i = 0; i < spline.Count - 1; i++)
            {
                var a = transform.TransformPoint(spline[i].x, 0f, spline[i].y);
                var b = transform.TransformPoint(spline[i + 1].x, 0f, spline[i + 1].y);
                Gizmos.DrawLine(a, b);
            }
        }

        Gizmos.color = new Color(1f, 1f, 0.2f, 0.9f);
        foreach (var p in _gizmoPathPoints)
        {
            var w = new Vector3(p.x, baseHeight, p.y);
            Gizmos.DrawSphere(w, worldSize * 0.0025f);
        }

        Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.9f);
        foreach (var p in _gizmoRiverPoints)
        {
            var w = new Vector3(p.x, baseHeight, p.y);
            Gizmos.DrawSphere(w, worldSize * 0.0025f);
        }
    }

    #endregion

    #region SplineSystem

    /// <summary>
    /// Builds merged spline samples for distance fields (world XZ only).
    /// </summary>
    public sealed class SplineSystem
    {
        /// <summary>
        /// Samples all splines and produces persistent native buffers for downstream jobs.
        /// </summary>
        public void BuildSamples(
            Transform terrainRoot,
            List<List<Vector2>> pathSplines,
            List<List<Vector2>> riverSplines,
            List<SplineContainer>? authoringPathSplines,
            List<SplineContainer>? authoringRiverSplines,
            int samplesPerSpline,
            Allocator allocator,
            ref NativeArray<float2> pathSamples,
            ref NativeArray<float2> riverSamples)
        {
            using var pathTmp = new NativeList<float2>(Allocator.Temp);
            using var riverTmp = new NativeList<float2>(Allocator.Temp);

            AppendListSplines(terrainRoot, pathSplines, samplesPerSpline, pathTmp);
            AppendContainerSplines(authoringPathSplines, samplesPerSpline, pathTmp);

            AppendListSplines(terrainRoot, riverSplines, samplesPerSpline, riverTmp);
            AppendContainerSplines(authoringRiverSplines, samplesPerSpline, riverTmp);

            if (pathTmp.Length == 0)
                pathTmp.Add(float2.zero);

            if (riverTmp.Length == 0)
                riverTmp.Add(float2.zero);

            var pathSrc = pathTmp.AsArray();
            var riverSrc = riverTmp.AsArray();

            if (!pathSamples.IsCreated || pathSamples.Length != pathSrc.Length)
            {
                if (pathSamples.IsCreated)
                    pathSamples.Dispose();
                pathSamples = new NativeArray<float2>(pathSrc, allocator);
            }
            else
            {
                pathSamples.CopyFrom(pathSrc);
            }

            if (!riverSamples.IsCreated || riverSamples.Length != riverSrc.Length)
            {
                if (riverSamples.IsCreated)
                    riverSamples.Dispose();
                riverSamples = new NativeArray<float2>(riverSrc, allocator);
            }
            else
            {
                riverSamples.CopyFrom(riverSrc);
            }
        }

        static void AppendListSplines(
            Transform terrainRoot,
            List<List<Vector2>> splines,
            int samplesPerSpline,
            NativeList<float2> xzOut)
        {
            if (splines == null)
                return;

            foreach (var ctrl in splines)
            {
                if (ctrl == null || ctrl.Count < 2)
                    continue;

                SampleCatmullRom2D(terrainRoot, ctrl, samplesPerSpline, xzOut);
            }
        }

        static void AppendContainerSplines(
            List<SplineContainer>? containers,
            int samplesPerSpline,
            NativeList<float2> xzOut)
        {
            if (containers == null)
                return;

            foreach (var c in containers)
            {
                if (c == null)
                    continue;

                if (c.Spline.Count < 2)
                    continue;

                for (var s = 0; s < samplesPerSpline; s++)
                {
                    var t = samplesPerSpline <= 1 ? 0f : s / (float)(samplesPerSpline - 1);
                    var world = c.EvaluatePosition(t);
                    xzOut.Add(new float2(world.x, world.z));
                }
            }
        }

        /// <summary>Control points are local XZ; samples are projected to world XZ (Y ignored).</summary>
        static void SampleCatmullRom2D(
            Transform terrainRoot,
            List<Vector2> controls,
            int totalSamples,
            NativeList<float2> xzOut)
        {
            if (controls.Count < 2 || totalSamples < 2)
                return;

            for (var s = 0; s < totalSamples; s++)
            {
                var u = s / (float)(totalSamples - 1);
                var f = u * (controls.Count - 1);
                var seg = (int)math.floor(f);
                if (seg >= controls.Count - 1)
                    seg = controls.Count - 2;

                var lt = f - seg;
                var p0 = ToFloat2(controls[math.max(0, seg - 1)]);
                var p1 = ToFloat2(controls[seg]);
                var p2 = ToFloat2(controls[seg + 1]);
                var p3 = ToFloat2(controls[math.min(controls.Count - 1, seg + 2)]);

                var pos2 = CatmullRom2(p0, p1, p2, p3, lt);
                var world = terrainRoot.TransformPoint(new Vector3(pos2.x, 0f, pos2.y));
                xzOut.Add(new float2(world.x, world.z));
            }
        }

        static float2 ToFloat2(Vector2 v) => new(v.x, v.y);

        static float2 CatmullRom2(float2 p0, float2 p1, float2 p2, float2 p3, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }

    #endregion

    #region DistanceFieldBaker

    /// <summary>
    /// Bakes world-space distance fields for merged spline samples.
    /// </summary>
    public sealed class DistanceFieldBaker
    {
        /// <summary>
        /// Runs Burst jobs to populate path and river distance fields.
        /// </summary>
        public void Bake(
            NativeArray<float2> pathSamples,
            NativeArray<float2> riverSamples,
            int resolution,
            float worldSize,
            Vector3 worldOrigin,
            NativeArray<float> pathDistanceField,
            NativeArray<float> riverDistanceField)
        {
            var pathJob = new PathDistanceFieldJob
            {
                Samples = pathSamples,
                Resolution = resolution,
                WorldSize = worldSize,
                WorldOrigin = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z),
                DistanceOut = pathDistanceField
            }.Schedule(pathDistanceField.Length, 64);

            var riverJob = new RiverDistanceFieldJob
            {
                Samples = riverSamples,
                Resolution = resolution,
                WorldSize = worldSize,
                WorldOrigin = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z),
                DistanceOut = riverDistanceField
            }.Schedule(riverDistanceField.Length, 64);

            JobHandle.CombineDependencies(pathJob, riverJob).Complete();
        }

        /// <summary>
        /// Bilinearly samples a baked distance field for a world XZ coordinate.
        /// </summary>
        public static float SampleDf(NativeArray<float> df, int resolution, float worldSize, Vector3 worldOrigin, Vector2 worldXz)
        {
            var fx = (worldXz.x - worldOrigin.x + worldSize * 0.5f) / worldSize * (resolution - 1);
            var fz = (worldXz.y - worldOrigin.z + worldSize * 0.5f) / worldSize * (resolution - 1);
            var ix = math.clamp((int)math.floor(fx), 0, resolution - 2);
            var iz = math.clamp((int)math.floor(fz), 0, resolution - 2);
            var tx = fx - ix;
            var tz = fz - iz;

            float Sample(int x, int z) => df[z * resolution + x];

            var v00 = Sample(ix, iz);
            var v10 = Sample(ix + 1, iz);
            var v01 = Sample(ix, iz + 1);
            var v11 = Sample(ix + 1, iz + 1);

            var a = math.lerp(v00, v10, tx);
            var b = math.lerp(v01, v11, tx);
            return math.lerp(a, b, tz);
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        struct PathDistanceFieldJob : IJobParallelFor
        {
            [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float2> Samples;
            public int Resolution;
            public float WorldSize;
            public float3 WorldOrigin;
            public NativeArray<float> DistanceOut;

            public void Execute(int index)
            {
                var res = Resolution;
                var ix = index % res;
                var iz = index / res;
                var wx = WorldOrigin.x + ((ix + 0.5f) / res - 0.5f) * WorldSize;
                var wz = WorldOrigin.z + ((iz + 0.5f) / res - 0.5f) * WorldSize;
                var p = new float2(wx, wz);

                var best = float.MaxValue;
                for (var i = 0; i < Samples.Length; i++)
                {
                    var d = math.distance(p, Samples[i]);
                    if (d < best)
                        best = d;
                }

                DistanceOut[index] = best;
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        struct RiverDistanceFieldJob : IJobParallelFor
        {
            [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float2> Samples;
            public int Resolution;
            public float WorldSize;
            public float3 WorldOrigin;
            public NativeArray<float> DistanceOut;

            public void Execute(int index)
            {
                var res = Resolution;
                var ix = index % res;
                var iz = index / res;
                var wx = WorldOrigin.x + ((ix + 0.5f) / res - 0.5f) * WorldSize;
                var wz = WorldOrigin.z + ((iz + 0.5f) / res - 0.5f) * WorldSize;
                var p = new float2(wx, wz);

                var best = float.MaxValue;
                for (var i = 0; i < Samples.Length; i++)
                {
                    var d = math.distance(p, Samples[i]);
                    if (d < best)
                        best = d;
                }

                DistanceOut[index] = best;
            }
        }
    }

    #endregion

    #region HeightmapGenerator

    /// <summary>
    /// Generates the world heightmap using Burst jobs.
    /// </summary>
    public sealed class HeightmapGenerator
    {
        /// <summary>
        /// Schedules and completes the heightmap job.
        /// </summary>
        public void Generate(
            NativeArray<float> pathDf,
            NativeArray<float> riverDf,
            int resolution,
            float worldSize,
            float baseH,
            float waterHeightAdjust,
            float maxVar,
            float flatR,
            float falloff,
            int noiseSeed,
            Vector3 worldOrigin,
            float riverBedDepth,
            float riverChannelHalfWidth,
            float riverCarveBlendDistance,
            bool cliffsEnabled,
            float cliffTh,
            float cliffPower,
            float ridgeFreqScale,
            float ridgeSharp,
            float ridgeBlendLow,
            float ridgeBlendHigh,
            NativeArray<float> heightOut,
            NativeArray<float> slopeOut)
        {
            var heightHandle = new HeightmapJob
            {
                PathDf = pathDf,
                RiverDf = riverDf,
                Resolution = resolution,
                WorldSize = worldSize,
                WorldOrigin = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z),
                BaseHeight = baseH,
                WaterHeightAdjustmentAmount = waterHeightAdjust,
                MaxVariation = maxVar,
                FlatRadius = flatR,
                FalloffDistance = falloff,
                Seed = noiseSeed,
                RiverBedDepth = riverBedDepth,
                RiverChannelHalfWidth = riverChannelHalfWidth,
                RiverCarveBlendDistance = riverCarveBlendDistance,
                CliffsEnabled = cliffsEnabled,
                CliffThreshold = cliffTh,
                CliffSteepPower = cliffPower,
                RidgeFrequencyScale = ridgeFreqScale,
                RidgeSharpness = ridgeSharp,
                RidgeBlendBillowLow = ridgeBlendLow,
                RidgeBlendBillowHigh = ridgeBlendHigh,
                HeightOut = heightOut
            }.Schedule(heightOut.Length, 64);

            new HeightmapSlopeJob
            {
                Heights = heightOut,
                Resolution = resolution,
                WorldSize = worldSize,
                SlopeOut = slopeOut
            }.Schedule(heightOut.Length, 64, heightHandle).Complete();
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        struct HeightmapJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> PathDf;
            [ReadOnly] public NativeArray<float> RiverDf;
            public int Resolution;
            public float WorldSize;
            public float3 WorldOrigin;
            public float BaseHeight;
            public float WaterHeightAdjustmentAmount;
            public float MaxVariation;
            public float FlatRadius;
            public float FalloffDistance;
            public int Seed;
            public float RiverBedDepth;
            public float RiverChannelHalfWidth;
            public float RiverCarveBlendDistance;
            public bool CliffsEnabled;
            public float CliffThreshold;
            public float CliffSteepPower;
            public float RidgeFrequencyScale;
            public float RidgeSharpness;
            public float RidgeBlendBillowLow;
            public float RidgeBlendBillowHigh;
            public NativeArray<float> HeightOut;

            public void Execute(int index)
            {
                var res = Resolution;
                var ix = index % res;
                var iz = index / res;
                var wx = WorldOrigin.x + ((ix + 0.5f) / res - 0.5f) * WorldSize;
                var wz = WorldOrigin.z + ((iz + 0.5f) / res - 0.5f) * WorldSize;

                var pathDist = PathDf[index];
                var riverDist = RiverDf[index];
                var distToAny = math.min(pathDist, riverDist);

                // Domain warp: low-frequency simplex offsets world XZ before main height sampling.
                const float warpScale = 0.001f;
                const float warpStrength = 15f;
                var warpPhase = new float2(Seed * 0.023f, Seed * 0.041f);
                var warpCoord = new float2(wx, wz) * warpScale + warpPhase;
                var warpOffset = new float2(
                    noise.snoise(warpCoord),
                    noise.snoise(warpCoord + new float2(17.3f, 29.1f))) * warpStrength;
                var warpedX = wx + warpOffset.x;
                var warpedZ = wz + warpOffset.y;

                var noiseCoord = new float2(warpedX, warpedZ) * 0.0042f + new float2(Seed * 0.031f, Seed * 0.017f);
                var billow = FbmBillow2(noiseCoord);
                float n;
                if (CliffsEnabled)
                {
                    var ridgeCoord = noiseCoord * RidgeFrequencyScale + new float2(Seed * 0.073f, -Seed * 0.051f);
                    var ridgeRaw = noise.snoise(ridgeCoord);
                    var ridge = math.saturate(math.abs(ridgeRaw) * RidgeSharpness);
                    var peakBlend = math.smoothstep(RidgeBlendBillowLow, RidgeBlendBillowHigh, billow);
                    n = math.lerp(billow, ridge, peakBlend);
                    var th = CliffThreshold;
                    if (n > th)
                    {
                        var u = math.saturate((n - th) / math.max(1e-5f, 1f - th));
                        u = math.pow(u, 1f / CliffSteepPower);
                        n = th + u * (1f - th);
                    }
                }
                else
                    n = billow;

                var shaped = math.smoothstep(0.1f, 0.9f, math.saturate(n));

                var t = (distToAny - FlatRadius) / math.max(1e-4f, FalloffDistance);
                var variationMask = math.smoothstep(0f, 1f, math.saturate(t));
                var noiseTerm = shaped * MaxVariation * variationMask;

                var baseNoiseH = BaseHeight + noiseTerm;

                var inner = RiverChannelHalfWidth;
                var outer = RiverChannelHalfWidth + RiverCarveBlendDistance;
                var carveMask = math.smoothstep(outer, inner, riverDist);
                // Cross-section weight (0 at outer blend, 1 at channel center). Multiply by RiverBedDepth —
                // do not min() with bed depth here: carveMask is in [0,1] and the old formula reduced to
                // min(bedDepth, carveMask), capping the carve at ~1 unit regardless of RiverBedDepth.
                var riverCarve = carveMask * RiverBedDepth;

                var wScale = inner / 2.2f;
                var bankMask = math.smoothstep(inner, inner + 3.5f * wScale, riverDist) *
                    (1f - math.smoothstep(inner + 3.5f * wScale, inner + 7f * wScale, riverDist));
                var bankRaise = bankMask * 0.6f;

                var h = baseNoiseH - riverCarve + bankRaise;
                if (h < BaseHeight)
                {
                    var denom = math.max(1e-4f, WaterHeightAdjustmentAmount);
                    h = h / denom;
                }

                HeightOut[index] = h;
            }

            /// <summary>Two octaves of billow (1 - |snoise|) fBm; normalized to ~[0,1] for shaping.</summary>
            static float FbmBillow2(float2 p)
            {
                var sum = 0f;
                var amp = 0.5f;
                var freq = 1f;
                var weight = 0f;
                for (var o = 0; o < 2; o++)
                {
                    var billow = 1f - math.abs(noise.snoise(p * freq));
                    sum += amp * billow;
                    weight += amp;
                    freq *= 2.02f;
                    amp *= 0.5f;
                }

                return sum / math.max(1e-5f, weight);
            }
        }

        /// <summary>
        /// Horizontal slope magnitude from finalized heightmap texels (central differences); runs after <see cref="HeightmapJob"/>.
        /// </summary>
        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        struct HeightmapSlopeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> Heights;
            public int Resolution;
            public float WorldSize;
            public NativeArray<float> SlopeOut;

            public void Execute(int index)
            {
                var res = Resolution;
                var ix = index % res;
                var iz = index / res;
                var ix0 = math.max(0, ix - 1);
                var ix1 = math.min(res - 1, ix + 1);
                var iz0 = math.max(0, iz - 1);
                var iz1 = math.min(res - 1, iz + 1);
                var hL = Heights[iz * res + ix0];
                var hR = Heights[iz * res + ix1];
                var hD = Heights[iz0 * res + ix];
                var hU = Heights[iz1 * res + ix];
                var cell = WorldSize / res;
                var dxWorld = (ix1 - ix0) * cell;
                var dzWorld = (iz1 - iz0) * cell;
                var dhdx = (hR - hL) / math.max(1e-4f, dxWorld);
                var dhdz = (hU - hD) / math.max(1e-4f, dzWorld);
                SlopeOut[index] = math.length(new float2(dhdx, dhdz));
            }
        }
    }

    #endregion

    #region SplatmapPainter

    /// <summary>
    /// Paints splat weights: R = path from distance field; G = rock blend from <see cref="HeightmapSlopeJob"/> slope; B = raw slope for shaders.
    /// </summary>
    public sealed class SplatmapPainter
    {
        /// <summary>
        /// Schedules splat painting and completes the job.
        /// </summary>
        public void Paint(
            NativeArray<float> pathDf,
            NativeArray<float> heightSlope,
            int dfResolution,
            float worldSize,
            int splatResolution,
            Vector3 worldOrigin,
            float rockSlopeBlendStart,
            float rockSlopeBlendEnd,
            NativeArray<float> rgbaOut)
        {
            new SplatmapJob
            {
                PathDf = pathDf,
                HeightSlope = heightSlope,
                DfResolution = dfResolution,
                WorldSize = worldSize,
                SplatResolution = splatResolution,
                WorldOrigin = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z),
                RockSlopeBlendStart = rockSlopeBlendStart,
                RockSlopeBlendEnd = rockSlopeBlendEnd,
                RgbaOut = rgbaOut
            }.Schedule(splatResolution * splatResolution, 64).Complete();
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        struct SplatmapJob : IJobParallelFor
        {
            [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float> PathDf;
            [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float> HeightSlope;
            public int DfResolution;
            public float WorldSize;
            public int SplatResolution;
            public float3 WorldOrigin;
            public float RockSlopeBlendStart;
            public float RockSlopeBlendEnd;
            [NativeDisableParallelForRestriction] public NativeArray<float> RgbaOut;

            public void Execute(int index)
            {
                var res = SplatResolution;
                var ix = index % res;
                var iz = index / res;
                var wx = WorldOrigin.x + ((ix + 0.5f) / res - 0.5f) * WorldSize;
                var wz = WorldOrigin.z + ((iz + 0.5f) / res - 0.5f) * WorldSize;

                var pathDist = SampleDfWorld(PathDf, DfResolution, WorldSize, WorldOrigin, wx, wz);
                var pathWeight = math.smoothstep(8f, 0f, pathDist);

                var slope = SampleSlopeWorld(HeightSlope, DfResolution, WorldSize, WorldOrigin, wx, wz);
                var s0 = math.max(1e-4f, RockSlopeBlendStart);
                var s1 = math.max(s0 + 1e-4f, RockSlopeBlendEnd);
                var rockWeight = math.saturate(math.smoothstep(s0, s1, slope));

                var baseIndex = index * 4;
                RgbaOut[baseIndex + 0] = pathWeight;
                RgbaOut[baseIndex + 1] = rockWeight;
                RgbaOut[baseIndex + 2] = slope;
                RgbaOut[baseIndex + 3] = 0f;
            }

            static float SampleSlopeWorld(
                NativeArray<float> slope,
                int resolution,
                float worldSize,
                float3 worldOrigin,
                float wx,
                float wz)
            {
                var hr = resolution;
                var fx = (wx - worldOrigin.x + worldSize * 0.5f) / worldSize * (hr - 1);
                var fz = (wz - worldOrigin.z + worldSize * 0.5f) / worldSize * (hr - 1);
                var ix = math.clamp((int)math.floor(fx), 0, hr - 2);
                var iz = math.clamp((int)math.floor(fz), 0, hr - 2);
                var tx = fx - ix;
                var tz = fz - iz;

                var v00 = slope[iz * hr + ix];
                var v10 = slope[iz * hr + ix + 1];
                var v01 = slope[(iz + 1) * hr + ix];
                var v11 = slope[(iz + 1) * hr + ix + 1];
                return math.lerp(math.lerp(v00, v10, tx), math.lerp(v01, v11, tx), tz);
            }

            static float SampleDfWorld(
                NativeArray<float> df,
                int resolution,
                float worldSize,
                float3 worldOrigin,
                float wx,
                float wz)
            {
                var fx = (wx - worldOrigin.x + worldSize * 0.5f) / worldSize * (resolution - 1);
                var fz = (wz - worldOrigin.z + worldSize * 0.5f) / worldSize * (resolution - 1);
                var ix = math.clamp((int)math.floor(fx), 0, resolution - 2);
                var iz = math.clamp((int)math.floor(fz), 0, resolution - 2);
                var tx = fx - ix;
                var tz = fz - iz;

                float S(int x, int z) => df[z * resolution + x];

                var v00 = S(ix, iz);
                var v10 = S(ix + 1, iz);
                var v01 = S(ix, iz + 1);
                var v11 = S(ix + 1, iz + 1);
                return math.lerp(math.lerp(v00, v10, tx), math.lerp(v01, v11, tx), tz);
            }
        }
    }

    #endregion

    #region ChunkManager

    /// <summary>
    /// Pools chunk meshes and regenerates geometry from the shared heightmap.
    /// </summary>
    public sealed class ChunkManager
    {
        readonly List<MeshFilter> _filters = new();
        readonly List<MeshRenderer> _renderers = new();
        readonly List<MeshCollider> _colliders = new();
        readonly List<Mesh> _meshes = new();
        Mesh[] _meshUploadSlots = Array.Empty<Mesh>();
        NativeArray<int> _lodLevels;
        NativeArray<TerrainMeshVertex> _vertexScratch;
        NativeArray<uint> _indexScratch;
        int _maxVertsPerChunk;
        int _maxIndicesPerChunk;
        int _totalChunks;
        int _poolSide;
        int _logicalChunkAxis;
        int _windowOriginX;
        int _windowOriginZ;

        public int StreamingWindowOriginX => _windowOriginX;
        public int StreamingWindowOriginZ => _windowOriginZ;
        float3 _worldOrigin;
        Transform? _terrainRoot;
        bool _scratchAllocated;

        /// <summary>
        /// Creates a fixed <paramref name="poolSide"/> × <paramref name="poolSide"/> pool under <c>TerrainChunks</c>.
        /// Logical world subdivisions use <paramref name="logicalChunkAxis"/>.
        /// </summary>
        public void InitializePool(Transform root, int poolSide, int logicalChunkAxis, float worldSize, string chunkName)
        {
            DisposeScratch();

            DestroyChunksHierarchy(root, chunkName);

            _terrainRoot = root;
            _poolSide = math.max(1, poolSide);
            _logicalChunkAxis = math.max(1, logicalChunkAxis);
            _totalChunks = _poolSide * _poolSide;
            _windowOriginX = 0;
            _windowOriginZ = 0;
            _filters.Clear();
            _renderers.Clear();
            _colliders.Clear();
            _meshes.Clear();

            var chunksParentGo = new GameObject("TerrainChunks");
            chunksParentGo.hideFlags = HideFlags.DontSave;
            var chunksParent = chunksParentGo.transform;
            chunksParent.SetParent(root, false);
            chunksParent.localPosition = Vector3.zero;
            chunksParent.localRotation = Quaternion.identity;
            chunksParent.localScale = Vector3.one;

            var chunkWorld = worldSize / _logicalChunkAxis;
            for (var z = 0; z < _poolSide; z++)
            {
                for (var x = 0; x < _poolSide; x++)
                {
                    var go = new GameObject($"{chunkName}_{x}_{z}");
                    go.transform.SetParent(chunksParent, false);
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    go.transform.localPosition = new Vector3(-worldSize * 0.5f + x * chunkWorld, 0f, -worldSize * 0.5f + z * chunkWorld);
                    var mf = go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();
                    var mc = go.AddComponent<MeshCollider>();
                    var mesh = new Mesh { name = $"{chunkName}_{x}_{z}" };
                    mesh.hideFlags = HideFlags.DontSave;
                    mf.sharedMesh = mesh;
                    mc.sharedMesh = mesh;
                    mc.convex = false;
                    _filters.Add(mf);
                    _renderers.Add(mr);
                    _colliders.Add(mc);
                    _meshes.Add(mesh);
                }
            }

            if (_lodLevels.IsCreated)
                _lodLevels.Dispose();

            _lodLevels = new NativeArray<int>(_totalChunks, Allocator.Persistent);
            for (var i = 0; i < _lodLevels.Length; i++)
                _lodLevels[i] = -1;

            _meshUploadSlots = new Mesh[_totalChunks];
            for (var i = 0; i < _totalChunks; i++)
                _meshUploadSlots[i] = _meshes[i];
        }

        /// <summary>
        /// Destroys all terrain chunk objects under <paramref name="terrainGeneratorRoot"/> (including any saved
        /// in the scene when <see cref="ChunkManager"/> has no live references after a reload).
        /// </summary>
        public void DestroyChunkObjects(Transform? terrainGeneratorRoot, string chunkNamePrefix)
        {
            DisposeScratch();

            if (terrainGeneratorRoot != null)
                DestroyChunksHierarchy(terrainGeneratorRoot, chunkNamePrefix);
            else
            {
                foreach (var f in _filters)
                {
                    if (f == null)
                        continue;
                    var mesh = f.sharedMesh;
                    if (mesh != null)
                    {
                        if (Application.isPlaying)
                            UnityEngine.Object.Destroy(mesh);
                        else
                            UnityEngine.Object.DestroyImmediate(mesh);
                    }

                    var go = f.gameObject;
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(go);
                    else
                        UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _filters.Clear();
            _renderers.Clear();
            _colliders.Clear();
            _meshes.Clear();
            _meshUploadSlots = Array.Empty<Mesh>();

            if (_lodLevels.IsCreated)
                _lodLevels.Dispose();

            _terrainRoot = null;
        }

        /// <summary>
        /// Removes the organized <c>TerrainChunks</c> container and any legacy chunk objects parented directly to <paramref name="root"/>.
        /// </summary>
        static void DestroyChunksHierarchy(Transform root, string chunkNamePrefix)
        {
            var container = root.Find("TerrainChunks");
            if (container != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(container.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(container.gameObject);
            }

            var prefix = chunkNamePrefix + "_";
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(child.gameObject);
                    else
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        /// <summary>
        /// Disposes pooled native scratch buffers.
        /// </summary>
        public void Dispose()
        {
            DisposeScratch();
            if (_lodLevels.IsCreated)
                _lodLevels.Dispose();
        }

        void DisposeScratch()
        {
            if (!_scratchAllocated)
                return;

            if (_vertexScratch.IsCreated)
                _vertexScratch.Dispose();
            if (_indexScratch.IsCreated)
                _indexScratch.Dispose();
            _scratchAllocated = false;
        }

        /// <summary>
        /// First-time mesh build after <see cref="InitializePool"/>; sets streaming window from <paramref name="anchorWorld"/>.
        /// </summary>
        public void GenerateAllChunkMeshes(
            Vector3 anchorWorld,
            NativeArray<float> heightmap,
            int heightResolution,
            float worldSize,
            int logicalChunkAxis,
            int poolSide,
            int baseDensity,
            float baseHeight,
            float maxVariation,
            Vector3 worldOrigin,
            Transform? camera,
            float lod1,
            float lod2,
            ref Vector2Int lastWindowOrigin)
        {
            _logicalChunkAxis = math.max(1, logicalChunkAxis);
            _poolSide = math.max(1, poolSide);
            _worldOrigin = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z);

            ComputeStreamingWindow(anchorWorld, worldOrigin, worldSize, _logicalChunkAxis, _poolSide, ref lastWindowOrigin);
            ApplyChunkTransforms(worldSize);

            EnsureScratch(baseDensity);

            for (var i = 0; i < _totalChunks; i++)
                _lodLevels[i] = SelectLodForSlotIndex(i, worldSize, camera, lod1, lod2);

            RunChunkMeshJob(heightmap, heightResolution, worldSize, baseDensity, baseHeight, maxVariation);
            UploadMeshesFromScratch(baseDensity);
        }

        /// <summary>
        /// Recenters the streaming window on <paramref name="anchorWorld"/> and refreshes LOD from <paramref name="lodCamera"/>.
        /// </summary>
        public (bool lodDirty, bool streamDirty) UpdateStreamingAndLod(
            Vector3 anchorWorld,
            Transform? lodCamera,
            float lod1,
            float lod2,
            NativeArray<float> heightmap,
            int heightResolution,
            float worldSize,
            float baseHeight,
            float maxVariation,
            Transform root,
            int logicalChunkAxis,
            int poolSide,
            int baseDensity,
            ref Vector2Int lastWindowOrigin)
        {
            if (_meshes.Count == 0 || !_lodLevels.IsCreated)
                return (false, false);

            _terrainRoot = root;
            _logicalChunkAxis = math.max(1, logicalChunkAxis);
            _poolSide = math.max(1, poolSide);
            _worldOrigin = new float3(root.position.x, root.position.y, root.position.z);

            var prevOx = _windowOriginX;
            var prevOz = _windowOriginZ;
            ComputeStreamingWindow(anchorWorld, root.position, worldSize, _logicalChunkAxis, _poolSide, ref lastWindowOrigin);
            var streamDirty = _windowOriginX != prevOx || _windowOriginZ != prevOz;
            if (streamDirty)
                ApplyChunkTransforms(worldSize);

            var lodDirty = false;
            for (var i = 0; i < _totalChunks; i++)
            {
                var lod = SelectLodForSlotIndex(i, worldSize, lodCamera, lod1, lod2);
                if (lod != _lodLevels[i])
                {
                    _lodLevels[i] = lod;
                    lodDirty = true;
                }
            }

            if (!streamDirty && !lodDirty)
                return (false, false);

            EnsureScratch(baseDensity);
            RunChunkMeshJob(heightmap, heightResolution, worldSize, baseDensity, baseHeight, maxVariation);
            UploadMeshesFromScratch(baseDensity);
            return (lodDirty, streamDirty);
        }

        void ComputeStreamingWindow(
            Vector3 anchorWorld,
            Vector3 worldOriginVec,
            float worldSize,
            int logicalAxis,
            int poolSide,
            ref Vector2Int lastWindowOrigin)
        {
            var o = TerrainLogicalChunkWindow.ComputeWindowOrigin(anchorWorld, worldOriginVec, worldSize, logicalAxis, poolSide);
            _windowOriginX = o.x;
            _windowOriginZ = o.y;
            lastWindowOrigin = o;
        }

        void ApplyChunkTransforms(float worldSize)
        {
            var chunkWorld = worldSize / _logicalChunkAxis;
            for (var i = 0; i < _totalChunks; i++)
            {
                var sx = i % _poolSide;
                var sz = i / _poolSide;
                var cx = _windowOriginX + sx;
                var cz = _windowOriginZ + sz;
                var valid = cx >= 0 && cz >= 0 && cx < _logicalChunkAxis && cz < _logicalChunkAxis;
                var f = _filters[i];
                if (f == null)
                    continue;
                f.transform.localPosition = valid
                    ? new Vector3(-worldSize * 0.5f + cx * chunkWorld, 0f, -worldSize * 0.5f + cz * chunkWorld)
                    : Vector3.zero;
            }
        }

        void RunChunkMeshJob(
            NativeArray<float> heightmap,
            int heightResolution,
            float worldSize,
            int baseDensity,
            float baseHeight,
            float maxVariation)
        {
            var handle = new ChunkMeshJob
            {
                Heightmap = heightmap,
                HeightResolution = heightResolution,
                WorldSize = worldSize,
                WorldOrigin = _worldOrigin,
                ChunkAxis = _logicalChunkAxis,
                PoolSide = _poolSide,
                WindowOriginX = _windowOriginX,
                WindowOriginZ = _windowOriginZ,
                BaseDensity = baseDensity,
                BaseHeight = baseHeight,
                MaxVariation = maxVariation,
                LodLevels = _lodLevels,
                VertexOut = _vertexScratch,
                IndexOut = _indexScratch,
                MaxVertsPerChunk = _maxVertsPerChunk,
                MaxIndicesPerChunk = _maxIndicesPerChunk
            }.Schedule(_totalChunks, 1);

            handle.Complete();
        }

        void UploadMeshesFromScratch(int baseDensity)
        {
            if (_meshUploadSlots.Length != _totalChunks)
                return;

            var validCount = 0;
            for (var i = 0; i < _totalChunks; i++)
            {
                if (IsSlotLogicalValid(i))
                    validCount++;
            }

            if (validCount == 0)
            {
                for (var i = 0; i < _totalChunks; i++)
                    ClearInactiveSlot(i);
                return;
            }

            var meshDataArray = Mesh.AllocateWritableMeshData(validCount);
            var validMeshes = new Mesh[validCount];
            var batch = 0;
            for (var i = 0; i < _totalChunks; i++)
            {
                if (!IsSlotLogicalValid(i))
                    continue;

                var seg = DensityForLod(baseDensity, _lodLevels[i]);
                var vertCount = (seg + 1) * (seg + 1);
                var indexCount = seg * seg * 6;
                var vStart = i * _maxVertsPerChunk;
                var iStart = i * _maxIndicesPerChunk;

                var meshData = meshDataArray[batch];
                meshData.SetVertexBufferParams(vertCount,
                    new VertexAttributeDescriptor(VertexAttribute.Position),
                    new VertexAttributeDescriptor(VertexAttribute.Normal),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, dimension: 2),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord1, dimension: 2));
                meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                meshData.subMeshCount = 1;

                var vDst = meshData.GetVertexData<TerrainMeshVertex>(0);
                var vSrc = _vertexScratch.GetSubArray(vStart, vertCount);
                vDst.CopyFrom(vSrc);

                var iDst = meshData.GetIndexData<uint>();
                var iSrc = _indexScratch.GetSubArray(iStart, indexCount);
                iDst.CopyFrom(iSrc);

                meshData.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), MeshUpdateFlags.DontRecalculateBounds);
                validMeshes[batch] = _meshes[i];
                batch++;
            }

            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, validMeshes, MeshUpdateFlags.Default);

            for (var i = 0; i < _totalChunks; i++)
            {
                if (!IsSlotLogicalValid(i))
                {
                    ClearInactiveSlot(i);
                    continue;
                }

                var mesh = _meshes[i];
                mesh.RecalculateBounds();
                if (i < _colliders.Count)
                {
                    var mc = _colliders[i];
                    if (mc != null)
                    {
                        mc.enabled = true;
                        mc.sharedMesh = null;
                        mc.sharedMesh = mesh;
                    }
                }

                if (i < _renderers.Count && _renderers[i] != null)
                    _renderers[i].enabled = true;
            }
        }

        void ClearInactiveSlot(int i)
        {
            if (i < _meshes.Count)
            {
                var mesh = _meshes[i];
                mesh.Clear();
                mesh.RecalculateBounds();
            }

            if (i < _colliders.Count && _colliders[i] != null)
            {
                var mc = _colliders[i];
                mc.sharedMesh = null;
                mc.enabled = false;
            }

            if (i < _renderers.Count && _renderers[i] != null)
                _renderers[i].enabled = false;
        }

        bool IsSlotLogicalValid(int slotIndex)
        {
            var sx = slotIndex % _poolSide;
            var sz = slotIndex / _poolSide;
            var cx = _windowOriginX + sx;
            var cz = _windowOriginZ + sz;
            return cx >= 0 && cz >= 0 && cx < _logicalChunkAxis && cz < _logicalChunkAxis;
        }

        int SelectLodForSlotIndex(int slotIndex, float worldSize, Transform? camera, float lod1, float lod2)
        {
            if (!IsSlotLogicalValid(slotIndex))
                return 0;

            var sx = slotIndex % _poolSide;
            var sz = slotIndex / _poolSide;
            var cx = _windowOriginX + sx;
            var cz = _windowOriginZ + sz;
            return SelectLod(cx, cz, worldSize, camera, lod1, lod2);
        }

        int SelectLod(int cx, int cz, float worldSize, Transform? camera, float lod1, float lod2)
        {
            var chunkWorld = worldSize / _logicalChunkAxis;
            var localCenter = new Vector3(-worldSize * 0.5f + (cx + 0.5f) * chunkWorld, 0f, -worldSize * 0.5f + (cz + 0.5f) * chunkWorld);
            if (camera == null || _terrainRoot == null)
                return 0;

            var worldCenter = _terrainRoot.TransformPoint(localCenter);
            var dx = worldCenter.x - camera.position.x;
            var dz = worldCenter.z - camera.position.z;
            var distSq = dx * dx + dz * dz;
            var lod2Sq = lod2 * lod2;
            var lod1Sq = lod1 * lod1;
            if (distSq > lod2Sq)
                return 2;
            if (distSq > lod1Sq)
                return 1;
            return 0;
        }

        static int DensityForLod(int baseDensity, int lod)
        {
            return lod switch
            {
                1 => math.max(2, baseDensity / 2),
                2 => math.max(2, baseDensity / 4),
                _ => baseDensity
            };
        }

        void EnsureScratch(int baseDensity)
        {
            var maxSeg = math.max(2, baseDensity);
            var neededVerts = (maxSeg + 1) * (maxSeg + 1);
            var neededIndices = maxSeg * maxSeg * 6;
            var vertCapacity = neededVerts * _totalChunks;
            var indexCapacity = neededIndices * _totalChunks;

            if (_scratchAllocated &&
                _vertexScratch.IsCreated &&
                _vertexScratch.Length == vertCapacity &&
                _maxVertsPerChunk == neededVerts &&
                _maxIndicesPerChunk == neededIndices)
                return;

            DisposeScratch();

            _maxVertsPerChunk = neededVerts;
            _maxIndicesPerChunk = neededIndices;
            _vertexScratch = new NativeArray<TerrainMeshVertex>(vertCapacity, Allocator.Persistent);
            _indexScratch = new NativeArray<uint>(indexCapacity, Allocator.Persistent);
            _scratchAllocated = true;
        }

        /// <summary>
        /// Assigns a shared material instance to all chunk renderers.
        /// </summary>
        public void AssignMaterial(Material? mat)
        {
            foreach (var r in _renderers)
            {
                if (r != null)
                    r.sharedMaterial = mat;
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        struct ChunkMeshJob : IJobParallelFor
        {
            [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float> Heightmap;
            public int HeightResolution;
            public float WorldSize;
            public float3 WorldOrigin;
            public int ChunkAxis;
            public int PoolSide;
            public int WindowOriginX;
            public int WindowOriginZ;
            public int BaseDensity;
            public float BaseHeight;
            public float MaxVariation;
            [ReadOnly] public NativeArray<int> LodLevels;
            [NativeDisableParallelForRestriction] public NativeArray<TerrainMeshVertex> VertexOut;
            [NativeDisableParallelForRestriction] public NativeArray<uint> IndexOut;
            public int MaxVertsPerChunk;
            public int MaxIndicesPerChunk;

            public void Execute(int chunkIndex)
            {
                var sx = chunkIndex % PoolSide;
                var sz = chunkIndex / PoolSide;
                var cx = WindowOriginX + sx;
                var cz = WindowOriginZ + sz;
                if (cx < 0 || cz < 0 || cx >= ChunkAxis || cz >= ChunkAxis)
                    return;

                var lod = LodLevels[chunkIndex];
                var seg = BaseDensity;
                if (lod == 1)
                    seg = math.max(2, BaseDensity / 2);
                else if (lod == 2)
                    seg = math.max(2, BaseDensity / 4);

                var vertsPerAxis = seg + 1;
                var chunkWorld = WorldSize / ChunkAxis;

                var vStart = chunkIndex * MaxVertsPerChunk;
                var iStart = chunkIndex * MaxIndicesPerChunk;

                for (var z = 0; z <= seg; z++)
                {
                    for (var x = 0; x <= seg; x++)
                    {
                        var t = vStart + z * vertsPerAxis + x;
                        var lx = x / (float)seg * chunkWorld;
                        var lz = z / (float)seg * chunkWorld;
                        var worldX = WorldOrigin.x - WorldSize * 0.5f + cx * chunkWorld + lx;
                        var worldZ = WorldOrigin.z - WorldSize * 0.5f + cz * chunkWorld + lz;
                        var h = SampleHeight(worldX, worldZ);
                        var uv0 = new float2((worldX - WorldOrigin.x) / WorldSize + 0.5f, (worldZ - WorldOrigin.z) / WorldSize + 0.5f);

                        var dhdx = (SampleHeight(worldX + 0.5f, worldZ) - SampleHeight(worldX - 0.5f, worldZ));
                        var dhdz = (SampleHeight(worldX, worldZ + 0.5f) - SampleHeight(worldX, worldZ - 0.5f));
                        var n = math.normalize(new float3(-dhdx, 1f, -dhdz));

                        var slope = math.length(new float2(dhdx, dhdz));
                        var h01 = math.saturate((h - BaseHeight) / math.max(1e-3f, MaxVariation));
                        VertexOut[t] = new TerrainMeshVertex
                        {
                            position = new float3(lx, h, lz),
                            normal = n,
                            uv0 = uv0,
                            uv1 = new float2(h01, slope)
                        };
                    }
                }

                var vi = 0u;
                for (var z = 0; z < seg; z++)
                {
                    for (var x = 0; x < seg; x++)
                    {
                        var i0 = (uint)(z * vertsPerAxis + x);
                        var i1 = (uint)(z * vertsPerAxis + x + 1);
                        var i2 = (uint)((z + 1) * vertsPerAxis + x);
                        var i3 = (uint)((z + 1) * vertsPerAxis + x + 1);

                        var idx = iStart + vi;
                        IndexOut[(int)idx + 0] = i0;
                        IndexOut[(int)idx + 1] = i2;
                        IndexOut[(int)idx + 2] = i1;
                        IndexOut[(int)idx + 3] = i1;
                        IndexOut[(int)idx + 4] = i2;
                        IndexOut[(int)idx + 5] = i3;
                        vi += 6;
                    }
                }
            }

            float SampleHeight(float worldX, float worldZ)
            {
                var hr = HeightResolution;
                var fx = (worldX - WorldOrigin.x + WorldSize * 0.5f) / WorldSize * (hr - 1);
                var fz = (worldZ - WorldOrigin.z + WorldSize * 0.5f) / WorldSize * (hr - 1);
                var ix = math.clamp((int)math.floor(fx), 0, hr - 2);
                var iz = math.clamp((int)math.floor(fz), 0, hr - 2);
                var tx = fx - ix;
                var tz = fz - iz;

                var heightmap = Heightmap;
                var h00 = heightmap[iz * hr + ix];
                var h10 = heightmap[iz * hr + (ix + 1)];
                var h01 = heightmap[(iz + 1) * hr + ix];
                var h11 = heightmap[(iz + 1) * hr + (ix + 1)];
                return math.lerp(math.lerp(h00, h10, tx), math.lerp(h01, h11, tx), tz);
            }
        }
    }

    #endregion
}
