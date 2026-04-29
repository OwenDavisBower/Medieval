using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public class MeshSpawnVariant
{
    public Mesh mesh;
    [Tooltip("Must use a shader compatible with GPU instancing and RenderMeshInstanced.")]
    public Material material;
    [Tooltip("Instances to spawn per logical terrain chunk (TerrainGenerator.chunkCount grid) for this mesh/material.")]
    public int instanceCount = 64;
    public float minScale = 0.8f;
    public float maxScale = 1.4f;
    [Tooltip("Radians per second added to yaw on GPU (0 = static after first build).")]
    public float windYawRadiansPerSecond;
    public int layer;
    public ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;
    public bool receiveShadows = true;
    public LightProbeUsage lightProbeUsage = LightProbeUsage.Off;

    internal void Validate()
    {
        instanceCount = Mathf.Max(0, instanceCount);
        minScale = Mathf.Max(0.01f, minScale);
        maxScale = Mathf.Max(minScale, maxScale);
    }
}

[CreateAssetMenu(fileName = "MeshSpawnConfig", menuName = "Medieval/Spawning/Mesh Spawn Config")]
public class MeshSpawnConfig : ScriptableObject
{
    const string DefaultComputePath = "Assets/Shaders/MeshInstance.compute";

    [SerializeField] MeshSpawnVariant[] meshVariants;

    [SerializeField, HideInInspector] Mesh rockMesh;
    [SerializeField, HideInInspector] Material rockMaterial;
    [SerializeField, HideInInspector] float minScale;
    [SerializeField, HideInInspector] float maxScale;
    [SerializeField, HideInInspector] float windRockYawRadiansPerSecond;
    [SerializeField, HideInInspector] int layer;
    [SerializeField, HideInInspector] ShadowCastingMode shadowCastingMode;
    [SerializeField, HideInInspector] bool receiveShadows;
    [SerializeField, HideInInspector] LightProbeUsage lightProbeUsage;

    [SerializeField, HideInInspector, FormerlySerializedAs("instanceCount")]
    int _legacyInstanceCount = -1;
    [SerializeField, HideInInspector, FormerlySerializedAs("rockCount")]
    int _legacyRockCount = -1;
    [Tooltip("Passed to terrain snap (meters above surface).")]
    [SerializeField] float terrainHeightOffset = 0.05f;
    [Tooltip("Clamp XZ to terrain footprint before sampling height; 0 disables clamp.")]
    [SerializeField] float terrainEdgeMargin = 8f;
    [Tooltip("Minimum distance from path centerline (meters). Values >= 0 use that distance. Any negative value (including -1) uses the auto default (4 m), aligned with MainScene tree spawn — not terrain flatRadius, which is much wider.")]
    [SerializeField] float pathClearance = -1f;
    [SerializeField, FormerlySerializedAs("maxAttemptsPerRock")]
    int maxAttemptsPerInstance = 60;
    [SerializeField, FormerlySerializedAs("rocksInstanceCompute")]
    ComputeShader meshInstanceCompute;

    public MeshSpawnVariant[] MeshVariants => meshVariants;
    public ComputeShader MeshInstanceCompute => meshInstanceCompute;
    public float TerrainHeightOffset => terrainHeightOffset;
    public float TerrainEdgeMargin => terrainEdgeMargin;
    public float PathClearance => pathClearance;
    public int MaxAttemptsPerInstance => maxAttemptsPerInstance;

    /// <summary>True when at least one variant has a mesh and material assigned.</summary>
    public bool HasRenderableVariants
    {
        get
        {
            if (meshVariants == null)
                return false;
            for (int i = 0; i < meshVariants.Length; i++)
            {
                MeshSpawnVariant v = meshVariants[i];
                if (v != null && v.mesh != null && v.material != null)
                    return true;
            }

            return false;
        }
    }

    public MeshSpawnVariant GetVariant(int index)
    {
        if (meshVariants == null || (uint)index >= (uint)meshVariants.Length)
            return null;
        return meshVariants[index];
    }

    public int VariantCount => meshVariants != null ? meshVariants.Length : 0;

    void OnEnable()
    {
        MigrateLegacyIfNeeded();
        MigrateGlobalInstanceCountToVariantsIfNeeded();
    }

    void OnValidate()
    {
        MigrateLegacyIfNeeded();
        MigrateGlobalInstanceCountToVariantsIfNeeded();

        maxAttemptsPerInstance = Mathf.Max(1, maxAttemptsPerInstance);
        terrainEdgeMargin = Mathf.Max(0f, terrainEdgeMargin);
        pathClearance = Mathf.Max(-1f, pathClearance);

        if (meshVariants != null)
        {
            for (int i = 0; i < meshVariants.Length; i++)
                meshVariants[i]?.Validate();
        }

#if UNITY_EDITOR
        if (meshInstanceCompute == null)
            meshInstanceCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(DefaultComputePath);
#endif
    }

    void MigrateGlobalInstanceCountToVariantsIfNeeded()
    {
        int legacy = -1;
        if (_legacyInstanceCount >= 0)
            legacy = _legacyInstanceCount;
        else if (_legacyRockCount >= 0)
            legacy = _legacyRockCount;

        if (legacy < 0 || meshVariants == null || meshVariants.Length == 0)
            return;

        int n = meshVariants.Length;
        int baseCount = legacy / n;
        int rem = legacy % n;
        for (int i = 0; i < n; i++)
        {
            if (meshVariants[i] != null)
                meshVariants[i].instanceCount = baseCount + (i < rem ? 1 : 0);
        }

        _legacyInstanceCount = -1;
        _legacyRockCount = -1;
#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }

    void MigrateLegacyIfNeeded()
    {
        if (meshVariants != null && meshVariants.Length > 0)
            return;
        if (rockMesh == null && rockMaterial == null)
            return;

        meshVariants = new[]
        {
            new MeshSpawnVariant
            {
                mesh = rockMesh,
                material = rockMaterial,
                minScale = minScale,
                maxScale = maxScale,
                windYawRadiansPerSecond = windRockYawRadiansPerSecond,
                layer = layer,
                shadowCastingMode = shadowCastingMode,
                receiveShadows = receiveShadows,
                lightProbeUsage = lightProbeUsage,
            }
        };
    }
}
