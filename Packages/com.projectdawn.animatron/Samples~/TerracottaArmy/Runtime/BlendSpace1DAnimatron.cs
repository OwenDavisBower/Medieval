using ProjectDawn.Animation;
using Unity.Entities;
using UnityEngine;
using Unity.Burst;
using System;

namespace ProjectDawn.Sample
{
    public class BlendSpace1DAnimatron : MonoBehaviour
    {
        void Awake()
        {
            var animatron = GetComponent<AnimatronAuthoring>();
            animatron.World.EntityManager.AddComponentData(animatron.GetOrCreateEntity(), new BlendSpace1DAnimatronPlayer());
        }
    }

    public struct BlendSpace1DAnimatronPlayer : IComponentData
    {
        public float Time;
    }

    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(PlayerSystemGroup))]
    public partial struct BlendSpace1DAnimatronSystem : ISystem
    {
        [BurstCompile]
        partial struct BlendSpace1DAnimatronJob : IJobEntity
        {
            public float DeltaTime;
            public float VerticalAxis;
            void Execute(ref BlendSpace1DAnimatronPlayer animatron, ref DynamicBuffer<JointPose> pose, in MotionRef motionHandle)
            {
                // This is purely for example reason to show how to use blend space 1D
                // It is expected that you will create your own player that will tailor to your game characters

                ref var motion = ref motionHandle.Value.Value;

                // This is simple global time incremental that is used for tracking time of single state
                // In this example the state is being in blend space 1D node
                animatron.Time += DeltaTime;

                // IMPORTANT: It is recommended to precompute this as it is quite expensive, in this sample it is done for simplicity
                Span<AnimationIndex> animations = stackalloc AnimationIndex[3]
                {
                    motion.FindAnimationIndex("Idle"),
                    motion.FindAnimationIndex("Walk"),
                    motion.FindAnimationIndex("Run"),
                };
                Span<float> positions = stackalloc float[3]
                {
                    0, 0.5f, 1.0f,
                };

                // Finally the actual sampling of blend space
                motion.SampleBlendSpace1D(animations, positions, VerticalAxis, animatron.Time, pose.AsTransformArray());
            }
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state)
        {
            new BlendSpace1DAnimatronJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                VerticalAxis = Input.GetAxis("Vertical"),
            }.ScheduleParallel();
        }
    }
}
