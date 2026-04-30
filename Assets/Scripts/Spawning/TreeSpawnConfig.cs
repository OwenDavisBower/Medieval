using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[System.Serializable]
public struct TreeSpawnWeightedVariant
{
    [Tooltip("Mesh drawn for this tree variant (GPU instancing / indirect draw).")]
    [SerializeField] Mesh mesh;
    [Tooltip("Material paired with Mesh (same slot as MeshRenderer.sharedMaterial).")]
    [SerializeField] Material material;
    [SerializeField, Min(0f)] float weight;
    [Tooltip("Layer used for rendering (matches MeshRenderer.gameObject.layer).")]
    [SerializeField] int layer;
    [SerializeField] ShadowCastingMode shadowCastingMode;
    [SerializeField] bool receiveShadows;
    [SerializeField] LightProbeUsage lightProbeUsage;
    [Tooltip("World-space capsule height for TreeColliderPool (matches CapsuleCollider.height on tree prefab).")]
    [SerializeField, Min(0.1f)] float capsuleHeight;
    [Tooltip("World-space capsule radius for TreeColliderPool (matches CapsuleCollider.radius).")]
    [SerializeField, Min(0.05f)] float capsuleRadius;

    [SerializeField, HideInInspector]
    [FormerlySerializedAs("prefab")]
    GameObject authoringPrefab;

    public Mesh Mesh => mesh;
    public Material Material => material;
    public float Weight => weight;
    public int Layer => layer;
    public ShadowCastingMode ShadowCastingMode => shadowCastingMode;
    public bool ReceiveShadows => receiveShadows;
    public LightProbeUsage LightProbeUsage => lightProbeUsage;
    public float CapsuleHeight => capsuleHeight;
    public float CapsuleRadius => capsuleRadius;

    public bool IsSpawnable => mesh != null && material != null;

    public bool ShouldMigrateAuthoringPrefab => authoringPrefab != null && (mesh == null || material == null);

    /// <summary>Fills mesh/material/render/capsule from <see cref="authoringPrefab"/> when those are unset; clears authoring prefab after success.</summary>
    public TreeSpawnWeightedVariant WithMigratedFromAuthoringPrefabIfNeeded()
    {
        if (authoringPrefab == null || (mesh != null && material != null))
            return this;

        var next = this;
        FillFromGameObjectInto(ref next, authoringPrefab);
        next.authoringPrefab = null;
        return next;
    }

    static void FillFromGameObjectInto(ref TreeSpawnWeightedVariant v, GameObject prefab)
    {
        Mesh meshOut = null;
        Material materialOut = null;
        MeshRenderer mrSettings = null;
        var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            var mr = renderers[r];
            if (mr == null || !mr.enabled)
                continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                continue;
            meshOut = mf.sharedMesh;
            materialOut = mr.sharedMaterial;
            mrSettings = mr;
            break;
        }

        if (meshOut == null)
        {
            var mf = prefab.GetComponentInChildren<MeshFilter>(true);
            if (mf != null)
            {
                meshOut = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    materialOut = mr.sharedMaterial;
                    mrSettings = mr;
                }
            }
        }

        if (mrSettings == null)
            mrSettings = prefab.GetComponentInChildren<MeshRenderer>(true);

        v.mesh = meshOut;
        v.material = materialOut;
        if (mrSettings != null)
        {
            v.layer = mrSettings.gameObject.layer;
            v.shadowCastingMode = mrSettings.shadowCastingMode;
            v.receiveShadows = mrSettings.receiveShadows;
            v.lightProbeUsage = mrSettings.lightProbeUsage;
        }
        else
        {
            v.layer = prefab.layer;
            v.shadowCastingMode = ShadowCastingMode.On;
            v.receiveShadows = true;
            v.lightProbeUsage = LightProbeUsage.Off;
        }

        var cap = prefab.GetComponentInChildren<CapsuleCollider>(true);
        if (cap != null)
        {
            v.capsuleHeight = Mathf.Max(0.1f, cap.height);
            v.capsuleRadius = Mathf.Max(0.05f, cap.radius);
        }
        else
        {
            v.capsuleHeight = 8f;
            v.capsuleRadius = 0.6f;
        }
    }
}

