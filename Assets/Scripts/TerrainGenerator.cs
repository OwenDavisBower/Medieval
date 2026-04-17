#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.AI.Navigation;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
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
/// Orchestrates procedural heightmap generation, splat painting, distance fields, and pooled terrain chunk meshes.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class TerrainGenerator : MonoBehaviour
{
    #region TerrainGenerator

    /// <summary>Heightmap resolution in texels per axis.</summary>
    public int worldResolution = 512;

    /// <summary>World extent along X and Z in Unity units; terrain spans this transform's XZ position ± half.</summary>
    public float worldSize = 1024f;

    /// <summary>Baseline terrain height in world units.</summary>
    public float baseHeight = 0f;

    /// <summary>Maximum fBm height contribution in world units (before bias).</summary>
    public float maxHeightVariation = 40f;

    /// <summary>Distance from path or river where terrain stays flat (noise masked out).</summary>
    public float flatRadius = 18f;

    /// <summary>Distance over which noise fades in after the flat radius.</summary>
    public float falloffDistance = 35f;

    /// <summary>Number of chunks along each axis (chunkCount × chunkCount).</summary>
    public int chunkCount = 8;

    /// <summary>Deterministic noise seed.</summary>
    public int seed = 42;

    [Header("Splines")]
    [SerializeField] int splineSampleCount = 400;

    /// <summary>Path splines as nested control-point lists; each <see cref="Vector2"/> is local XZ (relative to this transform).</summary>
    public List<List<Vector2>> pathSplines = new();

    /// <summary>River splines; local XZ only. Flow height along each river uses River Local Y High/Low when sampling.</summary>
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
    [SerializeField] int wormSegmentCount = 48;
    [SerializeField] float wormStepLength = 8f;
    [SerializeField] float wormNoiseScale = 0.015f;
    [SerializeField] float wormMaxTurnRadians = 0.45f;
    [SerializeField] float wormBoundaryMargin = 24f;
    [Tooltip("Local Y at river start (high) used when sampling list-based and worm river polylines for flow height.")]
    [SerializeField] float riverWormLocalYHigh = 35f;
    [Tooltip("Local Y at river end (low) used when sampling list-based and worm river polylines for flow height.")]
    [SerializeField] float riverWormLocalYLow = -15f;

    [Header("Rendering")]
    [SerializeField] Transform? cameraTransform;
    [SerializeField] Material? terrainMaterial;
    [SerializeField] int chunkVertexDensity = 32;
    [SerializeField] float lod1Distance = 180f;
    [SerializeField] float lod2Distance = 360f;
    [SerializeField] float lodCameraMoveEpsilonSqr = 0.25f;

    [Header("Navigation")]
    [Tooltip("Optional; if unset, a NavMeshSurface is added to this GameObject at runtime.")]
    [SerializeField] NavMeshSurface? navMeshSurface;
    [Tooltip("Half-width of the bake region on X/Z around the camera (world units). Only colliders overlapping this box are sent to the NavMesh builder.")]
    [SerializeField] float navMeshCameraRegionHalfExtentXZ = 100f;
    [Tooltip("Extra vertical padding below/above the expected terrain height band when fitting the bake volume.")]
    [SerializeField] float navMeshCameraVerticalPadding = 12f;
    [Tooltip("When the camera moves this far in XZ from the last bake focus, schedule a NavMesh rebuild (even if chunk LOD did not change).")]
    [SerializeField] float navMeshCameraRefocusMoveDistance = 42f;
    [Tooltip("Delay before rebuilding the NavMesh after chunk LOD mesh changes (coalesces rapid camera moves). Set to 0 to rebuild immediately.")]
    [SerializeField] float navMeshRebuildDebounceSeconds = 0.35f;

    const int SplatmapResolution = 512;

    SplineSystem _splineSystem = new();
    DistanceFieldBaker _distanceFieldBaker = new();
    HeightmapGenerator _heightmapGenerator = new();
    SplatmapPainter _splatmapPainter = new();
    ChunkManager _chunkManager = new();

    NativeArray<float> _pathDistanceField;
    NativeArray<float> _riverDistanceField;
    NativeArray<float> _riverNearestFlow;
    NativeArray<float> _heightmap;
    NativeArray<float> _splatmapRgba;
    NativeArray<float2> _pathSamples;
    NativeArray<float2> _riverSamples;
    NativeArray<float> _riverFlowHeights;

    Texture2D? _splatmapTexture;

    bool _chunksBuilt;
    Vector3 _lastCameraPos = new(float.NaN, float.NaN, float.NaN);
    bool _navMeshRebuildPending;
    double _navMeshRebuildDueTime;
    Vector2 _lastNavMeshFocusXz = new(float.NaN, float.NaN);

    AsyncOperation? _navMeshUpdateOp;
    bool _navMeshRebuildQueuedAfterAsync;
    Coroutine? _navMeshUpdateCoroutine;

    readonly List<Vector2> _gizmoPathPoints = new();
    readonly List<Vector2> _gizmoRiverPoints = new();

    readonly List<List<Vector2>> _generatedPathSplines = new();
    readonly List<List<Vector2>> _generatedRiverSplines = new();
    readonly List<List<Vector2>> _mergedPathsForBuild = new();
    readonly List<List<Vector2>> _mergedRiversForBuild = new();

    /// <summary>Fired in play mode after chunk meshes and colliders are built (end of <see cref="RunPipeline"/>).</summary>
    public static event Action<TerrainGenerator>? TerrainGenerated;

    /// <summary>True after a successful <see cref="RunPipeline"/> run with a valid heightmap.</summary>
    public bool IsTerrainReady => _chunksBuilt && _heightmap.IsCreated;

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

    void OnEnable()
    {
        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
    }

    void Start()
    {
        if (!Application.isPlaying && !IsEditorValidated())
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
        if (_navMeshUpdateCoroutine != null)
        {
            StopCoroutine(_navMeshUpdateCoroutine);
            _navMeshUpdateCoroutine = null;
        }

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
        if (!_chunksBuilt || cameraTransform == null)
            return;

        var p = cameraTransform.position;
        if (math.distancesq(new float3(_lastCameraPos.x, _lastCameraPos.y, _lastCameraPos.z), new float3(p.x, p.y, p.z)) < lodCameraMoveEpsilonSqr)
            return;

        _lastCameraPos = p;
        var lodDirty = _chunkManager.UpdateLodAndMeshes(
            cameraTransform,
            lod1Distance,
            lod2Distance,
            _heightmap,
            worldResolution,
            worldSize,
            baseHeight,
            maxHeightVariation,
            transform,
            chunkCount,
            chunkVertexDensity);
        if (lodDirty || ShouldRefocusNavMeshAroundCamera(p))
            RequestNavMeshRebuild();
    }

    bool ShouldRefocusNavMeshAroundCamera(Vector3 camWorld)
    {
        if (float.IsNaN(_lastNavMeshFocusXz.x))
            return false;

        var xz = new Vector2(camWorld.x, camWorld.z);
        var d = navMeshCameraRefocusMoveDistance;
        return (xz - _lastNavMeshFocusXz).sqrMagnitude >= d * d;
    }

    void LateUpdate()
    {
        if (!_navMeshRebuildPending || !_chunksBuilt)
            return;

        if (EditorOrRuntimeTime() < _navMeshRebuildDueTime)
            return;

        _navMeshRebuildPending = false;
        RebuildNavMesh();
    }

    static double EditorOrRuntimeTime()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return EditorApplication.timeSinceStartup;
#endif
        return Time.unscaledTimeAsDouble;
    }

    void EnsureNavMeshSurface()
    {
        if (navMeshSurface == null)
            navMeshSurface = GetComponent<NavMeshSurface>();
        if (navMeshSurface == null)
        {
            navMeshSurface = gameObject.AddComponent<NavMeshSurface>();
            navMeshSurface.collectObjects = CollectObjects.Volume;
            navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        }
    }

    void ApplyNavMeshCameraVolume()
    {
        if (navMeshSurface == null)
            return;

        navMeshSurface.collectObjects = CollectObjects.Volume;
        navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

        var half = math.max(4f, navMeshCameraRegionHalfExtentXZ);
        var pad = math.max(0f, navMeshCameraVerticalPadding);
        var yMin = transform.position.y + baseHeight - pad;
        var yMax = transform.position.y + baseHeight + maxHeightVariation + pad;
        var yMid = 0.5f * (yMin + yMax);
        var ySize = math.max(8f, yMax - yMin);

        Vector3 worldCenter;
        if (cameraTransform != null)
        {
            var c = cameraTransform.position;
            worldCenter = new Vector3(c.x, yMid, c.z);
        }
        else
        {
            worldCenter = transform.TransformPoint(Vector3.zero);
            worldCenter.y = yMid;
        }

        navMeshSurface.center = navMeshSurface.transform.InverseTransformPoint(worldCenter);
        navMeshSurface.size = new Vector3(half * 2f, ySize, half * 2f);
    }

    void RebuildNavMesh()
    {
        if (!_chunksBuilt)
            return;

        EnsureNavMeshSurface();
        ApplyNavMeshCameraVolume();
        var surface = navMeshSurface!;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            surface.BuildNavMesh();
            if (cameraTransform != null)
                _lastNavMeshFocusXz = new Vector2(cameraTransform.position.x, cameraTransform.position.z);
            return;
        }
