using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

#if ANIMATRON_AFFINE_TRANSFORM
using AnyTransform = Unity.Mathematics.AffineTransform;
#elif ANIMATRON_LOCAL_TRANSFORM
using AnyTransform = Unity.Transforms.LocalTransform;
#else
using AnyTransform = ProjectDawn.Animation.RigidTransform;
#endif

namespace ProjectDawn.Animation
{
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(PlayerSystemGroup))]
    public partial struct AnimatronSystem : ISystem
    {
        [BurstCompile]
        partial struct AnimatronJob : IJobEntity
        {
            public float DeltaTime;
            void Execute(ref Animatron animatron, ref DynamicBuffer<JointPose> pose, in MotionRef motionHandle)
            {
                ref var motion = ref motionHandle.Value.Value;

                if (!motion.IsValidAnimationIndex(animatron.AnimationIndex))
                    return;

                var previouPoseTime = motion.GetPoseTime(animatron.AnimationIndex, animatron.Time);

                animatron.Time += DeltaTime * animatron.Speed;

                var poseTime = motion.GetPoseTime(animatron.AnimationIndex, animatron.Time);

                animatron.PlayedPoses = new int2(previouPoseTime.PoseIndex, poseTime.PoseIndex);

                var transforms = pose.AsNativeArray().Reinterpret<AnyTransform>();
                motion.SamplePose(poseTime, animatron.SamplingMode, transforms);
            }
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state)
        {
            new AnimatronJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel();
        }
    }
}