[CreateAssetMenu(fileName = "TreeSpawnConfig", menuName = "Medieval/Spawning/Tree Spawn Config")]
public class TreeSpawnConfig : ScriptableObject
{
    [Tooltip("Weighted tree variants (mesh + material + collider sizing). Leave empty to use Tree Prefab only.")]
    [SerializeField]
    [FormerlySerializedAs("weightedTreePrefabs")]
    TreeSpawnWeightedVariant[] weightedTreeVariants;
    [Tooltip("Used when weighted variants are empty, or as fallback when no variant applies. Mesh/material are read from this prefab.")]
    [SerializeField] GameObject treePrefab;
    [Tooltip("Target trees per logical terrain chunk (TerrainGenerator.chunkCount grid), not world total.")]
    [SerializeField] int treeCount = 200;
    [Tooltip("Inset from procedural terrain edges when sampling tree positions (XZ).")]
    [SerializeField] float terrainEdgeMargin = 8f;
    [SerializeField] float minSeparation = 6f;
    [SerializeField] int maxAttemptsPerTree = 80;
    [Tooltip("-1 = use terrain flat corridor (TerrainGenerator.flatRadius + 2 world units). Otherwise minimum distance from the path spline centerline.")]
    [SerializeField] float pathClearance = -1f;
    [Tooltip("Radius burned into the procedural placement mask after each tree spawns (XZ).")]
    [SerializeField] float occupationFootprintRadius = 2.5f;
    [Tooltip("Passed to height sampling (meters above terrain surface), same role as TerrainSpawnUtility.")]
    [SerializeField] float terrainHeightOffset = 0.05f;
    [SerializeField] float instanceScaleMin = 0.9f;
    [SerializeField] float instanceScaleMax = 1.1f;

    Mesh[] _instancingMeshes;
    Material[] _instancingMaterials;
    int[] _instancingLayers;
    ShadowCastingMode[] _instancingShadows;
    bool[] _instancingReceiveShadows;
    LightProbeUsage[] _instancingProbes;
    float[] _instancingCapsuleHeight;
    float[] _instancingCapsuleRadius;

    public GameObject TreePrefab => treePrefab;

    /// <summary>Weighted entries; when empty or invalid, <see cref="TreePrefab"/> is used for every tree.</summary>
    public TreeSpawnWeightedVariant[] WeightedTreeVariants => weightedTreeVariants;

    public float TerrainHeightOffset => terrainHeightOffset;
    public float InstanceScaleMin => instanceScaleMin;
    public float InstanceScaleMax => instanceScaleMax;

    /// <summary>
    /// Applied in mesh local space after instance position/yaw/scale (e.g. -90° X for models authored in Z-up).
    /// Used by <see cref="TreeIndirectRenderer"/> and <see cref="TreeColliderPool"/>.
    /// </summary>
    public Quaternion InstanceMeshRotationOffset => Quaternion.Euler(-90f, 0f, 0f);

    public bool HasSpawnableTreePrefab()
    {
        if (weightedTreeVariants != null)
        {
            for (int i = 0; i < weightedTreeVariants.Length; i++)
            {
                if (weightedTreeVariants[i].IsSpawnable)
                    return true;
            }
        }

        return treePrefab != null;
    }

    /// <summary>Variant count matching Burst weights and instancing indices (sequential spawnable variants).</summary>
    public int GetBurstVariantCount()
    {
        if (weightedTreeVariants != null && weightedTreeVariants.Length > 0)
        {
            int c = 0;
            for (int i = 0; i < weightedTreeVariants.Length; i++)
            {
                if (weightedTreeVariants[i].IsSpawnable)
                    c++;
            }

            if (c > 0)
                return c;
        }

        return treePrefab != null ? 1 : 0;
    }

