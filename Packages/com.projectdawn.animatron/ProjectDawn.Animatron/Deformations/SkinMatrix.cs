using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Matrix buffer that contains the skinned transformations of bones in 
    /// relation to the bind pose.
    /// </summary>
    /// <remarks>
    /// This data structure is used for mesh deformations.
    /// </remarks>
    public struct SkinMatrix : IBufferElementData
    {
        /// <summary>
        /// The matrix buffer of the skinned transformations.
        /// </summary>
        public float3x4 Value;

        public static SkinMatrix Default => new()
        {
            Value = new float3x4(new float3(1, 0, 0), new float3(0, 1, 0), new float3(0, 0, 1), new float3(0, 0, 0))
        };
    }

    /// <summary>
    /// Specifies the index of the skin matrix in the <see cref="SkinMatrixBuffer"/>.
    /// The index is an offset in the GPU buffer, with a stride of <see cref="float3x4"/>.
    /// </summary>
    [MaterialProperty("_SkinMatrixIndex")]
    public struct SkinMatrixBufferIndex : IComponentData
    {
        public int Value;

        public static SkinMatrixBufferIndex Invalid => default;
    }
}