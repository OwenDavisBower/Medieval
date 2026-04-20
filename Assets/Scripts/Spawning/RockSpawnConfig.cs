using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "RockSpawnConfig", menuName = "Medieval/Spawning/Rock Spawn Config")]
public class RockSpawnConfig : ScriptableObject
{
    const string DefaultComputePath = "Assets/Shaders/RocksInstance.compute";

    [SerializeField] Mesh rockMesh;
    [Tooltip("Must use shader Universal Render Pipeline/RockIndirectInstanced (GPU instancing + RenderMeshInstanced).")]
    [SerializeField] Material rockMaterial;
    [SerializeField] ComputeShader rocksInstanceCompute;
    [SerializeField] int rockCount = 64;
    [SerializeField] float regionRadius = 80f;
    [SerializeField] Vector3 regionCenter;
    [SerializeField] float minScale = 0.8f;
    [SerializeField] float maxScale = 1.4f;
    [Tooltip("Passed to terrain snap (meters above surface).")]
    [SerializeField] float terrainHeightOffset = 0.05f;
    [Tooltip("Clamp XZ to terrain footprint before sampling height; 0 disables clamp.")]
    [SerializeField] float terrainEdgeMargin = 8f;
    [SerializeField] int maxAttemptsPerRock = 60;
    [Tooltip("Radians per second added to yaw on GPU (0 = static after first build).")]
    [SerializeField] float windRockYawRadiansPerSecond;
    [SerializeField] int layer;
    [SerializeField] ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;
    [SerializeField] bool receiveShadows = true;
    [SerializeField] LightProbeUsage lightProbeUsage = LightProbeUsage.Off;

    public Mesh RockMesh => rockMesh;
    public Material RockMaterial => rockMaterial;
    public ComputeShader RocksInstanceCompute => rocksInstanceCompute;
    public int RockCount => rockCount;
    public float RegionRadius => regionRadius;
    public Vector3 RegionCenter => regionCenter;
    public float MinScale => minScale;
    public float MaxScale => maxScale;
    public float TerrainHeightOffset => terrainHeightOffset;
    public float TerrainEdgeMargin => terrainEdgeMargin;
    public int MaxAttemptsPerRock => maxAttemptsPerRock;
    public float WindRockYawRadiansPerSecond => windRockYawRadiansPerSecond;
    public int Layer => layer;
    public ShadowCastingMode ShadowCastingMode => shadowCastingMode;
    public bool ReceiveShadows => receiveShadows;
    public LightProbeUsage LightProbeUsage => lightProbeUsage;

    void OnValidate()
    {
        rockCount = Mathf.Max(0, rockCount);
        regionRadius = Mathf.Max(0f, regionRadius);
        minScale = Mathf.Max(0.01f, minScale);
        maxScale = Mathf.Max(minScale, maxScale);
        maxAttemptsPerRock = Mathf.Max(1, maxAttemptsPerRock);
        terrainEdgeMargin = Mathf.Max(0f, terrainEdgeMargin);
#if UNITY_EDITOR
        if (rocksInstanceCompute == null)
            rocksInstanceCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(DefaultComputePath);
#endif
    }
}
