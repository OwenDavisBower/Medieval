using Unity.Burst;
using Unity.Entities;

namespace ProjectDawn.Animation
{
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateAfter(typeof(PlayerSystemGroup))]
    [UpdateBefore(typeof(PoseSystem))]
    [UpdateInGroup(typeof(AnimatronSystemGroup))]
    public partial struct PreviousPoseSystem : ISystem
    {
        [BurstCompile]
        unsafe partial struct PreviousPoseJob : IJobEntity
        {
            void Execute(ref DynamicBuffer<JointPreviousPose> previousPose, in DynamicBuffer<JointPose> pose)
            {
                previousPose.CopyFrom(pose.AsNativeArray().Reinterpret<JointPreviousPose>());
            }
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state)
        {
            new PreviousPoseJob().ScheduleParallel();
        }
    }
}
