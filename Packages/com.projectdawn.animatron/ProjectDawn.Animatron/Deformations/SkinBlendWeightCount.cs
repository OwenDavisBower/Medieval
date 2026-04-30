using Unity.Entities;
using Unity.Rendering;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Specifies the dimension of blend wight used for skinning.
    /// </summary>
    [MaterialProperty("_SkinBlendWeightCount")]
    public struct SkinBlendWeightCount : IComponentData
    {
        public float Value;
    }
}