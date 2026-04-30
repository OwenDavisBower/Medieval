using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static ProjectDawn.Animation.Motion;
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
    [UpdateBefore(typeof(PreviousPoseSystem))]
    [UpdateBefore(typeof(PoseSystem))]
    [UpdateInGroup(typeof(AnimatronSystemGroup))]
    public partial struct InertializerSystem : ISystem
    {
        EntityQuery m_Query;

        [BurstCompile]
        unsafe struct InertializerJob : IJobChunk
        {
            public float DeltaTime;

            public ComponentTypeHandle<Animatron> AnimatronTypeHandle;
            public ComponentTypeHandle<Inertializer> InertializerTypeHandle;
            [ReadOnly] public BufferTypeHandle<JointPose> JointPoseAccessor;
            public BufferTypeHandle<JointPreviousPose> JointPreviousPoseAccessor;
            public BufferTypeHandle<JointInertia> JointInertiaAccessor;
            public SharedComponentTypeHandle<MotionRef> MotionRefTypeHandle;

            void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var animatrons = chunk.GetNativeArray(ref AnimatronTypeHandle).AsSpan();
                var inertializers = chunk.GetNativeArray(ref InertializerTypeHandle).AsSpan();
                var poseBuffers = chunk.GetBufferAccessor(ref JointPoseAccessor);
                var inertiasBuffers = chunk.GetBufferAccessor(ref JointInertiaAccessor);
                var motionHandle = chunk.GetSharedComponent(MotionRefTypeHandle);

                var hasPreviousPoseBuffers = chunk.Has<JointPreviousPose>();
                var previousPoseBuffers = hasPreviousPoseBuffers ? chunk.GetBufferAccessor(ref JointPreviousPoseAccessor) : default;

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var entityIndex))
                {
                    ref var animatron = ref animatrons[entityIndex];
                    ref var inertializer = ref inertializers[entityIndex];
                    var pose = poseBuffers[entityIndex];
                    var inertias = inertiasBuffers[entityIndex];

                    inertializer.CheckValidity();

                    if (inertializer.InTransition)
                    {
                        inertializer.Time += DeltaTime * animatron.Speed;

                        float time = math.min(inertializer.Time, inertializer.Duration);
                        TimePower timePower = new TimePower(time);

                        for (int i = 0; i < pose.Length; i++)
                        {
                            var transforms = (AnyTransform*)pose.GetUnsafeReadOnlyPtr();
#if ANIMATRON_AFFINE_TRANSFORM
                            transforms[i].t += inertias[i].Position.Evaluate(timePower);
                            transforms[i].rs = math.mul(math.float3x3(inertias[i].Rotation.Evaluate(timePower)), transforms[i].rs);
#else
                            transforms[i].Position += inertias[i].Position.Evaluate(timePower);
                            transforms[i].Rotation = math.mul(inertias[i].Rotation.Evaluate(timePower), transforms[i].Rotation);
#endif

                        }

                        if (time == inertializer.Duration)
                            inertializer.InTransition = false;
                    }

                    // Handle inertializer request
                    if (inertializer.RequestedAnimationIndex != AnimationIndex.Default)
                    {
                        ref var motion = ref motionHandle.Value.Value;

                        float dt;
                        Span<AnyTransform> previousPose = hasPreviousPoseBuffers ?
                            previousPoseBuffers[entityIndex].AsNativeArray().Reinterpret<AnyTransform>().AsSpan() :
                            stackalloc AnyTransform[inertias.Length];
                        if (hasPreviousPoseBuffers)
                        {
                            dt = DeltaTime;
                        }
                        else
                        {
                            // TODO: In original intertialization it simply stores previous pose
                            // Worth to investigate, if its better to store it or recalculate on demand
                            // On draw back of this solution is that if there overlapping transsitions
                            // It will get previous pose without the intertalization values
                            var animation = motion.Animations[animatron.AnimationIndex.Value];
                            dt = 1.0f / animation.FrameRate;
                            float frame = (animatron.Time - dt) * animation.FrameRate;
                            if (animation.IsLooping)
                            {
                                frame = (frame + animation.End) % animation.Length;
                            }
                            else
                            {
                                if (frame < 0)
                                    dt += frame;
                                frame = math.clamp(frame, 0, animation.Length - 1);
                            }
                            frame += animation.Begin;
                            PoseTime previousPoseTime = new PoseTime
                            {
                                AnimationIndex = animatron.AnimationIndex.m_Value,
                                PoseIndex = (int)math.floor(frame),
                                Theta = math.frac(frame),
                            };
                            motion.SamplePose(previousPoseTime, animatron.SamplingMode, previousPose);
                        }

                        // As animator executed before cross fade we already have here valid source pose
                        var sourcePose = (AnyTransform*)pose.GetUnsafeReadOnlyPtr();

                        var targetPoseTime = motion.GetPoseTime(inertializer.RequestedAnimationIndex, 0);
                        var targetPose = stackalloc AnyTransform[inertias.Length];
                        var ts = new Span<AnyTransform>(targetPose, inertias.Length);
                        motion.SamplePose(targetPoseTime, animatron.SamplingMode, ts);

                        // Here we pre-compute the inertia values for each joint position and rotation
                        // Later on this inertia will be used to eveluate difference at specific time point
                        for (int i = 0; i < inertias.Length; i++)
                        {
                            inertias[i] = new JointInertia
                            {
#if ANIMATRON_AFFINE_TRANSFORM
                                Position = Float3Inertia.Create(previousPose[i].t, sourcePose[i].t, targetPose[i].t, dt, inertializer.Duration),
                                Rotation = QuaternionInertia.Create(math.quaternion(previousPose[i].rs), math.quaternion(sourcePose[i].rs), math.quaternion(targetPose[i].rs), dt, inertializer.Duration),
#else
                                Position = Float3Inertia.Create(previousPose[i].Position, sourcePose[i].Position, targetPose[i].Position, dt, inertializer.Duration),
                                Rotation = QuaternionInertia.Create(previousPose[i].Rotation, sourcePose[i].Rotation, targetPose[i].Rotation, dt, inertializer.Duration),
#endif
                            };
                        }

                        inertializer.InTransition = true;
                        inertializer.Time = 0;

                        animatron.AnimationIndex = inertializer.RequestedAnimationIndex;
                        animatron.Time = 0;

                        inertializer.RequestedAnimationIndex = AnimationIndex.Default;
                    }
                }
            }
        }

        [BurstCompile]
        void ISystem.OnCreate(ref SystemState state)
        {
            m_Query = SystemAPI.QueryBuilder()
                .WithAllRW<Animatron>()
                .WithAllRW<Inertializer>()
                .WithAll<JointPose>()
                .WithAllRW<JointInertia>()
                .WithAll<MotionRef>()
                .Build();
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state)
        {
            state.Dependency = new InertializerJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                AnimatronTypeHandle = SystemAPI.GetComponentTypeHandle<Animatron>(isReadOnly: false),
                InertializerTypeHandle = SystemAPI.GetComponentTypeHandle<Inertializer>(isReadOnly: false),
                JointPoseAccessor = SystemAPI.GetBufferTypeHandle<JointPose>(isReadOnly: true),
                JointPreviousPoseAccessor = SystemAPI.GetBufferTypeHandle<JointPreviousPose>(isReadOnly: false),
                JointInertiaAccessor = SystemAPI.GetBufferTypeHandle<JointInertia>(isReadOnly: false),
                MotionRefTypeHandle = SystemAPI.GetSharedComponentTypeHandle<MotionRef>(),
            }.ScheduleParallel(m_Query, state.Dependency);
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct JointInertiaSystem : ISystem
    {
        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI.GetSingleton<EndInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            foreach (var (currentPose, entity) in
                SystemAPI.Query<DynamicBuffer<JointPose>>().WithAll<Inertializer>().WithNone<JointInertia>().WithEntityAccess())
            {
                ecb.AddBuffer<JointInertia>(entity).Resize(currentPose.Length, Unity.Collections.NativeArrayOptions.ClearMemory);
            }
        }
    }
}
