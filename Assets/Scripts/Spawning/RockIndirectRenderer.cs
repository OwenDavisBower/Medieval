using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws mesh instances with <see cref="Graphics.RenderMeshInstanced"/> and per-instance
/// <see cref="Matrix4x4"/> from seeds (avoids indirect-draw instance indexing issues with custom buffers).
/// </summary>
[DisallowMultipleComponent]
public class RockIndirectRenderer : MonoBehaviour
{
    sealed class VariantBatch
    {
        public Mesh Mesh;
        public Material Material;
        public Matrix4x4[] Matrices;
        public int[] SeedIndices;
        public float WindYawRadiansPerSecond;
        public int Layer;
        public ShadowCastingMode ShadowCastingMode;
        public bool ReceiveShadows;
        public LightProbeUsage LightProbeUsage;
    }

    MeshSpawnConfig _config;
    RockInstanceSeed[] _seedSnapshot;
    Bounds _worldBounds;
    List<VariantBatch> _batches;
    bool _active;
    bool _perFrameMatrices;

    void OnDestroy() => Shutdown();

    void OnDisable() => Unhook();

    /// <summary>Build instance matrices from seeds and start drawing. Safe to call again (releases previous).</summary>
    public void Initialize(MeshSpawnConfig config, IReadOnlyList<RockInstanceSeed> seeds)
    {
        Shutdown();

        if (config == null || seeds == null || seeds.Count == 0 || !config.HasRenderableVariants)
            return;

        _config = config;
        _seedSnapshot = new RockInstanceSeed[seeds.Count];
        for (int i = 0; i < seeds.Count; i++)
            _seedSnapshot[i] = seeds[i];

        _batches = BuildBatches(config, seeds);
        if (_batches == null || _batches.Count == 0)
        {
            Shutdown();
            return;
        }

        _perFrameMatrices = AnyVariantWind(config, seeds);

        for (int b = 0; b < _batches.Count; b++)
            RebuildBatchMatrices(_batches[b], Time.time);

        _worldBounds = EncapsulateSeeds(config, seeds);

        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        _active = true;
    }

    static bool AnyVariantWind(MeshSpawnConfig config, IReadOnlyList<RockInstanceSeed> seeds)
    {
        for (int i = 0; i < seeds.Count; i++)
        {
            int v = (int)seeds[i].ScaleAndPad.y;
            MeshSpawnVariant variant = config.GetVariant(v);
            if (variant != null && Mathf.Abs(variant.windYawRadiansPerSecond) > 1e-6f)
                return true;
        }

        return false;
    }

    static List<VariantBatch> BuildBatches(MeshSpawnConfig config, IReadOnlyList<RockInstanceSeed> seeds)
    {
        var byVariant = new Dictionary<int, List<int>>();
        for (int i = 0; i < seeds.Count; i++)
        {
            int v = (int)seeds[i].ScaleAndPad.y;
            if (!byVariant.TryGetValue(v, out List<int> list))
            {
                list = new List<int>();
                byVariant[v] = list;
            }

            list.Add(i);
        }

        var batches = new List<VariantBatch>(byVariant.Count);
        foreach (KeyValuePair<int, List<int>> kv in byVariant)
        {
            MeshSpawnVariant variant = config.GetVariant(kv.Key);
            if (variant == null || variant.mesh == null || variant.material == null)
                continue;

            var batch = new VariantBatch
            {
                Mesh = variant.mesh,
                Material = variant.material,
                SeedIndices = kv.Value.ToArray(),
                Matrices = new Matrix4x4[kv.Value.Count],
                WindYawRadiansPerSecond = variant.windYawRadiansPerSecond,
                Layer = variant.layer,
                ShadowCastingMode = variant.shadowCastingMode,
                ReceiveShadows = variant.receiveShadows,
                LightProbeUsage = variant.lightProbeUsage
            };
            batches.Add(batch);
        }

        return batches.Count > 0 ? batches : null;
    }

    void RebuildMatrices(float timeSeconds)
    {
        if (_batches == null)
            return;
        for (int b = 0; b < _batches.Count; b++)
            RebuildBatchMatrices(_batches[b], timeSeconds);
    }

    void RebuildBatchMatrices(VariantBatch batch, float timeSeconds)
    {
        if (batch?.Matrices == null || batch.SeedIndices == null || _seedSnapshot == null)
            return;
        float wind = batch.WindYawRadiansPerSecond;
        for (int i = 0; i < batch.Matrices.Length; i++)
        {
            int si = batch.SeedIndices[i];
            batch.Matrices[i] = MatrixFromSeed(in _seedSnapshot[si], timeSeconds, wind);
        }
    }

    /// <summary>Matches <c>MeshInstance.compute</c> TRS: uniform scale, yaw about Y, then translation.</summary>
    public static Matrix4x4 MatrixFromSeed(in RockInstanceSeed seed, float timeSeconds, float windYawRadiansPerSecond)
    {
        var py = seed.PositionAndYaw;
        var pos = new Vector3(py.x, py.y, py.z);
        float yawRad = py.w + timeSeconds * windYawRadiansPerSecond;
        float scale = Mathf.Max(seed.ScaleAndPad.x, 1e-4f);
        var rot = Quaternion.Euler(0f, yawRad * Mathf.Rad2Deg, 0f);
        return Matrix4x4.TRS(pos, rot, new Vector3(scale, scale, scale));
    }

    static Bounds EncapsulateSeeds(MeshSpawnConfig config, IReadOnlyList<RockInstanceSeed> seeds)
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

        Vector3 maxExt = Vector3.zero;
        for (int i = 0; i < seeds.Count; i++)
        {
            int v = (int)seeds[i].ScaleAndPad.y;
            MeshSpawnVariant variant = config.GetVariant(v);
            if (variant == null || variant.mesh == null)
                continue;
            float scale = seeds[i].ScaleAndPad.x;
            Vector3 ext = variant.mesh.bounds.extents * scale * 1.25f;
            maxExt = Vector3.Max(maxExt, ext);
        }

        if (maxExt.sqrMagnitude <= 0f)
            maxExt = Vector3.one;

        Vector3 center = (min + max) * 0.5f;
        Vector3 size = Vector3.Max((max - min) + maxExt * 2f, maxExt * 2f);
        return new Bounds(center, size);
    }

    void OnBeginContextRendering(ScriptableRenderContext _, List<Camera> cameras)
    {
        if (!_active || _batches == null || _batches.Count == 0)
            return;

        if (_perFrameMatrices)
            RebuildMatrices(Time.time);

        for (int c = 0; c < cameras.Count; c++)
        {
            Camera cam = cameras[c];
            if (cam == null || !cam.isActiveAndEnabled)
                continue;

            for (int b = 0; b < _batches.Count; b++)
            {
                VariantBatch batch = _batches[b];
                if (batch.Mesh == null || batch.Material == null || batch.Matrices == null)
                    continue;

                var rp = new RenderParams(batch.Material)
                {
                    camera = cam,
                    layer = batch.Layer,
                    worldBounds = _worldBounds,
                    shadowCastingMode = batch.ShadowCastingMode,
                    receiveShadows = batch.ReceiveShadows,
                    lightProbeUsage = batch.LightProbeUsage
                };

                Graphics.RenderMeshInstanced(in rp, batch.Mesh, 0, batch.Matrices, batch.Matrices.Length);
            }
        }
    }

    void Unhook() => RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;

    void Shutdown()
    {
        Unhook();
        _active = false;
        _config = null;
        _batches = null;
        _seedSnapshot = null;
    }
}