    /// <summary>Fills weights for <see cref="GetBurstVariantCount"/> variants (same ordering as instancing cache).</summary>
    public void CopyBurstVariantWeights(NativeArray<float> destination)
    {
        int n = GetBurstVariantCount();
        if (!destination.IsCreated || destination.Length != n)
            throw new System.ArgumentException("Destination length must match GetBurstVariantCount().", nameof(destination));

        if (weightedTreeVariants != null && weightedTreeVariants.Length > 0)
        {
            int vi = 0;
            for (int i = 0; i < weightedTreeVariants.Length; i++)
            {
                if (!weightedTreeVariants[i].IsSpawnable)
                    continue;
                destination[vi++] = Mathf.Max(0f, weightedTreeVariants[i].Weight);
            }

            if (vi > 0)
                return;
        }

        if (treePrefab != null)
            destination[0] = 1f;
    }

    public bool TryGetVariantInstancing(
        int variantId,
        out Mesh mesh,
        out Material material,
        out int layer,
        out ShadowCastingMode shadowCastingMode,
        out bool receiveShadows,
        out LightProbeUsage lightProbeUsage,
        out float capsuleHeight,
        out float capsuleRadius)
    {
        EnsureInstancingCache();
        mesh = null;
        material = null;
        layer = 0;
        shadowCastingMode = ShadowCastingMode.On;
        receiveShadows = true;
        lightProbeUsage = LightProbeUsage.Off;
        capsuleHeight = 8f;
        capsuleRadius = 0.6f;

        if (_instancingMeshes == null || (uint)variantId >= (uint)_instancingMeshes.Length)
            return false;

        mesh = _instancingMeshes[variantId];
        material = _instancingMaterials != null ? _instancingMaterials[variantId] : null;
        layer = _instancingLayers != null ? _instancingLayers[variantId] : 0;
        shadowCastingMode = _instancingShadows != null ? _instancingShadows[variantId] : ShadowCastingMode.On;
        receiveShadows = _instancingReceiveShadows != null && _instancingReceiveShadows[variantId];
        lightProbeUsage = _instancingProbes != null ? _instancingProbes[variantId] : LightProbeUsage.Off;
        capsuleHeight = _instancingCapsuleHeight != null ? _instancingCapsuleHeight[variantId] : 8f;
        capsuleRadius = _instancingCapsuleRadius != null ? _instancingCapsuleRadius[variantId] : 0.6f;
        return mesh != null && material != null;
    }

    public int TreeCount => treeCount;
    public float TerrainEdgeMargin => terrainEdgeMargin;
    public float MinSeparation => minSeparation;
    public int MaxAttemptsPerTree => maxAttemptsPerTree;
    public float PathClearance => pathClearance;
    public float OccupationFootprintRadius => occupationFootprintRadius;

