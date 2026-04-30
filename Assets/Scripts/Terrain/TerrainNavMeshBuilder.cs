#nullable enable
using System;
using System.Collections;
using Unity.AI.Navigation;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Camera-follow NavMesh bake volume, async updates, and water-area modifier for procedural terrain.
/// Expects a <see cref="TerrainGenerator"/> on the same GameObject (or assign explicitly).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class TerrainNavMeshBuilder : MonoBehaviour
{
    const string NavMeshWaterLevelVolumeObjectName = "NavMeshWaterLevelVolume";
    const string NavMeshBakeExcludeLayerName = "Character";

    /// <summary>Must match Project Settings &gt; AI Navigation &gt; Areas (e.g. cost 3 for water).</summary>
    public const string DefaultNavMeshWaterAreaName = "Water";

    [SerializeField] TerrainGenerator? terrainGenerator;

    [Header("Navigation")]
    [Tooltip("Optional; if unset, a NavMeshSurface is added to this GameObject at runtime.")]
    [SerializeField] NavMeshSurface? navMeshSurface;
    [Tooltip("Half-width of the bake region on X/Z around the camera (world units).")]
    [SerializeField] float navMeshCameraRegionHalfExtentXZ = 100f;
    [Tooltip("Extra vertical padding below/above the expected terrain height band when fitting the bake volume.")]
    [SerializeField] float navMeshCameraVerticalPadding = 12f;
    [Tooltip("When the camera moves this far in XZ from the last bake focus, schedule a NavMesh rebuild.")]
    [SerializeField] float navMeshCameraRefocusMoveDistance = 42f;
    [Tooltip("Delay after chunk LOD mesh changes before rebuilding (coalesces rapid camera moves). 0 = immediate.")]
    [SerializeField] float navMeshRebuildDebounceSeconds = 0.35f;
    [Tooltip("World Y for sea level; NavMesh below this in the volume uses the 'Water' area.")]
    [SerializeField] float navMeshWaterLevelY = -0.1f;
    [Tooltip("Depth of the under-water tag volume in world Y.")]
    [SerializeField] float navMeshWaterVolumeDepth = 20000f;
    [Tooltip("Extra XZ half-extent beyond procedural worldSize so the Water volume covers baked colliders.")]
    [SerializeField] float navMeshWaterVolumeXzExtraHalfExtent = 32f;

    bool _navMeshRebuildPending;
    double _navMeshRebuildDueTime;
    Vector2 _lastNavMeshFocusXz = new(float.NaN, float.NaN);
    bool _warnedMissingWaterNavMeshArea;
    NavMeshModifierVolume? _navMeshWaterLevelVolume;

    AsyncOperation? _navMeshUpdateOp;
    bool _navMeshRebuildQueuedAfterAsync;
    Coroutine? _navMeshUpdateCoroutine;

    void Reset()
    {
        terrainGenerator ??= GetComponent<TerrainGenerator>();
    }

    void OnValidate()
    {
        if (terrainGenerator == null)
            terrainGenerator = GetComponent<TerrainGenerator>();
    }

    void Awake()
    {
        if (terrainGenerator == null)
            terrainGenerator = GetComponent<TerrainGenerator>();
    }

    void OnDestroy()
    {
        if (_navMeshUpdateCoroutine != null)
        {
            StopCoroutine(_navMeshUpdateCoroutine);
            _navMeshUpdateCoroutine = null;
        }
    }

    void LateUpdate()
    {
        var terrain = terrainGenerator;
        if (terrain == null || !_navMeshRebuildPending || !terrain.IsTerrainReady)
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

    /// <summary>
    /// Called from <see cref="TerrainGenerator"/> after chunk LOD meshes change or the camera moves enough to refocus the bake volume.
    /// </summary>
    public void NotifyCameraOrLodChange(Vector3 cameraWorld, bool chunkMeshesChanged)
    {
        var terrain = terrainGenerator;
        if (terrain == null || !terrain.IsTerrainReady)
            return;

        if (chunkMeshesChanged || ShouldRefocusNavMeshAroundCamera(cameraWorld))
            RequestNavMeshRebuild();
    }

    /// <summary>
    /// Called at the end of terrain generation; clears debounce and rebuilds immediately.
    /// </summary>
    public void RebuildImmediatelyAfterTerrainPipeline()
    {
        var terrain = terrainGenerator;
        if (terrain == null || !terrain.IsTerrainReady)
            return;

        _navMeshRebuildPending = false;
        RebuildNavMesh();
    }

    bool ShouldRefocusNavMeshAroundCamera(Vector3 camWorld)
    {
        if (float.IsNaN(_lastNavMeshFocusXz.x))
            return false;

        var xz = new Vector2(camWorld.x, camWorld.z);
        var d = navMeshCameraRefocusMoveDistance;
        return (xz - _lastNavMeshFocusXz).sqrMagnitude >= d * d;
    }

    void EnsureNavMeshWaterLevelAreaVolume()
    {
        var terrain = terrainGenerator;
        if (terrain == null)
            return;

        int water = NavMesh.GetAreaFromName(DefaultNavMeshWaterAreaName);
        if (water < 0)
        {
            if (!_warnedMissingWaterNavMeshArea)
            {
                _warnedMissingWaterNavMeshArea = true;
                Debug.LogWarning(
                    $"NavMesh area '{DefaultNavMeshWaterAreaName}' not found. Add it under Project Settings > AI Navigation (Areas) or the NavMesh will not tag under-water polys.");
            }

            if (_navMeshWaterLevelVolume != null)
                _navMeshWaterLevelVolume.enabled = false;
            return;
        }

        if (_navMeshWaterLevelVolume == null)
        {
            var child = transform.Find(NavMeshWaterLevelVolumeObjectName);
            if (child == null)
            {
                var go = new GameObject(NavMeshWaterLevelVolumeObjectName);
                child = go.transform;
                child.SetParent(transform, false);
            }

            _navMeshWaterLevelVolume = child.GetComponent<NavMeshModifierVolume>();
            if (_navMeshWaterLevelVolume == null)
                _navMeshWaterLevelVolume = child.gameObject.AddComponent<NavMeshModifierVolume>();
        }

        _navMeshWaterLevelVolume.enabled = true;
        _navMeshWaterLevelVolume.area = water;

        var t = _navMeshWaterLevelVolume.transform;
        t.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Inverse(transform.rotation));
        t.localScale = Vector3.one;

        var top = navMeshWaterLevelY;
        var h = math.max(0.1f, navMeshWaterVolumeDepth);
        var bottom = top - h;
        var cY = 0.5f * (top + bottom) - transform.position.y;
        var halfXz = terrain.worldSize * 0.5f + navMeshWaterVolumeXzExtraHalfExtent;
        _navMeshWaterLevelVolume.size = new Vector3(2f * halfXz, h, 2f * halfXz);
        _navMeshWaterLevelVolume.center = new Vector3(0f, cY, 0f);
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
        var terrain = terrainGenerator;
        if (navMeshSurface == null || terrain == null)
            return;

        navMeshSurface.collectObjects = CollectObjects.Volume;
        navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

        var half = math.max(4f, navMeshCameraRegionHalfExtentXZ);
        var pad = math.max(0f, navMeshCameraVerticalPadding);
        var yMin = transform.position.y + terrain.baseHeight - pad;
        var yMax = transform.position.y + terrain.baseHeight + terrain.maxHeightVariation + pad;
        var yMid = 0.5f * (yMin + yMax);
        var ySize = math.max(8f, yMax - yMin);

        Vector3 worldCenter;
        var cam = terrain.CameraTransform;
        if (cam != null)
        {
            var c = cam.position;
            worldCenter = new Vector3(c.x, yMid, c.z);
        }
        else
        {
            worldCenter = transform.TransformPoint(Vector3.zero);
            worldCenter.y = yMid;
        }

        navMeshSurface.center = navMeshSurface.transform.InverseTransformPoint(worldCenter);
        navMeshSurface.size = new Vector3(half * 2f, ySize, half * 2f);

        int characterLayer = LayerMask.NameToLayer(NavMeshBakeExcludeLayerName);
        if (characterLayer >= 0 && characterLayer < 32)
            navMeshSurface.layerMask = Physics.AllLayers & ~(1 << characterLayer);
    }

    void RebuildNavMesh()
    {
        var terrain = terrainGenerator;
        if (terrain == null || !terrain.IsTerrainReady)
            return;

        EnsureNavMeshWaterLevelAreaVolume();
        EnsureNavMeshSurface();
        ApplyNavMeshCameraVolume();
        var surface = navMeshSurface!;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            surface.BuildNavMesh();
            var cam = terrain.CameraTransform;
            if (cam != null)
                _lastNavMeshFocusXz = new Vector2(cam.position.x, cam.position.z);
            return;
        }
#endif

        if (surface.navMeshData == null)
        {
            surface.BuildNavMesh();
            var cam = terrain.CameraTransform;
            if (cam != null)
                _lastNavMeshFocusXz = new Vector2(cam.position.x, cam.position.z);
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

        var terrain = terrainGenerator;
        var cam = terrain != null ? terrain.CameraTransform : null;
        if (cam != null)
            _lastNavMeshFocusXz = new Vector2(cam.position.x, cam.position.z);

        if (_navMeshRebuildQueuedAfterAsync)
        {
            _navMeshRebuildQueuedAfterAsync = false;
            RebuildNavMesh();
        }
    }

    void RequestNavMeshRebuild()
    {
        var terrain = terrainGenerator;
        if (terrain == null || !terrain.IsTerrainReady)
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
}
