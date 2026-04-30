using Unity.Entities;
using Unity.Transforms;

namespace ProjectDawn.Animation
{
    [UpdateBefore(typeof(TransformSystemGroup))] // Required by SkinSystem as it changes local transform
    public partial class AnimatronSystemGroup : ComponentSystemGroup { }
}