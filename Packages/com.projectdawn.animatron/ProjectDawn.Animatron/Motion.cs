using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System;
using UnityEngine.Assertions;
using Unity.Burst;
using UnityEngine;
#if ANIMATRON_AFFINE_TRANSFORM
using AnyTransform = Unity.Mathematics.AffineTransform;
#elif ANIMATRON_LOCAL_TRANSFORM
using AnyTransform = Unity.Transforms.LocalTransform;
#else
using AnyTransform = ProjectDawn.Animation.RigidTransform;
#endif

namespace ProjectDawn.Animation
{
    public struct MotionRef : ISharedComponentData, System.IEquatable<MotionRef>
    {
        public BlobAssetReference<Motion> Value;

        public bool Equals(MotionRef other) => Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>
    /// Index of animation in Motion.
    /// Use <see cref="Motion.FindAnimationIndex(in FixedString64Bytes)"/> to create it.
    /// </summary>
    [Serializable]
    public struct AnimationIndex
    {
        [SerializeField]
        internal int m_Value;

        public int Value => m_Value;

        public static AnimationIndex Default => new();
        public static bool operator==(AnimationIndex lhs, AnimationIndex rhs) => lhs.m_Value == rhs.m_Value;
        public static bool operator!=(AnimationIndex lhs, AnimationIndex rhs) => lhs.m_Value != rhs.m_Value;
        public bool Equals(AnimationIndex other) => m_Value == other.m_Value;
        public override bool Equals(object other) => other is AnimationIndex && Equals((AnimationIndex)other);
        public override int GetHashCode() => m_Value.GetHashCode();
        public static AnimationIndex FromIndex(int index) => new() { m_Value = index };
    }

    /// <summary>
    /// Motion library that contains animation data.
    /// </summary>
    public struct Motion
    {
        public BlobArray<Animation> Animations;
        public BlobArray<FixedString64Bytes> AnimationNames;
        /// <summary>
        /// Flattened array of all joints transforms. Always the size of <see cref="PoseCount"/>/.
        /// </summary>
        public BlobArray<AnyTransform> Transforms;
        /// <summary>
        /// Number of joints.
        /// </summary>
        public int JointCount;
        /// <summary>
        /// Number of poses. Pose is single frame af all joints.
        /// </summary>
        public int PoseCount;
        /// <summary>
        /// The size of single pose.
        /// PoseStride = JointCount * sizeof(AnyTransform).
        /// </summary>
        public int PoseStride;
        /// <summary>
        /// For each pose contains the event.
        /// This is optional array, it might be zero size or <see cref="PoseCount"/>.
        /// </summary>
        public BlobArray<Event> Events;
        /// <summary>
        /// Memory blob that contains all events data. It is recommended to avoid directly using this field and instead use <see cref="GetEventData{T}(Event)"/>.
        /// </summary>
        public BlobArray<byte> EventData;

        /// <summary>
        /// Returns true, if animation index is valid for this motion data.
        /// </summary>
        public bool IsValidAnimationIndex(AnimationIndex index) => index.Value >= 0 && index.Value < Animations.Length;

        /// <summary>
        /// Returns pose time at given animation index and time.
        /// It can be used for sampling poses etc <see cref="SamplePose(in PoseTime, NativeArray{AnyTransform})"/>.
        /// </summary>
        public PoseTime GetPoseTime(AnimationIndex animationIndex, float time)
        {
            CheckIndexInRange(animationIndex.m_Value, Animations.Length);

            var animation = Animations[animationIndex.m_Value];

            var frame = (time * animation.FrameRate) * animation.Speed;

            if (animation.IsLooping)
            {
                frame = frame % animation.Length;
            }
            else
            {
                frame = math.clamp(frame, 0, animation.Length - 1);
            }

            frame += animation.Begin;

            return new PoseTime
            {
                AnimationIndex = animationIndex.m_Value,
                PoseIndex = (int)math.floor(frame),
                Theta = math.frac(frame),
            };
        }

