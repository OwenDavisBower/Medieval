#nullable enable
using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Procedural grass using a structured instance buffer, GPU frustum culling via compute shader,
/// and <see cref="Graphics.RenderMeshIndirect"/>. Placement skips splat path (R) and rock (G) from
/// <see cref="TerrainGenerator"/> the same way as the terrain shader.
/// </summary>
[DisallowMultipleComponent]
public sealed class GrassIndirectRenderer : MonoBehaviour
{
    const int ThreadGroupSize = 64;
    const string CullKernel = "CullGrass";

    [StructLayout(LayoutKind.Sequential)]
    public struct GrassInstanceData
    {
        public Vector3 Position;
        public float RotationYRad;
        public Vector3 Scale;
        public float Pad;
    }

    [Header("Terrain")]
    [SerializeField] TerrainGenerator? terrainGenerator;

    [Header("Rendering")]
    [Tooltip("Mesh must be indexed (e.g. Unity quad). Submesh 0 is used.")]
    [SerializeField] Mesh? grassMesh;

    [SerializeField] Material? grassMaterial;

    [Tooltip("Camera used for frustum culling and RenderParams.camera. If null, Camera.main is used.")]
    [SerializeField] Camera? targetCamera;

    [SerializeField] ComputeShader? cullingCompute;

    [Header("Placement")]
    [Tooltip("Target instances per square world unit (rejection sampling against splat).")]
    [SerializeField] float density = 0.08f;

    [Tooltip("Upper cap on allocated instances and splat rejection attempts.")]
    [SerializeField] int maxInstances = 400_000;

    [Tooltip("Reject placement when splat path weight (R) is above this.")]
    [SerializeField] float pathSplatThreshold = 0.08f;

    [Tooltip("Reject placement when splat rock weight (G) is above this.")]
    [SerializeField] float rockSplatThreshold = 0.08f;

    [SerializeField] float scaleMin = 0.65f;
    [SerializeField] float scaleMax = 1.35f;

    [SerializeField] int randomSeed = 12345;

    [Tooltip("Rebuild grass when TerrainGenerator finishes regenerating.")]
    [SerializeField] bool rebuildOnTerrainGenerated = true;

    [Header("Bounds")]
    [Tooltip("Vertical padding around terrain for RenderParams.worldBounds (sorting/culling).")]
    [SerializeField] float verticalBoundsPadding = 80f;

    static readonly int GrassInstancesId = Shader.PropertyToID("_GrassInstances");
    static readonly int VisibleIndicesId = Shader.PropertyToID("_VisibleIndices");

    GraphicsBuffer? _instanceBuffer;
    GraphicsBuffer? _visibleAppendBuffer;
    GraphicsBuffer? _argsBuffer;

    GrassInstanceData[]? _cpuInstances;
    int _instanceCount;
    int _allocatedCapacity;

    MaterialPropertyBlock? _mpb;
    Bounds _worldBounds;

    int _kernelCull = -1;

    void OnEnable()
    {
        if (terrainGenerator == null)
            terrainGenerator = TerrainGenerator.GetActiveOrFind();

        if (rebuildOnTerrainGenerated)
            TerrainGenerator.TerrainGenerated += OnTerrainGenerated;

        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;

        if (terrainGenerator != null && terrainGenerator.IsTerrainReady)
            RebuildGrassInstances();
    }

    void OnDisable()
    {
        if (rebuildOnTerrainGenerated)
            TerrainGenerator.TerrainGenerated -= OnTerrainGenerated;

        RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
    }

    void OnDestroy()
    {
        ReleaseBuffers();
    }

    void OnTerrainGenerated(TerrainGenerator _)
    {
        RebuildGrassInstances();
    }

