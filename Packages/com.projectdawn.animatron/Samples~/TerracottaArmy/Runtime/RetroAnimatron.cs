using ProjectDawn.Animation;
using Unity.Entities;
using UnityEngine;
using Unity.Burst;
#if ANIMATRON_AFFINE_TRANSFORM
using AnyTransform = Unity.Mathematics.AffineTransform;
#elif ANIMATRON_LOCAL_TRANSFORM
using AnyTransform = Unity.Transforms.LocalTransform;
#else
using AnyTransform = ProjectDawn.Animation.RigidTransform;
#endif

namespace ProjectDawn.Sample
{
    // A custom player that plays animations at a fixed, retro-style frame rate
    public class RetroAnimatron : MonoBehaviour
    {
        // For simplicity, the DefaultAnimation is played.
        // In a proper setup, another component would update the animation index dynamically.
        public string DefaultAnimation;

        public int FrameRate = 8;

        void Awake()
        {
            var animatron = GetComponent<AnimatronAuthoring>();
            animatron.TryFindAnimationIndex(DefaultAnimation, out var animationIndex);
            animatron.World.EntityManager.AddComponentData(animatron.GetOrCreateEntity(), new RetroAnimatronPlayer
            {
                AnimationIndex = animationIndex,
                FrameRate = FrameRate,
            });
        }
    }

    public struct RetroAnimatronPlayer : IComponentData
    {
        // Index of the animation currently playing
        public AnimationIndex AnimationIndex;

        // Total time since playback started
        public float Time;

        // Target frame rate for the animation
        public int FrameRate;
    }

    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(PlayerSystemGroup))]
    public partial struct RetroAnimatronSystem : ISystem
    {
        [BurstCompile]
        partial struct RetroAnimatronJob : IJobEntity
        {
            public float DeltaTime;
            void Execute(ref RetroAnimatronPlayer animatron, ref DynamicBuffer<JointPose> pose, in MotionRef motionHandle)
            {
                ref var motion = ref motionHandle.Value.Value;

                // This check is optional but recommended, as external code may modify the animation index
                if (!motion.IsValidAnimationIndex(animatron.AnimationIndex))
                    return;

                animatron.Time += DeltaTime;

                // Converts animation time to retro-style stepped frames based on the target frame rate
                var frame = (int)(animatron.Time * motion.Animations[animatron.AnimationIndex.Value].FrameRate);
                frame = (frame / animatron.FrameRate) * animatron.FrameRate;

                var poseTime = motion.GetPoseTime(animatron.AnimationIndex, frame);

                // Sample the pose for the calculated time
                motion.SamplePose(poseTime, pose.AsTransformArray());
            }
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state)
        {
            new RetroAnimatronJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel();
        }
    }
}