        /// <summary>
        /// Returns pose time at given animation index and time.
        /// It can be used for sampling poses etc <see cref="SamplePose(in PoseTime, NativeArray{AnyTransform})"/>.
        /// </summary>
        public PoseTime GetPoseTime(AnimationIndex animationIndex, int frame)
        {
            CheckIndexInRange(animationIndex.m_Value, Animations.Length);

            var animation = Animations[animationIndex.m_Value];

            if (animation.IsLooping)
            {
                frame = frame % animation.Length;
            }
            else
            {
                frame = math.clamp(frame, 0, animation.Length - 1);
            }

            frame += animation.Begin;

            return new PoseTime
            {
                AnimationIndex = animationIndex.m_Value,
                PoseIndex = frame,
            };
        }

        /// <summary>
        /// Samples pose at given time.
        /// </summary>
        /// <param name="time">The time used for sampling.</param>
        /// <param name="interpolation">What interpolation should be used.</param>
        /// <param name="result">The arry of joint transforms.</param>
        public unsafe void SamplePose(in PoseTime time, SamplingMode interpolation, Span<AnyTransform> result)
        {
            switch (interpolation)
            {
                case SamplingMode.Interpolated:
                    SamplePoseLinear(time, result);
                    break;
                //case Interpolation.UniformS:
                //    SamplePoseUniformS(time, result);
                //    break;
                default:
                    SamplePose(time, result);
                    break;
            }
        }

        /// <summary>
        /// Samples pose at given time without interpolation.
        /// </summary>
        public unsafe void SamplePose(in PoseTime time, NativeArray<AnyTransform> result) => SamplePose(time, result.AsSpan());
        /// <summary>
        /// Samples pose at given time without interpolation.
        /// </summary>
        public unsafe void SamplePose(in PoseTime time, Span<AnyTransform> result)
        {
            CheckIndexInRange(time.PoseIndex, PoseCount);
            UnsafeUtility.MemCpy(UnsafeUtility.AddressOf(ref result.GetPinnableReference()), (byte*) Transforms.GetUnsafePtr() + time.PoseIndex * PoseStride, PoseStride);
        }

        /// <summary>
        /// Samples pose at given time with linear interpolation.
        /// </summary>
        public unsafe void SamplePoseLinear(in PoseTime time, NativeArray<AnyTransform> result) => SamplePoseLinear(time, result.AsSpan());
        /// <summary>
        /// Samples pose at given time with linear interpolation.
        /// </summary>
        public unsafe void SamplePoseLinear(in PoseTime time, Span<AnyTransform> result)
        {
            CheckIndexInRange(time.AnimationIndex, Animations.Length);
            CheckIndexInRange(time.PoseIndex, PoseCount);
            Assert.AreEqual(JointCount, result.Length);

            var animation = Animations[time.AnimationIndex];
            int nextPoseIndex;
            if (animation.IsLooping)
            {
                nextPoseIndex = math.max(time.PoseIndex + 1 - animation.Begin, 0) % animation.Length + animation.Begin;
            }
            else
            {
                nextPoseIndex = math.clamp(time.PoseIndex + 1, animation.Begin, animation.End - 1);
            }

            for (int jointIndex = 0; jointIndex < JointCount; jointIndex++)
            {
                AnyTransform lhs = GetJointLocalToParentTransform(jointIndex, time.PoseIndex);
                AnyTransform rhs = GetJointLocalToParentTransform(jointIndex, nextPoseIndex);

#if ANIMATRON_AFFINE_TRANSFORM
                result[jointIndex] = new AnyTransform()
                {
                    t = math.lerp(lhs.t, rhs.t, time.Theta),
                    rs = new float3x3(math.slerp(new quaternion(lhs.rs), new quaternion(rhs.rs), time.Theta)),
                };
#elif ANIMATRON_LOCAL_TRANSFORM
                result[jointIndex] = new AnyTransform()
                {
                    Position = math.lerp(lhs.Position, rhs.Position, time.Theta),
                    Rotation = math.slerp(lhs.Rotation, rhs.Rotation, time.Theta),
                    Scale = math.lerp(lhs.Scale, rhs.Scale, time.Theta),
                };
#else
                result[jointIndex] = new AnyTransform()
                {
                    Position = math.lerp(lhs.Position, rhs.Position, time.Theta),
                    Rotation = math.slerp(lhs.Rotation, rhs.Rotation, time.Theta),
                };
#endif
            }
        }

