using System;
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
    [UpdateAfter(typeof(PlayerSystemGroup))]
    [UpdateBefore(typeof(PoseSystem))]
    [UpdateInGroup(typeof(AnimatronSystemGroup))]
    public partial struct CrossFaderSystem : ISystem
    {
        [BurstCompile]
        unsafe partial struct CrossFaderJob : IJobEntity
        {
            public float DeltaTime;
            void Execute(ref Animatron animatron, ref CrossFader crossFader, ref DynamicBuffer<JointPose> pose, in MotionRef motionHandle)
            {
                crossFader.CheckValidity();

                // Handle request for cross fade
                if (crossFader.RequestedAnimationIndex != AnimationIndex.Default)
                {
                    crossFader.AnimationIndex = animatron.AnimationIndex;
                    crossFader.Time = animatron.Time;
                    crossFader.Blend = crossFader.Duration;
                    crossFader.InTransition = true;

                    animatron.AnimationIndex = crossFader.RequestedAnimationIndex;
                    animatron.Time = 0;

                    crossFader.RequestedAnimationIndex = AnimationIndex.Default;
                }

                if (crossFader.InTransition)
                {
                    ref var motion = ref motionHandle.Value.Value;

                    // Continue to play previous track
                    crossFader.Time += DeltaTime * animatron.Speed;
                    var poseTime = motion.GetPoseTime(crossFader.AnimationIndex, crossFader.Time);
                    Span<AnyTransform> targetPose = stackalloc AnyTransform[pose.Length];
                    motion.SamplePose(poseTime, animatron.SamplingMode, targetPose);

                    // As animator executed before cross fade we already have here valid source pose
                    var sourcePose = pose.AsNativeArray().Reinterpret<AnyTransform>().AsSpan();

                    // Blend tracks
                    crossFader.Blend -= DeltaTime * animatron.Speed;
                    float time = math.saturate(crossFader.Blend / crossFader.Duration);
                    motion.Blend(sourcePose, targetPose, time);

                    // Once cross fade is complete we can disable it
                    if (time == 0.0f)
                        crossFader.InTransition = false;
                }
            }
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref Unity.Entities.SystemState state)
        {
            new CrossFaderJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel();
        }
    }
}