    /// <summary>Call after changing terrain or grass settings to repopulate the instance buffer.</summary>
    public void RebuildGrassInstances()
    {
        if (terrainGenerator == null)
            terrainGenerator = TerrainGenerator.GetActiveOrFind();

        if (terrainGenerator == null || !terrainGenerator.IsTerrainReady)
            return;

        var splat = terrainGenerator.SplatmapTexture;
        if (splat == null || grassMesh == null)
            return;

        if (cullingCompute == null)
        {
            Debug.LogWarning($"{nameof(GrassIndirectRenderer)}: assign a compute shader (GrassFrustumCull).", this);
            return;
        }

        if (grassMesh.indexFormat == IndexFormat.UInt16 && grassMesh.vertexCount > 65535)
        {
            Debug.LogWarning($"{nameof(GrassIndirectRenderer)}: mesh may overflow 16-bit indices.", grassMesh);
        }

        if (grassMesh.GetIndexCount(0) == 0)
        {
            Debug.LogWarning($"{nameof(GrassIndirectRenderer)}: grass mesh must have indices for indirect indexed draw.", grassMesh);
            return;
        }

        _kernelCull = cullingCompute.FindKernel(CullKernel);
        cullingCompute.GetKernelThreadGroupSizes(_kernelCull, out var x, out _, out _);
        if (x != ThreadGroupSize)
            Debug.LogWarning($"{nameof(GrassIndirectRenderer)}: expected {ThreadGroupSize} threads/group for {CullKernel}, got {x}.", cullingCompute);

        var origin = terrainGenerator.transform.position;
        float worldSize = terrainGenerator.worldSize;
        float half = worldSize * 0.5f;
        float area = worldSize * worldSize;
        int target = Mathf.Min(maxInstances, Mathf.Max(0, Mathf.RoundToInt(density * area)));

        if (_cpuInstances == null || _cpuInstances.Length < target)
            _cpuInstances = new GrassInstanceData[target];

        var rng = new System.Random(randomSeed);
        int res = splat.width;

        int written = 0;
        int attempts = 0;
        int maxAttempts = Mathf.Max(target * 25, target + 1);

        using (var splatData = splat.GetRawTextureData<float>())
        {
            while (written < target && attempts < maxAttempts)
            {
                attempts++;
                float wx = origin.x + (float)(rng.NextDouble() * worldSize - half);
                float wz = origin.z + (float)(rng.NextDouble() * worldSize - half);

                float u = (wx - origin.x) / worldSize + 0.5f;
                float v = (wz - origin.z) / worldSize + 0.5f;

                SampleSplatRG(splatData, res, u, v, out float pathW, out float rockW);
                if (pathW > pathSplatThreshold || rockW > rockSplatThreshold)
                    continue;

                float y = terrainGenerator.SampleHeightWorldXZ(wx, wz);
                float rot = (float)(rng.NextDouble() * (Math.PI * 2.0));
                float sx = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
                float sz = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
                float sy = Mathf.Lerp(scaleMin * 0.85f, scaleMax * 1.1f, (float)rng.NextDouble());

                ref var g = ref _cpuInstances[written];
                g.Position = new Vector3(wx, y, wz);
                g.RotationYRad = rot;
                g.Scale = new Vector3(sx, sy, sz);
                g.Pad = 0f;
                written++;
            }
        }

        _instanceCount = written;

        if (_instanceCount == 0)
        {
            ReleaseBuffers();
            return;
        }

        float yMin = terrainGenerator.baseHeight - terrainGenerator.maxHeightVariation - verticalBoundsPadding;
        float yMax = terrainGenerator.baseHeight + terrainGenerator.maxHeightVariation + verticalBoundsPadding;
        _worldBounds = new Bounds(
            new Vector3(origin.x, (yMin + yMax) * 0.5f, origin.z),
            new Vector3(worldSize, yMax - yMin, worldSize));

        EnsureGpuBuffers(_instanceCount);
        InitArgsBuffer();
        if (_instanceBuffer != null && _instanceCount > 0)
            _instanceBuffer.SetData(_cpuInstances, 0, 0, _instanceCount);
    }

    static void SampleSplatRG(NativeArray<float> splatData, int res, float u, float v, out float path, out float rock)
    {
        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);
        float fx = u * (res - 1);
        float fz = v * (res - 1);
        int x0 = Mathf.Clamp((int)fx, 0, res - 2);
        int z0 = Mathf.Clamp((int)fz, 0, res - 2);
        float tx = fx - x0;
        float tz = fz - z0;

        int Stride = 4;
        int i00 = (z0 * res + x0) * Stride;
        int i10 = (z0 * res + x0 + 1) * Stride;
        int i01 = ((z0 + 1) * res + x0) * Stride;
        int i11 = ((z0 + 1) * res + x0 + 1) * Stride;

