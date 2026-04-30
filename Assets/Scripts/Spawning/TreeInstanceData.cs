using Unity.Mathematics;

/// <summary>
/// Burst-friendly tree instance for GPU instancing and collider pooling (no UnityEngine.Object references).
/// </summary>
public struct TreeInstanceData
{
    public float3 Position;
    public quaternion Rotation;
    public float Scale;
    public int VariantId;
}