#endif

        if (surface.navMeshData == null)
        {
            surface.BuildNavMesh();
            if (cameraTransform != null)
                _lastNavMeshFocusXz = new Vector2(cameraTransform.position.x, cameraTransform.position.z);
            return;
        }

        if (_navMeshUpdateOp != null && !_navMeshUpdateOp.isDone)
        {
            _navMeshRebuildQueuedAfterAsync = true;
            return;
        }

        _navMeshUpdateOp = surface.UpdateNavMesh(surface.navMeshData);
        if (_navMeshUpdateCoroutine != null)
            StopCoroutine(_navMeshUpdateCoroutine);
        _navMeshUpdateCoroutine = StartCoroutine(WaitForNavMeshUpdateCoroutine());
    }

    IEnumerator WaitForNavMeshUpdateCoroutine()
    {
        if (_navMeshUpdateOp == null)
            yield break;

        yield return _navMeshUpdateOp;
        _navMeshUpdateOp = null;
        _navMeshUpdateCoroutine = null;

        if (cameraTransform != null)
            _lastNavMeshFocusXz = new Vector2(cameraTransform.position.x, cameraTransform.position.z);

        if (_navMeshRebuildQueuedAfterAsync)
        {
            _navMeshRebuildQueuedAfterAsync = false;
            RebuildNavMesh();
        }
    }

    void RequestNavMeshRebuild()
    {
        if (!_chunksBuilt)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            RebuildNavMesh();
            return;
        }
