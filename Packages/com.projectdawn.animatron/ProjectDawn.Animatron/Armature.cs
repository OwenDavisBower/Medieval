using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;
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
    /// <summary>
    /// Transform of joint at single pose.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct JointPose : IBufferElementData
    {
        public AnyTransform Value;

#if ANIMATRON_AFFINE_TRANSFORM
        public static JointPose Default => new JointPose { Value = AnyTransform.identity };
#else
        public static JointPose Default => new JointPose { Value = AnyTransform.Identity };
#endif
    }

    /// <summary>
    /// Transforms of joint at previous single pose.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct JointPreviousPose : IBufferElementData
    {
        public AnyTransform Value;

#if ANIMATRON_AFFINE_TRANSFORM
        public static JointPreviousPose Default => new JointPreviousPose { Value = AnyTransform.identity };
#else
        public static JointPreviousPose Default => new JointPreviousPose { Value = AnyTransform.Identity };
#endif
    }

    /// <summary>
    /// Skin is subset of armature joints.
    /// </summary>
    public struct Skin
    {
        /// <summary>
        /// Root index in <see cref="JointPose"/>.
        /// </summary>
        public int Root;
        /// <summary>
        /// Begin offset in the <see cref="Armature.SkinBindPoses"/>.
        /// </summary>
        public int Begin;
        /// <summary>
        /// End Offset in the <see cref="Armature.SkinBindPoses"/>.
        /// </summary>
        public int End;
        /// <summary>
        /// Bounding box of this skin in object space.
        /// </summary>
        public AABB Bounds;
        /// <summary>
        /// The total amount of bindings in this skin.
        /// </summary>
        public int Length => End - Begin;
    }

    /// <summary>
    /// Used for assigning <see cref="Armature"/> as component for entity.
    /// </summary>
    public struct ArmatureRef : ISharedComponentData, System.IEquatable<ArmatureRef>
    {
        public BlobAssetReference<Armature> Value;

        public void Dispose()
        {
            Value.Dispose();
        }

        public bool Equals(ArmatureRef other) => (Value == other.Value);
        public override int GetHashCode() => (int)Value.GetHashCode();
    }

    /// <summary>
    /// Used for assigning <see cref="Animation.Armature"/> as component for entity and skins index.
    /// </summary>
    public struct SkinRef : ISharedComponentData, System.IEquatable<SkinRef>
    {
        public BlobAssetReference<Armature> Armature;
        public int SkinIndex;

        public void Dispose()
        {
            Armature.Dispose();
        }

        public bool Equals(SkinRef other) => (Armature == other.Armature && SkinIndex == other.SkinIndex);
        public override int GetHashCode() => (int)math.hash(new int2(Armature.GetHashCode(), SkinIndex));
    }

    public struct Armature
    {
        /// <summary>
        /// Names of joints. This can be empty, if rig is baked without the option.
        /// </summary>
        public BlobArray<FixedString64Bytes> JointNames;
        /// <summary>
        /// Each joint index to parent.
        /// </summary>
        public BlobArray<int> JointParentIndices;
        /// <summary>
        /// Skin is subset of armature joints.
        /// </summary>
        public BlobArray<Skin> Skins;
        /// <summary>
        /// Flattened array of all skins bind poses.
        /// Bind pose is matrix that transforms from object to bone space (a.k.a. parent space of that bone).
        /// Use <see cref="GetSkinBindPoses(Skin)"/> to get bind poses of particular skin.
        /// </summary>
        public BlobArray<float4x4> SkinBindPoses;
        /// <summary>
        /// Flattened array of all kins indices to joints.
        /// Use <see cref="GetSkinJointIndices(Skin)"/> to get skin joint indices of particular skin.
        /// </summary>
        public BlobArray<int> SkinJointIndices;

        /// <summary>
        /// Total number of joints in the armature.
        /// </summary>
        public int JointCount => JointParentIndices.Length;

        /// <summary>
        /// Returns a read-only span of bind pose matrices for the given skin.
        /// </summary>
        public ReadOnlySpan<float4x4> GetSkinBindPoses(Skin skin)
        {
            CheckIndexInRange(skin.Begin, SkinBindPoses.Length - 1);
            CheckIndexInRange(skin.End, SkinBindPoses.Length - 1);
            unsafe
            {
                return new ReadOnlySpan<float4x4>((float4x4*)SkinBindPoses.GetUnsafePtr() + skin.Begin, skin.Length);
            }
        }

        /// <summary>
        /// Returns a read-only span of joint indices for the given skin.
        /// </summary>
        public ReadOnlySpan<int> GetSkinJointIndices(Skin skin)
        {
            CheckIndexInRange(skin.Begin, SkinBindPoses.Length - 1);
            CheckIndexInRange(skin.End, SkinBindPoses.Length - 1);
            unsafe
            {
                return new ReadOnlySpan<int>((int*)SkinBindPoses.GetUnsafePtr() + skin.Begin, skin.Length);
            }
        }

        /// <summary>
        /// Finds the index of a joint by name. Throws if not found.
        /// </summary>
        public int FindJointIndex(in FixedString64Bytes name)
        {
            if (TryFindJointIndex(name, out var jointIndex))
                return jointIndex;
            throw new InvalidOperationException($"Failed to find joint with name {name}");
        }

        /// <summary>
        /// Tries to find the index of a joint by name.
        /// </summary>
        public bool TryFindJointIndex(in FixedString64Bytes name, out int index)
        {
            for (index = 0; index < JointNames.Length; index++)
                if (JointNames[index] == name)
                    return true;
            return false;
        }

        /// <summary>
        /// Finds the index of a joint by name. Throws if not found.
        /// </summary>
        public int FindJointIndex(string name)
        {
            if (TryFindJointIndex(name, out var jointIndex))
                return jointIndex;
            throw new InvalidOperationException($"Failed to find joint with name {name}");
        }

        /// <summary>
        /// Tries to find the index of a joint by name.
        /// </summary>
        public bool TryFindJointIndex(string name, out int index)
        {
            for (index = 0; index < JointNames.Length; index++)
                if (JointNames[index] == name)
                    return true;
            return false;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CheckIndexInRange(int index, int length)
        {
            // This checks both < 0 and >= Length with one comparison
            if ((uint)index >= (uint)length)
                throw new IndexOutOfRangeException($"Index {index} is out of range in container of '{length}' Length.");
        }
    }

    public static class PoseUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NativeArray<AnyTransform> AsTransformArray(this NativeArray<JointPose> pose) => pose.Reinterpret<AnyTransform>();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NativeArray<AnyTransform> AsTransformArray(this NativeArray<JointPreviousPose> pose) => pose.Reinterpret<AnyTransform>();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NativeArray<AnyTransform> AsTransformArray(this DynamicBuffer<JointPose> pose) => pose.AsNativeArray().Reinterpret<AnyTransform>();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NativeArray<AnyTransform> AsTransformArray(this DynamicBuffer<JointPreviousPose> pose) => pose.AsNativeArray().Reinterpret<AnyTransform>();
    }
}