        /// <summary>
        /// Samples pose at given time with uniform-s interpolation. Typically recommended for ogranic looking motion.
        /// </summary>
        public unsafe void SamplePoseUniformS(in PoseTime time, NativeArray<AnyTransform> result) => SamplePoseLinear(time, result.AsSpan());
        /// <summary>
        /// Samples pose at given time with uniform-s interpolation. Typically recommended for ogranic looking motion.
        /// </summary>
        public unsafe void SamplePoseUniformS(in PoseTime time, Span<AnyTransform> result)
        {
            // https://animcoding.com/post/animation-tech-intro-part-3-blending/
            float alpha = time.Theta;
            float sqt = alpha * alpha;
            float uniformS = sqt / (2.0f * (sqt - alpha) + 1.0f);

            SamplePoseLinear(new PoseTime
            {
                AnimationIndex = time.AnimationIndex,
                PoseIndex = time.PoseIndex,
                Theta = uniformS,
            }, result);
        }

        /// <summary>
        /// Samples a pose from a 1D blend space using the given input value.  
        /// The blend space must be sorted by blend values in ascending order.
        /// </summary>
        /// <param name="blendSpace">
        /// A read-only span of tuples containing an <see cref="AnimationIndex"/> and its associated blend value.  
        /// The array must be sorted by blend values.
        /// </param>
        /// <param name="time">
        /// The current playback time used to evaluate the animations.
        /// </param>
        /// <param name="position">
        /// The input value used to select and interpolate between blend space entries.
        /// </param>
        /// <param name="result">
        /// A writable span of <see cref="AnyTransform"/> where the sampled pose will be written.
        /// </param>
        public unsafe void SampleBlendSpace1D(ReadOnlySpan<AnimationIndex> animations, ReadOnlySpan<float> positions, float position, float time, Span<AnyTransform> result)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (animations.Length == 0)
                throw new InvalidOperationException();

            if (animations.Length != positions.Length)
                throw new InvalidOperationException("Animations and positions length must match.");
#endif

            if (positions[0] >= position)
            {
                PoseTime poseTime = GetPoseTime(animations[0], time);
                SamplePose(poseTime, SamplingMode.Interpolated, result);
                return;
            }

            for (int i = 1; i < animations.Length; i++)
            {
                var lower = positions[i - 1];
                var upper = positions[i];

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                if (lower >= upper)
                    throw new InvalidOperationException("Blend space must be sorted by ascending blend values.");
#endif

                if (lower <= position && position < upper)
                {
                    PoseTime sourcePoseTime = GetPoseTime(animations[i - 1], time);
                    SamplePose(sourcePoseTime, SamplingMode.Interpolated, result);

                    Span<AnyTransform> targetPose = stackalloc AnyTransform[JointCount];
                    PoseTime targetPoseTime = GetPoseTime(animations[i], time);
                    SamplePose(targetPoseTime, SamplingMode.Interpolated, targetPose);

                    float alpha = (position - lower) / (upper - lower);
                    Blend(result, targetPose, alpha);

                    return;
                }
            }

            PoseTime lastPoseTime = GetPoseTime(animations[animations.Length - 1], time);
            SamplePose(lastPoseTime, SamplingMode.Interpolated, result);
        }

