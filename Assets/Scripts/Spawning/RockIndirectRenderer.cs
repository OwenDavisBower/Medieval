using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws rock instances with <see cref="Graphics.RenderMeshInstanced"/> and per-instance
/// <see cref="Matrix4x4"/> from seeds (avoids indirect-draw instance indexing issues with custom buffers).
/// </summary>
[DisallowMultipleComponent]
public class RockIndirectRenderer : MonoBehaviour
{
    RockSpawnConfig _config;
    Matrix4x4[] _instanceMatrices;
    RockInstanceSeed[] _seedSnapshot;
    Bounds _worldBounds;
    int _instanceCount;
    Mesh _mesh;
    Material _material;
    bool _active;
    bool _perFrameMatrices;

    void OnDestroy() => Shutdown();

    void OnDisable() => Unhook();

    /// <summary>Build instance matrices from seeds and start drawing. Safe to call again (releases previous).</summary>
    public void Initialize(RockSpawnConfig config, IReadOnlyList<RockInstanceSeed> seeds)
    {
        Shutdown();

        if (config == null || seeds == null || seeds.Count == 0)
            return;
        if (config.RockMesh == null || config.RockMaterial == null)
            return;

        _mesh = config.RockMesh;
        _material = config.RockMaterial;
        _config = config;
        _instanceCount = seeds.Count;
        _seedSnapshot = new RockInstanceSeed[seeds.Count];
        for (int i = 0; i < seeds.Count; i++)
            _seedSnapshot[i] = seeds[i];

        _instanceMatrices = new Matrix4x4[_instanceCount];
        _perFrameMatrices = Mathf.Abs(config.WindRockYawRadiansPerSecond) > 1e-6f;
        RebuildMatrices(Time.time);

        float maxScale = config.MaxScale;
        _worldBounds = EncapsulateSeeds(seeds, _mesh, maxScale);

        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        _active = true;
    }

    void RebuildMatrices(float timeSeconds)
    {
        if (_seedSnapshot == null || _instanceMatrices == null)
            return;
        float wind = _config != null ? _config.WindRockYawRadiansPerSecond : 0f;
        for (int i = 0; i < _instanceCount; i++)
            _instanceMatrices[i] = MatrixFromSeed(in _seedSnapshot[i], timeSeconds, wind);
    }

    /// <summary>Matches <c>RocksInstance.compute</c> TRS: uniform scale, yaw about Y, then translation.</summary>
    public static Matrix4x4 MatrixFromSeed(in RockInstanceSeed seed, float timeSeconds, float windYawRadiansPerSecond)
    {
        var py = seed.PositionAndYaw;
        var pos = new Vector3(py.x, py.y, py.z);
        float yawRad = py.w + timeSeconds * windYawRadiansPerSecond;
        float scale = Mathf.Max(seed.ScaleAndPad.x, 1e-4f);
        var rot = Quaternion.Euler(0f, yawRad * Mathf.Rad2Deg, 0f);
        return Matrix4x4.TRS(pos, rot, new Vector3(scale, scale, scale));
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

    void OnBeginContextRendering(ScriptableRenderContext _, List<Camera> cameras)
    {
        if (!_active || _mesh == null || _material == null || _instanceMatrices == null)
            return;

        if (_perFrameMatrices)
            RebuildMatrices(Time.time);

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
                lightProbeUsage = _config != null ? _config.LightProbeUsage : LightProbeUsage.Off
            };

            Graphics.RenderMeshInstanced(in rp, _mesh, 0, _instanceMatrices, _instanceCount);
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
        _instanceCount = 0;
        _instanceMatrices = null;
        _seedSnapshot = null;
    }
}
