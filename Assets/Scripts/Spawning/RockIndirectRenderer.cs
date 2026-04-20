using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// GPU rock instances: seeds in a structured buffer, matrices from compute, draw via <see cref="Graphics.RenderMeshIndirect"/>.
/// </summary>
[DisallowMultipleComponent]
public class RockIndirectRenderer : MonoBehaviour
{
    static readonly int RockObjectToWorldId = Shader.PropertyToID("_RockObjectToWorld");
    static readonly int SeedsId = Shader.PropertyToID("_Seeds");
    static readonly int ObjectToWorldId = Shader.PropertyToID("_ObjectToWorld");
    static readonly int CountId = Shader.PropertyToID("_Count");
    static readonly int TimeSecondsId = Shader.PropertyToID("_TimeSeconds");
    static readonly int WindYawRadiansPerSecondId = Shader.PropertyToID("_WindYawRadiansPerSecond");

    const int ThreadGroupSize = 64;
    const string KernelBuildMatrices = "BuildMatrices";

    RockSpawnConfig _config;
    GraphicsBuffer _seedsBuffer;
    GraphicsBuffer _matricesBuffer;
    GraphicsBuffer _argsBuffer;
    MaterialPropertyBlock _mpb;
    Bounds _worldBounds;
    int _instanceCount;
    Mesh _mesh;
    Material _material;
    ComputeShader _compute;
    int _kernelBuild;
    bool _active;
    bool _perFrameCompute;

    void OnDestroy() => Shutdown();

    void OnDisable() => Unhook();

    /// <summary>Build GPU buffers, dispatch compute, and start drawing. Safe to call again (releases previous).</summary>
    public void Initialize(RockSpawnConfig config, IReadOnlyList<RockInstanceSeed> seeds)
    {
        Shutdown();

        if (config == null || seeds == null || seeds.Count == 0)
            return;
        if (config.RockMesh == null || config.RockMaterial == null || config.RocksInstanceCompute == null)
            return;

        _mesh = config.RockMesh;
        _material = config.RockMaterial;
        _compute = config.RocksInstanceCompute;
        _config = config;
        _instanceCount = seeds.Count;

        _kernelBuild = _compute.FindKernel(KernelBuildMatrices);
        if (_kernelBuild < 0)
        {
            Debug.LogError("RocksInstance.compute must define kernel BuildMatrices.", this);
            _mesh = null;
            _material = null;
            _compute = null;
            _config = null;
            _instanceCount = 0;
            return;
        }

        _perFrameCompute = Mathf.Abs(config.WindRockYawRadiansPerSecond) > 1e-6f;

        _seedsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _instanceCount, RockInstanceSeed.Stride);
        _matricesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _instanceCount, sizeof(float) * 16);
        _argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);

        var seedArray = new RockInstanceSeed[seeds.Count];
        for (int i = 0; i < seeds.Count; i++)
            seedArray[i] = seeds[i];
        _seedsBuffer.SetData(seedArray);

        var args = new GraphicsBuffer.IndirectDrawIndexedArgs
        {
            indexCountPerInstance = _mesh.GetIndexCount(0),
            instanceCount = (uint)_instanceCount,
            startIndex = _mesh.GetIndexStart(0),
            baseVertexIndex = (uint)_mesh.GetBaseVertex(0),
            startInstance = 0
        };
        _argsBuffer.SetData(new[] { args });

        _compute.SetBuffer(_kernelBuild, SeedsId, _seedsBuffer);
        _compute.SetBuffer(_kernelBuild, ObjectToWorldId, _matricesBuffer);
        _compute.SetInt(CountId, _instanceCount);
        _compute.SetFloat(WindYawRadiansPerSecondId, config.WindRockYawRadiansPerSecond);

        _mpb = new MaterialPropertyBlock();
        _mpb.SetBuffer(RockObjectToWorldId, _matricesBuffer);

        float maxScale = config.MaxScale;
        _worldBounds = EncapsulateSeeds(seeds, _mesh, maxScale);

        DispatchBuildMatrices();
        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        _active = true;
    }

    static Bounds EncapsulateSeeds(IReadOnlyList<RockInstanceSeed> seeds, Mesh mesh, float maxScale)
    {
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < seeds.Count; i++)
        {
            Vector4 p = seeds[i].PositionAndYaw;
            var wp = new Vector3(p.x, p.y, p.z);
            min = Vector3.Min(min, wp);
            max = Vector3.Max(max, wp);
        }

        Vector3 ext = mesh.bounds.extents * maxScale * 1.25f;
        Vector3 center = (min + max) * 0.5f;
        Vector3 size = Vector3.Max((max - min) + ext * 2f, ext * 2f);
        return new Bounds(center, size);
    }

    void DispatchBuildMatrices()
    {
        if (_compute == null || _instanceCount == 0)
            return;

        _compute.SetFloat(TimeSecondsId, Time.time);
        int groups = Mathf.CeilToInt(_instanceCount / (float)ThreadGroupSize);
        _compute.Dispatch(_kernelBuild, groups, 1, 1);
    }

    void OnBeginContextRendering(ScriptableRenderContext _, List<Camera> cameras)
    {
        if (!_active || _mesh == null || _material == null || _argsBuffer == null)
            return;

        if (_perFrameCompute)
            DispatchBuildMatrices();

        for (int i = 0; i < cameras.Count; i++)
        {
            Camera cam = cameras[i];
            if (cam == null || !cam.isActiveAndEnabled)
                continue;

            var rp = new RenderParams(_material)
            {
                camera = cam,
                layer = _config != null ? _config.Layer : 0,
                worldBounds = _worldBounds,
                shadowCastingMode = _config != null ? _config.ShadowCastingMode : ShadowCastingMode.On,
                receiveShadows = _config == null || _config.ReceiveShadows,
                lightProbeUsage = _config != null ? _config.LightProbeUsage : LightProbeUsage.Off,
                matProps = _mpb
            };

            Graphics.RenderMeshIndirect(rp, _mesh, _argsBuffer);
        }
    }

    void Unhook() => RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;

    void Shutdown()
    {
        Unhook();
        _active = false;
        _config = null;
        _mesh = null;
        _material = null;
        _compute = null;
        _instanceCount = 0;
        _mpb = null;

        if (_seedsBuffer != null)
        {
            _seedsBuffer.Release();
            _seedsBuffer = null;
        }

        if (_matricesBuffer != null)
        {
            _matricesBuffer.Release();
            _matricesBuffer = null;
        }

        if (_argsBuffer != null)
        {
            _argsBuffer.Release();
            _argsBuffer = null;
        }
    }
}
