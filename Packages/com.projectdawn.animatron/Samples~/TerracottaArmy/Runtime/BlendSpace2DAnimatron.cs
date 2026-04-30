using ProjectDawn.Animation;
using Unity.Entities;
using UnityEngine;
using Unity.Burst;
using System;
using Unity.Mathematics;

namespace ProjectDawn.Sample
{
    public class BlendSpace2DAnimatron : MonoBehaviour
    {
        void Awake()
        {
            var animatron = GetComponent<AnimatronAuthoring>();
            animatron.World.EntityManager.AddComponentData(animatron.GetOrCreateEntity(), new BlendSpace2DAnimatronPlayer());
        }
    }

    public struct BlendSpace2DAnimatronPlayer : IComponentData
    {
        public float Time;
    }

    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(PlayerSystemGroup))]
    public partial struct BlendSpace2DAnimatronSystem : ISystem
    {
        [BurstCompile]
        partial struct BlendSpace2DAnimatronJob : IJobEntity
        {
            public float DeltaTime;
            public float VerticalAxis;
            public float HorizontalAxis;
            void Execute(ref BlendSpace2DAnimatronPlayer animatron, ref DynamicBuffer<JointPose> pose, in MotionRef motionHandle)
            {
                // This is purely for example reason to show how to use blend space 2D
                // It is expected that you will create your own player that will tailor to your game characters

                ref var motion = ref motionHandle.Value.Value;

                // This is simple global time incremental that is used for tracking time of single state
                // In this example the state is being in blend space 2D node
                animatron.Time += DeltaTime;

                // IMPORTANT: It is recommended to precompute this as it is quite expensive, in this sample it is done for simplicity
                Span<AnimationIndex> animations = stackalloc AnimationIndex[6]
                {
                    motion.FindAnimationIndex("Idle"),
                    motion.FindAnimationIndex("Walk"),
                    motion.FindAnimationIndex("Run"),
                    motion.FindAnimationIndex("RunBackward"),
                    motion.FindAnimationIndex("RunRight"),
                    motion.FindAnimationIndex("RunLeft"),
                };
                Span<float2> positions = stackalloc float2[6]
                {
                    new float2(0, 0),
                    new float2(0, 0.5f),
                    new float2(0, 1),
                    new float2(0, -1),
                    new float2(1, 0),
                    new float2(-1, 0),
                };
                var blendSpace = new BlendSpace2D(animations, positions, Unity.Collections.Allocator.Temp);

                // Finally the actual sampling of blend space
                var input = new float2(HorizontalAxis, VerticalAxis);
                motion.SampleBlendSpace2D(blendSpace, input, animatron.Time, pose.AsTransformArray());
            }
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state)
        {
            new BlendSpace2DAnimatronJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                VerticalAxis = Input.GetAxis("Vertical"),
                HorizontalAxis = Input.GetAxis("Horizontal"),
            }.ScheduleParallel();
        }
    }
}