        float p00 = splatData[i00 + 0];
        float p10 = splatData[i10 + 0];
        float p01 = splatData[i01 + 0];
        float p11 = splatData[i11 + 0];
        path = Mathf.Lerp(Mathf.Lerp(p00, p10, tx), Mathf.Lerp(p01, p11, tx), tz);

        float r00 = splatData[i00 + 1];
        float r10 = splatData[i10 + 1];
        float r01 = splatData[i01 + 1];
        float r11 = splatData[i11 + 1];
        rock = Mathf.Lerp(Mathf.Lerp(r00, r10, tx), Mathf.Lerp(r01, r11, tx), tz);
    }

    void EnsureGpuBuffers(int count)
    {
        int cap = Mathf.Max(count, 1);
        if (cap > maxInstances)
            cap = maxInstances;

        if (_allocatedCapacity >= cap && _instanceBuffer != null && _visibleAppendBuffer != null && _argsBuffer != null)
            return;

        ReleaseBuffers();

        _allocatedCapacity = Mathf.Min(maxInstances, Mathf.NextPowerOfTwo(Mathf.Max(cap, 64)));

        _instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _allocatedCapacity, Marshal.SizeOf<GrassInstanceData>());
        _visibleAppendBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, _allocatedCapacity, sizeof(uint));

        _argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
    }

    void InitArgsBuffer()
    {
        if (grassMesh == null || _argsBuffer == null)
            return;

        uint ic = grassMesh.GetIndexCount(0);
        var args = new GraphicsBuffer.IndirectDrawIndexedArgs
        {
            indexCountPerInstance = ic,
            instanceCount = 0,
            startIndex = 0,
            baseVertexIndex = 0,
            startInstance = 0
        };
        _argsBuffer.SetData(new[] { args });
    }

    void ReleaseBuffers()
    {
        _instanceBuffer?.Release();
        _instanceBuffer = null;
        _visibleAppendBuffer?.Release();
        _visibleAppendBuffer = null;
        _argsBuffer?.Release();
        _argsBuffer = null;
        _allocatedCapacity = 0;
    }

    void OnBeginContextRendering(ScriptableRenderContext _, System.Collections.Generic.List<Camera> cameras)
    {
        if (!Application.isPlaying)
            return;

        if (_kernelCull < 0 || _instanceCount <= 0 || grassMesh == null || grassMaterial == null || cullingCompute == null)
            return;

        if (_instanceBuffer == null || _visibleAppendBuffer == null || _argsBuffer == null)
            return;

        var cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
            return;

        bool any = false;
        foreach (var c in cameras)
        {
            if (c == cam)
            {
                any = true;
                break;
            }
        }

        if (!any)
            return;

        GeometryUtility.CalculateFrustumPlanes(cam, _planes);

        _visibleAppendBuffer.SetCounterValue(0);

        cullingCompute.SetBuffer(_kernelCull, "_GrassInstances", _instanceBuffer);
        cullingCompute.SetBuffer(_kernelCull, "_VisibleIndices", _visibleAppendBuffer);
        cullingCompute.SetInt("_InstanceCount", _instanceCount);

        for (var i = 0; i < 6; i++)
        {
            var p = _planes[i];
            _frustumPlaneVectors[i] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
        }

        cullingCompute.SetVectorArray("_FrustumPlanes", _frustumPlaneVectors);

        int groups = Mathf.CeilToInt(_instanceCount / (float)ThreadGroupSize);
        cullingCompute.Dispatch(_kernelCull, Mathf.Max(1, groups), 1, 1);

        GraphicsBuffer.CopyCount(_visibleAppendBuffer, _argsBuffer, sizeof(uint));

        _mpb ??= new MaterialPropertyBlock();
        _mpb.SetBuffer(GrassInstancesId, _instanceBuffer);
        _mpb.SetBuffer(VisibleIndicesId, _visibleAppendBuffer);

        var rp = new RenderParams(grassMaterial)
        {
            worldBounds = _worldBounds,
            matProps = _mpb,
            camera = cam,
            layer = gameObject.layer
        };

        Graphics.RenderMeshIndirect(rp, grassMesh, _argsBuffer, 1, 0);
    }

    readonly Plane[] _planes = new Plane[6];
    readonly Vector4[] _frustumPlaneVectors = new Vector4[6];
}
