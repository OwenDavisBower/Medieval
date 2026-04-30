using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws tree instances with <see cref="Graphics.RenderMeshInstanced"/>; batches by <see cref="TreeInstanceData.VariantId"/>.
/// </summary>
[DisallowMultipleComponent]
public class TreeIndirectRenderer : MonoBehaviour
{
    sealed class VariantBatch
    {
        public Mesh Mesh;
        public Material Material;
        public Matrix4x4[] Matrices;
        public int[] InstanceIndices;
        public int Layer;
        public ShadowCastingMode ShadowCastingMode;
        public bool ReceiveShadows;
        public LightProbeUsage LightProbeUsage;
    }

    TreeSpawnConfig _config;
    Matrix4x4 _instanceMeshRotationMatrix = Matrix4x4.identity;
    TreeInstanceData[] _snapshot;
    Bounds _worldBounds;
    List<VariantBatch> _batches;
    List<Material> _instancingMaterialCopies;
    bool _active;

    void OnDestroy() => Shutdown();

    void OnDisable() => Unhook();

    public void Initialize(TreeSpawnConfig config, IReadOnlyList<TreeInstanceData> instances)
    {
        Shutdown();

        if (config == null || instances == null || instances.Count == 0 || !config.HasSpawnableTreePrefab())
            return;

        _config = config;
        _instanceMeshRotationMatrix = Matrix4x4.Rotate(config.InstanceMeshRotationOffset);
        _snapshot = new TreeInstanceData[instances.Count];
        for (int i = 0; i < instances.Count; i++)
            _snapshot[i] = instances[i];

        _batches = BuildBatches(config, instances);
        if (_batches == null || _batches.Count == 0)
        {
            Shutdown();
            return;
        }

        EnsureMaterialsAllowInstancing(_batches);

        for (int b = 0; b < _batches.Count; b++)
            RebuildBatchMatrices(_batches[b]);

        _worldBounds = EncapsulateInstances(config, instances);

        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        _active = true;
    }

    static List<VariantBatch> BuildBatches(TreeSpawnConfig config, IReadOnlyList<TreeInstanceData> instances)
    {
        var byVariant = new Dictionary<int, List<int>>();
        for (int i = 0; i < instances.Count; i++)
        {
            int v = instances[i].VariantId;
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
            if (!config.TryGetVariantInstancing(
                    kv.Key,
                    out Mesh mesh,
                    out Material material,
                    out int layer,
                    out ShadowCastingMode shadow,
                    out bool receiveShadows,
                    out LightProbeUsage probes,
                    out _,
                    out _))
                continue;

            if (mesh == null || material == null)
                continue;

            var batch = new VariantBatch
            {
                Mesh = mesh,
                Material = material,
                InstanceIndices = kv.Value.ToArray(),
                Matrices = new Matrix4x4[kv.Value.Count],
                Layer = layer,
                ShadowCastingMode = shadow,
                ReceiveShadows = receiveShadows,
                LightProbeUsage = probes
            };
            batches.Add(batch);
        }

        return batches.Count > 0 ? batches : null;
    }

    /// <summary>
    /// <see cref="Graphics.RenderMeshInstanced"/> requires <see cref="Material.enableInstancing"/>.
    /// Clone prefab materials when needed so we never mutate <see cref="Renderer.sharedMaterial"/> assets.
    /// </summary>
    static void EnsureMaterialsAllowInstancing(List<VariantBatch> batches, List<Material> ownedCopies)
    {
        ownedCopies.Clear();
        for (int b = 0; b < batches.Count; b++)
        {
            Material m = batches[b].Material;
            if (m == null || m.enableInstancing)
                continue;

            var copy = new Material(m)
            {
                name = m.name + " (Instancing)",
                enableInstancing = true
            };
            ownedCopies.Add(copy);
            batches[b].Material = copy;
        }
    }

    void EnsureMaterialsAllowInstancing(List<VariantBatch> batches)
    {
        _instancingMaterialCopies ??= new List<Material>();
        EnsureMaterialsAllowInstancing(batches, _instancingMaterialCopies);
    }

    void RebuildBatchMatrices(VariantBatch batch)
    {
        if (batch?.Matrices == null || batch.InstanceIndices == null || _snapshot == null)
            return;

        for (int i = 0; i < batch.Matrices.Length; i++)
        {
            int si = batch.InstanceIndices[i];
            batch.Matrices[i] = MatrixFromInstance(in _snapshot[si]) * _instanceMeshRotationMatrix;
        }
    }

    public static Matrix4x4 MatrixFromInstance(in TreeInstanceData d)
    {
        var s = math.max(d.Scale, 1e-4f);
        var scale = new Vector3(s, s, s);
        return Matrix4x4.TRS((Vector3)d.Position, (Quaternion)d.Rotation, scale);
    }

    static Bounds EncapsulateInstances(TreeSpawnConfig config, IReadOnlyList<TreeInstanceData> instances)
    {
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < instances.Count; i++)
        {
            float3 p = instances[i].Position;
            var wp = new Vector3(p.x, p.y, p.z);
            min = Vector3.Min(min, wp);
            max = Vector3.Max(max, wp);
        }

        Vector3 maxExt = Vector3.zero;
        for (int i = 0; i < instances.Count; i++)
        {
            if (!config.TryGetVariantInstancing(
                    instances[i].VariantId,
                    out Mesh mesh,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _))
                continue;

            if (mesh == null)
                continue;

            float scale = math.max(instances[i].Scale, 1e-4f);
            Vector3 ext = mesh.bounds.extents * scale * 1.25f;
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
        _instanceMeshRotationMatrix = Matrix4x4.identity;
        _batches = null;
        _snapshot = null;
        if (_instancingMaterialCopies != null)
        {
            for (int i = 0; i < _instancingMaterialCopies.Count; i++)
            {
                if (_instancingMaterialCopies[i] != null)
                    Destroy(_instancingMaterialCopies[i]);
            }

            _instancingMaterialCopies = null;
        }
    }
}