        public unsafe void SampleBlendSpace2D(BlendSpace2D blendSpace, float2 position, float time, Span<AnyTransform> result)
        {
            var r = blendSpace.Evaluate(position);

            PoseTime poseTime0 = GetPoseTime(r.Item1, time);
            SamplePose(poseTime0, SamplingMode.Interpolated, result);

            Span<AnyTransform> pose1 = stackalloc AnyTransform[JointCount];
            PoseTime poseTime1 = GetPoseTime(r.Item2, time);
            SamplePose(poseTime1, SamplingMode.Interpolated, pose1);

            Span<AnyTransform> pose2 = stackalloc AnyTransform[JointCount];
            PoseTime poseTime2 = GetPoseTime(r.Item3, time);
            SamplePose(poseTime2, SamplingMode.Interpolated, pose2);

            Blend3(result, pose1, pose2, r.Item4.x, r.Item4.y, r.Item4.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetJointTransformIndex(int jointIndex, int poseIndex)
        {
            CheckIndexInRange(jointIndex, JointCount);
            CheckIndexInRange(poseIndex, PoseCount);
            return poseIndex * JointCount + jointIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AnyTransform GetJointLocalToParentTransform(int jointIndex, int poseIndex)
        {
            CheckIndexInRange(jointIndex, JointCount);
            CheckIndexInRange(poseIndex, PoseCount);
            return Transforms[poseIndex * JointCount + jointIndex];
        }

        /// <summary>
        /// Returns animation normalized time that ranges from 0 to 1. Where 0 indicates the start of animation and 1 the end.
        /// </summary>
        public float GetNormalizedTime(AnimationIndex animationIndex, float time)
        {
            var animation = Animations[animationIndex.m_Value];
            if (animation.IsLooping)
            {
                return time % animation.Time;
            }
            else
            {
                return math.saturate(time / animation.Time);
            }
        }

        /// <summary>
        /// Returns animation index with given name.
        /// </summary>
        /// <exception cref="System.Exception"></exception>
        public bool TryFindAnimationIndex(in FixedString64Bytes name, out AnimationIndex index)
        {
            for (index.m_Value = 0; index.m_Value < AnimationNames.Length; index.m_Value++)
            {
                if (AnimationNames[index.m_Value] == name)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns animation index with given name.
        /// </summary>
        /// <exception cref="System.Exception"></exception>
        public AnimationIndex FindAnimationIndex(in FixedString64Bytes name)
        {
            for (int index = 0; index < AnimationNames.Length; index++)
            {
                if (AnimationNames[index] == name)
                {
                    return AnimationIndex.FromIndex(index);
                }
            }
            throw new InvalidOperationException($"Failed to find animation with name {name}");
        }

        /// <summary>
        /// Linearly blends two transform arrays.
        /// </summary>
        /// <param name="source">First array and also the result.</param>
        /// <param name="target">Second array.</param>
        /// <param name="alpha">Blend value.</param>
        public unsafe void Blend([NoAlias] Span<AnyTransform> source, [NoAlias] ReadOnlySpan<AnyTransform> target, float alpha)
        {
            for (int jointIndex = 0; jointIndex < JointCount; jointIndex++)
            {
#if ANIMATRON_AFFINE_TRANSFORM
                source[jointIndex] = new AnyTransform()
                {
                    t = math.lerp(source[jointIndex].t, target[jointIndex].t, alpha),
                    rs = new float3x3(math.slerp(new quaternion(source[jointIndex].rs), new quaternion(target[jointIndex].rs), alpha)),
                };
#elif ANIMATRON_LOCAL_TRANSFORM
                source[jointIndex] = new AnyTransform()
                {
                    Position = math.lerp(source[jointIndex].Position, target[jointIndex].Position, alpha),
                    Rotation = math.slerp(source[jointIndex].Rotation, target[jointIndex].Rotation, alpha),
                    Scale = math.lerp(source[jointIndex].Scale, target[jointIndex].Scale, alpha),
                };
#else
                source[jointIndex] = new AnyTransform()
                {
                    Position = math.lerp(source[jointIndex].Position, target[jointIndex].Position, alpha),
                    Rotation = math.slerp(source[jointIndex].Rotation, target[jointIndex].Rotation, alpha),
                };
#endif
            }
        }

        /// <summary>
        /// Linearly blends three transform arrays.
        /// </summary>
        /// <param name="source">First array and also the result.</param>
        /// <param name="target1">Second array.</param>
        /// <param name="target2">Third array.</param>
        /// <param name="alpha">Weight for target1.</param>
        /// <param name="beta">Weight for target2.</param>
        /// <param name="gamma">Weight for source.</param>
        public unsafe void Blend3(
            [NoAlias] Span<AnyTransform> source,
            [NoAlias] ReadOnlySpan<AnyTransform> target1,
            [NoAlias] ReadOnlySpan<AnyTransform> target2,
            float gamma,
            float alpha,
            float beta)
        {
            var t0 = (alpha + gamma) != 0 ? alpha / (alpha + gamma) : 0;
            var t1 = (alpha + beta + gamma) != 0 ? beta / (alpha + beta + gamma) : 0;

            for (int jointIndex = 0; jointIndex < JointCount; jointIndex++)
            {
#if ANIMATRON_AFFINE_TRANSFORM
                float3 t = gamma * source[jointIndex].t 
                         + alpha * target1[jointIndex].t 
                         + beta * target2[jointIndex].t;

                quaternion q = math.slerp(math.slerp(
                                    new quaternion(source[jointIndex].rs),
                                    new quaternion(target1[jointIndex].rs), t0),
                                    new quaternion(target2[jointIndex].rs), t1);

                source[jointIndex] = new AnyTransform()
                {
                    t = t,
                    rs = new float3x3(q)
                };
#elif ANIMATRON_LOCAL_TRANSFORM
                source[jointIndex] = new AnyTransform()
                {
                    Position = gamma * source[jointIndex].Position
                             + alpha * target1[jointIndex].Position
                             + beta * target2[jointIndex].Position,
                    Rotation = math.slerp(math.slerp(
                                           source[jointIndex].Rotation,
                                           target1[jointIndex].Rotation, t0),
                                          target2[jointIndex].Rotation, t1),
                    Scale = gamma * source[jointIndex].Scale
                          + alpha * target1[jointIndex].Scale
                          + beta * target2[jointIndex].Scale
                };
#else
                source[jointIndex] = new AnyTransform()
                {
                    Position = gamma * source[jointIndex].Position
                             + alpha * target1[jointIndex].Position
                             + beta * target2[jointIndex].Position,
                    Rotation = math.slerp(math.slerp(
                            source[jointIndex].Rotation,
                            target1[jointIndex].Rotation, t0),
                            target2[jointIndex].Rotation, t1)
                };
#endif
            }
        }


        /// <summary>
        /// Returns event withing the pose range, if it exists.
        /// </summary>
        public bool TryGetEvent<T>(AnimationIndex animationIndex, int2 poses, out T e) where T : unmanaged, IEventData
        {
            CheckIndexInRange(animationIndex.m_Value, Animations.Length);

            ComponentType type = typeof(T);
            ulong stableTypeHash = TypeManager.GetTypeInfo(type.TypeIndex).StableTypeHash;

            var animation = Animations[animationIndex.m_Value];

            unsafe
            {
                if (poses.x <= poses.y)
                {
                    for (int poseIndex = poses.x; poseIndex < poses.y; poseIndex++)
                    {
                        if (Events[poseIndex].StableTypeHash == stableTypeHash)
                        {
                            e = GetEventData<T>(Events[poseIndex]);
                            return true;
                        }
                    }
                }
                else
                {
                    for (int poseIndex = poses.x; poseIndex < animation.End; poseIndex++)
                    {
                        if (Events[poseIndex].StableTypeHash == stableTypeHash)
                        {
                            e = GetEventData<T>(Events[poseIndex]);
                            return true;
                        }
                    }
                    for (int poseIndex = animation.Begin; poseIndex < poses.y; poseIndex++)
                    {
                        if (Events[poseIndex].StableTypeHash == stableTypeHash)
                        {
                            e = GetEventData<T>(Events[poseIndex]);
                            return true;
                        }
                    }
                }
            }

            e = default;
            return false;
        }

        /// <summary>
        /// Returns true, if event exists within pose range.
        /// </summary>
        public bool HasEvent<T>(AnimationIndex animationIndex, int2 poses) where T : unmanaged, IEventData
        {
            CheckIndexInRange(animationIndex.m_Value, Animations.Length);

            ComponentType type = typeof(T);
            ulong stableTypeHash = TypeManager.GetTypeInfo(type.TypeIndex).StableTypeHash;

            var animation = Animations[animationIndex.m_Value];

            if (poses.x < poses.y)
            {
                for (int poseIndex = poses.x; poseIndex < poses.y; poseIndex++)
                {
                    if (Events[poseIndex].StableTypeHash == stableTypeHash)
                        return true;
                }
            }
            else
            {
                for (int poseIndex = poses.x; poseIndex < animation.End; poseIndex++)
                {
                    if (Events[poseIndex].StableTypeHash == stableTypeHash)
                        return true;
                }
                for (int poseIndex = animation.Begin; poseIndex < poses.y; poseIndex++)
                {
                    if (Events[poseIndex].StableTypeHash == stableTypeHash)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns event data associated with the event.
        /// </summary>
        public T GetEventData<T>(Event e) where T : unmanaged
        {
            unsafe
            {
                CheckEventType<T>(e);
                return *(T*)((char*)EventData.GetUnsafePtr() + e.DataOffset);
            }
        }

        /// <summary>
        /// Used for sampling the pose.
        /// </summary>
        public struct PoseTime
        {
            /// <summary>
            /// Curren animation index.
            /// </summary>
            public int AnimationIndex;
            /// <summary>
            /// Current index of the pose.
            /// </summary>
            public int PoseIndex;
            /// <summary>
            /// Fractional value between current pose index and next one.
            /// Used for blending between current pose and next pose.
            /// </summary>
            public float Theta;
        }

        public struct Animation
        {
            /// <summary>
            /// Begin in <see cref="Motion.Transforms"/> array.
            /// </summary>
            public int Begin;
            /// <summary>
            /// End in <see cref="Motion.Transforms"/> array.
            /// </summary>
            public int End;
            /// <summary>
            /// Frame rate at which keyframes are sampled.
            /// </summary>
            public float FrameRate;
            /// <summary>
            /// Does animation loop.
            /// </summary>
            public bool IsLooping;
            /// <summary>
            /// The speed of animation. The 1 is default and 0 is frozen.
            /// </summary>
            public float Speed;
            /// <summary>
            /// Animation time in seconds.
            /// </summary>
            public float Time => Length / FrameRate;
            /// <summary>
            /// Animation frame count.
            /// </summary>
            public int Length => End - Begin;
        }

        public struct Event 
        {
            public ulong StableTypeHash;
            /// <summary>
            /// Begin in <see cref="Motion.EventData"/> array.
            /// </summary>
            public int DataOffset;
            /// <summary>
            /// Returns true, if the event is valid.
            /// </summary>
            public bool IsValid => StableTypeHash != 0;
            /// <summary>
            /// A unique index of the component type
            /// </summary>
            /// <returns>Component TypeIndex, otherwise <seealso cref="TypeIndex.Null"/></returns>
            public TypeIndex GetTypeIndex() => TypeManager.GetTypeIndexFromStableTypeHash(StableTypeHash);

            /// <summary>
            /// Gets the managed <see cref="Type"/> based on the component's <see cref="TypeIndex"/>.
            /// </summary>
            public Type GetManagedType() => TypeManager.GetType(GetTypeIndex());
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CheckIndexInRange(int index, int length)
        {
            // This checks both < 0 and >= Length with one comparison
            if ((uint)index >= (uint)length)
                throw new IndexOutOfRangeException($"Index {index} is out of range in container of '{length}' Length.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CheckEventType<T>(in Event e)
        {
            ulong stableTypeHash = TypeManager.GetTypeInfo<T>().StableTypeHash;
            if (e.StableTypeHash != stableTypeHash)
                throw new IndexOutOfRangeException($"Event type {typeof(T).Name} does not match serialized hash {stableTypeHash}.");
        }
    }

    /// <summary>
    /// Marks this structure as usable for Animatron events.
    /// Also indicates that it implements <see cref="IComponentData"/>.
    /// The type must be blittable: https://learn.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types.
    /// </summary>
    public interface IEventData : IComponentData { }
}