    void OnValidate()
    {
        terrainEdgeMargin = Mathf.Max(0f, terrainEdgeMargin);
        instanceScaleMin = Mathf.Max(0.01f, instanceScaleMin);
        instanceScaleMax = Mathf.Max(instanceScaleMin, instanceScaleMax);

#if UNITY_EDITOR
        if (weightedTreeVariants != null)
        {
            bool dirty = false;
            for (int i = 0; i < weightedTreeVariants.Length; i++)
            {
                if (!weightedTreeVariants[i].ShouldMigrateAuthoringPrefab)
                    continue;
                weightedTreeVariants[i] = weightedTreeVariants[i].WithMigratedFromAuthoringPrefabIfNeeded();
                dirty = true;
            }

            if (dirty)
                UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        RebuildVariantInstancingCache();
    }

    void OnEnable() => RebuildVariantInstancingCache();

    void EnsureInstancingCache()
    {
        int n = GetBurstVariantCount();
        if (_instancingMeshes == null || _instancingMeshes.Length != n)
            RebuildVariantInstancingCache();
    }

    void RebuildVariantInstancingCache()
    {
        int n = GetBurstVariantCount();
        if (n == 0)
        {
            _instancingMeshes = null;
            _instancingMaterials = null;
            _instancingLayers = null;
            _instancingShadows = null;
            _instancingReceiveShadows = null;
            _instancingProbes = null;
            _instancingCapsuleHeight = null;
            _instancingCapsuleRadius = null;
            return;
        }

        _instancingMeshes = new Mesh[n];
        _instancingMaterials = new Material[n];
        _instancingLayers = new int[n];
        _instancingShadows = new ShadowCastingMode[n];
        _instancingReceiveShadows = new bool[n];
        _instancingProbes = new LightProbeUsage[n];
        _instancingCapsuleHeight = new float[n];
        _instancingCapsuleRadius = new float[n];

        if (weightedTreeVariants != null && weightedTreeVariants.Length > 0)
        {
            int vi = 0;
            for (int i = 0; i < weightedTreeVariants.Length; i++)
            {
                var e = weightedTreeVariants[i];
                if (!e.IsSpawnable)
                    continue;
                FillInstancingFromVariant(vi++, e);
            }

            if (vi != 0)
                return;
        }

        if (treePrefab != null)
            FillInstancingFromPrefab(0, treePrefab);
    }

    void FillInstancingFromVariant(int index, TreeSpawnWeightedVariant e)
    {
        _instancingMeshes[index] = e.Mesh;
        _instancingMaterials[index] = e.Material;
        _instancingLayers[index] = e.Layer;
        _instancingShadows[index] = e.ShadowCastingMode;
        _instancingReceiveShadows[index] = e.ReceiveShadows;
        _instancingProbes[index] = e.LightProbeUsage;
        // Match former struct field defaults (C# 9 has no struct field initializers).
        _instancingCapsuleHeight[index] = e.CapsuleHeight > 0f ? e.CapsuleHeight : 8f;
        _instancingCapsuleRadius[index] = e.CapsuleRadius > 0f ? e.CapsuleRadius : 0.6f;
    }

    void FillInstancingFromPrefab(int index, GameObject prefab)
    {
        Mesh mesh = null;
        Material material = null;
        MeshRenderer mrSettings = null;
        var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            var mr = renderers[r];
            if (mr == null || !mr.enabled)
                continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                continue;
            mesh = mf.sharedMesh;
            material = mr.sharedMaterial;
            mrSettings = mr;
            break;
        }

        if (mesh == null)
        {
            var mf = prefab.GetComponentInChildren<MeshFilter>(true);
            if (mf != null)
            {
                mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    material = mr.sharedMaterial;
                    mrSettings = mr;
                }
            }
        }

        if (mrSettings == null)
            mrSettings = prefab.GetComponentInChildren<MeshRenderer>(true);

        _instancingMeshes[index] = mesh;
        _instancingMaterials[index] = material;
        if (mrSettings != null)
        {
            _instancingLayers[index] = mrSettings.gameObject.layer;
            _instancingShadows[index] = mrSettings.shadowCastingMode;
            _instancingReceiveShadows[index] = mrSettings.receiveShadows;
            _instancingProbes[index] = mrSettings.lightProbeUsage;
        }
        else
        {
            _instancingLayers[index] = prefab.layer;
            _instancingShadows[index] = ShadowCastingMode.On;
            _instancingReceiveShadows[index] = true;
            _instancingProbes[index] = LightProbeUsage.Off;
        }

        var cap = prefab.GetComponentInChildren<CapsuleCollider>(true);
        if (cap != null)
        {
            _instancingCapsuleHeight[index] = Mathf.Max(0.1f, cap.height);
            _instancingCapsuleRadius[index] = Mathf.Max(0.05f, cap.radius);
        }
        else
        {
            _instancingCapsuleHeight[index] = 8f;
            _instancingCapsuleRadius[index] = 0.6f;
        }
    }
}