#endif

        var debounce = System.Math.Max(0.0, navMeshRebuildDebounceSeconds);
        if (debounce <= 0.0)
        {
            RebuildNavMesh();
            return;
        }

        _navMeshRebuildPending = true;
        _navMeshRebuildDueTime = EditorOrRuntimeTime() + debounce;
    }

    /// <summary>
    /// Disposes native buffers and rebuilds the full procedural pipeline (edit or play mode).
    /// </summary>
    [ContextMenu("Regenerate")]
    public void Regenerate()
    {
        DisposeNativeBuffers();
        _chunkManager.DestroyChunkObjects();
        _chunksBuilt = false;
        if (_splatmapTexture != null)
        {
            if (Application.isPlaying)
                Destroy(_splatmapTexture);
            else
                DestroyImmediate(_splatmapTexture);
            _splatmapTexture = null;
        }

        RunPipeline();
    }

    /// <summary>
    /// Destroys pooled chunk objects and disposes all native allocations for a clean Inspector reset.
    /// </summary>
    [ContextMenu("Clear")]
    public void ClearTerrain()
    {
        DisposeNativeBuffers();
        _chunkManager.DestroyChunkObjects();
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
            riverWormLocalYHigh,
            riverWormLocalYLow,
            Allocator.Persistent,
            out _pathSamples,
            out _riverSamples,
            out _riverFlowHeights);

        RebuildGizmoPoints();

        var count = worldResolution * worldResolution;
        _pathDistanceField = new NativeArray<float>(count, Allocator.Persistent);
        _riverDistanceField = new NativeArray<float>(count, Allocator.Persistent);
        _riverNearestFlow = new NativeArray<float>(count, Allocator.Persistent);
        _heightmap = new NativeArray<float>(count, Allocator.Persistent);
        _splatmapRgba = new NativeArray<float>(SplatmapResolution * SplatmapResolution * 4, Allocator.Persistent);

        _distanceFieldBaker.Bake(
            _pathSamples,
            _riverSamples,
            _riverFlowHeights,
            worldResolution,
            worldSize,
            transform.position,
            _pathDistanceField,
            _riverDistanceField,
            _riverNearestFlow);

        _heightmapGenerator.Generate(
            _pathDistanceField,
            _riverDistanceField,
            _riverNearestFlow,
            worldResolution,
            worldSize,
            baseHeight,
            maxHeightVariation,
            flatRadius,
            falloffDistance,
            seed,
            transform.position,
            _heightmap);

        _splatmapPainter.Paint(
            _heightmap,
            _pathDistanceField,
            _riverDistanceField,
            worldResolution,
            worldSize,
            SplatmapResolution,
            transform.position,
            _splatmapRgba);

        _splatmapTexture = new Texture2D(SplatmapResolution, SplatmapResolution, TextureFormat.RGBA32, false, true)
        {
            name = "ProceduralSplatmap",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _splatmapTexture.SetPixelData(_splatmapRgba, 0);
        _splatmapTexture.Apply(false, false);

        _chunkManager.InitializePool(transform, chunkCount, worldSize, "TerrainChunk");
        _chunksBuilt = true;

        _chunkManager.GenerateAllChunkMeshes(
            _heightmap,
            worldResolution,
            worldSize,
            chunkCount,
            chunkVertexDensity,
            baseHeight,
            maxHeightVariation,
            transform.position,
            cameraTransform,
            lod1Distance,
            lod2Distance);

        _chunkManager.AssignMaterial(terrainMaterial);
        if (cameraTransform != null)
            _lastCameraPos = cameraTransform.position;

        _navMeshRebuildPending = false;
        RebuildNavMesh();

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
            Seed = seed,
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
        if (_riverNearestFlow.IsCreated)
            _riverNearestFlow.Dispose();
        if (_heightmap.IsCreated)
            _heightmap.Dispose();
        if (_splatmapRgba.IsCreated)
            _splatmapRgba.Dispose();
        if (_pathSamples.IsCreated)
            _pathSamples.Dispose();
        if (_riverSamples.IsCreated)
            _riverSamples.Dispose();
        if (_riverFlowHeights.IsCreated)
            _riverFlowHeights.Dispose();
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
    /// Builds merged spline samples for distance fields and river flow heights.
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
            float riverFlowLocalYHigh,
            float riverFlowLocalYLow,
            Allocator allocator,
            out NativeArray<float2> pathSamples,
            out NativeArray<float2> riverSamples,
            out NativeArray<float> riverFlowHeights)
        {
            using var pathTmp = new NativeList<float2>(Allocator.Temp);
            using var riverTmp = new NativeList<float2>(Allocator.Temp);
            using var flowTmp = new NativeList<float>(Allocator.Temp);

            AppendListSplines(terrainRoot, pathSplines, samplesPerSpline, pathTmp, default, 0f, 0f);
            AppendContainerSplines(authoringPathSplines, samplesPerSpline, pathTmp, default);

            AppendListSplines(terrainRoot, riverSplines, samplesPerSpline, riverTmp, flowTmp, riverFlowLocalYHigh, riverFlowLocalYLow);
            AppendContainerSplines(authoringRiverSplines, samplesPerSpline, riverTmp, flowTmp);

            if (pathTmp.Length == 0)
                pathTmp.Add(float2.zero);

            if (riverTmp.Length == 0)
            {
                riverTmp.Add(float2.zero);
                flowTmp.Add(0f);
            }

            pathSamples = new NativeArray<float2>(pathTmp.AsArray(), allocator);
            riverSamples = new NativeArray<float2>(riverTmp.AsArray(), allocator);
            riverFlowHeights = new NativeArray<float>(flowTmp.AsArray(), allocator);
        }

        static void AppendListSplines(
            Transform terrainRoot,
            List<List<Vector2>> splines,
            int samplesPerSpline,
            NativeList<float2> xzOut,
            NativeList<float> flowOut,
            float riverLocalYHigh,
            float riverLocalYLow)
        {
            if (splines == null)
                return;

            foreach (var ctrl in splines)
            {
                if (ctrl == null || ctrl.Count < 2)
                    continue;

                SampleCatmullRom2D(terrainRoot, ctrl, samplesPerSpline, xzOut, flowOut, riverLocalYHigh, riverLocalYLow);
            }
        }

        static void AppendContainerSplines(
            List<SplineContainer>? containers,
            int samplesPerSpline,
            NativeList<float2> xzOut,
            NativeList<float> flowOut)
        {
            if (containers == null)
                return;

            foreach (var c in containers)
            {
                if (c == null)
                    continue;

                if (c.Spline.Count < 2)
                    continue;

                var yStart = c.EvaluatePosition(0f).y;
                var yEnd = c.EvaluatePosition(1f).y;
                var hi = math.max(yStart, yEnd);
                var lo = math.min(yStart, yEnd);

                for (var s = 0; s < samplesPerSpline; s++)
                {
                    var t = samplesPerSpline <= 1 ? 0f : s / (float)(samplesPerSpline - 1);
                    var world = c.EvaluatePosition(t);
                    xzOut.Add(new float2(world.x, world.z));
                    if (flowOut.IsCreated)
                        flowOut.Add(math.lerp(hi, lo, t));
                }
            }
        }

        /// <summary>Control points are local XZ; <paramref name="riverLocalYHigh"/>/<paramref name="riverLocalYLow"/> define flow height when <paramref name="flowOut"/> is used.</summary>
        static void SampleCatmullRom2D(
            Transform terrainRoot,
            List<Vector2> controls,
            int totalSamples,
            NativeList<float2> xzOut,
            NativeList<float> flowOut,
            float riverLocalYHigh,
            float riverLocalYLow)
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
                float localY;
                if (flowOut.IsCreated)
                    localY = math.lerp(riverLocalYHigh, riverLocalYLow, u);
                else
                    localY = 0f;

                var world = terrainRoot.TransformPoint(new Vector3(pos2.x, localY, pos2.y));
                xzOut.Add(new float2(world.x, world.z));

                if (flowOut.IsCreated)
                    flowOut.Add(world.y);
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
        /// Runs Burst jobs to populate path and river distance fields (and nearest river flow height).
        /// </summary>
        public void Bake(
            NativeArray<float2> pathSamples,
            NativeArray<float2> riverSamples,
            NativeArray<float> riverFlowHeights,
            int resolution,
            float worldSize,
            Vector3 worldOrigin,
            NativeArray<float> pathDistanceField,
            NativeArray<float> riverDistanceField,
            NativeArray<float> riverNearestFlow)
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
                FlowSamples = riverFlowHeights,
                Resolution = resolution,
                WorldSize = worldSize,
                WorldOrigin = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z),
                DistanceOut = riverDistanceField,
                FlowOut = riverNearestFlow
            }.Schedule(riverDistanceField.Length, 64);

            pathJob.Complete();
            riverJob.Complete();
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
            [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float> FlowSamples;
            public int Resolution;
            public float WorldSize;
            public float3 WorldOrigin;
            public NativeArray<float> DistanceOut;
            public NativeArray<float> FlowOut;

            public void Execute(int index)
            {
                var res = Resolution;
                var ix = index % res;
                var iz = index / res;
                var wx = WorldOrigin.x + ((ix + 0.5f) / res - 0.5f) * WorldSize;
                var wz = WorldOrigin.z + ((iz + 0.5f) / res - 0.5f) * WorldSize;
                var p = new float2(wx, wz);

                var best = float.MaxValue;
                var bestFlow = 0f;
                for (var i = 0; i < Samples.Length; i++)
                {
                    var d = math.distance(p, Samples[i]);
                    if (d < best)
                    {
                        best = d;
                        if (FlowSamples.IsCreated && i < FlowSamples.Length)
                            bestFlow = FlowSamples[i];
                    }
                }

                DistanceOut[index] = best;
                FlowOut[index] = bestFlow;
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
            NativeArray<float> riverFlow,
            int resolution,
            float worldSize,
            float baseH,
            float maxVar,
            float flatR,
            float falloff,
            int noiseSeed,
            Vector3 worldOrigin,
            NativeArray<float> heightOut)
        {
            new HeightmapJob
            {
                PathDf = pathDf,
                RiverDf = riverDf,
                RiverFlow = riverFlow,
                Resolution = resolution,
                WorldSize = worldSize,
                WorldOrigin = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z),
                BaseHeight = baseH,
                MaxVariation = maxVar,
                FlatRadius = flatR,
                FalloffDistance = falloff,
                Seed = noiseSeed,
                HeightOut = heightOut
            }.Schedule(heightOut.Length, 64).Complete();
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        struct HeightmapJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> PathDf;
            [ReadOnly] public NativeArray<float> RiverDf;
            [ReadOnly] public NativeArray<float> RiverFlow;
            public int Resolution;
            public float WorldSize;
            public float3 WorldOrigin;
            public float BaseHeight;
            public float MaxVariation;
            public float FlatRadius;
            public float FalloffDistance;
            public int Seed;
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

                var t = (distToAny - FlatRadius) / math.max(1e-4f, FalloffDistance);
                var variationMask = math.smoothstep(0f, 1f, math.saturate(t));

                var noiseCoord = new float2(wx, wz) * 0.0042f + new float2(Seed * 0.031f, Seed * 0.017f);
                var n = Fbm3(noiseCoord);
                var biased = math.pow(math.abs(n), 1.8f) * math.sign(n);
                var noiseTerm = biased * MaxVariation * variationMask;

                var riverFlowH = RiverFlow[index];
                var baseNoiseH = BaseHeight + noiseTerm;

                var inner = 2.2f;
                var outer = 6.0f;
                var carveMask = math.smoothstep(outer, inner, riverDist);
                var targetBed = riverFlowH - 3f;
                var depth = math.max(0f, baseNoiseH - targetBed);
                var riverCarve = math.min(3f, carveMask * math.smoothstep(0f, 3f, depth));

                var bankMask = math.smoothstep(inner, inner + 3.5f, riverDist) * (1f - math.smoothstep(inner + 3.5f, inner + 7f, riverDist));
                var bankRaise = bankMask * 0.6f;

                HeightOut[index] = baseNoiseH - riverCarve + bankRaise;
            }

            static float Fbm3(float2 p)
            {
                var sum = 0f;
                var amp = 0.5f;
                var freq = 1f;
                for (var o = 0; o < 3; o++)
                {
                    sum += amp * noise.snoise(p * freq);
                    freq *= 2.02f;
                    amp *= 0.5f;
                }

                return sum;
            }
        }
    }

    #endregion

    #region SplatmapPainter

    /// <summary>
    /// Paints a normalized RGBA splatmap using Burst and height/slope cues.
    /// </summary>
    public sealed class SplatmapPainter
    {
        /// <summary>
        /// Schedules splat painting and completes the job.
        /// </summary>
        public void Paint(
            NativeArray<float> heightmap,
            NativeArray<float> pathDf,
            NativeArray<float> riverDf,
            int heightResolution,
            float worldSize,
            int splatResolution,
            Vector3 worldOrigin,
            NativeArray<float> rgbaOut)
        {
            new SplatmapJob
            {
                Heightmap = heightmap,
                PathDf = pathDf,
                RiverDf = riverDf,
                HeightResolution = heightResolution,
                WorldSize = worldSize,
                SplatResolution = splatResolution,
                WorldOrigin = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z),
                RgbaOut = rgbaOut
            }.Schedule(splatResolution * splatResolution, 64).Complete();
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        struct SplatmapJob : IJobParallelFor
        {
            [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float> Heightmap;
            [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float> PathDf;
            [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float> RiverDf;
            public int HeightResolution;
            public float WorldSize;
            public int SplatResolution;
            public float3 WorldOrigin;
            [NativeDisableParallelForRestriction] public NativeArray<float> RgbaOut;

            public void Execute(int index)
            {
                var res = SplatResolution;
                var ix = index % res;
                var iz = index / res;
                var wx = WorldOrigin.x + ((ix + 0.5f) / res - 0.5f) * WorldSize;
                var wz = WorldOrigin.z + ((iz + 0.5f) / res - 0.5f) * WorldSize;

                var h = SampleHeightBilinear(wx, wz);
                var gx = SampleHeightBilinear(wx + 0.75f, wz);
                var gz = SampleHeightBilinear(wx, wz + 0.75f);
                var slope = math.length(new float2(gx - h, gz - h));

                var grass = math.saturate(1f - slope * 3.5f);
                var rock = math.saturate(slope * 4.5f - 0.15f);
                var sand = 0.05f;

                var pathDist = SampleDfWorld(PathDf, HeightResolution, WorldSize, WorldOrigin, wx, wz);
                var pathWeight = math.smoothstep(8f, 0f, pathDist);

                var riverDist = SampleDfWorld(RiverDf, HeightResolution, WorldSize, WorldOrigin, wx, wz);
                var bankBlend = math.smoothstep(6f, 0f, riverDist);
                sand = math.lerp(sand, 1f, bankBlend);

                var r = grass;
                var g = rock;
                var b = sand;
                var a = pathWeight;

                var rgb = new float3(r, g, b) * (1f - a);
                r = rgb.x;
                g = rgb.y;
                b = rgb.z;

                var sum = r + g + b + a + 1e-5f;
                r /= sum;
                g /= sum;
                b /= sum;
                a /= sum;

                var baseIndex = index * 4;
                RgbaOut[baseIndex + 0] = r;
                RgbaOut[baseIndex + 1] = g;
                RgbaOut[baseIndex + 2] = b;
                RgbaOut[baseIndex + 3] = a;
            }

            float SampleHeightBilinear(float wx, float wz)
            {
                var hr = HeightResolution;
                var fx = (wx - WorldOrigin.x + WorldSize * 0.5f) / WorldSize * (hr - 1);
                var fz = (wz - WorldOrigin.z + WorldSize * 0.5f) / WorldSize * (hr - 1);
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
        int _chunkAxis;
        float3 _worldOrigin;
        Transform? _terrainRoot;
        bool _scratchAllocated;

        /// <summary>
        /// Creates pooled chunk objects under the provided root.
        /// </summary>
        public void InitializePool(Transform root, int chunkAxis, float worldSize, string chunkName)
        {
            DisposeScratch();

            _terrainRoot = root;
            _chunkAxis = chunkAxis;
            _totalChunks = chunkAxis * chunkAxis;
            _filters.Clear();
            _renderers.Clear();
            _colliders.Clear();
            _meshes.Clear();

            var chunkWorld = worldSize / math.max(1, chunkAxis);
            for (var z = 0; z < chunkAxis; z++)
            {
                for (var x = 0; x < chunkAxis; x++)
                {
                    var go = new GameObject($"{chunkName}_{x}_{z}");
                    go.transform.SetParent(root, false);
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
        /// Destroys pooled chunk GameObjects.
        /// </summary>
        public void DestroyChunkObjects()
        {
            DisposeScratch();

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
        /// Builds meshes for all chunks at the current LOD selection.
        /// </summary>
        public void GenerateAllChunkMeshes(
            NativeArray<float> heightmap,
            int heightResolution,
            float worldSize,
            int chunkAxis,
            int baseDensity,
            float baseHeight,
            float maxVariation,
            Vector3 worldOrigin,
            Transform? camera,
            float lod1,
            float lod2)
        {
            _chunkAxis = chunkAxis;
            _worldOrigin = new float3(worldOrigin.x, worldOrigin.y, worldOrigin.z);

            EnsureScratch(baseDensity);

            for (var i = 0; i < _totalChunks; i++)
            {
                var lod = SelectLod(i, chunkAxis, worldSize, camera, lod1, lod2);
                _lodLevels[i] = lod;
            }

            var handle = new ChunkMeshJob
            {
                Heightmap = heightmap,
                HeightResolution = heightResolution,
                WorldSize = worldSize,
                WorldOrigin = _worldOrigin,
                ChunkAxis = chunkAxis,
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

            UploadMeshesFromScratch(baseDensity);
        }

        /// <summary>
        /// Updates LOD and rebuilds meshes when the camera moves meaningfully.
        /// </summary>
        public bool UpdateLodAndMeshes(
            Transform camera,
            float lod1,
            float lod2,
            NativeArray<float> heightmap,
            int heightResolution,
            float worldSize,
            float baseHeight,
            float maxVariation,
            Transform root,
            int chunkAxis,
            int baseDensity)
        {
            if (_meshes.Count == 0 || !_lodLevels.IsCreated)
                return false;

            _terrainRoot = root;
            _chunkAxis = chunkAxis;
            _worldOrigin = new float3(root.position.x, root.position.y, root.position.z);

            var dirty = false;
            for (var i = 0; i < _totalChunks; i++)
            {
                var lod = SelectLod(i, chunkAxis, worldSize, camera, lod1, lod2);
                if (lod != _lodLevels[i])
                {
                    _lodLevels[i] = lod;
                    dirty = true;
                }
            }

            if (!dirty)
                return false;

            var handle = new ChunkMeshJob
            {
                Heightmap = heightmap,
                HeightResolution = heightResolution,
                WorldSize = worldSize,
                WorldOrigin = _worldOrigin,
                ChunkAxis = chunkAxis,
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

            UploadMeshesFromScratch(baseDensity);
            return true;
        }

        void UploadMeshesFromScratch(int baseDensity)
        {
            if (_meshUploadSlots.Length != _totalChunks)
                return;

            var meshDataArray = Mesh.AllocateWritableMeshData(_totalChunks);
            for (var i = 0; i < _totalChunks; i++)
            {
                var seg = DensityForLod(baseDensity, _lodLevels[i]);
                var vertCount = (seg + 1) * (seg + 1);
                var indexCount = seg * seg * 6;
                var vStart = i * _maxVertsPerChunk;
                var iStart = i * _maxIndicesPerChunk;

                var meshData = meshDataArray[i];
                meshData.SetVertexBufferParams(vertCount,
                    new VertexAttributeDescriptor(VertexAttribute.Position),
                    new VertexAttributeDescriptor(VertexAttribute.Normal),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, dimension: 2),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord1, dimension: 2));
                meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                meshData.subMeshCount = 1;

                var vDst = meshData.GetVertexData<TerrainMeshVertex>(0);
                var vSrc = _vertexScratch.GetSubArray(vStart, vertCount);
                for (var k = 0; k < vertCount; k++)
                    vDst[k] = vSrc[k];

                var iDst = meshData.GetIndexData<uint>();
                var iSrc = _indexScratch.GetSubArray(iStart, indexCount);
                for (var k = 0; k < indexCount; k++)
                    iDst[k] = iSrc[k];

                meshData.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), MeshUpdateFlags.DontRecalculateBounds);
            }

            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, _meshUploadSlots, MeshUpdateFlags.Default);

            for (var i = 0; i < _totalChunks; i++)
            {
                var mesh = _meshes[i];
                mesh.RecalculateBounds();
                if (i < _colliders.Count)
                {
                    var mc = _colliders[i];
                    if (mc != null)
                    {
                        mc.sharedMesh = null;
                        mc.sharedMesh = mesh;
                    }
                }
            }
        }

        int SelectLod(int chunkIndex, int chunkAxis, float worldSize, Transform? camera, float lod1, float lod2)
        {
            var cx = chunkIndex % chunkAxis;
            var cz = chunkIndex / chunkAxis;
            var chunkWorld = worldSize / chunkAxis;
            var localCenter = new Vector3(-worldSize * 0.5f + (cx + 0.5f) * chunkWorld, 0f, -worldSize * 0.5f + (cz + 0.5f) * chunkWorld);
            if (camera == null || _terrainRoot == null)
                return 0;

            var worldCenter = _terrainRoot.TransformPoint(localCenter);
            var dist = Vector2.Distance(
                new Vector2(worldCenter.x, worldCenter.z),
                new Vector2(camera.position.x, camera.position.z));
            if (dist > lod2)
                return 2;
            if (dist > lod1)
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
                var lod = LodLevels[chunkIndex];
                var seg = BaseDensity;
                if (lod == 1)
                    seg = math.max(2, BaseDensity / 2);
                else if (lod == 2)
                    seg = math.max(2, BaseDensity / 4);

                var vertsPerAxis = seg + 1;
                var chunkWorld = WorldSize / ChunkAxis;
                var cx = chunkIndex % ChunkAxis;
                var cz = chunkIndex / ChunkAxis;

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
