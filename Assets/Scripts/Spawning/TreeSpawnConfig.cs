using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[System.Serializable]
public struct TreeSpawnWeightedPrefab
{
    [SerializeField] GameObject prefab;
    [SerializeField, Min(0f)] float weight;

    public GameObject Prefab => prefab;
    public float Weight => weight;
}

[CreateAssetMenu(fileName = "TreeSpawnConfig", menuName = "Medieval/Spawning/Tree Spawn Config")]
public class TreeSpawnConfig : ScriptableObject
{
    [Tooltip("Several tree prefabs with relative spawn weights. Leave empty to use Tree Prefab only.")]
    [SerializeField] TreeSpawnWeightedPrefab[] weightedTreePrefabs;
    [Tooltip("Used when Weighted Tree Prefabs is empty, or as fallback when no variant applies.")]
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
    public TreeSpawnWeightedPrefab[] WeightedTreePrefabs => weightedTreePrefabs;

    public float TerrainHeightOffset => terrainHeightOffset;
    public float InstanceScaleMin => instanceScaleMin;
    public float InstanceScaleMax => instanceScaleMax;

    public bool HasSpawnableTreePrefab()
    {
        if (weightedTreePrefabs != null)
        {
            for (int i = 0; i < weightedTreePrefabs.Length; i++)
            {
                if (weightedTreePrefabs[i].Prefab != null)
                    return true;
            }
        }

        return treePrefab != null;
    }

    /// <summary>Variant count matching Burst weights and instancing indices (sequential non-null prefabs).</summary>
    public int GetBurstVariantCount()
    {
        if (weightedTreePrefabs != null && weightedTreePrefabs.Length > 0)
        {
            int c = 0;
            for (int i = 0; i < weightedTreePrefabs.Length; i++)
            {
                if (weightedTreePrefabs[i].Prefab != null)
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

        if (weightedTreePrefabs != null && weightedTreePrefabs.Length > 0)
        {
            int vi = 0;
            for (int i = 0; i < weightedTreePrefabs.Length; i++)
            {
                if (weightedTreePrefabs[i].Prefab == null)
                    continue;
                destination[vi++] = Mathf.Max(0f, weightedTreePrefabs[i].Weight);
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

    /// <summary>Picks a tree prefab using relative weights; if all weights are zero, picks uniformly among non-null prefabs; otherwise uses <see cref="TreePrefab"/>.</summary>
    public GameObject PickTreePrefab()
    {
        if (weightedTreePrefabs != null && weightedTreePrefabs.Length > 0)
        {
            float total = 0f;
            int nonNullCount = 0;
            for (int i = 0; i < weightedTreePrefabs.Length; i++)
            {
                var e = weightedTreePrefabs[i];
                if (e.Prefab == null)
                    continue;
                nonNullCount++;
                if (e.Weight > 0f)
                    total += e.Weight;
            }

            if (nonNullCount > 0 && total > 0f)
            {
                float r = Random.Range(0f, total);
                for (int i = 0; i < weightedTreePrefabs.Length; i++)
                {
                    var e = weightedTreePrefabs[i];
                    if (e.Prefab == null || e.Weight <= 0f)
                        continue;
                    r -= e.Weight;
                    if (r <= 0f)
                        return e.Prefab;
                }
            }
            else if (nonNullCount > 0)
            {
                int pick = Random.Range(0, nonNullCount);
                for (int i = 0; i < weightedTreePrefabs.Length; i++)
                {
                    if (weightedTreePrefabs[i].Prefab == null)
                        continue;
                    if (pick == 0)
                        return weightedTreePrefabs[i].Prefab;
                    pick--;
                }
            }
        }

        return treePrefab;
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

        if (weightedTreePrefabs != null && weightedTreePrefabs.Length > 0)
        {
            int vi = 0;
            for (int i = 0; i < weightedTreePrefabs.Length; i++)
            {
                GameObject p = weightedTreePrefabs[i].Prefab;
                if (p == null)
                    continue;
                FillInstancingFromPrefab(vi++, p);
            }

            if (vi != 0)
                return;
        }

        if (treePrefab != null)
            FillInstancingFromPrefab(0, treePrefab);
    }

    void FillInstancingFromPrefab(int index, GameObject prefab)
    {
        var mf = prefab.GetComponentInChildren<MeshFilter>(true);
        var mr = prefab.GetComponentInChildren<MeshRenderer>(true);
        _instancingMeshes[index] = mf != null ? mf.sharedMesh : null;
        _instancingMaterials[index] = mr != null ? mr.sharedMaterial : null;
        if (mr != null)
        {
            _instancingLayers[index] = mr.gameObject.layer;
            _instancingShadows[index] = mr.shadowCastingMode;
            _instancingReceiveShadows[index] = mr.receiveShadows;
            _instancingProbes[index] = mr.lightProbeUsage;
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
