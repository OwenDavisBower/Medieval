using Unity.Burst;
using Unity.Entities;

namespace ProjectDawn.Animation
{
    [UpdateBefore(typeof(PoseSystem))]
    [UpdateInGroup(typeof(AnimatronSystemGroup))]
    public partial class PlayerSystemGroup : ComponentSystemGroup { }
}
